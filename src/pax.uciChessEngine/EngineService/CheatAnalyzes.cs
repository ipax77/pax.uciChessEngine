using pax.chess;
using System.Threading.Channels;
using System.Collections.Concurrent;
using System.Text;

namespace pax.uciChessEngine;

public class CheatAnalyzis
{
    public CheatAnalyzis(List<string> engines, TimeSpan timePerHalfMove, int pvs, int threads)
    {
        this.engines = engines;
        this.timePerHalfMove = timePerHalfMove;
        this.pvs = pvs;
        this.threads = threads;
    }

    private List<string> engines;
    private TimeSpan timePerHalfMove;
    private int pvs;
    private int threads;

    public event EventHandler<CheatAnalyzisEventArgs>? CheatAnalyzisProgress;

    public void UpdateSettings(TimeSpan timePerHalfMove, int pvs, int threads)
    {
        this.timePerHalfMove = timePerHalfMove;
        this.pvs = pvs;
        this.threads = threads;
    }

    public void OnCheatAnalyzisProgress(CheatAnalyzisEventArgs e)
    {
        CheatAnalyzisProgress?.Invoke(this, e);
    }

    public async Task AnalyzeGame(Game game, CancellationToken token = default)
    {
        if (threads <= 0)
        {
            threads = Math.Max(Environment.ProcessorCount / 2, 1);
        }

        var moveChunks = game.State.Moves.Chunk(game.State.Moves.Count / threads);
        var infoChannel = Channel.CreateUnbounded<EngineMoveResult>();
        int suggestedMaxVariations = game.State.Moves.Count * engines.Count;
        int variationsDone = 0;

        _ = ProduceVariations(game, moveChunks, infoChannel, token);

        ConcurrentBag<EngineMoveResult> variationsBag = new();

        while (await infoChannel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            if (infoChannel.Reader.TryRead(out EngineMoveResult? variation))
            {
                Interlocked.Increment(ref variationsDone);

                OnCheatAnalyzisProgress(new CheatAnalyzisEventArgs()
                {
                    VariationsProcessed = variationsDone,
                    SuggestedMax = suggestedMaxVariations,
                    Results = variationsBag.ToList()
                });
                variationsBag.Add(variation);
            }
        }
        var result = AnalyzeResults(game, variationsBag);
        OnCheatAnalyzisProgress(new CheatAnalyzisEventArgs()
        {
            VariationsProcessed = suggestedMaxVariations,
            SuggestedMax = suggestedMaxVariations,
            Results = variationsBag.ToList(),
            Done = true
        });
    }

    private static string AnalyzeResults(Game game, ConcurrentBag<EngineMoveResult> results)
    {
        StringBuilder sb = new();
        for (int i = 0; i < game.State.Moves.Count; i++)
        {
            var moveResults = results.Where(x => x.HalfMove == i).OrderBy(o => o.EngineName);
            var pgnMove = game.State.Moves[i].PgnMove;
            sb.Append($"Game move: {pgnMove}\t");
            foreach (var result in moveResults)
            {
                var pvMove = result.FirstPvMoves.FirstOrDefault(f => f.PgnMove == pgnMove);
                var bestMove = result.FirstPvMoves.First();
                if (pvMove == null)
                {
                    sb.Append($"{result.EngineName}: None (best: {bestMove.PgnMove} {bestMove.Evaluation?.ChartScore()})\t");
                }
                else
                {
                    var index = result.FirstPvMoves.IndexOf(pvMove);
                    sb.Append($"{result.EngineName}: {index}. => {pvMove.Evaluation?.ChartScore()} {(pvMove != bestMove ? bestMove.Evaluation?.ChartScore().ToString() : "")}\t");
                }
            }
            sb.Append(Environment.NewLine);
        }
        Console.WriteLine(sb.ToString());
        return sb.ToString();
    }

    private async Task ProduceVariations(Game game, IEnumerable<Move[]> chunks, Channel<EngineMoveResult> channel, CancellationToken token)
    {
        ParallelOptions po = new()
        {
            MaxDegreeOfParallelism = chunks.Count(),
            CancellationToken = token
        };
        try
        {
            await Parallel.ForEachAsync(chunks, po, async (data, token) =>
            {
                await ChunkAnalyze(game, channel, data, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            channel.Writer.Complete();
        }
    }

    private async Task ChunkAnalyze(Game game, Channel<EngineMoveResult> channel, IEnumerable<Move> moves, CancellationToken token)
    {
        if (!moves.Any())
        {
            return;
        }
        int startPos = moves.First().HalfMoveNumber;

        foreach (var ent in engines)
        {

            Engine engine = new(Path.GetFileNameWithoutExtension(ent) ?? "unknown", ent);
            await engine.Start().ConfigureAwait(false);
            await engine.IsReady().ConfigureAwait(false);
            await engine.GetOptions().ConfigureAwait(false);
            await engine.IsReady().ConfigureAwait(false);
            await engine.SetOption("Threads", pvs).ConfigureAwait(false);
            await engine.SetOption("MultiPV", pvs).ConfigureAwait(false);

            try
            {
                for (int i = 0; i < moves.Count(); i++)
                {
                    var move = moves.ElementAt(i);
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    await engine.Send($"position startpos moves {String.Join(" ", game.State.Moves.Take(startPos + i).Select(s => s.EngineMove.ToString()))}").ConfigureAwait(false);
                    await engine.IsReady().ConfigureAwait(false);
                    engine.Status.Pvs.Clear();
                    await engine.Send("go").ConfigureAwait(false);
                    await Task.Delay(timePerHalfMove, token).ConfigureAwait(false);
                    var info = await engine.GetStopInfo().ConfigureAwait(false);

                    GetVariations(game, channel, info, startPos + i);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                engine.Dispose();
            }
        }
    }

    private void GetVariations(Game game, Channel<EngineMoveResult> channel, EngineInfo? info, int halfMove)
    {
        if (info != null && info.PvInfos.Any())
        {
            List<Move> evals = new();
            Game pgnGame = new();
            for (int i = 0; i < halfMove; i++)
            {
                var iresult = pgnGame.Move(game.State.Moves[i].EngineMove);
                if (iresult != MoveState.Ok)
                {
                    throw new MoveException($"enginemove failed: {game.State.Moves[i].EngineMove}");
                }
            }
            foreach (var pv in info.PvInfos)
            {
                Game pvGame = new(pgnGame);

                var rresult = pvGame.Move(pv.Moves.ElementAt(0));
                if (rresult != MoveState.Ok)
                {
                    throw new MoveException($"enginemove failed: {pv.Moves.ElementAt(0)}");
                }
                var move = pvGame.State.Moves.Last();
                move.Evaluation = new Evaluation(pv.Score, pv.Mate, halfMove % 2 != 0);

                evals.Add(move);
            }

            channel.Writer.TryWrite(new EngineMoveResult()
            {
                HalfMove = halfMove,
                IsBlack = halfMove % 2 != 0,
                EngineName = info.EngineName,
                FirstPvMoves = evals
            });
        }
    }
}

public class CheatAnalyzisEventArgs : EventArgs
{
    public int VariationsProcessed { get; init; }
    public int SuggestedMax { get; init; }
    public List<EngineMoveResult> Results { get; init; } = new();
    public bool Done { get; init; } = false;
}

public record EngineMoveResult
{
    public int HalfMove { get; init; }
    public bool IsBlack { get; init; }
    public string EngineName { get; init; } = "unknown";
    public List<Move> FirstPvMoves { get; init; } = new();
}

public record MoveEval
{
    public Move Move { get; init; } = null!;
    public double Eval { get; init; }
}
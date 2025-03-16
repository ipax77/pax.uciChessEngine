using pax.chess;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace pax.uciChessEngine;

public class GameAnalyzes
{
    public Game Game { get; private set; }
    private readonly KeyValuePair<string, string> Engine;
    private int _Done;
    public int Done => _Done;
    private TimeSpan TimeSpan;
    private int Pvs;
    private Channel<Variation> InfoChannel = Channel.CreateUnbounded<Variation>();

    public GameAnalyzes(Game game, KeyValuePair<string, string> engine)
    {
        Game = game;
        Engine = engine;
    }

    public async IAsyncEnumerable<Variation> Analyze([EnumeratorCancellation] CancellationToken token, TimeSpan timespan = new TimeSpan(), int pvs = 2, int threads = 0)
    {
        if (threads == 0)
        {
            threads = Environment.ProcessorCount / 2;
        }
        if (timespan == TimeSpan.Zero)
        {
            timespan = TimeSpan.FromSeconds(2);
        }
        Pvs = pvs;
        TimeSpan = timespan;
        int chunkSize = Game.State.Moves.Count / threads;
        var chunks = Game.State.Moves.Chunk(chunkSize);

        InfoChannel = Channel.CreateUnbounded<Variation>();
        _ = ProduceRatings(chunks, token);

        while (await InfoChannel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            if (InfoChannel.Reader.TryRead(out Variation? variation))
            {
                yield return variation;
            }
        }
    }

    private async Task ProduceRatings(IEnumerable<Move[]> chunks, CancellationToken token)
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
                await ChunkAnalyze(data, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            InfoChannel.Writer.Complete();
        }
    }

    private async Task ChunkAnalyze(Move[] moves, CancellationToken token)
    {
        if (moves.Length == 0)
        {
            return;
        }
        int startPos = moves.First().HalfMoveNumber;
        Engine engine = new(Engine.Key, Engine.Value);
        await engine.Start().ConfigureAwait(false);
        await engine.IsReady().ConfigureAwait(false);
        await engine.GetOptions().ConfigureAwait(false);
        await engine.IsReady().ConfigureAwait(false);
        await engine.SetOption("Threads", Pvs).ConfigureAwait(false);
        await engine.SetOption("MultiPV", Pvs).ConfigureAwait(false);

        try
        {
            for (int i = 0; i < moves.Length; i++)
            {
                var move = moves[i];
                if (token.IsCancellationRequested)
                {
                    break;
                }
                await engine.Send($"position startpos moves {String.Join(" ", Game.State.Moves.Take(startPos + i).Select(s => s.EngineMove.ToString()))}").ConfigureAwait(false);
                await engine.IsReady().ConfigureAwait(false);
                engine.Status.Pvs.Clear();
                await engine.Send("go").ConfigureAwait(false);
                await Task.Delay(TimeSpan, token).ConfigureAwait(false);
                var info = await engine.GetStopInfo().ConfigureAwait(false);

                GetVariations(info, startPos + i);

                Interlocked.Increment(ref _Done);
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

    private void GetVariations(EngineInfo? info, int halfMove)
    {
        if (info != null && info.PvInfos.Count != 0)
        {
            Game pgnGame = new();
            for (int i = 0; i < halfMove; i++)
            {
                var iresult = pgnGame.Move(Game.State.Moves[i].EngineMove);
                if (iresult != MoveState.Ok)
                {
                    throw new MoveException($"enginemove failed: {Game.State.Moves[i].EngineMove}");
                }
            }

            foreach (var pv in info.PvInfos)
            {
                Variation variation = new(halfMove);
                variation.Evaluation = new Evaluation(pv.Score, pv.Mate, halfMove % 2 != 0);
                variation.Pv = pv.MultiPv;
                Game pvGame = new(pgnGame);

                for (int i = 0; i < pv.Moves.Count; i++)
                {
                    var rresult = pvGame.Move(pv.Moves.ElementAt(i));
                    if (rresult != MoveState.Ok)
                    {
                        throw new MoveException($"enginemove failed: {pv.Moves.ElementAt(i)}");
                    }
                    variation.Moves.Add(pvGame.State.Moves.Last());
                }
                InfoChannel.Writer.TryWrite(variation);
            }
        }
    }
}

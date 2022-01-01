using pax.chess;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace pax.uciChessEngine;

public class GameAnalysis
{
    private Channel<InfoHelper> InfoChannel = Channel.CreateUnbounded<InfoHelper>();
    private int _Done;
    public int Done => _Done;

    public async IAsyncEnumerable<InfoHelper> Analyse(Game game, [EnumeratorCancellation] CancellationToken token, int _threads = 0)
    {
        int threads = _threads;
        if (threads <= 0)
        {
            threads = Environment.ProcessorCount / 2;
        }

        List<EngineMoveNum> engineMoves = new List<EngineMoveNum>();
        for (int i = 0; i < game.State.Moves.Count; i++)
        {
            var move = game.State.Moves[i];
            engineMoves.Add(new EngineMoveNum(i, move.EngineMove));
        }
        int chunkSize = engineMoves.Count / threads + 1;
        var chunks = engineMoves.Chunk(chunkSize);

        _ = Produce(game, chunks, token);

        while (await InfoChannel.Reader.WaitToReadAsync(token))
        {
            InfoHelper? info;
            if (InfoChannel.Reader.TryRead(out info))
            {
                yield return info;
            }
        }
        Console.WriteLine("indahouse");
    }

    private async Task Produce(Game game, IEnumerable<EngineMoveNum[]> chunks, CancellationToken token)
    {
        ParallelOptions po = new ParallelOptions()
        {
            MaxDegreeOfParallelism = chunks.Count(),
            CancellationToken = token
        };
        try
        {
            await Parallel.ForEachAsync(chunks, po, async (data, token) =>
            {
                await ChunkAnalyse(game, data, token);
            });
        }
        catch (OperationCanceledException) { };
        InfoChannel.Writer.Complete();
    }

    private async Task ChunkAnalyse(Game game, EngineMoveNum[] engineMoves, CancellationToken token)
    {
        int startPos = engineMoves[0].HalfMoveNumber;
        Engine engine = new Engine("Stockfish", @"C:\data\stockfish_14.1_win_x64_avx2\stockfish_14.1_win_x64_avx2.exe");
        engine.Start();
        if (!await engine.IsReady())
        {
            Console.WriteLine($"engine error 1.5");
        }
        await engine.GetOptions();
        if (!await engine.IsReady())
        {
            Console.WriteLine($"engine error 1.55");
        }
        engine.SetOption("Threads", 2);
        if (!await engine.IsReady())
        {
            Console.WriteLine($"engine error 1.6");
        }
        engine.SetOption("MultiPV", 2);
        if (!await engine.IsReady())
        {
            Console.WriteLine($"engine error 1.7");
        }
        for (int i = 0; i < engineMoves.Length; i++)
        {
            if (token.IsCancellationRequested)
            {
                engine.Dispose();
                return;
            }
            engine.Send($"position startpos moves {String.Join(" ", game.State.Moves.Take(startPos + i).Select(s => Map.GetEngineMoveString(s)))}");
            if (!await engine.IsReady())
            {
                Console.WriteLine($"engine error 2");
            }
            var move = game.State.Moves.Last();

            engine.Send("go");
            await Task.Delay(2000);
            engine.Send("stop");
            if (!await engine.IsReady())
            {
                Console.WriteLine($"engine error 3");
            }

            var info = engine.GetInfo();
            if (info.PvInfos.Count > 1)
            {
                var gameMove = game.State.Moves[startPos + i].PgnMove;
                var result = new InfoHelper(
                    startPos + i,
                    new Evaluation(info.PvInfos[0].Score, info.PvInfos[0].Mate, game.State.Info.BlackToMove),
                    new Evaluation(info.PvInfos[1].Score, info.PvInfos[1].Mate, game.State.Info.BlackToMove),
                    info.PvInfos[0].Moves.First().ToString(),
                    info.PvInfos[1].Moves.First().ToString(),
                    gameMove);
                InfoChannel.Writer.TryWrite(result);
            }
            else
            {
                Console.WriteLine($"go not enough pvs :(");
            }
            Interlocked.Increment(ref _Done);
        }
        engine.Dispose();
    }
}


public record InfoHelper
{
    public int HalfMoveNumber { get; init; }
    public Evaluation Eval { get; init; }
    public Evaluation RunnerEval { get; init; }
    public string BestMove { get; init; }
    public string RunnerMove { get; init; }
    public string GameMove { get; init; }

    public InfoHelper(int moveNumber, Evaluation eval, Evaluation reval, string bmove, string rmove, string gmove)
    {
        HalfMoveNumber = moveNumber;
        Eval = eval;
        RunnerEval = reval;
        BestMove = bmove;
        RunnerMove = rmove;
        GameMove = gmove;
    }
}
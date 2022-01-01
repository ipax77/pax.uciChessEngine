using pax.chess;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace pax.uciChessEngine;

public class GameAnalysis
{
    private Channel<InfoHelper> InfoChannel = Channel.CreateUnbounded<InfoHelper>();
    private int _Done;
    public int Done => _Done;
    public ConcurrentBag<InfoHelper> Infos { get; private set; } = new ConcurrentBag<InfoHelper>();
    public Game Game { get; private set; }
    public KeyValuePair<string, string> Engine { get; private set; }

    public GameAnalysis(Game game, KeyValuePair<string, string> engine)
    {
        Game = game;
        Engine = engine;
    }

    public async IAsyncEnumerable<InfoHelper> Analyse([EnumeratorCancellation] CancellationToken token, int _threads = 0)
    {
        int threads = _threads;
        if (threads <= 0)
        {
            threads = Environment.ProcessorCount / 2;
        }

        List<EngineMoveNum> engineMoves = new List<EngineMoveNum>();
        for (int i = 0; i < Game.State.Moves.Count; i++)
        {
            var move = Game.State.Moves[i];
            engineMoves.Add(new EngineMoveNum(i, move.EngineMove));
        }
        int chunkSize = engineMoves.Count / threads + 1;
        var chunks = engineMoves.Chunk(chunkSize);

        _ = Produce(Game, chunks, token);

        while (await InfoChannel.Reader.WaitToReadAsync(token))
        {
            InfoHelper? info;
            if (InfoChannel.Reader.TryRead(out info))
            {
                Infos.Add(info);
                yield return info;
            }
        }
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
        Engine engine = new Engine(Engine.Key, Engine.Value);
        engine.Start();
        await engine.IsReady();
        await engine.GetOptions();
        await engine.IsReady();
        engine.SetOption("Threads", 2);
        await engine.IsReady();
        engine.SetOption("MultiPV", 2);
        await engine.IsReady();

        for (int i = 0; i < engineMoves.Length; i++)
        {
            if (token.IsCancellationRequested)
            {
                engine.Dispose();
                return;
            }
            engine.Send($"position startpos moves {String.Join(" ", game.State.Moves.Take(startPos + i).Select(s => s.EngineMove.ToString()))}");
            await engine.IsReady();

            engine.Send("go");
            await Task.Delay(2000);
            engine.Send("stop");
            await engine.IsReady();

            var info = engine.GetInfo();
            if (info.PvInfos.Count > 1)
            {
                var gameMove = game.State.Moves[startPos + i].PgnMove;
                var result = new InfoHelper(
                    startPos + i,
                    new Evaluation(info.PvInfos[0].Score, info.PvInfos[0].Mate, (startPos + i) % 2 != 0),
                    new Evaluation(info.PvInfos[1].Score, info.PvInfos[1].Mate, (startPos + i) % 2 != 0),
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

public record AnalyzeRequest
{
    public string EngineName { get; set; } = "Stockfish";
    public int Threads { get; set; }
    public int MultiPV { get; set; }
    public Game? Game { get; set; }
}

public record InfoHelper
{
    public int HalfMoveNumber { get; init; }
    public Evaluation Eval { get; init; }
    public Evaluation RunnerEval { get; init; }
    public string BestMove { get; init; }
    public string RunnerMove { get; init; }
    public string GameMove { get; init; }
    public string BestPgnMove { get; set; } = String.Empty;
    public string RunnerPgnMove { get; set; } = String.Empty;

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
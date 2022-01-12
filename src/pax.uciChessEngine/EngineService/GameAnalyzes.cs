using Microsoft.Extensions.Logging;
using pax.chess;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace pax.uciChessEngine;

public class GameAnalyzes
{
    public Game Game { get; private set; }
    private KeyValuePair<string, string> Engine;
    private int _Done;
    public int Done => _Done;
    private TimeSpan TimeSpan;
    private int Pvs;
    private ILogger<Engine> logger => StatusService.logger;
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

        while (await InfoChannel.Reader.WaitToReadAsync(token))
        {
            if (InfoChannel.Reader.TryRead(out Variation? variation))
            {
                yield return variation;
            }
        }
    }

    private async Task ProduceRatings(IEnumerable<Move[]> chunks, CancellationToken token)
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
                await ChunkAnalyze(data, token);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            InfoChannel.Writer.Complete();
        }
    }

    private async Task ChunkAnalyze(IEnumerable<Move> moves, CancellationToken token)
    {
        if (!moves.Any())
        {
            return;
        }
        int startPos = moves.First().HalfMoveNumber;
        Engine engine = new Engine(Engine.Key, Engine.Value);
        engine.Start();
        await engine.IsReady();
        await engine.GetOptions();
        await engine.IsReady();
        engine.SetOption("Threads", Pvs);
        await engine.IsReady();
        engine.SetOption("MultiPV", Pvs);
        await engine.IsReady();

        try
        {
            for (int i = 0; i < moves.Count(); i++)
            {
                var move = moves.ElementAt(i);
                if (token.IsCancellationRequested)
                {
                    engine.Dispose();
                    return;
                }
                engine.Send($"position startpos moves {String.Join(" ", Game.State.Moves.Take(startPos + i).Select(s => s.EngineMove.ToString()))}");
                await engine.IsReady();
                engine.Status.Pvs.Clear();
                engine.Send("go");
                await Task.Delay(TimeSpan, token);
                var info = await engine.GetStopInfo();

                GetVariations(info, startPos + i);

                Interlocked.Increment(ref _Done);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError($"chunk analyze failed: {ex.Message}");
        }
        finally
        {
            engine.Dispose();
        }
    }

    private void GetVariations(EngineInfo? info, int halfMove)
    {
        if (info != null && info.PvInfos.Any())
        {
            Game pgnGame = new Game();
            for (int i = 0; i < halfMove; i++)
            {
                var iresult = pgnGame.Move(Game.State.Moves[i].EngineMove);
                if (iresult != MoveState.Ok)
                {
                    logger.LogError($"enginemove failed: {Game.State.Moves[i].EngineMove}");
                    return;
                }
            }

            foreach (var pv in info.PvInfos)
            {
                Variation variation = new Variation(halfMove);
                variation.Evaluation = new Evaluation(pv.Score, pv.Mate, halfMove % 2 != 0);
                variation.Pv = pv.MultiPv;
                // Game pvGame = new Game(pgnGame);
                Game pvGame = new Game();
                for (int i = 0; i < halfMove; i++)
                {
                    var iresult = pvGame.Move(Game.State.Moves[i].EngineMove);
                    if (iresult != MoveState.Ok)
                    {
                        logger.LogError($"enginemove failed: {Game.State.Moves[i].EngineMove}");
                        return;
                    }
                }

                for (int i = 0; i < pv.Moves.Count; i++)
                {
                    var rresult = pvGame.Move(pv.Moves[i]);
                    if (rresult != MoveState.Ok)
                    {
                        logger.LogError($"enginemove failed: {pv.Moves[i]}");
                        break;
                    }
                    variation.Moves.Add(pvGame.State.Moves.Last());
                }
                InfoChannel.Writer.TryWrite(variation);
            }
        }
    }

    private Rating? CollectInfo(EngineInfo? info, int halfMove)
    {
        if (info == null)
        {
            return null;
        }

        if (info.PvInfos.Any())
        {
            Move gameMove = Game.State.Moves[halfMove];

            Game pgnGame = new Game();
            for (int i = 0; i < halfMove; i++)
            {
                var iresult = pgnGame.Move(Game.State.Moves[i].EngineMove);
                if (iresult != MoveState.Ok)
                {
                    logger.LogError($"enginemove failed: {Game.State.Moves[i].EngineMove}");
                    return null;
                }
            }

            EngineMove bestEngineMove = info.PvInfos[0].Moves[0];
            Evaluation bestEval = new Evaluation(info.PvInfos[0].Score, info.PvInfos[0].Mate, halfMove % 2 != 0);
            var result = pgnGame.Move(bestEngineMove);
            if (result != MoveState.Ok)
            {
                logger.LogError($"enginemove failed: {bestEngineMove}");
                return null;
            }
            Move bestMove = pgnGame.State.Moves.Last();
            bestMove.Evaluation = bestEval;

            Rating rating = new Rating(gameMove, bestMove);
            rating.BestLine.AddRange(info.PvInfos[0].Moves);

            for (int i = 1; i < info.PvInfos.Count; i++)
            {
                EngineMove engineMove = info.PvInfos[i].Moves[0];
                Evaluation eval = new Evaluation(info.PvInfos[i].Score, info.PvInfos[i].Mate, halfMove % 2 != 0);
                pgnGame.Revert();
                var rresult = pgnGame.Move(engineMove);
                if (rresult != MoveState.Ok)
                {
                    logger.LogError($"enginemove failed: {engineMove}");
                    return null;
                }

                Move move = pgnGame.State.Moves.Last();
                move.Evaluation = eval;
                rating.RunnerMoves.Add(move);
                rating.RunnerLines.Add(info.PvInfos[i].Moves);
            }
            return rating;
        }
        return null;
    }
}

public record Rating
{
    public Move GameMove { get; init; }
    public Move BestMove { get; init; }
    public List<EngineMove> BestLine { get; init; } = new List<EngineMove>();
    public List<Move> RunnerMoves { get; init; } = new List<Move>();
    public List<List<EngineMove>> RunnerLines { get; init; } = new List<List<EngineMove>>();

    public Rating(Move gameMove, Move bestMove)
    {
        GameMove = gameMove;
        BestMove = bestMove;
    }
}


using System.Text;
using pax.chess;
using pax.chess.Extensions;

namespace pax.uciChessEngine;

public sealed class EngineGame : IAsyncDisposable
{
    private readonly UciEngine _whiteEngine;
    private readonly UciEngine _blackEngine;
    private readonly CancellationTokenSource cts = new();
    private readonly int _threads;
    private readonly List<string> _moves = [];
    private readonly ChessGame _chessGame;
    public ChessGame ChessGame => _chessGame;
    public event EventHandler<EngineMoveEventArgs>? MoveReady;
    public event EventHandler<EventArgs>? GameFinished;

    public EngineGame(UciEngine whiteEngine, UciEngine blackEngine, int threadsPerEngine = 2)
    {
        _whiteEngine = whiteEngine;
        _blackEngine = blackEngine;
        _threads = Math.Max(1, threadsPerEngine);
        _chessGame = new();
        _chessGame.ActivatePositionHashing();
    }

    public async Task Start(ChessClock chessClock)
    {
        _chessGame.SetClock(chessClock);
        var t1 = _whiteEngine.StartAsync();
        var t2 = _blackEngine.StartAsync();
        await Task.WhenAll(t1, t2);

        await _whiteEngine.SendAsync("ucinewgame", cts.Token);
        await _blackEngine.SendAsync("ucinewgame", cts.Token);

        await _whiteEngine.SetOption("Threads", _threads, cts.Token);
        await _blackEngine.SetOption("Threads", _threads, cts.Token);

        _whiteEngine.MoveReady += WhiteMoveReady;
        _blackEngine.MoveReady += BlackMoveReady;

        await _whiteEngine.SendAsync("position startpos", cts.Token);
        await _blackEngine.SendAsync("position startpos", cts.Token);

        await _whiteEngine.SendAsync(GetGoString(), cts.Token);
        _chessGame.Clock?.Start();
    }

    private string GetGoString()
    {
        StringBuilder sb = new();
        sb.Append("go wtime ");
        sb.Append(Convert.ToInt32(_chessGame.Clock!.WhiteTime.TotalMilliseconds));
        sb.Append(" btime ");
        sb.Append(Convert.ToInt32(_chessGame.Clock!.BlackTime.TotalMilliseconds));
        var increment = Convert.ToInt32(_chessGame.Clock!.Increment.TotalMilliseconds);
        sb.Append(" winc ");
        sb.Append(increment);
        sb.Append(" binc ");
        sb.Append(increment);
        return sb.ToString();
    }

    private async Task Move(UciEngine engine)
    {
        string movesString = string.Join(" ", _moves);

        await engine.SendAsync(
            $"position startpos moves {movesString}",
            cts.Token);

        await engine.SendAsync(GetGoString(), cts.Token);
        OnMoveReady(new() { Move = _moves.Last(), Eval = engine.Status.GetEval(ChessGame.CurrentPosition.SideToMove) });
    }

    public async Task Stop()
    {
        try
        {
            await _whiteEngine.SendAsync("stop", CancellationToken.None);
            await _blackEngine.SendAsync("stop", CancellationToken.None);
        }
        finally
        {
            await cts.CancelAsync();
        }
    }

    private async void BlackMoveReady(object? sender, MoveEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Move))
        {
            return;
        }
        _moves.Add(e.Move);
        var move = Uci.CreateMove(e.Move, ChessGame.CurrentPosition);
        ArgumentNullException.ThrowIfNull(move, e.Move);
        var moveResult = _chessGame.TryApplyMove(move);
        if (moveResult != MoveState.Ok)
        {
            Console.WriteLine(e.Move + " " + FenSerializer.Serialize(ChessGame.CurrentPosition));
            throw new InvalidOperationException(moveResult.ToString());
        }

        var result = _chessGame.Result;
        if (result != null)
        {
            await Terminate();
            return;
        }

        _chessGame.Clock?.ApplyMove(PieceColor.Black);
        await Move(_whiteEngine);
    }

    private async void WhiteMoveReady(object? sender, MoveEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Move))
        {
            return;
        }
        _moves.Add(e.Move);
        var move = Uci.CreateMove(e.Move, ChessGame.CurrentPosition);
        ArgumentNullException.ThrowIfNull(move, e.Move);
        var moveResult = _chessGame.TryApplyMove(move);
        if (moveResult != MoveState.Ok)
        {
            Console.WriteLine(e.Move + " " + FenSerializer.Serialize(ChessGame.CurrentPosition));
            throw new InvalidOperationException(moveResult.ToString());
        }

        var result = _chessGame.Result;
        if (result != null)
        {
            await Terminate();
            return;
        }
        _chessGame.Clock?.ApplyMove(PieceColor.White);
        await Move(_blackEngine);
    }

    private async Task Terminate()
    {
        await Stop();
        OnGameFinished();
    }

    private void OnMoveReady(EngineMoveEventArgs e)
    {
        MoveReady?.Invoke(this, e);
    }

    private void OnGameFinished()
    {
        GameFinished?.Invoke(this, new());
    }

    public async ValueTask DisposeAsync()
    {
        await cts.CancelAsync();
        cts.Dispose();
        await _whiteEngine.DisposeAsync();
        await _blackEngine.DisposeAsync();
    }
}

using System.Text;
using pax.chess;
using pax.chess.Extensions;

namespace pax.uciChessEngine;

public interface IEngineGame
{
    ChessGame ChessGame { get; }

    event EventHandler<EventArgs>? GameFinished;
    event EventHandler<EngineMoveEventArgs>? MoveReady;

    ValueTask DisposeAsync();
    Task Start();
    Task StopGame();
}

public sealed class EngineGame : IAsyncDisposable, IEngineGame
{
    private readonly UciEngine _whiteEngine;
    private readonly UciEngine _blackEngine;
    private readonly CancellationTokenSource cts = new();
    private readonly int _threads;
    private readonly List<string> _moves;
    private readonly string _startPosition;
    private readonly ChessGame _chessGame;
    public ChessGame ChessGame => _chessGame;
    public event EventHandler<EngineMoveEventArgs>? MoveReady;
    public event EventHandler<EventArgs>? GameFinished;

    public EngineGame(
        UciEngine whiteEngine,
        UciEngine blackEngine,
        ChessGame game,
        int threadsPerEngine = 2,
        IEnumerable<string>? initialMoves = null)
    {
        _chessGame = game;
        _whiteEngine = whiteEngine;
        _blackEngine = blackEngine;
        _threads = Math.Max(1, threadsPerEngine);
        _startPosition = FenSerializer.Serialize(_chessGame.InitialPosition);
        _moves = new List<string>(initialMoves ?? Enumerable.Empty<string>());
    }

    public async Task Start()
    {
        try
        {
            var t1 = _whiteEngine.StartAsync();
            var t2 = _blackEngine.StartAsync();
            await Task.WhenAll(t1, t2);

            await _whiteEngine.SendAsync("ucinewgame", cts.Token);
            await _blackEngine.SendAsync("ucinewgame", cts.Token);

            await _whiteEngine.SetOption("Threads", _threads, cts.Token);
            await _blackEngine.SetOption("Threads", _threads, cts.Token);

            _whiteEngine.MoveReady += WhiteMoveReady;
            _blackEngine.MoveReady += BlackMoveReady;

            var positionCommand = BuildPositionCommand();
            await _whiteEngine.SendAsync(positionCommand, cts.Token);
            await _blackEngine.SendAsync(positionCommand, cts.Token);

            var toMove = _chessGame.CurrentPosition.SideToMove;
            await (toMove == PieceColor.White
                ? _whiteEngine.SendAsync(GetGoString(), cts.Token)
                : _blackEngine.SendAsync(GetGoString(), cts.Token));

            _chessGame.Clock?.Start(toMove);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // StopGame requested during startup; swallow to allow graceful shutdown.
        }
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
        if (cts.IsCancellationRequested)
            return;

        string movesString = string.Join(" ", _moves);

        try
        {
            await engine.SendAsync(BuildPositionCommand(movesString), cts.Token);
            await engine.SendAsync(GetGoString(), cts.Token);
            OnMoveReady(new() { Move = _moves.Last(), Eval = engine.Status.GetEval(ChessGame.CurrentPosition.SideToMove) });
        }
        catch (OperationCanceledException)
        {
            // Expected when game is stopped; ignore.
        }
    }

    private string BuildPositionCommand(string? moves = null)
    {
        var sb = new StringBuilder($"position fen {_startPosition}");
        var commandMoves = moves ?? string.Join(" ", _moves);
        if (!string.IsNullOrWhiteSpace(commandMoves))
        {
            sb.Append(" moves ");
            sb.Append(commandMoves);
        }
        return sb.ToString();
    }

    public async Task StopGame()
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
        if (cts.IsCancellationRequested)
            return;
        if (string.IsNullOrWhiteSpace(e.Move))
        {
            return;
        }
        try
        {
            _moves.Add(e.Move);
            var move = Uci.CreateMove(e.Move, ChessGame.CurrentPosition);
            ArgumentNullException.ThrowIfNull(move, e.Move);
            var san = PgnSerializer.ToSan(move, ChessGame.CurrentPosition);
            var moveResult = _chessGame.ApplyMove(move, san);
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
        catch (OperationCanceledException)
        {
        }
    }

    private async void WhiteMoveReady(object? sender, MoveEventArgs e)
    {
        if (cts.IsCancellationRequested)
            return;
        if (string.IsNullOrWhiteSpace(e.Move))
        {
            return;
        }
        try
        {
            _moves.Add(e.Move);
            var move = Uci.CreateMove(e.Move, ChessGame.CurrentPosition);
            ArgumentNullException.ThrowIfNull(move, e.Move);
            var san = PgnSerializer.ToSan(move, ChessGame.CurrentPosition);
            var moveResult = _chessGame.ApplyMove(move, san);
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
        catch (OperationCanceledException)
        {
        }
    }

    private async Task Terminate()
    {
        try
        {
            await StopGame();
        }
        catch (OperationCanceledException)
        {
        }
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

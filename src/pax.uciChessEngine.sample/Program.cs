using pax.chess;
using pax.uciChessEngine;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

// await AnalyseGame();
await PlayGame();

static async Task PlayGame()
{
    var b1 = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
    var b2 = @"C:\data\chess\engines\lc0-v0.32.1-windows-gpu-nvidia-cuda11\lc0.exe";
    var engine1 = new UciEngine(b2);
    var engine2 = new UciEngine(b1);

    using var clock = new ChessClock(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(0), new ChessClockConsoleDisplay());
    var chessGame = new ChessGame(new ChessGameOptions { Clock = clock });
    await using var game = new EngineGame(engine1, engine2, chessGame);
    var gameFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var stopCts = new CancellationTokenSource();

    Console.Clear();
    Console.WriteLine("Engine game started. Press Ctrl+C to stop.");

    Console.CancelKeyPress += OnCancelKeyPress;

    game.MoveReady += (_, e) =>
    {
        Console.WriteLine(game.ChessGame.CurrentPosition.Board.ToString());
        Console.WriteLine(e.Eval);
        Console.WriteLine();
    };

    game.GameFinished += (_, _) =>
    {
        var result = game.ChessGame.Result;
        Console.WriteLine($"result: {result}, {game.ChessGame.Conclusion?.Termination}");

        var pgn = PgnSerializer.Serialize(game.ChessGame);
        Console.WriteLine(pgn);
        gameFinished.TrySetResult();
    };

    try
    {
        await game.Start();
        await gameFinished.Task.WaitAsync(stopCts.Token);
    }
    catch (OperationCanceledException) when (stopCts.IsCancellationRequested)
    {
        await game.StopGame();
    }
    finally
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
    }

    void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        stopCts.Cancel();
    }
}

static async Task AnalyseGame()
{
    var b1 = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
    var game = PgnSerializer.Parse("1. e4 e5 2. Bc4 Bc5 3. Qh5 Nf6 4. Qxf7#");
    var gameAnalysis = new GameAnalysis(b1, game);
    var results = await gameAnalysis.AnalyseGame();
    foreach (var result in results)
    {
        Console.WriteLine($"{result.MoveNumber} => {result.Eval}");
    }
}

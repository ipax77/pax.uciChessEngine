using pax.chess;
using pax.uciChessEngine;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

await AnalyseGame();

Console.ReadLine();

static async Task PlayGame()
{
    var b1 = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
    var b2 = @"C:\data\chess\engines\lc0-v0.32.1-windows-gpu-nvidia-cuda11\lc0.exe";
    var engine1 = new UciEngine(b2);
    var engine2 = new UciEngine(b1);

    var game = new EngineGame(engine1, engine2);
    var clock = new ChessClock(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(0), new ChessClockConsoleDisplay());

    Console.Clear();
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
    };

    await game.Start(clock);

    await game.DisposeAsync();
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
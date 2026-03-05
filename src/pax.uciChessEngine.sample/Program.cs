using pax.chess;
using pax.uciChessEngine;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

var binaryPath = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
var engine1 = new UciEngine(binaryPath);
var engine2 = new UciEngine(binaryPath);

var game = new EngineGame(engine1, engine2);
var clock = new ChessClock(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(0));

game.MoveReady += (_, _) =>
{
    Console.WriteLine(game.ChessGame.CurrentPosition.Board.ToString());
};

game.GameFinished += (_, _) =>
{
    var result = game.ChessGame.Result;
    Console.WriteLine($"result: {result}");

    var pgn = PgnSerializer.Serialize(game.ChessGame);
    Console.WriteLine(pgn);
};

await game.Start(clock);

Console.ReadLine();
await game.DisposeAsync();
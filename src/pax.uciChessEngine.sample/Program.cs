using pax.uciChessEngine;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

var binaryPath = @"C:\data\chess\engines\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
var engine = new UciEngine(binaryPath);
await engine.StartAsync();
string bestMove = await engine.GetBestMoveAsync(
    "rn1qkbnr/pppb1ppp/3p4/4p3/4P3/3P1N2/PPP2PPP/RNBQKB1R w KQkq - 0 5",
    TimeSpan.FromSeconds(1));
Console.WriteLine(bestMove);
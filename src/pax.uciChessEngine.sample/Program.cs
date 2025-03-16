using pax.uciChessEngine;
using pax.chess;

namespace pax.uciChessEngine.sample;


class Program
{
    static bool isGameOver = false;
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        string engine1Path = @"C:\data\chess\engines\stockfish\stockfish-windows-x86-64.exe";
        string engine2Path = @"C:\data\chess\engines\lc0-v0.31.2-windows-gpu-nvidia-cuda\lc0.exe";

        Game game = new();
        EngineGameOptions options = new()
        {
            Description = "Test",
            BlackEngineName = "Stockfish",
            BlackEngine = new("Stockfish", engine1Path),
            WhiteEngineName = "Lc0",
            WhiteEngine = new("Lc0", engine2Path),
            TimeInSeconds = 180,
            IncrementInSeconds = 2,
            Threads = 8

        };
        EngineGame engineGame = new(game, options);
        engineGame.EngineMoved += EngineMoved;
        engineGame.Start();
        int i = 0;
        while (!isGameOver)
        {
            Task.Delay(1000).Wait();
            i++;
            if (i % 100 == 0)
            {
                Console.WriteLine(Pgn.MapPieces(game.State));
            }
        }
        Console.WriteLine(Pgn.MapPieces(game.State));
        Console.WriteLine("Game over.");
        engineGame.EngineMoved -= EngineMoved;
        engineGame.Dispose();
        Console.ReadLine();
    }

    static void EngineMoved(object? sender, EngineMoveEventArgs e)
    {
        Console.WriteLine($"{e.EngineName}: {e.EngineMove} ({e.EngineInfo.Evaluation})");
        if (e.GameOver)
        {
            isGameOver = true;
        }
    }
}

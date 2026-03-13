# Introduction

C# dotnet Universal Chess Interface wrapper for interacting with chess engine processes.

# Getting started

## Installation
You can install it with the Package Manager in your IDE or alternatively using the command line:

```bash
dotnet add package pax.uciChessEngine
```
## Usage

The library now exposes a `UciEngine` that talks to a UCI-compatible binary and a small pooling helper in `pax.uciChessEngine.EngineServices` for safe reuse.

### Quick start (single engine)

```csharp
using pax.uciChessEngine;

await using var engine = new UciEngine(@"path\to\engine.binary");
await engine.StartAsync();
await engine.SetOption("Threads", 4);
await engine.SendAsync("ucinewgame", CancellationToken.None);
var bestMove = await engine.GetBestMoveAsync("1r1qr2k/p1n2p2/b1np1Np1/2p5/8/2NPB1PP/Pb1Q2B1/4RRK1 w - - 2 21", TimeSpan.FromSeconds(2));
Console.WriteLine($"Best move: {bestMove}");
```

### Pooled usage (multiple consumers)

```csharp
using pax.uciChessEngine;
using pax.uciChessEngine.EngineServices;

var options = new EngineRunOptions
{
    BinaryPath = @"path\to\engine.exe",
    PoolSize = 2,
    Threads = 4,
    Pvs = 2,
    HashMb = 256
};

var provider = EngineService.GetEngineSessionProvider(options);

await using var lease = await provider.AcquireAsync(CancellationToken.None);
var evals = await lease.Session.UseAsync(
    engine => EngineService.GetEvaluation(
        fen: "startpos",
        sideToMove: pax.chess.PieceColor.White,
        thinkTime: TimeSpan.FromMilliseconds(300),
        engine: engine,
        token: CancellationToken.None),
    CancellationToken.None);

Console.WriteLine($"Depth: {evals[0].Depth}, Score: {evals[0].Score}");
```

Sample Project [pax.BlazorChess](https://github.com/ipax77/pax.BlazorChess)

## ChangeLog

<details open="open"><summary>v0.7.0</summary>

>- ** Breaking Changes **
>- Update to dotnet 10
- UciEngine usability refreshed with async API.
- Engine pooling

</details>

<details><summary>v0.6.1</summary>

>- ** Breaking Changes **
>- Update to dotnet 8
>- Logging disabled by default. Enable it with:
`
Engine engine = new Engine("EngineName", @"path\to\engine\binary", LogLevel.Warning);
`

</details>

# Introduction

C# dotnet 6 Universal Chess Interface wrapper for interacting with chess engine processes.

# Getting started
## Prerequisites
dotnet 6

## Installation
You can install it with the Package Manager in your IDE or alternatively using the command line:

```bash
dotnet add package pax.uciChessEngine
```
## Usage
```csharp
Engine engine = new Engine("EngineName", @"path\to\engine\binary");
engine.Start();
await engine.IsReady();
var options = await engine.GetOptions();
engine.SetOption("Threads", 4);
await engine.IsReady();
engine.SetOption("MultiPV", 4);
await engine.IsReady();
engine.Send("ucinewgame");
engine.Send("go");
Task.Delay(2000).GetAwaiter().GetResult();
engine.Send("stop");
await engine.IsReady();
var info = engine.GetInfo();

for (int i = 0; i < info.PvInfos.Count; i++)
{
    var pvInfo = info.PvInfos[i];
    Evaluation eval = new Evaluation(pvInfo.Score, pvInfo.Mate, false);
    Console.WriteLine($"pvInfo{i} - move: {pvInfo.Moves[0]}; eval: {eval}");
}
engine.Dispose();
```
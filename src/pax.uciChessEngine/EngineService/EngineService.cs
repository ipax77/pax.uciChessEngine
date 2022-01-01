using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using pax.chess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.uciChessEngine;
public class EngineService
{
    private List<Analyzes> Analyzes = new List<Analyzes>();
    private List<EngineGame> EngineGames = new List<EngineGame>();
    private List<GameAnalysis> GameAnalyses = new List<GameAnalysis>();

    public Dictionary<string, string> AvailableEngines { get; private set; } = new Dictionary<string, string>();
    private IConfiguration _configuration;

    public int CpuCoresTotal { get; private set; }
    public int CpuCoresUsed => EngineGames.Sum(s => s.CpuCoresUsed) + Analyzes.Sum(s => s.CpuCoresUsed);
    public int CpuCoresAvailable => CpuCoresTotal - CpuCoresUsed;

    public EngineService(ILogger<EngineService> logger, IConfiguration configuration)
    {
        _configuration = configuration;
        UpdateEngines();
        CpuCoresTotal = Environment.ProcessorCount;
    }

    public void UpdateEngines()
    {
        AvailableEngines = _configuration.GetSection("ChessEngines").GetChildren().ToDictionary(x => x.Key, x => x.Value);
    }

    public List<Analyzes> GetAnalyzes() => Analyzes;
    public List<EngineGame> GetEngineGames() => EngineGames;
    public List<GameAnalysis> GetGameAnalyses() => GameAnalyses;

    public async Task<Analyzes> CreateAnalyzes(Game game, string? fen = null)
    {
        Analyzes analyzes = new Analyzes(game, fen);
        if (AvailableEngines.Any())
        {
            await analyzes.AddEngine(new Engine(AvailableEngines.First().Key, AvailableEngines.First().Value));

        }
        Analyzes.Add(analyzes);
        return analyzes;
    }

    public void DeleteAnalyzes(Analyzes? analyzes)
    {
        if (analyzes != null)
        {
            analyzes.Dispose();
            Analyzes.Remove(analyzes);
            analyzes = null;
        }
    }

    public EngineGame? CreateEngineGame(Game game, string desc = "")
    {
        Engine whiteEngine;
        Engine blackEngine;
        if (AvailableEngines.Count == 1)
        {
            whiteEngine = new Engine(AvailableEngines.First().Key, AvailableEngines.First().Value);
            blackEngine = new Engine(AvailableEngines.First().Key, AvailableEngines.First().Value);
        } else if (AvailableEngines.Count > 1)
        {
            whiteEngine = new Engine(AvailableEngines.ElementAt(0).Key, AvailableEngines.ElementAt(0).Value);
            blackEngine = new Engine(AvailableEngines.ElementAt(1).Key, AvailableEngines.ElementAt(1).Value);
        } else
        {
            return null;
        }
        EngineGame engineGame = new EngineGame(whiteEngine, blackEngine, game, desc);
        EngineGames.Add(engineGame);
        return engineGame;
    }

    public void DeleteEngineGame(EngineGame engineGame)
    {
        EngineGames.Remove(engineGame);
        engineGame.Dispose();
    }

    public GameAnalysis CreateGameAnalyzes(Game game, string engineName)
    {
        GameAnalysis analyses = new GameAnalysis(game, new KeyValuePair<string, string>(engineName, AvailableEngines[engineName]));
        GameAnalyses.Add(analyses);
        return analyses;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using pax.chess;

namespace pax.uciChessEngine;
public class EngineService : IDisposable
{
    private List<Analyzes> Analyzes = new List<Analyzes>();
    private List<EngineGame> EngineGames = new List<EngineGame>();
    private List<GameAnalyzes> GameAnalyzis = new List<GameAnalyzes>();

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
        // todo correct order !?
        AvailableEngines = _configuration.GetSection("ChessEngines").GetChildren().Reverse().ToDictionary(x => x.Key, x => x.Value);
    }

    public List<Analyzes> GetAnalyzes() => Analyzes;
    public List<EngineGame> GetEngineGames() => EngineGames;
    public List<GameAnalyzes> GetGameAnalyses() => GameAnalyzis;

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
        EngineGameOptions options = new EngineGameOptions()
        {
            WhiteEngineName = whiteEngine.Name,
            BlackEngineName = blackEngine.Name,
            WhiteEngine = new KeyValuePair<string, string>(whiteEngine.Name, whiteEngine.Binary),
            BlackEngine = new KeyValuePair<string, string>(blackEngine.Name, blackEngine.Binary)
        };

        EngineGame engineGame = new EngineGame(game, options);
        EngineGames.Add(engineGame);
        return engineGame;
    }

    public void SetEngineGameOptions(EngineGame engineGame, EngineGameOptions options)
    {
        options.WhiteEngine = new KeyValuePair<string, string>(options.WhiteEngineName, AvailableEngines[options.WhiteEngineName]);
        options.BlackEngine = new KeyValuePair<string, string>(options.BlackEngineName, AvailableEngines[options.BlackEngineName]);
        engineGame.SetOptions(options);
    }

    public void DeleteEngineGame(EngineGame engineGame)
    {
        EngineGames.Remove(engineGame);
        engineGame.Dispose();
    }

    public GameAnalyzes CreateGameAnalyzes(Game game, string engineName)
    {
        GameAnalyzes analyses = new GameAnalyzes(game, new KeyValuePair<string, string>(engineName, AvailableEngines[engineName]));
        GameAnalyzis.Add(analyses);
        return analyses;
    }

    public void DeleteGameAnalyzes(GameAnalyzes gameAnalyzes)
    {
        GameAnalyzis.Remove(gameAnalyzes);
        return;
    }

    public void Dispose()
    {
        Analyzes.ForEach(x => x.Dispose());
        EngineGames.ForEach(x => x.Dispose());
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using pax.chess;
using System.Diagnostics.CodeAnalysis;

namespace pax.uciChessEngine;
[SuppressMessage(
    "Usage", "CA1024:Use properties where appropriate",
    Justification = "Caller is not allowed to change the lists")]
[SuppressMessage(
    "Usage", "CA2000:Dispose objects before losing scope",
    Justification = "the engine is transfered to another object that handles the dispose")]
public sealed class EngineService : IDisposable
{
    private readonly List<Analyzes> Analyzes = new();
    private readonly List<EngineGame> EngineGames = new();
    private readonly List<GameAnalyzes> GameAnalyzis = new();

    public Dictionary<string, string> AvailableEngines { get; private set; } = new Dictionary<string, string>();
    private readonly IConfiguration _configuration;

    public int CpuCoresTotal { get; private set; }
    public int CpuCoresUsed => EngineGames.Sum(s => s.CpuCoresUsed) + Analyzes.Sum(s => s.CpuCoresUsed);
    public int CpuCoresAvailable => CpuCoresTotal - CpuCoresUsed;
    private readonly ILogger<EngineService> logger;

    public EngineService(ILogger<EngineService> logger, IConfiguration configuration)
    {
        this.logger = logger;
        _configuration = configuration;
        UpdateEngines();
        CpuCoresTotal = Environment.ProcessorCount;
    }

    public void UpdateEngines()
    {
        // todo correct order !?
        AvailableEngines = _configuration.GetSection("ChessEngines").GetChildren().Reverse().ToDictionary(x => x.Key, x => x.Value);
    }

    public ICollection<Analyzes> GetAnalyzes() => Analyzes;
    public ICollection<EngineGame> GetEngineGames() => EngineGames;
    public ICollection<GameAnalyzes> GetGameAnalyses() => GameAnalyzis;

    public async Task<Analyzes> CreateAnalyzes(Game game, string? fen = null)
    {
        Analyzes analyzes = new(game, fen);
        if (AvailableEngines.Any())
        {
            Engine engine = new(AvailableEngines.First().Key, AvailableEngines.First().Value);
            await analyzes.AddEngine(engine).ConfigureAwait(false);

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
        }
        else if (AvailableEngines.Count > 1)
        {
            whiteEngine = new Engine(AvailableEngines.ElementAt(0).Key, AvailableEngines.ElementAt(0).Value);
            blackEngine = new Engine(AvailableEngines.ElementAt(1).Key, AvailableEngines.ElementAt(1).Value);
        }
        else
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
        if (engineGame == null)
        {
            throw new ArgumentNullException(nameof(engineGame));
        }
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        options.WhiteEngine = new KeyValuePair<string, string>(options.WhiteEngineName, AvailableEngines[options.WhiteEngineName]);
        options.BlackEngine = new KeyValuePair<string, string>(options.BlackEngineName, AvailableEngines[options.BlackEngineName]);
        engineGame.SetOptions(options);
    }

    public void DeleteEngineGame(EngineGame engineGame)
    {
        EngineGames.Remove(engineGame);
        engineGame?.Dispose();
    }

    public GameAnalyzes CreateGameAnalyzes(Game game, string engineName)
    {
        GameAnalyzes analyses = new(game, new KeyValuePair<string, string>(engineName, AvailableEngines[engineName]));
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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.uciChessEngine;
public class EngineService
{
    public List<Analyzes> Analyzes { get; private set; } = new List<Analyzes>();
    public List<EngineGame> EngineGames { get; private set; } = new List<EngineGame>();
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
}

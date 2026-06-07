using System.ComponentModel.DataAnnotations;

namespace pax.uciChessEngine.EngineServices;

public sealed class EngineRunOptions
{
    public const string UciEngineType = "UCI";
    public const string UciWithWeightsEngineType = "UCI + weights";

    public Guid Id { get; } = Guid.NewGuid();

    [Required]
    public string BinaryPath { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string EngineType { get; set; } = UciEngineType;
    public string? WeightsPath { get; set; }
    public string? ExtraOptions { get; set; }
    public bool IsEnabled { get; set; } = true;

    [Range(0, 256)]
    public int Threads { get; set; }

    [Range(0, 50)]
    public int Pvs { get; set; }

    [Range(0, 65536)]
    public int HashMb { get; set; }

    [Range(1, 32)]
    public int PoolSize { get; set; } = 2;

    public int IdelTimeoutMs { get; set; } = 2000;
}

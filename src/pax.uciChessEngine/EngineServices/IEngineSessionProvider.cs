namespace pax.uciChessEngine.EngineServices;

public interface IEngineSessionProvider
{
    Task<EngineLease> AcquireAsync(CancellationToken ct);
    Task CleanupIdleAsync();
    Guid Id();
    ValueTask DisposeAsync();
}

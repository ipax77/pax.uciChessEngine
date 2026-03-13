namespace pax.uciChessEngine.EngineServices;

public sealed class EngineLease : IAsyncDisposable
{
    private readonly EngineSessionProvider _provider;

    public EngineSession Session { get; }

    internal EngineLease(EngineSession session, EngineSessionProvider provider)
    {
        Session = session;
        _provider = provider;
    }

    public ValueTask DisposeAsync()
    {
        EngineSessionProvider.Release(Session);
        return ValueTask.CompletedTask;
    }
}

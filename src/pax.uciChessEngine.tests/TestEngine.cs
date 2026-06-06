namespace pax.uciChessEngine.tests;

internal static class TestEngine
{
    public static string RequirePath()
    {
        var path = Environment.GetEnvironmentVariable("UCI_ENGINE_PATH");

        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Inconclusive(
                "Set UCI_ENGINE_PATH to the path of a UCI-compatible chess engine binary.");
        }

        if (!File.Exists(path))
        {
            Assert.Inconclusive(
                $"The configured UCI_ENGINE_PATH does not exist: {path}");
        }

        return path;
    }
}

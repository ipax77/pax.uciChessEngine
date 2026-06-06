# pax.uciChessEngine tests

The parser and status tests run without an installed chess engine. Integration tests require a UCI-compatible chess engine binary and read its path from the `UCI_ENGINE_PATH` environment variable.

## Configure integration tests

Set `UCI_ENGINE_PATH` to the full path of the engine executable:

```powershell
$env:UCI_ENGINE_PATH = "C:\path\to\stockfish.exe"
```

If `UCI_ENGINE_PATH` is missing, blank, or points to a file that does not exist, integration tests are marked inconclusive instead of failed.

## Run tests

Run all tests:

```powershell
dotnet test
```

Run only integration tests:

```powershell
dotnet test --filter TestCategory=Integration
```

Run only non-integration tests:

```powershell
dotnet test --filter "TestCategory!=Integration"
```

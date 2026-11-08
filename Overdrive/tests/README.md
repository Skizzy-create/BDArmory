# Overdrive unit tests

Dependency-free tests (no NuGet, no test framework) for the pure-logic Overdrive files:
`BDArmory/Control/FireAuthority.cs` and `BDArmory/VesselSpawning/MultiSpawnLayout.cs`.

Run from the repository root (the folder that contains `BDArmory/` and `Overdrive/`):

    dotnet run --project Overdrive/tests/OverdriveTests

Prints `PASS`/`FAIL` per test and a final `N passed, M failed` line; exit code 1 if any test failed.
This project is not part of the BDArmory build (it links the source files, it does not modify them).

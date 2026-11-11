# Overdrive unit tests

Dependency-free tests (no NuGet, no test framework) for the pure-logic Overdrive files:
`BDArmory/Control/FireAuthority.cs`, `BDArmory/Control/GuardFireLogic.cs` (guard mode missile fire: launch flag,
quotas, weapon type readiness, point defence retries), `BDArmory/VesselSpawning/MultiSpawnLayout.cs`, and the perf probe
and perf logic (`BDArmory/Utils/PerfProbe.cs`, `BDArmory/Utils/PerfLogic.cs`; see `Overdrive/PERF.md`).

Run from the repository root (the folder that contains `BDArmory/` and `Overdrive/`):

    dotnet run --project Overdrive/tests/OverdriveTests

Prints `PASS`/`FAIL` per test and a final `N passed, M failed` line; exit code 1 if any test failed.
This project is not part of the BDArmory build (it links the source files, it does not modify them).

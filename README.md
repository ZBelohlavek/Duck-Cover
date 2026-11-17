# Duck and Cover

An original 5v5 attack-and-defend shooter where the building remembers every round. Walls that break
stay broken for the whole match, teams swap sides every round, and eliminated players keep helping
their team through cameras, scout bots and pings.

Unity 6 (6000.3) with URP, the Input System and Netcode for GameObjects.

## Getting started

1. Open the folder in Unity Hub with Unity 6000.3. The first open downloads the packages.
2. Open `Assets/DuckAndCover/Scenes/Boot.unity` and press Play. You should see a small greybox room
   and a "booted" line in the console.

## Checking work

```
dotnet run --project Tests/CoreSim        # rule tests, no Unity needed
dotnet build Tests/UnityCompileCheck      # compiles the Unity scripts against stubs
Tools/unity.ps1 check                     # real Unity compile (editor closed)
Tools/unity.ps1 capture                   # screenshots + log in Screenshots/
```

See `CLAUDE.md` for the project rules and `docs/ROADMAP.md` for what is being built.

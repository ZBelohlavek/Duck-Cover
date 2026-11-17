# Roadmap

The base game is split into 11 work packages. Each is sized for one thread and ends in one pull
request. The full scope and "done when" of each is in the plan:
https://claude.ai/artifact/JJ4fD74nbDB3AsykT2WWen

| Package | Kind | Depends on | Checked by | Status |
|---|---|---|---|---|
| WP0 Project bootstrap | Scripts | Repo | Unity compile check | Done |
| WP1 Match and zone rules | Scripts, Core only | Contracts | dotnet tests | Done |
| WP2 Destruction and breach rules | Scripts, Core only | Contracts | dotnet tests, balance simulation | Ready |
| WP3 Network foundation | Scripts | WP0, WP1 | Two-instance smoke test | In review (smoke test passed in Unity) |
| WP4 Player and weapons | Scripts | WP0, WP3 (and WP7's health rules) | dotnet tests, smoke test, screenshots | In review (cloud checks pass; real Unity check and capture not run yet) |
| WP5 Breakable surfaces in Unity | Scripts | WP2, WP3 | Smoke test grid checksum, screenshots | Waiting |
| WP6 Round presentation and objective | Scripts | WP1, WP3, WP4 | Smoke test plays a full round | Waiting |
| WP7 Gadgets on the ability pipeline | Scripts | WP2, WP4, WP5 | Tests per gadget, smoke test | Waiting |
| WP8 Backline | Scripts | WP3, WP4, WP5 | Tests for energy and actions, smoke test | Waiting |
| WP9 First map greybox | Scripts and layout | WP6 (zones answered by the map itself for now) | Map validator, screenshots | In review (cloud checks pass; real Unity capture not run yet) |
| WP10 Asset search and import | Models | None | Asset manifest reviewed by Zach | In progress |

## Where each package puts its code

- WP1: `Core/Match`, `Core/Ruleset`, `Core/Zones`, `Core/Spawn`, `Core/Events`, mode data in `Data/Modes`; tests in
  `MatchTests.cs`, `ZoneTests.cs`, `SpawnTests.cs` and `ModeDataTests.cs` (helpers in `MatchHarness.cs`).
- WP2: `Core/Destruction`, `Core/Breach`; tests in `DestructionTests.cs` and `BreachTests.cs`; the
  worst-case simulation as a CoreSim report (`dotnet run --project Tests/CoreSim -- report breach`).
- WP3: `Core/Session` (the session itself, no network library: `SessionHost`, `SessionClient`, the wire
  format, scripted play), `Scripts/Net` (`NetSession`: Netcode for GameObjects and Unity Relay as a pipe
  for it), `Game/SessionRunner`, `SessionDebugPanel` and `NetSmoke`; tests in `SessionTests.cs` and
  `WireTests.cs`; `Tools/netsmoke` (processes over TCP, no Unity) and `Tools/unity netsmoke` (two built
  players over Netcode).
- WP4: rules in `Core/Combat` beside WP7's health rules (`MovementRules`, `MoveMath`, `Hitboxes`,
  `HitHistory` and `CombatWorld` for lag compensation, `WeaponRules`, `WeaponDefs`, and `CombatServer`,
  which puts abilities, movement and weapons together for a host); weapon data in `Data/Weapons`; Unity
  side in `Game` (`LocalPlayer`, `CombatView`, `PlaceholderBody`, `PhysicsWorld`, `Paint`, the test arena
  in `BootRoom`) and `UI` (`UIKit`, `Hud`); tests in `HitboxTests.cs`, `MovementTests.cs`,
  `WeaponTests.cs` and `CombatSessionTests.cs` (helpers in `CombatHarness.cs`).
- WP9: the map as data in `Core/Maps` (`MapDef`, the layout in `GantryYard`, `MapNav` and `MapValidator`
  for the fairness checks); `Game/MapScene` builds the greybox and `Game/MapView` shows zone owners and
  the shift; tests and the `report map` plan in `MapTests.cs`.
- WP4 to WP8: `Scripts/Game` and `Scripts/UI`, with their rules in Core folders of their own
  (`Core/Combat`, `Core/Abilities`, `Core/Backline`).

## Playable milestones

1. Two players move and shoot each other over the network in an empty room (WP0, WP3, WP4).
2. A full round with plant and defuse (WP1, WP6).
3. Walls break, stay broken and hit their limits (WP2, WP5, WP7).
4. Dead players matter (WP8).
5. First real map with art, ready for a playtest with friends (WP9, WP10).

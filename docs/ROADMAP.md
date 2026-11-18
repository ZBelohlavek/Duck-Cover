# Roadmap

The game is a 5v5 hero shooter with MOBA-lite modes. The work is split into packages, each sized for one
thread and ending in one pull request. The base plan, with every package's scope and "done when":
https://claude.ai/artifact/JJ4fD74nbDB3AsykT2WWen. The competitive modes plan (Overrun, King of the Hill,
Flag Rush; the Breakwater map; WP14 to WP21), approved Nov 18:
https://claude.ai/artifact/7qbJG2ukrZDQ6ZTfX3nXGs

## Modes

- **Overrun:** one-lane push on Breakwater. Scrapper waves every 30 s; destroy the enemy Forward Bastion,
  then the Core Bastion. Minions always damage Bastions; heroes only while their own Scrappers are within
  12 m. Seven jungle camps (one Juggernaut, two damage, two experience, two speed), launch pads, ledges.
- **King of the Hill:** one moving hill, on Gantry Yard (built in WP1 and WP9).
- **Flag Rush:** one flag at a random fair spot, carried into the enemy base; first to 5, no minions.
- **Capture and Hold:** built, kept as a back-pocket mode (three zones are a lot for 5v5).

## Packages

| Package | Kind | Depends on | Checked by | Status |
|---|---|---|---|---|
| WP0 Project bootstrap | Scripts | Repo | Unity compile check | Done |
| WP1 Match and zone rules | Core | Contracts | dotnet tests | Done |
| WP2 Breakable cover rules | Core | Contracts | dotnet tests | After the first playtest |
| WP3 Network foundation | Scripts | WP0, WP1 | Netsmoke | Done |
| WP4 Player and weapons | Scripts | WP0, WP3 | Tests, netsmoke, screenshots | Done |
| WP5 Breakable cover in Unity | Scripts | WP2, WP3 | Netsmoke, screenshots | After the first playtest |
| WP6 Match presentation and zones | Scripts | WP1, WP3, WP4 | Netsmoke plays a full match | Waiting |
| WP7 Ability and effect rules | Core | Contracts | dotnet tests per effect | Done |
| WP8 Progression rules (now 20 levels) | Core | Contracts | dotnet tests, balance report | Done |
| WP9 First map, Gantry Yard | Layout | WP6 | Map validator, screenshots | Done |
| WP10 Asset search and import | Models | None | Asset manifest reviewed by Zach | In progress |
| WP11 Hero kits | Scripts and data | WP4, WP7, WP8 | Tests per hero, netsmoke | Done (Lancer reworked; other three paths to come) |
| WP12 Progression and hero UI | Scripts | WP6, WP8 | Screenshots, netsmoke | Waiting |
| WP13 Balance pass and playtest build | Scripts | All above | Simulation report, a played match | Waiting |
| WP14 Units (combat contract moves to unit ids) | Core | Merged combat and abilities | Weapons, abilities and statuses hit a test unit as they hit a hero; hero kit tests pass | Ready, start first |
| WP21 Flag Rush | Core, map data, Unity | Merged match rules, Gantry Yard | Flag rules tested; fair flag spots pass the validator; netsmoke flag match; capture | Ready, start first |
| WP15 Lanes, waves and camps | Core | WP14 | Cloud wave report; all four camp kinds tested | Waiting |
| WP18 Breakwater greybox, launch pads and ledges | Layout and Core | WP14's lane fields | Validator (mirror symmetry, lane, one-way ledges and drop, jungle joins the lane at both ends); map plan | Waiting |
| WP20 Hero kits against waves | Data and tests | WP14 | Each hero has a tested way to clear or protect a wave | Waiting |
| WP16 Overrun rules | Core | WP15 | Overrun from data; bot matches end inside 15 minutes | Waiting |
| WP19 Overrun in Unity | Scripts | WP16, WP18 | Two players over the network; netsmoke with 40 units; capture | Waiting |

WP17 (Salvage and build pads) was dropped on Nov 18.

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
- Environments: themes in `Game/Env` (`EnvTheme`, `PaintedTextures`, `EnvMesh`, `SkyPainter`,
  `MapDresser`), chosen per layout by `MapDef.theme`; see `docs/ENVIRONMENTS.md`.
- WP14 to WP21: units, lanes, waves, camps and Bastions in Core folders of their own (`Core/Units`,
  `Core/Lanes`, `Core/Overrun`, `Core/Flag`), Breakwater beside Gantry Yard in `Core/Maps`, mode data in
  `Data/Modes`; the Unity side in `Game` and `UI`.

## Playable milestones

1. Two players move and shoot each other over the network (WP0, WP3, WP4). Done.
2. Heroes with abilities and levels in a King of the Hill match on Gantry Yard (WP1, WP7, WP8, WP9, WP11, WP6, WP12).
3. Flag Rush on Gantry Yard (WP21).
4. Overrun on Breakwater with waves, Bastions and camps (WP14, WP15, WP16, WP18, WP19, WP20).
5. Art and balance, ready for a playtest with friends (WP10, WP13).

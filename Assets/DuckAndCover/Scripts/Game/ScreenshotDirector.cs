using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Plays through the shot list, saves a PNG of each, and writes log.txt with every warning and
    /// error from the run. Lets Claude (or anyone) check the game's look without clicking through
    /// it. Started by "Duck and Cover > Capture Screenshots" or Tools/unity capture.
    /// When you add a screen or a scene worth checking, add a shot to <see cref="Shots"/>.
    /// </summary>
    public class ScreenshotDirector : MonoBehaviour
    {
        public string outputDirectory;

        const int Width = 1920;
        const int Height = 1080;
        const float TimeoutSeconds = 300f;

        /// <summary>One screenshot: set the game up, wait, then save name.png.</summary>
        public struct Shot
        {
            public string name;
            public Action setup;
            public float wait;

            public Shot(string name, Action setup, float wait) { this.name = name; this.setup = setup; this.wait = wait; }
        }

        /// <summary>The shot list, in order. Names start with a number so the files sort.</summary>
        static List<Shot> Shots() => new List<Shot>
        {
            // The whole map from above team A's end, then the hall shutter before and after the shift.
            new Shot("01-map", null, 1.5f),
            new Shot("02-shutter-shut", () => LookAt(new Vector3(-14f, 2.5f, 1f), new Vector3(-14f, 1.5f, -14f)), 0.4f),
            new Shot("03-shutter-open", () => MapView.PreviewShift = true, MapView.ShiftSeconds + 0.6f),
            new Shot("04-hall-inside", () => LookAt(new Vector3(-14f, 1.7f, -21f), new Vector3(-14f, 1.4f, -6f)), 0.4f),
            new Shot("05-middle", () => { MapView.PreviewShift = false; LookAt(new Vector3(-22f, 4.3f, 2f), new Vector3(0f, 0.5f, 0f)); }, MapView.ShiftSeconds + 0.6f),
            // Host a match alone with three practice dummies on the other team, and wait for play to start.
            new Shot("05b-pick", HostPracticeMatch, 1.5f),
            new Shot("06-spawn", StartPracticeMatch, 5.5f),
            new Shot("07-dummies", () => Stand(new Vec3(-4f, 0f, -8.5f)), 1.2f),
            new Shot("08-after-shots", () => Shoot(12), 1.6f),
            new Shot("09-bodies", () => WatchFrom(new Vector3(-2.5f, 2.2f, 4f), new Vector3(1.5f, 1f, 9f)), 0.5f),
            // Hero kits (WP11): the dummies are given heroes and use their abilities toward the player.
            new Shot("10-abilities", DummiesUseAbilities, 1.6f),
            new Shot("11-hush", DummyThrowsHush, 1.0f),
            new Shot("11b-war-leap", DummyWarLeaps, 1.4f),
            new Shot("12-hero-hud", LevelUpAndLook, 1.2f),
            // The soldier (Lancer) model: his rifle in first person, then firing, then a dummy shown as a Lancer
            // from the player's eyes, then every clip on a lineup away from the map (studio floor).
            new Shot("12b-fp-rifle", () => { HideHud = true; }, 0.5f),
            new Shot("12c-fp-fire", FirstPersonBurst, 0.12f),
            new Shot("12d-lancer-enemy", DummyBecomesLancer, 1.2f),
            new Shot("12e-lancer-moves", () => LancerLineup(0), 0.6f),
            new Shot("12f-lancer-actions", () => LancerLineup(1), 0.6f),
            new Shot("12g-lancer-close", () => LancerLineup(2), 0.6f),
            // Environment kit: the lane mock-up, built far off to one side of the real map.
            new Shot("13-lane", () => LaneShot(new Vector3(0f, 1.8f, -50f), new Vector3(0f, 1.2f, -38f)), 1.0f),
            new Shot("14-lane-gap", () => LaneShot(new Vector3(1.5f, 1.8f, -41f), new Vector3(-16f, 1.2f, -50f)), 0.6f),
            new Shot("15-lane-above", () => LaneShot(new Vector3(0f, 55f, -62f), new Vector3(0f, 0f, -10f)), 0.6f),
            // Flag Rush (WP21): a new match against two dummies; the flag, a dummy carrying it, then the player.
            new Shot("16-flag-spawn", StartFlagMatch, 6f),
            new Shot("17-flag-up", WatchTheFlag, 0.6f),
            new Shot("18-flag-carrier", DummyTakesTheFlag, 1.0f),
            new Shot("19-flag-mine", PlayerTakesTheFlag, 1.2f),
            // Breakwater (WP18), the Overrun map, built far off to the other side. No HUD over these.
            new Shot("20-bw-above", () => BreakwaterShot(new Vector3(-112f, 100f, -2f), new Vector3(4f, 0f, 0f)), 1.2f),
            new Shot("21-bw-base", () => BreakwaterShot(new Vector3(-45f, 40f, -114f), new Vector3(-12f, 0f, -70f)), 0.6f),
            new Shot("22-bw-lane", () => BreakwaterShot(new Vector3(-19.5f, 2f, -52f), new Vector3(9f, 1.2f, -31.5f)), 0.6f),
            new Shot("23-bw-ledge", () => BreakwaterShot(new Vector3(-37.5f, 4.7f, -57f), new Vector3(-9f, 0.5f, -42f)), 0.6f),
            new Shot("24-bw-gap", () => BreakwaterShot(new Vector3(6f, 1.8f, -40f), new Vector3(-21f, 1.4f, -29f)), 0.6f),
            new Shot("25-bw-shelf", () => BreakwaterShot(new Vector3(-33f, 5.7f, 5f), new Vector3(9f, 0.5f, -1f)), 0.6f),
            new Shot("26-bw-mid", () => BreakwaterShot(new Vector3(24f, 1.8f, -26f), new Vector3(9f, 2.5f, 4f)), 0.6f),
            new Shot("27-bw-jungle", () => { BreakwaterShot(new Vector3(12f, 1.8f, -87f), new Vector3(39f, 1.2f, -78f)); JungleScaleFigures(); }, 0.6f),
            new Shot("27b-bw-cover", () => { BreakwaterShot(new Vector3(-21f, 1.7f, -64f), new Vector3(-20.5f, 0.9f, -76f)); CoverScaleFigures(); }, 0.6f),
            new Shot("28-bw-camps", () => BreakwaterShot(new Vector3(30f, 38f, -104f), new Vector3(36f, 0f, -78f)), 0.6f),
            // Overrun on Breakwater (WP19): a real match, so the map is built in place of Gantry Yard. The
            // HUD with the Bastion cards, the first wave on its way, the waves meeting at mid-lane (match time
            // runs five times faster while it waits), the player behind their wave, and team B's Forward
            // Bastion knocked down so its side gate opens.
            new Shot("29-ov-hud", StartOverrunMatch, 6f),
            new Shot("30-ov-wave", WatchTheWave, 0.8f),
            new Shot("31-ov-clash", WatchTheClash, 5.5f),
            new Shot("32-ov-lane", PlayerJoinsTheWave, 1.2f),
            new Shot("33-ov-gate", KnockDownForwardB, MapView.ShiftSeconds + 1f),
        };

        /// <summary>True while a shot wants the world without the HUD over it (the UI reads it).</summary>
        public static bool HideHud;
        /// <summary>True while a shot wants the HUD without the upgrade (trait) panel, which would cover the scene.</summary>
        public static bool HideTraits;

        /// <summary>Where Breakwater is built for its shots: away from the real map and the lane mock-up.</summary>
        static readonly Vector3 BreakwaterOffset = new Vector3(3000f, 0f, 0f);
        static GameObject breakwaterPreview;

        static void BreakwaterShot(Vector3 position, Vector3 target)
        {
            if (breakwaterPreview == null) breakwaterPreview = MapScene.BuildPreview(Breakwater.Build(), BreakwaterOffset);
            HideHud = true;
            LookAt(position + BreakwaterOffset, target + BreakwaterOffset);
            WatchFrom(position + BreakwaterOffset, target + BreakwaterOffset);
        }

        /// <summary>
        /// Two stand-ins on the long jungle trail in shot 27, side by side across it, so the trail's width can be
        /// judged: one as wide as Bulwark's model (2.4 m at the shoulders, red) and one hero-sized (1 m, navy).
        /// Art only, no colliders.
        /// </summary>
        static bool scaleFigures;

        static void JungleScaleFigures()
        {
            if (breakwaterPreview == null || scaleFigures) return;
            scaleFigures = true;
            // On the trail from (21, -86) to (39, -76): its midpoint, the direction along it and across it.
            var mid = new Vector3(30f, 0f, -81f);
            var along = new Vector3(18f, 0f, 10f).normalized;
            var across = new Vector3(-along.z, 0f, along.x);
            float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            var tank = Paint.Block(breakwaterPreview.transform, "ScaleTank", mid - across * 1.6f + Vector3.up * 1.1f, new Vector3(2.4f, 2.2f, 1.2f), new Color(0.6f, 0.08f, 0.08f), false);
            tank.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var hero = Paint.Block(breakwaterPreview.transform, "ScaleHero", mid + across * 1.2f + Vector3.up * 0.925f, new Vector3(1f, 1.85f, 0.6f), new Color(0.12f, 0.18f, 0.35f), false);
            hero.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Shot 27b, what an attacker coming down the lane sees at standing eye height: hero stand-ins at the rules'
        /// body sizes (Ruleset: 1.8 m standing, 1.2 m crouched, eyes 1.62 / 1.05 m as yellow marks) taking cover
        /// near the Core. Behind the low wall LaneLowCoreWest (1.25 m) one crouches (hidden) and one stands (head
        /// and eyes over the wall, ready to shoot); behind the crate stack LaneStackMiddle (2.6 m) one stands (hidden);
        /// one stands in the open for comparison. Art only, no colliders.
        /// </summary>
        static bool coverFigures;

        static void CoverScaleFigures()
        {
            if (breakwaterPreview == null || coverFigures) return;
            coverFigures = true;
            var r = Ruleset.Default;
            var navy = new Color(0.12f, 0.18f, 0.35f);
            var teal = new Color(0.1f, 0.45f, 0.5f);
            var eye = new Color(1f, 0.85f, 0.1f);
            void Figure(string name, Vector3 feet, float height, float eyeHeight, Color c)
            {
                Paint.Block(breakwaterPreview.transform, name, feet + Vector3.up * (height * 0.5f), new Vector3(0.7f, height, 0.5f), c, false);
                // The eye mark faces the camera (toward +z, up the lane).
                Paint.Block(breakwaterPreview.transform, name + "Eye", feet + new Vector3(0f, eyeHeight, 0.25f), new Vector3(0.4f, 0.1f, 0.12f), eye, false);
            }
            Figure("CoverCrouched", new Vector3(-24.7f, 0f, -79.75f), r.crouchHeight, r.crouchEyeHeight, teal);
            Figure("CoverStanding", new Vector3(-23.3f, 0f, -79.75f), r.standHeight, r.eyeHeight, navy);
            Figure("CoverStandingCrate", new Vector3(-18f, 0f, -71.4f), r.standHeight, r.eyeHeight, navy);
            Figure("OpenStanding", new Vector3(-19.5f, 0f, -78f), r.standHeight, r.eyeHeight, navy);
        }

        /// <summary>Where the lane mock-up is built: far enough that neither map shows in the other's shots.</summary>
        static readonly Vector3 LaneOffset = new Vector3(0f, 0f, 1500f);
        static GameObject lanePreview;

        static void LaneShot(Vector3 position, Vector3 target)
        {
            if (lanePreview == null) lanePreview = MapScene.BuildPreview(LaneStudy.Build(), LaneOffset);
            HideHud = true;
            LookAt(position + LaneOffset, target + LaneOffset);
            WatchFrom(position + LaneOffset, target + LaneOffset);
        }

        /// <summary>Host Flag Rush alone against two practice dummies, with the first flag up a second into play.</summary>
        static void StartFlagMatch()
        {
            var rules = Ruleset.Default;
            rules.heroPickSeconds = 1.5f;
            rules.deploySeconds = 1.5f;
            rules.flagFirstSpawnSeconds = 1f;
            rules.flagDropResetSeconds = 60f; // the dropped flag stays put for the last shot
            var runner = SessionRunner.Ensure();
            // The practice match from shot 06 still holds its port: Netcode shuts its transport down on a
            // later frame and the old NetworkManager is destroyed at the end of this one. Leave first, give
            // both a moment, and host on the next port, so the new host never meets the old socket.
            runner.Leave();
            runner.StartCoroutine(HostFlagMatchSoon(runner, rules));
        }

        static IEnumerator HostFlagMatchSoon(SessionRunner runner, Ruleset rules)
        {
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.25f);
            runner.Host("flag_rush", "Capture", false, runner.Settings.defaultPort + 1, false, rules);
            var session = runner.Session;
            if (session == null || session.Client == null) { Debug.LogError("Duck and Cover: the capture could not host Flag Rush."); yield break; }
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            if (local != null) { local.scripted = true; local.freeCamera = false; }
            session.Client.Welcomed += welcome =>
            {
                for (int i = 0; i < 2; i++) session.Client.Send(new AddDummyCommand { team = TeamId.B });
                session.Client.Send(new PickHeroCommand { heroId = "lancer" });
                session.Client.Send(new StartMatchCommand());
            };
        }

        static FlagState ServerFlag()
        {
            var runner = SessionRunner.Instance;
            if (runner == null || runner.Flag == null) { Debug.LogError("Duck and Cover: the capture has no Flag Rush match."); return null; }
            return runner.Flag.State;
        }

        /// <summary>From above and behind team A's half, looking at the flag where it appeared.</summary>
        static void WatchTheFlag()
        {
            var f = ServerFlag();
            if (f == null) return;
            if (f.status != FlagStatus.AtSpot) Debug.LogError("Duck and Cover: the capture expected the flag at its spot, it is " + f.status + ".");
            var at = f.position.ToVector3();
            WatchFrom(at + new Vector3(-9f, 9f, -14f), at + Vector3.up);
        }

        /// <summary>The first dummy steps onto the flag (a server teleport) and carries it.</summary>
        static void DummyTakesTheFlag()
        {
            var runner = SessionRunner.Instance;
            var f = ServerFlag();
            if (f == null) return;
            int dummy = CombatServer.FirstDummyId;
            var at = f.position;
            runner.Combat.Movement.Teleport(dummy, at, 200f);
            WatchFrom(at.ToVector3() + new Vector3(-6f, 4f, -7f), at.ToVector3() + Vector3.up * 1.5f);
            CombatView.Instance.StartCoroutine(Expect(() => f.IsCarrier(dummy), 0.5f, "a practice dummy to carry the flag"));
        }

        /// <summary>The carrier goes down, and the player picks the dropped flag up: back to first person, facing the enemy base.</summary>
        static void PlayerTakesTheFlag()
        {
            var runner = SessionRunner.Instance;
            var f = ServerFlag();
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            if (f == null || local == null) return;
            int me = runner.Client.PlayerId;
            runner.Match.EliminatePlayer(CombatServer.FirstDummyId, me);
            var at = f.position;
            runner.Combat.Movement.Teleport(me, at, 0f);
            local.freeCamera = false;
            local.LookAt(0f, 4f);
            HideTraits = true; // the levels from the flag would put an upgrade offer over the view
            CombatView.Instance.StartCoroutine(Expect(() => f.IsCarrier(me), 0.6f, "the player to carry the flag"));
        }

        // ---- Overrun (WP19) ----

        /// <summary>Host Overrun alone against two practice dummies on Breakwater; the first wave walks at the start of play.</summary>
        static void StartOverrunMatch()
        {
            var rules = Ruleset.Default;
            rules.heroPickSeconds = 1.5f;
            rules.deploySeconds = 1.5f;
            var runner = SessionRunner.Ensure();
            runner.Speed = 1f;
            // As for Flag Rush: let the last match's socket go, and host on a port of its own.
            runner.Leave();
            runner.StartCoroutine(HostOverrunSoon(runner, rules));
        }

        static IEnumerator HostOverrunSoon(SessionRunner runner, Ruleset rules)
        {
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.25f);
            runner.Host("overrun", "Capture", false, runner.Settings.defaultPort + 2, false, rules);
            var session = runner.Session;
            if (session == null || session.Client == null) { Debug.LogError("Duck and Cover: the capture could not host Overrun."); yield break; }
            if (MapScene.Current == null || MapScene.Current.id != Breakwater.Id) Debug.LogError("Duck and Cover: Overrun did not build Breakwater.");
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            if (local != null) { local.scripted = true; local.freeCamera = false; }
            session.Client.Welcomed += welcome =>
            {
                for (int i = 0; i < 2; i++) session.Client.Send(new AddDummyCommand { team = TeamId.B });
                session.Client.Send(new PickHeroCommand { heroId = "lancer" });
                session.Client.Send(new StartMatchCommand());
            };
            HideTraits = true;
        }

        static bool OverrunRunning(out SessionRunner runner)
        {
            runner = SessionRunner.Instance;
            if (runner != null && runner.Overrun != null && runner.Lanes != null && runner.Combat != null) return true;
            Debug.LogError("Duck and Cover: the capture has no Overrun match running.");
            return false;
        }

        /// <summary>The living minion of a team furthest toward the enemy base (team A pushes toward +z), or null.</summary>
        static UnitState FrontMinion(SessionRunner runner, TeamId team)
        {
            UnitState best = null;
            foreach (var u in runner.Combat.Units.Living())
            {
                if (u.team != team || !runner.Overrun.Rules.IsMinion(u.typeId)) continue;
                float ahead = team == TeamId.A ? u.position.z : -u.position.z;
                if (best == null || ahead > (team == TeamId.A ? best.position.z : -best.position.z)) best = u;
            }
            return best;
        }

        /// <summary>From behind and above team A's first wave, looking down the lane ahead of it.</summary>
        static void WatchTheWave()
        {
            if (!OverrunRunning(out var runner)) return;
            HideHud = true;
            var front = FrontMinion(runner, TeamId.A);
            if (front == null) { Debug.LogError("Duck and Cover: the capture found no minions on the lane."); return; }
            var at = front.position.ToVector3();
            WatchFrom(at + new Vector3(-3f, 3.5f, -9f), at + new Vector3(0f, 0.8f, 4f));
        }

        /// <summary>Match time runs faster until the waves meet at mid-lane; watched from above the lane on A's side.</summary>
        static void WatchTheClash()
        {
            if (!OverrunRunning(out var runner)) return;
            HideHud = true;
            runner.Speed = 5f;
            WatchFrom(new Vector3(19.5f, 10f, -24f), new Vector3(15f, 0f, 0f));
            CombatView.Instance.StartCoroutine(Expect(() => runner.Combat.Units.Living().Count >= 12, 5f, "both waves on the lane (12 or more units)"));
        }

        /// <summary>Back to normal speed, and the player stands behind team A's front minion, facing down the lane, HUD up.</summary>
        static void PlayerJoinsTheWave()
        {
            if (!OverrunRunning(out var runner)) return;
            runner.Speed = 1f;
            HideTraits = true;
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            var front = FrontMinion(runner, TeamId.A);
            if (local == null || front == null) { Debug.LogError("Duck and Cover: the capture could not put the player on the lane."); return; }
            var feet = front.position + new Vec3(-1.5f, 0f, -5f);
            runner.Combat.Movement.Teleport(runner.Client.PlayerId, feet, 0f);
            local.freeCamera = false;
            local.LookAt(0f, 6f);
        }

        /// <summary>
        /// Team B's Forward Bastion falls (a server-side hit from one of A's minions, which may always hurt
        /// it), so A scores and B's side gate rolls up; watched from the side route outside that gate.
        /// </summary>
        static void KnockDownForwardB()
        {
            if (!OverrunRunning(out var runner)) return;
            HideTraits = true;
            var b = runner.Overrun.State.Bastion(TeamId.B, BastionTier.Forward);
            var minion = FrontMinion(runner, TeamId.A);
            if (b == null || minion == null) { Debug.LogError("Duck and Cover: the capture could not find B's Forward Bastion and an A minion."); return; }
            for (int i = 0; i < 10 && runner.Combat.Units.IsAlive(b.unitId); i++)
                runner.Combat.Damage.Damage(minion.id, b.unitId, 10000f, LaneRuleset.AttackId);
            WatchFrom(new Vector3(-54f, 2.4f, 84f), new Vector3(-18f, 2f, 84f));
            CombatView.Instance.StartCoroutine(Expect(() => !b.alive && runner.Overrun.State.gateOpenB && runner.Match.State.scoreA == 1, 1f,
                "B's Forward Bastion down, B's side gate open and team A on 1"));
        }

        /// <summary>Log an error if <paramref name="ok"/> doesn't hold after <paramref name="seconds"/>.</summary>
        static IEnumerator Expect(Func<bool> ok, float seconds, string what)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (!ok()) Debug.LogError("Duck and Cover: the capture expected " + what + ".");
        }

        static readonly string[] DummyHeroes = { "bulwark", "rigger", "mender" };

        /// <summary>Give each practice dummy a hero (server side) and have it use an ability toward the player.</summary>
        static void DummiesUseAbilities()
        {
            var runner = SessionRunner.Instance;
            if (runner == null || runner.Heroes == null || runner.Client == null) { Debug.LogError("Duck and Cover: the capture has no hero server."); return; }
            int me = runner.Client.PlayerId;
            int used = 0;
            for (int i = 0; i < DummyHeroes.Length; i++)
            {
                int dummy = CombatServer.FirstDummyId + i;
                if (!runner.Heroes.GiveKit(dummy, DummyHeroes[i]).IsAccepted) continue;
                // Bulwark: turns to the player, raises Rampart and sends out Patch Drone. Rigger: Combat Stim
                // (Cloak would hide him from the shot). Mender: a field near its feet.
                if (i == 0) FaceTarget(runner, dummy, me);
                if (DummyCast(runner, dummy, i == 1 ? 1 : 0, me, i == 0 ? 0f : i == 1 ? 0f : -0.6f)) used++;
                if (i == 0 && DummyCast(runner, dummy, 1, me, -0.5f)) used++;
            }
            // The player's Lancer marks the first dummy (the camera already faces it).
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            if (local != null) local.UseAbility(1, "lancer_mark");
            if (used < 4) Debug.LogError("Duck and Cover: the capture's dummies used " + used + " abilities, expected 4.");
            WatchFrom(new Vector3(-8f, 6f, -14f), new Vector3(-1f, 0f, 2f));
        }

        /// <summary>Turn a dummy (server side) to face a player, so what it holds up faces them.</summary>
        static void FaceTarget(SessionRunner runner, int dummy, int targetId)
        {
            var from = runner.Combat.Movement.Motion(dummy);
            var to = runner.Combat.Movement.Motion(targetId);
            if (from == null || to == null) return;
            Vec3 d = to.position - from.position;
            from.yaw = (float)(Math.Atan2(d.x, d.z) * 180.0 / Math.PI);
        }

        /// <summary>The Rigger dummy throws Hush Grenade at the player: the HUD says SILENCED for the next shots.</summary>
        static void DummyThrowsHush()
        {
            var runner = SessionRunner.Instance;
            if (runner == null || runner.Heroes == null || runner.Client == null) return;
            int rigger = CombatServer.FirstDummyId + 1;
            int me = runner.Client.PlayerId;
            var mine = runner.Combat.Movement.Motion(me);
            if (mine == null) return;
            // Step the Rigger to 7 m in front of the player (the grenade reaches 25 m and the dummies stand
            // further off), facing back at them, and watch both from the side.
            Vec3 ahead = MoveMath.Forward(mine.yaw);
            Vec3 at = mine.position + ahead * 7f;
            runner.Combat.Movement.Teleport(rigger, at, mine.yaw + 180f);
            Vec3 side = MoveMath.Right(mine.yaw);
            WatchFrom((mine.position + ahead * 3.5f + side * 7f).ToVector3() + Vector3.up * 3f, (mine.position + ahead * 3.5f).ToVector3() + Vector3.up * 1f);
            if (!DummyCast(runner, rigger, 2, me, 0f)) Debug.LogError("Duck and Cover: the capture's Rigger could not throw Hush Grenade.");
            CombatView.Instance.StartCoroutine(Expect(() => runner.Combat.Abilities.IsSilenced(me), 0.5f, "the player silenced by Hush Grenade"));
        }

        /// <summary>
        /// The Rigger dummy becomes the war chief and War Leaps at the player from 10 m: he lands in front of
        /// them and knocks them up (the HUD says STUNNED), then swings his greataxe, seen through the player's eyes.
        /// </summary>
        static void DummyWarLeaps()
        {
            var runner = SessionRunner.Instance;
            if (runner == null || runner.Heroes == null || runner.Client == null) return;
            int chief = CombatServer.FirstDummyId + 1;
            int me = runner.Client.PlayerId;
            var mine = runner.Combat.Movement.Motion(me);
            if (mine == null) return;
            if (!runner.Heroes.GiveKit(chief, "warchief").IsAccepted) { Debug.LogError("Duck and Cover: the capture could not give a dummy the war chief's kit."); return; }
            runner.Combat.Weapons.SetLoadout(chief, runner.Combat.Weapons.Library.LoadoutFor("warchief"));
            Vec3 ahead = MoveMath.Forward(mine.yaw);
            runner.Combat.Movement.Teleport(chief, mine.position + ahead * 10f, mine.yaw + 180f);
            Vec3 side = MoveMath.Right(mine.yaw);
            WatchFrom((mine.position + ahead * 5f + side * 9f).ToVector3() + Vector3.up * 3f, (mine.position + ahead * 4f).ToVector3() + Vector3.up * 1f);
            if (!DummyCast(runner, chief, 0, me, 0f)) Debug.LogError("Duck and Cover: the capture's war chief could not War Leap.");
            CombatView.Instance.StartCoroutine(SwingAfter(runner, chief, me, 0.8f));
            CombatView.Instance.StartCoroutine(Expect(() => runner.Combat.Abilities.IsStunned(me), 1.0f, "the player knocked up by War Leap"));
        }

        /// <summary>After the leap lands, the dummy (server side) swings its greataxe at the player.</summary>
        static IEnumerator SwingAfter(SessionRunner runner, int dummy, int targetId, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            var rules = runner.Match.Rules;
            var from = runner.Combat.Movement.Motion(dummy);
            var to = runner.Combat.Movement.Motion(targetId);
            if (from == null || to == null) yield break;
            // A dummy has no machine of its own to fly the leap's arc, so put it where the leap lands.
            Vec3 back = from.position - to.position;
            back = new Vec3(back.x, 0f, back.z);
            if (back.Magnitude > 1e-3f) runner.Combat.Movement.Teleport(dummy, to.position + back.Normalized * 1.8f, from.yaw);
            // The player has no body of their own on screen, so look out of their eyes at him instead of from the side.
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            if (local != null && back.Magnitude > 1e-3f)
            {
                local.freeCamera = false;
                local.LookAt((float)(Math.Atan2(back.x, back.z) * 180.0 / Math.PI), 8f);
            }
            // Give the lag-compensation history a few ticks of the dummy in its new spot, and every view time to draw it there.
            yield return new WaitForSecondsRealtime(0.25f);
            if (!runner.Match.State.IsAlive(dummy) || !runner.Match.State.IsAlive(targetId)) yield break;
            Vec3 eye = from.position + Vec3.Up * rules.eyeHeight;
            var result = runner.Combat.Apply(new FireCommand
            {
                playerId = dummy, weaponId = "greataxe", origin = eye, tick = runner.Match.State.tick,
                direction = (Hitboxes.Chest(rules, to.Pose(true)) - eye).Normalized,
            });
            if (!result.IsAccepted) Debug.LogWarning("Duck and Cover: the capture's war chief could not swing: " + result.reason);
            else Debug.Log("Duck and Cover: the capture's war chief swung from " + Vec3.Distance(from.position, to.position).ToString("0.0") + " m; the player has " + runner.Combat.Damage.Unit(targetId).health.ToString("0") + " health.");
        }

        /// <summary>Back to the player's eyes, a few levels up, so the ability bar and the upgrade on offer show.</summary>
        static void LevelUpAndLook()
        {
            var runner = SessionRunner.Instance;
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            if (runner == null || runner.Heroes == null || local == null) return;
            runner.Heroes.Progression.Grant(runner.Client.PlayerId, 1000f, ExperienceSource.Match);
            local.freeCamera = false;
        }

        /// <summary>
        /// The first practice dummy becomes a Lancer (on the server, for looks only: its kit stays), so the player
        /// sees the soldier model as an enemy, firing toward them, with their own rifle in view.
        /// </summary>
        static void DummyBecomesLancer()
        {
            HideHud = true;
            var runner = SessionRunner.Instance;
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            var dummy = runner == null || runner.Match == null ? null : runner.Match.State.Player(CombatServer.FirstDummyId);
            if (dummy == null || local == null) { Debug.LogError("Duck and Cover: the capture found no practice dummy to show as a Lancer."); return; }
            dummy.heroId = "lancer";
            local.freeCamera = false;
            CombatView.Instance.StartCoroutine(DummyFires(CombatServer.FirstDummyId));
        }

        /// <summary>Once the dummy's model is up, show it firing (looks only; dummies don't shoot).</summary>
        static IEnumerator DummyFires(int dummyId)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            var body = CombatView.Instance == null ? null : CombatView.Instance.BodyOf(dummyId);
            if (body == null || body.Model == null) { Debug.LogError("Duck and Cover: the Lancer dummy has no model (is Resources/HeroArt/lancer imported?)."); yield break; }
            float until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until)
            {
                body.Model.Fired();
                yield return new WaitForSecondsRealtime(0.1f);
            }
        }

        /// <summary>A few shots from the player's rifle, captured mid-burst (no hit check: shot 08 already counts hits).</summary>
        static void FirstPersonBurst()
        {
            HideHud = true;
            var view = CombatView.Instance;
            if (view == null || view.Local == null) return;
            view.StartCoroutine(Burst(view.Local, 6, () => { }));
            // The flash lasts a few hundredths of a second in play; hold it for the picture.
            view.StartCoroutine(HoldFlash(view.Local));
        }

        static IEnumerator HoldFlash(LocalPlayer local)
        {
            yield return new WaitForSecondsRealtime(0.06f);
            if (local.ViewModel != null) local.ViewModel.Fired(0.5f);
        }

        static Transform lineup;

        /// <summary>
        /// Lancer in every clip, held at a telling moment, on a floor far from the map. Page 0: moving; page 1:
        /// shooting, reloading, jumping, vaulting and his abilities; page 2: three of them close up.
        /// </summary>
        static void LancerLineup(int page)
        {
            HideHud = true;
            var runner = SessionRunner.Instance;
            var art = HeroArt.For("lancer");
            if (runner == null || runner.ClientRules == null || art == null) { Debug.LogError("Duck and Cover: the capture has no Lancer model to line up."); return; }
            if (lineup != null) Destroy(lineup.gameObject);
            lineup = new GameObject("Lineup").transform;
            var origin = new Vector3(0f, 0f, 400f);
            Paint.Block(lineup, "Floor", origin + new Vector3(0f, -0.05f, 0f), new Vector3(40f, 0.1f, 20f), new Color(0.30f, 0.33f, 0.38f), false);
            var moving = new[] { ("idle", 1f), ("run", 0.3f), ("strafe_left", 0.3f), ("strafe_right", 0.3f), ("backpedal", 0.3f), ("sprint", 0.25f), ("crouch_walk", 0.4f) };
            var acting = new[] { ("aim", 0f), ("reload", 0.8f), ("jump", art.def.jumpAir), ("vault", 0.37f), ("slip", 0.27f), ("tracer_mark", 0.37f), ("grenade", 0.8f) };
            var close = new[] { ("aim", 0f), ("run", 0.3f), ("tracer_mark", 0.37f) };
            var set = page == 0 ? moving : page == 1 ? acting : close;
            float spacing = page == 2 ? 1.3f : 1.55f;
            for (int i = 0; i < set.Length; i++)
            {
                var (clip, at) = set[i];
                var body = PlaceholderBody.Create("Lancer " + clip, TeamId.A, runner.ClientRules, false, "lancer");
                body.transform.SetParent(lineup, false);
                body.Pose(origin + new Vector3((i - (set.Length - 1) * 0.5f) * spacing, 0f, 0f), page == 2 ? 200f : 165f, clip == "crouch_walk");
                if (body.Model == null) { Debug.LogError("Duck and Cover: the Lancer lineup body has no model."); return; }
                body.Model.Freeze(clip, at);
                if (clip == "aim") body.Model.Fired(10f);
            }
            if (page == 2) WatchFrom(origin + new Vector3(-0.6f, 1.55f, -3.3f), origin + new Vector3(0f, 1.05f, 0f));
            else WatchFrom(origin + new Vector3(0f, 1.6f, -6.2f), origin + new Vector3(0f, 0.95f, 0f));
        }

        /// <summary>A dummy uses an ability (server side, as a client's command would), aimed at a player's chest, dipped by <paramref name="dip"/>.</summary>
        static bool DummyCast(SessionRunner runner, int dummy, int slot, int targetId, float dip)
        {
            var rules = runner.Match.Rules;
            var from = runner.Combat.Movement.Motion(dummy);
            var to = runner.Combat.Movement.Motion(targetId);
            if (from == null || to == null) return false;
            Vec3 eye = from.position + Vec3.Up * rules.eyeHeight;
            Vec3 dir = (Hitboxes.Chest(rules, to.Pose(true)) - eye).Normalized + Vec3.Up * dip;
            var result = runner.Combat.Apply(new UseAbilityCommand
            {
                playerId = dummy, slot = slot, abilityId = "", aimOrigin = eye, aimDirection = dir.Normalized, tick = runner.Match.State.tick,
            });
            if (!result.IsAccepted) Debug.LogWarning("Duck and Cover: capture dummy " + dummy + " slot " + slot + " refused: " + result.reason);
            return result.IsAccepted;
        }

        /// <summary>Point the camera before a match starts (nobody is playing, so nothing moves it back).</summary>
        static void LookAt(Vector3 position, Vector3 target)
        {
            var cam = Camera.main;
            if (cam != null) cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        }

        /// <summary>Host the practice match and pick Lancer, but stay in the lobby so the pick screen shows.</summary>
        static void HostPracticeMatch()
        {
            var rules = Ruleset.Default;
            rules.heroPickSeconds = 1.5f;
            rules.deploySeconds = 1.5f;
            var runner = SessionRunner.Ensure();
            runner.Host("capture_hold", "Capture", false, 0, false, rules);
            var session = runner.Session;
            if (session == null || session.Client == null) { Debug.LogError("Duck and Cover: the capture could not host a match."); return; }
            if (CombatView.Instance != null && CombatView.Instance.Local != null) CombatView.Instance.Local.scripted = true;
            session.Client.Welcomed += welcome =>
            {
                for (int i = 0; i < 3; i++) session.Client.Send(new AddDummyCommand { team = TeamId.B });
                session.Client.Send(new PickHeroCommand { heroId = "lancer" });
            };
        }

        static void StartPracticeMatch()
        {
            var runner = SessionRunner.Instance;
            var client = runner == null || runner.Session == null ? null : runner.Session.Client;
            if (client == null || client.State == null) { Debug.LogError("Duck and Cover: the capture's practice match was not hosted."); return; }
            if (client.State.phase != MatchPhase.Lobby) Debug.LogError("Duck and Cover: the capture expected the practice match in its lobby, it is " + client.State.phase + ".");
            client.Send(new StartMatchCommand());
        }

        /// <summary>Put the local player somewhere (through the server, as a teleport), facing the first practice dummy.</summary>
        static void Stand(Vec3 feet)
        {
            var runner = SessionRunner.Instance;
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            if (runner == null || runner.Combat == null || runner.Client == null || local == null)
            {
                Debug.LogError("Duck and Cover: the capture has no running match to stand in.");
                return;
            }
            if (!local.Active) Debug.LogError("Duck and Cover: the local player was not in the world when the capture needed it (phase " + runner.Client.State.phase + ").");
            runner.Combat.Movement.Teleport(runner.Client.PlayerId, feet, 0f);
            CombatView.Instance.StartCoroutine(AimWhenPlaced(runner, local, feet));
        }

        static IEnumerator AimWhenPlaced(SessionRunner runner, LocalPlayer local, Vec3 feet)
        {
            yield return new WaitForSecondsRealtime(0.4f); // the teleport comes back through the session
            var rules = runner.ClientRules;
            var dummy = runner.Combat.Movement.Motion(CombatServer.FirstDummyId);
            if (dummy == null) { Debug.LogError("Duck and Cover: the capture found no practice dummy."); yield break; }
            Vec3 eye = feet + Vec3.Up * rules.eyeHeight;
            Vec3 d = (Hitboxes.Chest(rules, dummy.Pose(true)) - eye).Normalized;
            local.LookAt(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.y) * Mathf.Rad2Deg);
        }

        /// <summary>Fire at whatever is in front, and report whether the server counted any hits.</summary>
        static void Shoot(int shots)
        {
            var runner = SessionRunner.Instance;
            var view = CombatView.Instance;
            if (runner == null || runner.Client == null || view == null || view.Local == null) return;
            int hits = 0;
            runner.Client.Events.Subscribe(e => { if (e is DamagedEvent d && d.attackerId == runner.Client.PlayerId) hits++; });
            view.StartCoroutine(Burst(view.Local, shots, () =>
            {
                if (hits == 0) Debug.LogError("Duck and Cover: the capture fired " + shots + " shots at a dummy and the server counted no hits.");
                else Debug.Log("Duck and Cover: capture shots hit " + hits + " times.");
            }));
        }

        /// <summary>Take the camera off the player and look at the scene from outside.</summary>
        static void WatchFrom(Vector3 position, Vector3 target)
        {
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            var cam = Camera.main;
            if (local == null || cam == null) return;
            local.freeCamera = true;
            cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        }

        static IEnumerator Burst(LocalPlayer local, int shots, Action done)
        {
            int fired = 0;
            float until = Time.realtimeSinceStartup + 1.2f;
            while (fired < shots && Time.realtimeSinceStartup < until)
            {
                if (local.FireOnce()) fired++;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.25f);
            done();
        }

        readonly List<string> messages = new List<string>();
        readonly List<string> saved = new List<string>();
        int errors;
        bool finished;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            if (type == LogType.Exception && IsEditorSearchIndexing(stack))
            {
                // Unity's own Search window indexer throws once on a project with a fresh Library.
                // It is not the game's, so list it without failing the run.
                messages.Add("[Editor, ignored] " + message);
                return;
            }
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
            messages.Add("[" + type + "] " + message + (type == LogType.Exception ? "\n" + stack : ""));
        }

        static bool IsEditorSearchIndexing(string stack) =>
            stack != null && stack.Contains("UnityEditor.Search.SearchInit") && !stack.Contains("DuckAndCover");

        IEnumerator Start()
        {
            StartCoroutine(Watchdog());
            Directory.CreateDirectory(outputDirectory);
            foreach (var old in Directory.GetFiles(outputDirectory, "*.png")) File.Delete(old);

            // Colours first: the early captures drew every block in one colour.
            var paintCheck = Paint.SelfCheckSetup();
            yield return null;
            yield return null;
            string paint = Paint.SelfCheckRead(paintCheck);
            if (paint != null) Debug.LogError("Duck and Cover: colour check failed, " + paint);
            else Debug.Log("Duck and Cover: colour check passed.");
            yield return null;

            foreach (var shot in Shots())
            {
                HideHud = false; // each shot that wants the bare world asks again
                HideTraits = false;
                try { shot.setup?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
                yield return new WaitForSecondsRealtime(shot.wait);
                Save(shot.name);
                if (finished) yield break;
            }
            Finish("Done.");
        }

        IEnumerator Watchdog()
        {
            yield return new WaitForSecondsRealtime(TimeoutSeconds);
            if (finished) yield break;
            errors++;
            messages.Add("[Error] Screenshot run timed out after " + TimeoutSeconds + " seconds.");
            Finish("Timed out.");
        }

        bool loggedCanvasScale;

        /// <summary>
        /// Canvas scalers size the UI, and its text's glyphs, for the window, which in a batch-mode run is
        /// not the 1920 x 1080 picture being saved: text came out rasterised small and stretched, so soft.
        /// For the shot, give each root canvas the scale it would have on a 1920 x 1080 screen (the layout
        /// size, so 1 here) and rebuild the text at it; the scalers set their own again on the next frame.
        /// In the game the window is the screen, so this only makes the shots show what a player sees.
        /// </summary>
        void SizeCanvasesForCapture(string shot)
        {
            float scale = Height / 1080f;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                if (!loggedCanvasScale)
                {
                    loggedCanvasScale = true;
                    Debug.Log("Duck and Cover: capture " + shot + " window " + Screen.width + " x " + Screen.height + ", " + canvas.name +
                              " scale " + canvas.scaleFactor + ", set to " + scale + " for the " + Width + " x " + Height + " shot.");
                }
                canvas.scaleFactor = scale;
            }
            // Text draws its glyphs at the canvas's scale when it builds its mesh: build it again at this one.
            foreach (var text in FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None)) text.SetAllDirty();
        }

        void Save(string name)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("Duck and Cover: no main camera to capture " + name + ".");
                return;
            }
            var target = new RenderTexture(Width, Height, 24);
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previous = cam.targetTexture;
            try
            {
                cam.targetTexture = target;
                SizeCanvasesForCapture(name);
                Canvas.ForceUpdateCanvases(); // the HUD lays itself out for the size being rendered
                cam.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                string path = Path.Combine(outputDirectory, name + ".png");
                File.WriteAllBytes(path, readback.EncodeToPNG());
                saved.Add(path);
            }
            finally
            {
                cam.targetTexture = previous;
                RenderTexture.active = null;
                target.Release();
                Destroy(target);
                Destroy(readback);
            }
        }

        void Finish(string outcome)
        {
            if (finished) return;
            finished = true;

            var report = new StringBuilder();
            report.AppendLine("Duck and Cover screenshot run: " + outcome);
            report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("Unity: " + Application.unityVersion + ", graphics: " + SystemInfo.graphicsDeviceType);
            report.AppendLine("Screenshots: " + saved.Count);
            foreach (var p in saved) report.AppendLine("  " + Path.GetFileName(p));
            report.AppendLine("Errors: " + errors + ", warnings and errors logged: " + messages.Count);
            foreach (var m in messages) report.AppendLine(m);
            try { File.WriteAllText(Path.Combine(outputDirectory, "log.txt"), report.ToString()); }
            catch (Exception ex) { Debug.LogWarning("Duck and Cover: could not write capture log. " + ex.Message); }
            Debug.Log("Duck and Cover: saved " + saved.Count + " screenshots to " + outputDirectory + " (" + errors + " errors).");

#if UNITY_EDITOR
            if (CaptureRequest.ExitWhenDone) UnityEditor.EditorApplication.Exit(errors > 0 ? 1 : 0);
            else UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(errors > 0 ? 1 : 0);
#endif
        }
    }
}

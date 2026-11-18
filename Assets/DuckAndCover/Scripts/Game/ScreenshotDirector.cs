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
            new Shot("06-spawn", StartPracticeMatch, 5.5f),
            new Shot("07-dummies", () => Stand(new Vec3(-4f, 0f, -8.5f)), 1.2f),
            new Shot("08-after-shots", () => Shoot(12), 1.6f),
            new Shot("09-bodies", () => WatchFrom(new Vector3(-2.5f, 2.2f, 4f), new Vector3(1.5f, 1f, 9f)), 0.5f),
            // Hero kits (WP11): the dummies are given heroes and use their abilities toward the player.
            new Shot("10-abilities", DummiesUseAbilities, 1.6f),
            new Shot("11-gate", DummyRaisesGate, 1.0f),
            new Shot("12-hero-hud", LevelUpAndLook, 1.2f),
        };

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
                // Bulwark: a panel toward the player. Rigger: a mine and a sentry on the floor ahead. Mender: a field near its feet.
                if (DummyCast(runner, dummy, 0, me, i == 0 ? 0f : i == 1 ? -0.3f : -0.6f)) used++;
                if (i == 1 && DummyCast(runner, dummy, 1, me, -0.5f)) used++;
            }
            // The player's Lancer marks the first dummy (the camera already faces it).
            var local = CombatView.Instance == null ? null : CombatView.Instance.Local;
            if (local != null) local.UseAbility(1, "lancer_mark");
            if (used < 4) Debug.LogError("Duck and Cover: the capture's dummies used " + used + " abilities, expected 4.");
            WatchFrom(new Vector3(-8f, 6f, -14f), new Vector3(-1f, 0f, 2f));
        }

        static void DummyRaisesGate()
        {
            var runner = SessionRunner.Instance;
            if (runner == null || runner.Heroes == null) return;
            int rigger = CombatServer.FirstDummyId + 1;
            int formLevel = runner.Heroes.Progression.Rules.UltimateFormLevel();
            while (runner.Heroes.Progression.Level(rigger) < formLevel && runner.Heroes.Progression.Grant(rigger, 500f, ExperienceSource.Match) > 0f) { }
            if (formLevel > 0) runner.Heroes.Progression.Apply(new ChooseUpgradeCommand { playerId = rigger, level = formLevel, option = 0 });
            var unit = runner.Combat.Abilities.Unit(rigger);
            if (unit != null) unit.ultimateCharge = runner.Match.Rules.ultimateChargeMax;
            if (!DummyCast(runner, rigger, 2, runner.Client.PlayerId, 0f)) Debug.LogError("Duck and Cover: the capture's Rigger could not raise its gate.");
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

        static void StartPracticeMatch()
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
                session.Client.Send(new StartMatchCommand());
            };
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

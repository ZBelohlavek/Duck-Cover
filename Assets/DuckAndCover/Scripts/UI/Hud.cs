using System.Collections.Generic;
using DuckAndCover.Core;
using DuckAndCover.Game;
using UnityEngine;
using UnityEngine.UI;

namespace DuckAndCover.UI
{
    /// <summary>
    /// The fighting HUD: crosshair and hit marker, health and shield, the weapon and its ammo, the
    /// hero's abilities with their cooldowns, level and experience, the upgrade on offer, the respawn
    /// countdown, who took whom down, and in Flag Rush the captures and where the flag is (WP21).
    /// It reads the latest state and the local player; it decides
    /// nothing. Score and zones come with WP6; the ability and upgrade panels here are plain text
    /// stand-ins until the hero UI (WP12).
    /// </summary>
    public class Hud : MonoBehaviour
    {
        const float BarWidth = 360f;
        const float HitMarkerSeconds = 0.22f;
        const float FeedSeconds = 6f;
        static readonly Color ShieldColor = new Color(0.45f, 0.85f, 1.00f);
        static readonly Color HealthColor = new Color(0.35f, 0.90f, 0.40f);

        Canvas canvas;
        GameObject fight;
        RectTransform healthFill, shieldFill;
        Text healthText, weaponText, ammoText, stanceText, centerText, feedText, levelText, choiceText;
        readonly List<Text> abilityTexts = new List<Text>();
        GameObject choicePanel;
        GameObject flagPanel;
        Text flagScoreA, flagScoreB, flagGoal, flagStatus;
        static readonly string[] AbilityKeys = { "E", "F", "X" };
        GameObject hitMarker;
        readonly List<Image> hitTicks = new List<Image>();
        readonly List<KeyValuePair<float, string>> feed = new List<KeyValuePair<float, string>>();
        SessionClient client;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (NetSmoke.Arg("-duckSmoke") != null) return; // the network smoke test draws nothing
            var go = new GameObject("Hud");
            DontDestroyOnLoad(go);
            go.AddComponent<Hud>();
        }

        void Awake()
        {
            canvas = UIKit.Canvas("HudCanvas", transform, 10);
            var root = canvas.transform;

            fight = UIKit.Rect(root, "Fight", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f)).gameObject;
            var f = fight.transform;
            var mid = new Vector2(0.5f, 0.5f);

            // Crosshair: a dot and four ticks.
            UIKit.Box(f, "Dot", mid, Vector2.zero, new Vector2(4f, 4f), UITheme.Text);
            UIKit.Box(f, "TickLeft", mid, new Vector2(-14f, 0f), new Vector2(12f, 2f), UITheme.Text);
            UIKit.Box(f, "TickRight", mid, new Vector2(14f, 0f), new Vector2(12f, 2f), UITheme.Text);
            UIKit.Box(f, "TickUp", mid, new Vector2(0f, 14f), new Vector2(2f, 12f), UITheme.Text);
            UIKit.Box(f, "TickDown", mid, new Vector2(0f, -14f), new Vector2(2f, 12f), UITheme.Text);

            // Hit marker: four diagonal ticks around the crosshair.
            hitMarker = UIKit.Rect(f, "HitMarker", mid, Vector2.zero, new Vector2(60f, 60f)).gameObject;
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? -1f : 1f;
                var tick = UIKit.Box(hitMarker.transform, "Tick" + i, mid, new Vector2(18f * sx, 18f * sy), new Vector2(16f, 3f), UITheme.Text);
                tick.rectTransform.localRotation = Quaternion.Euler(0f, 0f, sx * sy > 0f ? 45f : -45f);
                hitTicks.Add(tick);
            }

            // Bottom left: shield over health.
            var bl = new Vector2(0f, 0f);
            UIKit.Box(f, "VitalsBack", bl, new Vector2(40f, 40f), new Vector2(BarWidth + 130f, 84f), UITheme.Panel);
            shieldFill = UIKit.Bar(f, "Shield", bl, new Vector2(56f, 92f), new Vector2(BarWidth, 14f), ShieldColor);
            healthFill = UIKit.Bar(f, "Health", bl, new Vector2(56f, 56f), new Vector2(BarWidth, 28f), HealthColor);
            healthText = UIKit.Label(f, "HealthText", bl, new Vector2(BarWidth + 70f, 52f), new Vector2(100f, 56f), "", 30, UITheme.Text, TextAnchor.MiddleLeft);
            stanceText = UIKit.Label(f, "Stance", bl, new Vector2(56f, 134f), new Vector2(300f, 30f), "", 20, UITheme.TextDim, TextAnchor.MiddleLeft);

            // Bottom right: weapon and ammo.
            var br = new Vector2(1f, 0f);
            UIKit.Box(f, "WeaponBack", br, new Vector2(-40f, 40f), new Vector2(300f, 110f), UITheme.Panel);
            ammoText = UIKit.Label(f, "Ammo", br, new Vector2(-60f, 78f), new Vector2(260f, 60f), "", 44, UITheme.Text, TextAnchor.MiddleRight);
            weaponText = UIKit.Label(f, "Weapon", br, new Vector2(-60f, 48f), new Vector2(260f, 30f), "", 22, UITheme.TextDim, TextAnchor.MiddleRight);

            // Bottom middle: the hero's abilities (E, F, X for the ultimate), level above them.
            var bm = new Vector2(0.5f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 230f;
                UIKit.Box(f, "AbilityBack" + i, bm, new Vector2(x, 40f), new Vector2(220f, 76f), UITheme.Panel);
                abilityTexts.Add(UIKit.Label(f, "Ability" + i, bm, new Vector2(x, 42f), new Vector2(204f, 72f), "", 20, UITheme.Text, TextAnchor.MiddleCenter));
            }
            levelText = UIKit.Label(f, "Level", bm, new Vector2(0f, 124f), new Vector2(680f, 30f), "", 22, UITheme.TextDim, TextAnchor.MiddleCenter);

            // Right middle: the upgrade on offer, when there is one.
            var rm = new Vector2(1f, 0.5f);
            choicePanel = UIKit.Box(f, "ChoiceBack", rm, new Vector2(-40f, 0f), new Vector2(520f, 230f), UITheme.Panel).gameObject;
            choiceText = UIKit.Label(choicePanel.transform, "Choice", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(490f, 210f), "", 20, UITheme.Text, TextAnchor.MiddleLeft);

            // Top middle, Flag Rush only: captures each side and what the flag is doing. Shows outside the fight too.
            var tm = new Vector2(0.5f, 1f);
            flagPanel = UIKit.Rect(root, "FlagRush", tm, new Vector2(0f, -40f), new Vector2(1920f, 140f)).gameObject;
            var fp = flagPanel.transform;
            UIKit.Box(fp, "ScoreBack", tm, new Vector2(0f, -40f), new Vector2(360f, 72f), UITheme.Panel);
            UIKit.Box(fp, "ScoreA", tm, new Vector2(-130f, -40f), new Vector2(84f, 60f), UITheme.TeamA);
            UIKit.Box(fp, "ScoreB", tm, new Vector2(130f, -40f), new Vector2(84f, 60f), UITheme.TeamB);
            flagScoreA = UIKit.Label(fp, "CapturesA", tm, new Vector2(-130f, -40f), new Vector2(84f, 60f), "0", 40, UITheme.Text, TextAnchor.MiddleCenter);
            flagScoreB = UIKit.Label(fp, "CapturesB", tm, new Vector2(130f, -40f), new Vector2(84f, 60f), "0", 40, UITheme.Text, TextAnchor.MiddleCenter);
            flagGoal = UIKit.Label(fp, "Goal", tm, new Vector2(0f, -40f), new Vector2(160f, 60f), "", 20, UITheme.TextDim, TextAnchor.MiddleCenter);
            flagStatus = UIKit.Label(fp, "FlagStatus", tm, new Vector2(0f, -104f), new Vector2(1100f, 40f), "", 26, UITheme.Text, TextAnchor.MiddleCenter);
            flagPanel.SetActive(false);

            // Messages in the middle, and the feed top right. These show outside the fight too.
            centerText = UIKit.Label(root, "Center", new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(1200f, 80f), "", 40, UITheme.Text, TextAnchor.MiddleCenter);
            feedText = UIKit.Label(root, "Feed", new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(700f, 160f), "", 22, UITheme.Text, TextAnchor.UpperRight);
        }

        void OnDestroy() => Bind(null);

        void Bind(SessionClient next)
        {
            if (client == next) return;
            if (client != null) client.Events.Unsubscribe(OnEvent);
            client = next;
            feed.Clear();
            if (client != null) client.Events.Subscribe(OnEvent);
        }

        void OnEvent(GameEvent e)
        {
            string line;
            switch (e)
            {
                case PlayerEliminatedEvent down when !down.disconnected:
                    string victim = Name(down.playerId);
                    line = down.killerId >= 0 ? Name(down.killerId) + "  took down  " + victim : victim + "  went down";
                    break;
                case FlagTakenEvent taken:
                    line = Name(taken.playerId) + "  took the flag";
                    break;
                case FlagDroppedEvent dropped:
                    line = Name(dropped.playerId) + "  dropped the flag";
                    break;
                case FlagCapturedEvent captured:
                    line = Name(captured.playerId) + "  CAPTURED the flag for " + TeamName(captured.team);
                    break;
                case FlagResetEvent _:
                    line = "The flag reset";
                    break;
                default:
                    return;
            }
            feed.Add(new KeyValuePair<float, string>(Time.unscaledTime, line));
            if (feed.Count > 5) feed.RemoveAt(0);
        }

        static string TeamName(TeamId team) => team == TeamId.A ? "Team A" : team == TeamId.B ? "Team B" : "nobody";

        string Name(int playerId) =>
            client.Snapshot != null && client.Snapshot.IsDummy(playerId) ? "Practice dummy" : client.NameOf(playerId);

        void LateUpdate()
        {
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            if (canvas.enabled == ScreenshotDirector.HideHud) canvas.enabled = !ScreenshotDirector.HideHud;
            var runner = SessionRunner.Instance;
            Bind(runner == null ? null : runner.Client);

            var snapshot = client == null ? null : client.Snapshot;
            var me = client == null ? null : client.Me;
            var local = LocalPlayer.Instance;
            bool fighting = snapshot != null && me != null && me.alive && local != null && local.Active;
            if (fight.activeSelf != fighting) fight.SetActive(fighting);

            feed.RemoveAll(line => Time.unscaledTime - line.Key > FeedSeconds);
            var lines = new System.Text.StringBuilder();
            foreach (var line in feed) lines.AppendLine(line.Value);
            feedText.text = lines.ToString();

            centerText.text = CenterMessage(snapshot, me);
            ShowFlag(snapshot, me);
            if (!fighting) return;

            var unit = snapshot.abilities == null ? null : snapshot.abilities.Unit(me.id);
            float health = unit == null ? 0f : unit.health;
            float maxHealth = unit == null ? 1f : Mathf.Max(1f, unit.stats.Get(StatIds.MaxHealth));
            float shield = unit == null ? 0f : unit.Shield;
            UIKit.SetFill(healthFill, BarWidth, health / maxHealth);
            UIKit.SetFill(shieldFill, BarWidth, shield / Mathf.Max(1f, runner.ClientRules.maxShield));
            healthText.text = Mathf.CeilToInt(health).ToString();
            healthText.color = health <= maxHealth * runner.ClientRules.lowHealthShare ? UITheme.Danger : UITheme.Text;

            weaponText.text = local.WeaponName;
            ammoText.text = local.Reloading ? "Reloading" : local.Ammo + " / " + local.Magazine;
            ammoText.color = local.Reloading ? UITheme.Warning : local.Ammo == 0 ? UITheme.Danger : UITheme.Text;
            stanceText.text = local.Vaulting ? "Vaulting" : local.Crouched ? "Crouched" : local.Sprinting ? "Sprinting" : local.CarryingFlag ? "Carrying the flag: no sprint or dash" : "";

            ShowAbilities(snapshot, me, unit);
            ShowChoice(snapshot, me, local);

            bool hit = Time.time - local.LastHitAt < HitMarkerSeconds;
            if (hitMarker.activeSelf != hit) hitMarker.SetActive(hit);
            if (hit)
            {
                Color c = local.LastHitEliminated ? UITheme.Danger : local.LastHitWasHead ? UITheme.Warning : UITheme.Text;
                foreach (var tick in hitTicks) tick.color = c;
            }
        }

        /// <summary>Whether the player has picked their ultimate's form, if their hero offers one (the server holds the ultimate until then).</summary>
        static bool FormPicked(MatchSnapshot snapshot, PlayerState me, Ruleset rules)
        {
            var progression = rules.progression == null ? new ProgressionRuleset() : rules.progression;
            if (!HeroServer.OffersUltimateForm(HeroData.Catalog.Resolve(me.heroId), progression)) return true;
            var mine = snapshot == null || snapshot.progression == null ? null : snapshot.progression.Player(me.id);
            return mine != null && mine.HasPicked(progression.UltimateFormLevel());
        }

        void ShowAbilities(MatchSnapshot snapshot, PlayerState me, HeroUnit unit)
        {
            var lib = HeroData.Abilities;
            var rules = SessionRunner.Instance.ClientRules;
            for (int i = 0; i < abilityTexts.Count; i++)
            {
                var label = abilityTexts[i];
                var slot = unit != null && i < unit.slots.Count ? unit.slots[i] : null;
                var baseDef = slot == null ? null : lib.Ability(slot.abilityId);
                if (baseDef == null) { label.text = ""; continue; }
                var def = AbilityParams.Resolve(baseDef, unit.changes, lib, rules.minCooldownSeconds).def;
                string key = def.ultimate ? AbilityKeys[2] : i < 2 ? AbilityKeys[i] : "";
                string status;
                Color color = UITheme.Text;
                if (def.ultimate)
                {
                    int need = Mathf.Max(def.unlockLevel, rules.ultimateUnlockLevel);
                    if (me.level < need) { status = "Unlocks at level " + need; color = UITheme.TextDim; }
                    else if (!FormPicked(snapshot, me, rules)) { status = "Pick its form (hold Z or V)"; color = UITheme.TextDim; }
                    else if (unit.ultimateCharge >= def.ultimateCost - 0.01f) { status = "Ready"; color = UITheme.Warning; }
                    else { status = Mathf.FloorToInt(100f * unit.ultimateCharge / Mathf.Max(1f, def.ultimateCost)) + "%"; color = UITheme.TextDim; }
                }
                else if (slot.casting) status = "Casting";
                else if (slot.charges > 0) status = def.charges > 1 ? "Ready (" + slot.charges + ")" : "Ready";
                else
                {
                    float rate = Mathf.Max(0.01f, unit.stats.Get(StatIds.CooldownRate));
                    status = (slot.rechargeLeft / rate).ToString("0.0") + " s";
                    color = UITheme.TextDim;
                }
                label.text = "[" + key + "] " + def.displayName + "\n" + status;
                label.color = color;
            }

            var hero = HeroData.Catalog.Resolve(me.heroId);
            var prog = snapshot.progression == null ? null : snapshot.progression.Player(me.id);
            levelText.text = (hero == null ? "" : hero.displayName + "   ") + "Level " + me.level + (prog == null ? "" : "   " + Mathf.FloorToInt(prog.xp) + " xp");
        }

        void ShowChoice(MatchSnapshot snapshot, PlayerState me, LocalPlayer local)
        {
            int level = LocalPlayer.PendingLevel(snapshot, me.id, SessionRunner.Instance.ClientRules.progression);
            var hero = level > 0 ? HeroData.Catalog.Resolve(me.heroId) : null;
            var offer = hero == null ? null : hero.Level(level);
            bool show = offer != null && offer.options != null && offer.options.Length >= 2;
            if (choicePanel.activeSelf != show) choicePanel.SetActive(show);
            if (!show) return;
            var left = hero.Upgrade(offer.options[0]);
            var right = hero.Upgrade(offer.options[1]);
            string kind = left == null ? "upgrade" : left.kind;
            var text = new System.Text.StringBuilder();
            text.Append("Level ").Append(level).Append(": choose a ").Append(kind).Append("\n\n");
            text.Append(Option("Hold Z", left, local.ChoiceHeld == 0 ? local.ChoiceProgress : 0f)).Append("\n\n");
            text.Append(Option("Hold V", right, local.ChoiceHeld == 1 ? local.ChoiceProgress : 0f));
            choiceText.text = text.ToString();
        }

        static string Option(string key, UpgradeDef u, float progress)
        {
            if (u == null) return key + ": ?";
            string bar = progress > 0f ? "  " + new string('|', Mathf.CeilToInt(progress * 10f)) : "";
            return key + ": " + u.displayName + bar + "\n   " + u.description;
        }

        /// <summary>Flag Rush: captures for each team (first to the mode's score), and one line on the flag.</summary>
        void ShowFlag(MatchSnapshot snapshot, PlayerState me)
        {
            var f = snapshot == null ? null : snapshot.flag;
            var state = snapshot == null ? null : snapshot.match;
            bool show = f != null && state != null && state.phase >= MatchPhase.Deploy;
            if (flagPanel.activeSelf != show) flagPanel.SetActive(show);
            if (!show) return;

            flagScoreA.text = state.scoreA.ToString();
            flagScoreB.text = state.scoreB.ToString();
            flagGoal.text = state.phase == MatchPhase.Overtime ? "OVERTIME" : "First to " + SessionRunner.ModeById(state.modeId).scoreToWin;
            flagGoal.color = state.phase == MatchPhase.Overtime ? UITheme.Warning : UITheme.TextDim;

            TeamId mine = me == null ? TeamId.None : me.team;
            string line;
            Color color = UITheme.Text;
            if (state.phase == MatchPhase.MatchEnd) line = state.winner == TeamId.None ? "Draw" : TeamName(state.winner) + " wins";
            else if (state.phase == MatchPhase.Deploy) line = "Carry the flag into the enemy base";
            else
            {
                switch (f.status)
                {
                    case FlagStatus.Waiting:
                        line = "Flag appears in " + Mathf.Max(0, Mathf.CeilToInt(f.appearAt - state.elapsed)) + " (ringed on the map)";
                        color = UITheme.TextDim;
                        break;
                    case FlagStatus.AtSpot:
                        line = "The flag is up: grab it";
                        color = UITheme.Warning;
                        break;
                    case FlagStatus.Dropped:
                        line = "Flag dropped: resets in " + Mathf.Max(0, Mathf.CeilToInt(f.resetAt - state.elapsed));
                        color = UITheme.Warning;
                        break;
                    default:
                        if (me != null && f.carrierId == me.id) { line = "YOU HAVE THE FLAG: take it into the enemy base"; color = UITheme.ForTeam(mine); }
                        else if (f.carrierTeam == mine) { line = "Your team has the flag: escort " + Name(f.carrierId); color = UITheme.ForTeam(mine); }
                        else { line = "Enemy has the flag: stop " + Name(f.carrierId); color = UITheme.Danger; }
                        break;
                }
                if (state.phase == MatchPhase.Overtime) line = "Overtime, next capture wins.   " + line;
            }
            flagStatus.text = line;
            flagStatus.color = color;
        }

        string CenterMessage(MatchSnapshot snapshot, PlayerState me)
        {
            if (snapshot == null || me == null) return "";
            var state = snapshot.match;
            if (state.phase == MatchPhase.Deploy) return "Fight starts in " + Mathf.CeilToInt(state.phaseTimeLeft);
            if (state.IsPlaying && !me.alive && me.connected)
            {
                // The wait rounds up to the next wave, which the state copy carries.
                float at = me.respawnAt;
                float wave = state.nextWaveAt;
                float every = SessionRunner.Instance.ClientRules.respawnWaveSeconds;
                if (every > 0f) while (wave < at - 1e-4f) wave += every;
                else wave = at;
                return "Back in " + Mathf.Max(1, Mathf.CeilToInt(wave - state.elapsed));
            }
            return "";
        }
    }
}

using System.Collections.Generic;
using DuckAndCover.Core;
using DuckAndCover.Game;
using UnityEngine;
using UnityEngine.UI;

namespace DuckAndCover.UI
{
    /// <summary>
    /// The fighting HUD, drawn as a helmet display: thin glowing line work and slanted shapes on the
    /// visor, no boxes. Crosshair and hit marker; health and shield as segmented bars beside the level
    /// badge (its ring is the experience to the next level); the weapon's ammo as a number and a row of
    /// rounds; the hero's abilities as slanted slots that fill as they recharge; the upgrade on offer;
    /// the score header at the top (team bars meeting at the clock) with each mode's own parts: the
    /// zones in the zone modes, the flag line in Flag Rush (WP21), and in Overrun (WP19) the four
    /// Bastions with their shields and health, the next wave and the late push. Also the respawn
    /// countdown and who took whom down.
    /// The visor's edge takes the hero's identity colour; team colours stay on everything about teams.
    /// It reads the latest state and the local player; it decides nothing.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        const float HitMarkerSeconds = 0.22f;
        const float FeedSeconds = 6f;
        static readonly string[] AbilityKeys = { "E", "F", "X" };

        // Layout, in the visor's 1920 x 1080 pixels from the bottom left.
        const float BarX = 196f, HealthY = 100f;
        /// <summary>Each health cell is this many points, so heroes with more health get a longer bar.</summary>
        const float HealthPerCell = 25f, CellWidth = 20f, CellGap = 3f;
        static readonly Vector2 Badge = new Vector2(118f, 118f);
        const float SlotY = 60f;
        const float HeaderY = 1012f;
        const float TeamBarInner = 60f, TeamBarWidth = 210f;

        Canvas canvas;
        GameObject fight;
        VisorGraphic frame, crosshair, vitals, badge, ammoRow, hitMarker, choiceLines, headerLines, centreLines;
        Text healthTag, healthText, levelText, xpText, heroText, stanceText, ammoText, magazineText, weaponText, reloadText;
        Text centerText, feedText;
        string frameHero;
        float fightingSince = -1f;
        bool visorChecked;
        int lastLevel = -1;
        float lastXpShare = -1f;

        sealed class AbilitySlot
        {
            public VisorGraphic lines;
            public Text key, status, name;
            public float x, width, height;
        }
        readonly List<AbilitySlot> slots = new List<AbilitySlot>();

        GameObject choicePanel;
        Text choiceHeader;
        readonly Text[] choiceNames = new Text[2], choiceTexts = new Text[2];

        // The score header, shared by every mode, and each mode's own parts.
        GameObject header;
        Text headerClock, headerGoal, headerScoreA, headerScoreB;
        GameObject zoneRow;
        VisorGraphic zoneLines;
        readonly List<Text> zoneLetters = new List<Text>();
        GameObject statusRow;
        Text statusText;
        /// <summary>The player's own state above the ability slots: cloaked, ambush, silenced.</summary>
        Text selfStateText;
        GameObject overrunRow;
        sealed class BastionCard
        {
            public TeamId team;
            public BastionTier tier;
            public float x;
            public VisorGraphic lines;
            public Text state;
        }
        readonly List<BastionCard> bastionCards = new List<BastionCard>();
        const float CardWidth = 196f, CardHeight = 54f;

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
            var visor = UIKit.Layer(canvas.transform, "Visor");
            fight = UIKit.Layer(visor, "Fight").gameObject;
            var f = fight.transform;

            frame = UIKit.Lines(f, "Frame");
            BuildCrosshair(f);
            BuildVitals(f);
            BuildWeapon(f);
            BuildAbilities(f);
            BuildChoice(f);
            BuildHeader(visor);

            // Messages in the middle, and the feed top right. These show outside the fight too.
            centreLines = UIKit.Lines(visor, "CentreBrackets");
            Bracket(centreLines, 960f, 700f, 330f, 34f, UITheme.Visor);
            centreLines.Commit();
            centerText = UIKit.Readout(visor, "Center", new Vector2(960f, 700f), new Vector2(620f, 70f), "", 46, UITheme.Text, TextAnchor.MiddleCenter, true);
            feedText = UIKit.Readout(visor, "Feed", new Vector2(1560f, 950f), new Vector2(640f, 160f), "", 21, UITheme.Text, TextAnchor.UpperRight);
        }

        // ---- Building ----

        void BuildCrosshair(Transform f)
        {
            var cross = crosshair = UIKit.Lines(f, "Crosshair");
            const float cx = 960f, cy = 540f;
            cross.Fill(VisorGraphic.Regular(cx, cy, 2.2f, 8, 0f), UITheme.Text, 2f);
            cross.Segment(new Vector2(cx - 24f, cy), new Vector2(cx - 10f, cy), 2f, UITheme.Text, 3f);
            cross.Segment(new Vector2(cx + 10f, cy), new Vector2(cx + 24f, cy), 2f, UITheme.Text, 3f);
            cross.Segment(new Vector2(cx, cy + 10f), new Vector2(cx, cy + 24f), 2f, UITheme.Text, 3f);
            cross.Segment(new Vector2(cx, cy - 10f), new Vector2(cx, cy - 24f), 2f, UITheme.Text, 3f);
            var faint = UITheme.WithAlpha(UITheme.Visor, 0.45f);
            for (int i = 0; i < 4; i++) cross.Arc(cx, cy, 36f, 1.2f, 45f + 90f * i - 18f, 45f + 90f * i + 18f, faint, 3f);
            cross.Commit();

            // Hit marker: four slanted ticks around the crosshair, tinted by what the hit did.
            hitMarker = UIKit.Lines(f, "HitMarker");
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + 90f * i) * Mathf.Deg2Rad;
                float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
                hitMarker.Segment(new Vector2(cx + dx * 13f, cy + dy * 13f), new Vector2(cx + dx * 26f, cy + dy * 26f), 2.6f, Color.white, 4f);
            }
            hitMarker.Commit();
        }

        void BuildVitals(Transform f)
        {
            vitals = UIKit.Lines(f, "Vitals");
            badge = UIKit.Lines(f, "LevelBadge");
            UIKit.Readout(f, "LevelTag", new Vector2(Badge.x, Badge.y + 30f), new Vector2(60f, 20f), "LEVEL", 15, UITheme.TextDim, TextAnchor.MiddleCenter);
            levelText = UIKit.Readout(f, "Level", new Vector2(Badge.x, Badge.y - 4f), new Vector2(100f, 56f), "", 44, UITheme.Text, TextAnchor.MiddleCenter, true);
            xpText = UIKit.Readout(f, "Xp", new Vector2(BarX + 150f, 74f), new Vector2(300f, 24f), "", 19, UITheme.TextDim, TextAnchor.MiddleLeft);
            heroText = UIKit.Readout(f, "Hero", new Vector2(BarX + 150f, 176f), new Vector2(300f, 30f), "", 24, UITheme.Text, TextAnchor.MiddleLeft, true);
            stanceText = UIKit.Readout(f, "Stance", new Vector2(BarX + 200f, 208f), new Vector2(400f, 26f), "", 19, UITheme.Text, TextAnchor.MiddleLeft);
            healthTag = UIKit.Readout(f, "HealthTag", new Vector2(BarX + 400f, HealthY + 44f), new Vector2(100f, 18f), "HEALTH", 15, UITheme.TextDim, TextAnchor.MiddleLeft);
            healthText = UIKit.Readout(f, "HealthText", new Vector2(BarX + 400f, HealthY + 12f), new Vector2(120f, 50f), "", 42, UITheme.Text, TextAnchor.MiddleLeft, true);
        }

        void BuildWeapon(Transform f)
        {
            var rule = UIKit.Lines(f, "WeaponRule");
            rule.Line(new[] { new Vector2(1470f, 84f), new Vector2(1690f, 84f), new Vector2(1704f, 96f), new Vector2(1884f, 96f) }, 1.3f, UITheme.VisorFaint, false, 3f);
            rule.Fill(VisorGraphic.Regular(1690f, 84f, 4f, 4, 45f), UITheme.Visor, 2f);
            rule.Commit();
            ammoRow = UIKit.Lines(f, "Rounds");
            ammoText = UIKit.Readout(f, "Ammo", new Vector2(1666f, 136f), new Vector2(240f, 70f), "", 62, UITheme.Text, TextAnchor.MiddleRight, true);
            magazineText = UIKit.Readout(f, "Magazine", new Vector2(1844f, 124f), new Vector2(100f, 30f), "", 24, UITheme.TextDim, TextAnchor.MiddleLeft);
            weaponText = UIKit.Readout(f, "Weapon", new Vector2(1734f, 70f), new Vector2(300f, 26f), "", 19, UITheme.TextDim, TextAnchor.MiddleRight);
            reloadText = UIKit.Readout(f, "Reload", new Vector2(1734f, 204f), new Vector2(300f, 24f), "", 18, UITheme.Warning, TextAnchor.MiddleRight, true);
        }

        void BuildAbilities(Transform f)
        {
            for (int i = 0; i < 3; i++)
            {
                bool ultimate = i == 2;
                var slot = new AbilitySlot
                {
                    x = 960f + (i - 1) * 156f + (ultimate ? 8f : 0f),
                    width = ultimate ? 128f : 112f,
                    height = ultimate ? 76f : 66f,
                };
                slot.lines = UIKit.Lines(f, "Ability" + i);
                float top = SlotY + slot.height;
                float left = slot.x - slot.width * 0.5f - 7f;
                slot.key = UIKit.Readout(f, "AbilityKey" + i, new Vector2(left + 18f, top + 16f), new Vector2(26f, 22f), AbilityKeys[i], 18, UITheme.Text, TextAnchor.MiddleCenter, true);
                slot.status = UIKit.Readout(f, "AbilityStatus" + i, new Vector2(slot.x + 7f, SlotY + slot.height * 0.5f), new Vector2(slot.width, 40f), "", 26, UITheme.Text, TextAnchor.MiddleCenter, true);
                slot.name = UIKit.Readout(f, "AbilityName" + i, new Vector2(slot.x, SlotY - 16f), new Vector2(170f, 22f), "", 17, UITheme.TextDim, TextAnchor.MiddleCenter, true);
                slots.Add(slot);
            }
            selfStateText = UIKit.Readout(f, "SelfState", new Vector2(960f, SlotY + 128f), new Vector2(420f, 28f), "", 22, UITheme.Visor, TextAnchor.MiddleCenter, true);
        }

        void BuildChoice(Transform f)
        {
            choicePanel = UIKit.Rect(f, "Choice", Vector2.zero, Vector2.zero, new Vector2(1920f, 1080f)).gameObject;
            var c = choicePanel.transform;
            var glass = UIKit.Lines(c, "ChoiceGlass");
            // Tinted glass that fades in from the left, edged above and below by hairlines.
            Color clear = UITheme.WithAlpha(UITheme.Glass, 0f), tint = UITheme.WithAlpha(UITheme.Glass, 0.62f);
            glass.Quad(new Vector2(1300f, 372f), new Vector2(1470f, 372f), new Vector2(1470f, 724f), new Vector2(1300f, 724f), clear, tint, tint, clear);
            glass.Quad(new Vector2(1470f, 372f), new Vector2(1920f, 372f), new Vector2(1920f, 724f), new Vector2(1470f, 724f), tint, tint, tint, tint);
            glass.Segment(new Vector2(1400f, 724f), new Vector2(1920f, 724f), 1.2f, UITheme.VisorFaint, 2f);
            glass.Segment(new Vector2(1400f, 372f), new Vector2(1920f, 372f), 1.2f, UITheme.VisorFaint, 2f);
            glass.Commit();
            choiceLines = UIKit.Lines(c, "ChoiceLines");
            choiceHeader = UIKit.Readout(c, "ChoiceHeader", new Vector2(1650f, 694f), new Vector2(460f, 28f), "", 20, UITheme.Visor, TextAnchor.MiddleLeft, true);
            for (int i = 0; i < 2; i++)
            {
                float top = OptionTop(i);
                UIKit.Readout(c, "ChoiceKey" + i, new Vector2(1438f, top - 18f), new Vector2(30f, 28f), i == 0 ? "Z" : "V", 20, UITheme.Text, TextAnchor.MiddleCenter, true);
                UIKit.Readout(c, "ChoiceHold" + i, new Vector2(1436f, top - 46f), new Vector2(50f, 16f), "HOLD", 11, UITheme.TextDim, TextAnchor.MiddleCenter);
                choiceNames[i] = UIKit.Readout(c, "ChoiceName" + i, new Vector2(1676f, top - 18f), new Vector2(412f, 30f), "", 23, UITheme.Text, TextAnchor.MiddleLeft, true);
                var text = UIKit.Readout(c, "ChoiceText" + i, new Vector2(1676f, top - 72f), new Vector2(412f, 70f), "", 17, UITheme.TextDim, TextAnchor.UpperLeft);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                choiceTexts[i] = text;
            }
        }

        static float OptionTop(int option) => 660f - option * 140f;

        void BuildHeader(Transform visor)
        {
            header = UIKit.Rect(visor, "Header", Vector2.zero, Vector2.zero, new Vector2(1920f, 1080f)).gameObject;
            var h = header.transform;
            var emblem = UIKit.Lines(h, "Emblem");
            emblem.Fill(VisorGraphic.Regular(960f, HeaderY, 44f, 6, 90f), UITheme.Glass);
            emblem.Line(VisorGraphic.Regular(960f, HeaderY, 44f, 6, 90f), 1.6f, UITheme.Visor, true, 4f);
            emblem.Line(VisorGraphic.Regular(960f, HeaderY, 50f, 6, 90f), 1f, UITheme.VisorFaint, true, 0f);
            emblem.Commit();
            headerLines = UIKit.Lines(h, "TeamBars");
            headerClock = UIKit.Readout(h, "Clock", new Vector2(960f, HeaderY), new Vector2(120f, 40f), "", 24, UITheme.Text, TextAnchor.MiddleCenter, true);
            headerGoal = UIKit.Readout(h, "Goal", new Vector2(960f, HeaderY - 66f), new Vector2(420f, 22f), "", 17, UITheme.TextDim, TextAnchor.MiddleCenter, true);
            headerScoreA = UIKit.Readout(h, "ScoreA", new Vector2(960f - TeamBarInner - 70f, HeaderY - 30f), new Vector2(120f, 36f), "0", 30, UITheme.TeamA, TextAnchor.MiddleRight, true);
            headerScoreB = UIKit.Readout(h, "ScoreB", new Vector2(960f + TeamBarInner + 70f, HeaderY - 30f), new Vector2(120f, 36f), "0", 30, UITheme.TeamB, TextAnchor.MiddleLeft, true);

            // Zone modes: one diamond per live zone under the clock, in its owner's colour.
            zoneRow = UIKit.Rect(h, "Zones", Vector2.zero, Vector2.zero, new Vector2(1920f, 1080f)).gameObject;
            zoneLines = UIKit.Lines(zoneRow.transform, "ZoneLines");

            // Flag Rush and Overrun: one line on what to do, on a strip of tinted glass.
            statusRow = UIKit.Rect(h, "Status", Vector2.zero, Vector2.zero, new Vector2(1920f, 1080f)).gameObject;
            var strip = UIKit.Lines(statusRow.transform, "StatusGlass");
            const float sy = 900f, half = 20f;
            Color clear = UITheme.WithAlpha(UITheme.Glass, 0f), tint = UITheme.WithAlpha(UITheme.Glass, 0.7f);
            strip.Quad(new Vector2(380f, sy - half), new Vector2(560f, sy - half), new Vector2(560f, sy + half), new Vector2(380f, sy + half), clear, tint, tint, clear);
            strip.Quad(new Vector2(560f, sy - half), new Vector2(1360f, sy - half), new Vector2(1360f, sy + half), new Vector2(560f, sy + half), tint, tint, tint, tint);
            strip.Quad(new Vector2(1360f, sy - half), new Vector2(1540f, sy - half), new Vector2(1540f, sy + half), new Vector2(1360f, sy + half), tint, clear, clear, tint);
            strip.Segment(new Vector2(560f, sy + half), new Vector2(1360f, sy + half), 1f, UITheme.VisorFaint, 2f);
            strip.Segment(new Vector2(560f, sy - half), new Vector2(1360f, sy - half), 1f, UITheme.VisorFaint, 2f);
            strip.Commit();
            statusText = UIKit.Readout(statusRow.transform, "StatusText", new Vector2(960f, sy), new Vector2(1300f, 34f), "", 21, UITheme.Text, TextAnchor.MiddleCenter, true);

            // Overrun: the four Bastions in the order they stand along the lane (A's Core, A's Forward, B's Forward, B's Core).
            overrunRow = UIKit.Rect(h, "Bastions", Vector2.zero, Vector2.zero, new Vector2(1920f, 1080f)).gameObject;
            var order = new[] { (TeamId.A, BastionTier.Core, 960f - 620f), (TeamId.A, BastionTier.Forward, 960f - 400f), (TeamId.B, BastionTier.Forward, 960f + 400f), (TeamId.B, BastionTier.Core, 960f + 620f) };
            foreach (var (team, tier, x) in order)
            {
                string name = "Bastion" + team + tier;
                var card = new BastionCard { team = team, tier = tier, x = x };
                card.lines = UIKit.Lines(overrunRow.transform, name);
                float top = HeaderY + CardHeight * 0.5f;
                UIKit.Readout(overrunRow.transform, name + "Name", new Vector2(x - 44f, top - 14f), new Vector2(110f, 20f), TierName(tier).ToUpperInvariant(), 17, UITheme.Text, TextAnchor.MiddleLeft, true);
                card.state = UIKit.Readout(overrunRow.transform, name + "State", new Vector2(x + 50f, top - 14f), new Vector2(90f, 20f), "", 16, UITheme.TextDim, TextAnchor.MiddleRight, true);
                bastionCards.Add(card);
            }
        }

        /// <summary>Two angled brackets either side of a point, like the corners of a sight.</summary>
        static void Bracket(VisorGraphic g, float cx, float cy, float halfWidth, float halfHeight, Color c)
        {
            float l = cx - halfWidth, r = cx + halfWidth;
            g.Line(new[] { new Vector2(l + 16f, cy + halfHeight), new Vector2(l, cy + halfHeight - 12f), new Vector2(l, cy - halfHeight + 12f), new Vector2(l + 16f, cy - halfHeight) }, 1.6f, c, false, 4f);
            g.Line(new[] { new Vector2(r - 16f, cy + halfHeight), new Vector2(r, cy + halfHeight - 12f), new Vector2(r, cy - halfHeight + 12f), new Vector2(r - 16f, cy - halfHeight) }, 1.6f, c, false, 4f);
        }

        /// <summary>The visor's edge: darkened rims, edge lines in the hero's colour, and a range ladder either side.</summary>
        void DrawFrame(string heroId)
        {
            frameHero = heroId;
            lastLevel = -1; // the level badge takes the hero's colour too
            var g = frame;
            g.Clear();
            Color edge = UITheme.Vignette, clear = UITheme.WithAlpha(UITheme.Vignette, 0f);
            g.Quad(new Vector2(0f, 0f), new Vector2(150f, 0f), new Vector2(150f, 1080f), new Vector2(0f, 1080f), edge, clear, clear, edge);
            g.Quad(new Vector2(1770f, 0f), new Vector2(1920f, 0f), new Vector2(1920f, 1080f), new Vector2(1770f, 1080f), clear, edge, edge, clear);
            g.Quad(new Vector2(0f, 960f), new Vector2(1920f, 960f), new Vector2(1920f, 1080f), new Vector2(0f, 1080f), clear, clear, edge, edge);
            g.Quad(new Vector2(0f, 0f), new Vector2(1920f, 0f), new Vector2(1920f, 120f), new Vector2(0f, 120f), edge, edge, clear, clear);

            Color hero = UITheme.WithAlpha(UITheme.ForHero(heroId), 0.55f);
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? 1f : -1f, o = side == 0 ? 0f : 1920f;
                g.Line(new[] { new Vector2(o + s * 26f, 720f), new Vector2(o + s * 26f, 880f), new Vector2(o + s * 110f, 1000f), new Vector2(o + s * 300f, 1052f) }, 1.6f, hero, false, 5f);
                g.Line(new[] { new Vector2(o + s * 26f, 380f), new Vector2(o + s * 26f, 300f), new Vector2(o + s * 54f, 250f) }, 1.6f, hero, false, 5f);
                for (int i = 0; i <= 8; i++)
                {
                    float y = 440f + i * 25f, len = i % 4 == 0 ? 18f : 9f;
                    g.Segment(new Vector2(o + s * 64f, y), new Vector2(o + s * (64f + len), y), 1.2f, UITheme.VisorFaint, 0f);
                }
            }
            g.Commit();
        }

        // ---- Events ----

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
                case BastionShieldBrokenEvent broken:
                    line = TeamName(broken.team) + "'s " + TierName(broken.tier) + " Bastion: shield down";
                    break;
                case BastionDestroyedEvent fallen:
                    line = TeamName(fallen.byTeam) + "  DESTROYED  " + TeamName(fallen.team) + "'s " + TierName(fallen.tier) + " Bastion";
                    break;
                case SideGateOpenedEvent gate:
                    line = TeamName(gate.team) + "'s side gate is open";
                    break;
                case LatePushStartedEvent _:
                    line = "Late push: waves come faster, Bastions take more damage";
                    break;
                case CampTakenEvent camp:
                    line = TeamName(camp.team) + "  took the " + CampName(camp.kind) + " camp";
                    break;
                case WaveSpawnedEvent wave when wave.juggernaut:
                    line = TeamName(wave.team) + "'s Juggernaut joins the wave";
                    break;
                default:
                    return;
            }
            feed.Add(new KeyValuePair<float, string>(Time.unscaledTime, line));
            if (feed.Count > 5) feed.RemoveAt(0);
        }

        static string TierName(BastionTier tier) => tier == BastionTier.Core ? "Core" : "Forward";

        static string CampName(CampKind kind)
        {
            switch (kind)
            {
                case CampKind.Juggernaut: return "Juggernaut";
                case CampKind.Damage: return "damage";
                case CampKind.Experience: return "experience";
                case CampKind.Speed: return "speed";
                default: return kind.ToString();
            }
        }

        static string TeamName(TeamId team) => team == TeamId.A ? "Team A" : team == TeamId.B ? "Team B" : "nobody";

        string Name(int playerId) =>
            client.Snapshot != null && client.Snapshot.IsDummy(playerId) ? "Practice dummy" : client.NameOf(playerId);

        // ---- Every frame ----

        void LateUpdate()
        {
            UIKit.KeepInFront(canvas);
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
            bool centre = centerText.text != "";
            if (centreLines.gameObject.activeSelf != centre) centreLines.gameObject.SetActive(centre);
            ShowHeader(snapshot, me);
            if (!fighting) return;

            if (frameHero != me.heroId) DrawFrame(me.heroId);
            CheckVisor();
            var unit = snapshot.abilities == null ? null : snapshot.abilities.Unit(me.id);
            ShowVitals(snapshot, me, unit, local, runner);
            ShowWeapon(local);
            ShowAbilities(snapshot, me, unit);
            ShowChoice(snapshot, me, local);

            bool hit = Time.time - local.LastHitAt < HitMarkerSeconds;
            if (hitMarker.gameObject.activeSelf != hit) hitMarker.gameObject.SetActive(hit);
            if (hit) hitMarker.color = local.LastHitEliminated ? UITheme.Danger : local.LastHitWasHead ? UITheme.Warning : UITheme.Text;
        }

        /// <summary>
        /// Once a second into the first fight, log whether the visor's line work is being built (the
        /// capture's log shows it), and warn if a graphic holds shapes that Unity never asked to draw.
        /// </summary>
        void CheckVisor()
        {
            if (visorChecked) return;
            if (fightingSince < 0f) fightingSince = Time.unscaledTime;
            if (Time.unscaledTime - fightingSince < 1f) return;
            visorChecked = true;
            var r = crosshair.canvasRenderer;
            string line = "Duck and Cover: visor check, crosshair " + crosshair.VertexCount + " corners, " + crosshair.MeshBuilds + " mesh builds, active " +
                          crosshair.isActiveAndEnabled + ", cull " + r.cull + ", materials " + r.materialCount + ", inherited alpha " + r.GetInheritedAlpha() +
                          "; health bars " + vitals.VertexCount + " corners, " + vitals.MeshBuilds + " builds.";
            if (crosshair.VertexCount > 0 && crosshair.MeshBuilds == 0) Debug.LogWarning(line);
            else Debug.Log(line);
        }

        void ShowVitals(MatchSnapshot snapshot, PlayerState me, HeroUnit unit, LocalPlayer local, SessionRunner runner)
        {
            float health = unit == null ? 0f : unit.health;
            float maxHealth = unit == null ? 1f : Mathf.Max(1f, unit.stats.Get(StatIds.MaxHealth));
            float shield = unit == null ? 0f : unit.Shield;
            bool low = health <= maxHealth * runner.ClientRules.lowHealthShare;

            var g = vitals;
            g.Clear();
            float right = g.PointCells(BarX, HealthY, CellWidth, 26f, 9f, CellGap, health, maxHealth, HealthPerCell, low ? UITheme.Danger : UITheme.Health, UITheme.VisorFaint);
            // Shield sits above health in the same 25-point cells, only as long as the shield is.
            if (shield > 0.5f) g.PointCells(BarX + 12f, HealthY + 34f, CellWidth, 8f, 3f, CellGap, shield, shield, HealthPerCell, UITheme.Shield, UITheme.VisorFaint, false);
            g.Commit();
            healthText.rectTransform.anchoredPosition = new Vector2(right + 72f, HealthY + 12f);
            healthTag.rectTransform.anchoredPosition = new Vector2(right + 64f, HealthY + 44f);
            healthText.text = Mathf.CeilToInt(health).ToString();
            healthText.color = low ? UITheme.Danger : UITheme.Text;

            var hero = HeroData.Catalog.Resolve(me.heroId);
            heroText.text = hero == null ? "" : hero.displayName.ToUpperInvariant();
            heroText.color = UITheme.ForHero(me.heroId);
            stanceText.text = local.Vaulting ? "Vaulting" : local.Crouched ? "Crouched" : local.Sprinting ? "Sprinting" : local.CarryingFlag ? "Carrying the flag: no sprint or dash" : "";

            // The level badge: the level, and a ring that fills toward the next one.
            var progression = runner.ClientRules.progression == null ? new ProgressionRuleset() : runner.ClientRules.progression;
            var prog = snapshot.progression == null ? null : snapshot.progression.Player(me.id);
            float xp = prog == null ? 0f : prog.xp;
            int level = me.level;
            float share = 1f;
            var need = progression.xpToReach;
            if (level < progression.maxLevel && need != null && level < need.Length)
            {
                float from = level - 1 < need.Length ? need[level - 1] : 0f, to = need[level];
                share = to > from ? Mathf.Clamp01((xp - from) / (to - from)) : 1f;
                xpText.text = "XP  " + Mathf.FloorToInt(xp) + " / " + Mathf.CeilToInt(to);
            }
            else xpText.text = "MAX LEVEL";
            levelText.text = level.ToString();
            if (level != lastLevel || Mathf.Abs(share - lastXpShare) > 0.004f)
            {
                lastLevel = level;
                lastXpShare = share;
                DrawBadge(share, UITheme.ForHero(me.heroId));
            }
        }

        void DrawBadge(float share, Color hero)
        {
            var g = badge;
            g.Clear();
            var hex = VisorGraphic.Regular(Badge.x, Badge.y, 46f, 6, 90f);
            g.Fill(hex, UITheme.Glass);
            g.Line(hex, 2f, hero, true, 5f);
            g.Arc(Badge.x, Badge.y, 58f, 4f, 0f, 360f, UITheme.WithAlpha(UITheme.Visor, 0.16f), 0f);
            if (share > 0f) g.Arc(Badge.x, Badge.y, 58f, 4f, 90f, 90f - 360f * share, UITheme.Visor, 5f);
            g.Commit();
        }

        void ShowWeapon(LocalPlayer local)
        {
            weaponText.text = local.WeaponName.ToUpperInvariant();
            ammoText.text = local.NoAmmo ? "" : local.Ammo.ToString();
            ammoText.color = local.Reloading ? UITheme.TextDim : local.Ammo == 0 ? UITheme.Danger : local.HealRounds ? UITheme.Health : UITheme.Text;
            magazineText.text = local.Melee ? "MELEE" : local.NoAmmo ? "NO RELOAD" : "/ " + local.Magazine;
            reloadText.text = local.Reloading ? "RELOADING" : local.Ammo == 0 ? "RELOAD (R)" : local.HealRounds ? "HEALING ROUNDS" : "";
            reloadText.color = local.Reloading ? UITheme.Warning : local.HealRounds && local.Ammo > 0 ? UITheme.Health : UITheme.Danger;

            // One cell per round for magazines up to 40, else 20 cells that each stand for several.
            int mag = Mathf.Max(1, local.Magazine);
            int cells = mag <= 40 ? mag : 20;
            var g = ammoRow;
            g.Clear();
            if (local.NoAmmo) { g.Commit(); return; } // a swing or endless darts spend nothing: no ammo bar
            g.SegmentedBar(1884f - 300f, 164f, 300f, 14f, 3f, cells, cells > 24 ? 2f : 3f, (float)local.Ammo / mag,
                           local.HealRounds ? UITheme.Health : local.Ammo * 4 <= mag ? UITheme.Warning : UITheme.Visor, UITheme.VisorFaint, true);
            g.Commit();
        }

        void ShowAbilities(MatchSnapshot snapshot, PlayerState me, HeroUnit unit)
        {
            var lib = HeroData.Abilities;
            var rules = SessionRunner.Instance.ClientRules;
            Color hero = UITheme.ForHero(me.heroId);
            ShowSelfState(unit);
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                var slot = unit != null && i < unit.slots.Count ? unit.slots[i] : null;
                var baseDef = slot == null ? null : lib.Ability(slot.abilityId);
                if (baseDef == null)
                {
                    s.lines.Clear();
                    s.lines.Commit();
                    s.status.text = s.name.text = s.key.text = "";
                    continue;
                }
                var def = AbilityParams.Resolve(baseDef, unit.changes, lib, rules.minCooldownSeconds).def;
                s.key.text = i < AbilityKeys.Length ? AbilityKeys[i] : "";
                s.name.text = def.displayName.ToUpperInvariant();

                // How full the slot is (recharge or ultimate charge), whether it is ready, and what it says.
                float fill;
                bool ready = false;
                int pips = 0;
                string status = "";
                Color edge = UITheme.VisorFaint, text = UITheme.Text;
                if (def.ultimate)
                {
                    fill = Mathf.Clamp01(unit.ultimateCharge / Mathf.Max(1f, def.ultimateCost));
                    if (unit.ultimateCharge >= def.ultimateCost - 0.01f) { status = "READY"; text = UITheme.Warning; edge = UITheme.Warning; ready = true; }
                    else status = Mathf.FloorToInt(100f * fill) + "%";
                }
                else if (def.held && (slot.raised || slot.charges > 0))
                {
                    // A held shield: how much strength it has, and whether it is up.
                    fill = Mathf.Clamp01(slot.raised ? ShieldShare(snapshot, slot) : slot.reserve);
                    ready = true;
                    edge = hero;
                    status = slot.raised ? "UP " + Mathf.RoundToInt(100f * fill) + "%" : fill < 0.999f ? Mathf.RoundToInt(100f * fill) + "%" : "";
                    if (slot.raised) text = UITheme.Warning;
                }
                else if (slot.casting) { status = "CAST"; fill = 1f; edge = hero; }
                else if (slot.charges > 0)
                {
                    fill = 1f;
                    ready = true;
                    edge = hero;
                    pips = def.charges > 1 ? slot.charges : 1;
                }
                else
                {
                    float rate = Mathf.Max(0.01f, unit.stats.Get(StatIds.CooldownRate));
                    fill = def.cooldown > 0f ? 1f - Mathf.Clamp01(slot.rechargeLeft / def.cooldown) : 0f;
                    status = (slot.rechargeLeft / rate).ToString("0.0");
                    text = UITheme.TextDim;
                }
                s.status.text = status;
                s.status.color = text;
                s.name.color = UITheme.Text;
                DrawSlot(s, fill, ready, pips, edge, def.ultimate ? UITheme.Warning : hero);
            }
        }

        /// <summary>Cloaked (hidden from the other team), the ambush window after attacking out of it, or silenced.</summary>
        void ShowSelfState(HeroUnit unit)
        {
            string text = "";
            Color color = UITheme.Visor;
            if (unit != null)
            {
                if (unit.HasStatus(AbilityLibrary.SilenceStatus)) { text = "SILENCED"; color = UITheme.Danger; }
                else if (unit.stealthed) text = "CLOAKED  //  HIDDEN FROM ENEMIES";
                else if (unit.ambushLeft > 0f) { text = "AMBUSH  " + unit.ambushLeft.ToString("0.0"); color = UITheme.Warning; }
            }
            selfStateText.text = text;
            selfStateText.color = color;
        }

        /// <summary>A raised held ability's object health as a share of full.</summary>
        static float ShieldShare(MatchSnapshot snapshot, AbilitySlotState slot)
        {
            var obj = snapshot.abilities == null ? null : snapshot.abilities.Object(slot.heldObjectId);
            return obj == null || obj.def == null || obj.def.health <= 0f ? slot.reserve : obj.health / obj.def.health;
        }

        void DrawSlot(AbilitySlot s, float fill, bool ready, int pips, Color edge, Color tone)
        {
            const float slant = 14f;
            float left = s.x - s.width * 0.5f - slant * 0.5f;
            var g = s.lines;
            g.Clear();
            var shape = VisorGraphic.Slanted(left, SlotY, s.width, s.height, slant);
            g.Fill(shape, UITheme.Glass);
            if (fill > 0f) g.Fill(VisorGraphic.Slanted(left, SlotY, s.width, s.height * fill, slant * fill), UITheme.WithAlpha(tone, ready ? 0.30f : 0.20f));
            g.Line(shape, ready ? 2.2f : 1.4f, edge, true, ready ? 6f : 0f);
            // The key, on a small slanted tag above the slot's top left corner.
            float top = SlotY + s.height;
            var tag = VisorGraphic.Slanted(left + 6f, top + 5f, 24f, 22f, 4f);
            g.Fill(tag, UITheme.Glass);
            g.Line(tag, 1.2f, UITheme.Visor, true, 0f);
            // Charges ready: a diamond for each, in the slot's middle.
            float cy = SlotY + s.height * 0.5f, cx = s.x;
            for (int p = 0; p < pips; p++)
                g.Fill(VisorGraphic.Regular(cx + (p - (pips - 1) * 0.5f) * 22f, cy, 8f, 4, 90f), tone, 5f);
            g.Commit();
        }

        void ShowChoice(MatchSnapshot snapshot, PlayerState me, LocalPlayer local)
        {
            int level = LocalPlayer.PendingLevel(snapshot, me.id, SessionRunner.Instance.ClientRules.progression);
            var hero = level > 0 ? HeroData.Catalog.Resolve(me.heroId) : null;
            var offer = hero == null ? null : hero.Level(level);
            bool show = offer != null && offer.options != null && offer.options.Length >= 2 && !ScreenshotDirector.HideTraits;
            if (choicePanel.activeSelf != show) choicePanel.SetActive(show);
            if (!show) return;
            var options = new[] { hero.Upgrade(offer.options[0]), hero.Upgrade(offer.options[1]) };
            string kind = options[0] == null ? "upgrade" : options[0].kind;
            choiceHeader.text = "LEVEL " + level + "   //   CHOOSE A " + kind.ToUpperInvariant();
            Color tone = UITheme.ForHero(me.heroId);

            var g = choiceLines;
            g.Clear();
            g.Segment(new Vector2(1420f, 676f), new Vector2(1884f, 676f), 1.4f, UITheme.Visor, 3f);
            g.Fill(VisorGraphic.Slanted(1420f, 676f, 46f, 5f, 3f), UITheme.Visor, 3f);
            for (int i = 0; i < 2; i++)
            {
                var u = options[i];
                choiceNames[i].text = u == null ? "?" : u.displayName;
                choiceTexts[i].text = u == null ? "" : u.description;
                float progress = local.ChoiceHeld == i ? local.ChoiceProgress : 0f;
                float top = OptionTop(i);
                var tag = VisorGraphic.Slanted(1420f, top - 34f, 32f, 32f, 6f);
                g.Fill(tag, progress > 0f ? UITheme.WithAlpha(tone, 0.45f) : UITheme.Glass);
                g.Line(tag, 1.6f, tone, true, 4f);
                float y = top - 120f;
                g.Segment(new Vector2(1420f, y), new Vector2(1884f, y), 1f, UITheme.VisorFaint, 0f);
                if (progress > 0f) g.Segment(new Vector2(1420f, y), new Vector2(1420f + 464f * Mathf.Clamp01(progress), y), 3f, tone, 5f);
            }
            g.Commit();
        }

        // ---- The score header ----

        void ShowHeader(MatchSnapshot snapshot, PlayerState me)
        {
            var state = snapshot == null ? null : snapshot.match;
            bool show = state != null && state.phase >= MatchPhase.Deploy;
            if (header.activeSelf != show) header.SetActive(show);
            bool overrun = show && snapshot.overrun != null;
            if (overrunRow.activeSelf != overrun)
            {
                overrunRow.SetActive(overrun);
                // The feed moves down under the Bastion cards and the status line while they show.
                feedText.rectTransform.anchoredPosition = new Vector2(1560f, overrun ? 780f : 950f);
            }
            if (!show) return;

            var mode = SessionRunner.ModeById(state.modeId);
            bool flag = snapshot.flag != null;
            int toWin = Mathf.Max(1, mode.scoreToWin);
            headerScoreA.text = state.scoreA.ToString();
            headerScoreB.text = state.scoreB.ToString();

            // The clock: time left to the cap, or the overtime call.
            if (state.phase == MatchPhase.Overtime)
            {
                headerClock.text = overrun ? "SUDDEN\nDEATH" : "OVER\nTIME";
                headerClock.fontSize = 15;
                headerClock.color = overrun ? UITheme.Danger : UITheme.Warning;
            }
            else
            {
                float left = state.phase == MatchPhase.Deploy ? mode.timeCapSeconds : Mathf.Max(0f, mode.timeCapSeconds - state.elapsed);
                int secs = Mathf.CeilToInt(left);
                headerClock.text = (secs / 60) + ":" + (secs % 60).ToString("00");
                headerClock.fontSize = 24;
                bool late = overrun && snapshot.overrun.latePush;
                headerClock.color = late ? UITheme.Warning : UITheme.Text;
            }
            headerGoal.text = overrun && snapshot.overrun.latePush ? "LATE PUSH" : (flag || overrun ? "FIRST TO " + toWin : mode.displayName.ToUpperInvariant() + "  //  " + toWin);
            headerGoal.color = overrun && snapshot.overrun.latePush ? UITheme.Warning : UITheme.Text;

            // Team bars meet at the clock: captures and Bastions count in cells, zone points fill smoothly.
            int cells = toWin <= 12 ? toWin : 1;
            var g = headerLines;
            g.Clear();
            float y = HeaderY - 4f;
            g.SegmentedBar(960f - TeamBarInner - TeamBarWidth, y, TeamBarWidth, 16f, -8f, cells, 5f, (float)state.scoreA / toWin, UITheme.TeamA, UITheme.WithAlpha(UITheme.TeamA, 0.5f), true);
            g.SegmentedBar(960f + TeamBarInner, y, TeamBarWidth, 16f, 8f, cells, 5f, (float)state.scoreB / toWin, UITheme.TeamB, UITheme.WithAlpha(UITheme.TeamB, 0.5f));
            g.Segment(new Vector2(960f - TeamBarInner - TeamBarWidth - 30f, y + 26f), new Vector2(960f - TeamBarInner, y + 26f), 1f, UITheme.VisorFaint, 0f);
            g.Segment(new Vector2(960f + TeamBarInner, y + 26f), new Vector2(960f + TeamBarInner + TeamBarWidth + 30f, y + 26f), 1f, UITheme.VisorFaint, 0f);
            g.Commit();

            bool zones = !flag && !overrun;
            if (zoneRow.activeSelf != zones) zoneRow.SetActive(zones);
            if (zones) ShowZones(state);
            bool status = flag || overrun;
            if (statusRow.activeSelf != status) statusRow.SetActive(status);
            if (flag) ShowFlagStatus(snapshot, me);
            if (overrun) ShowOverrun(snapshot, me);
        }

        /// <summary>Zone modes: a diamond per live zone, in its owner's colour, with a ring for capture progress; red when contested.</summary>
        void ShowZones(MatchState state)
        {
            var live = new List<int>();
            for (int i = 0; i < state.zones.Count; i++) if (state.zones[i].active) live.Add(i);
            while (zoneLetters.Count < live.Count)
                zoneLetters.Add(UIKit.Readout(zoneRow.transform, "ZoneLetter" + zoneLetters.Count, Vector2.zero, new Vector2(30f, 24f), "", 16, UITheme.Text, TextAnchor.MiddleCenter, true));
            var g = zoneLines;
            g.Clear();
            const float y = 900f;
            for (int k = 0; k < zoneLetters.Count; k++)
            {
                var letter = zoneLetters[k];
                if (k >= live.Count) { letter.text = ""; continue; }
                var z = state.zones[live[k]];
                float x = 960f + (k - (live.Count - 1) * 0.5f) * 56f;
                var diamond = VisorGraphic.Regular(x, y, 20f, 4, 90f);
                Color owner = UITheme.ForTeam(z.owner);
                g.Fill(diamond, z.owner == TeamId.None ? UITheme.Glass : UITheme.WithAlpha(owner, 0.45f));
                g.Line(diamond, 1.6f, z.contested ? UITheme.Danger : z.owner == TeamId.None ? UITheme.Visor : owner, true, 4f);
                if (z.progress > 0.001f && z.progress < 0.999f)
                    g.Arc(x, y, 27f, 2.5f, 90f, 90f - 360f * z.progress, UITheme.ForTeam(z.progressTeam), 3f);
                letter.rectTransform.anchoredPosition = new Vector2(x, y);
                letter.text = ((char)('A' + live[k])).ToString();
            }
            g.Commit();
        }

        void SetStatus(string line, Color color)
        {
            statusText.text = line;
            statusText.color = color;
        }

        /// <summary>Flag Rush: one line on the flag.</summary>
        void ShowFlagStatus(MatchSnapshot snapshot, PlayerState me)
        {
            var f = snapshot.flag;
            var state = snapshot.match;
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
            SetStatus(line, color);
        }

        /// <summary>Overrun: each Bastion's shield and health, and one line on what to do.</summary>
        void ShowOverrun(MatchSnapshot snapshot, PlayerState me)
        {
            var o = snapshot.overrun;
            var state = snapshot.match;
            foreach (var card in bastionCards)
            {
                var b = o.Bastion(card.team, card.tier);
                if (b == null) continue;
                Color team = UITheme.ForTeam(card.team);
                float slant = card.team == TeamId.A ? -10f : 10f;
                float left = card.x - CardWidth * 0.5f, bottom = HeaderY - CardHeight * 0.5f;
                var g = card.lines;
                g.Clear();
                var shape = VisorGraphic.Slanted(left, bottom, CardWidth, CardHeight, slant);
                g.Fill(shape, UITheme.Glass);
                g.Line(shape, 1.6f, b.alive ? team : UITheme.WithAlpha(team, 0.35f), true, b.alive ? 4f : 0f);
                float shieldShare = b.alive && b.maxShield > 0f ? b.shield / b.maxShield : 0f;
                float healthShare = b.alive && b.maxHealth > 0f ? b.health / b.maxHealth : 0f;
                g.SegmentedBar(left + 12f, bottom + 22f, CardWidth - 30f, 5f, 0f, 1, 0f, shieldShare, UITheme.Shield, UITheme.WithAlpha(UITheme.Visor, 0.18f));
                g.SegmentedBar(left + 12f, bottom + 8f, CardWidth - 30f, 10f, 0f, 1, 0f, healthShare, UITheme.Health, UITheme.VisorFaint);
                if (!b.alive)
                {
                    g.Segment(new Vector2(left + 20f, bottom + 6f), new Vector2(left + CardWidth - 20f, bottom + CardHeight - 6f), 2f, UITheme.Danger, 3f);
                    g.Segment(new Vector2(left + 20f, bottom + CardHeight - 6f), new Vector2(left + CardWidth - 20f, bottom + 6f), 2f, UITheme.Danger, 3f);
                }
                g.Commit();
                if (!b.alive) { card.state.text = "DOWN"; card.state.color = UITheme.Danger; }
                else if (b.sealedOff) { card.state.text = "SEALED"; card.state.color = UITheme.TextDim; }
                else if (b.openToHeroes) { card.state.text = "OPEN"; card.state.color = UITheme.ForTeam(card.team.Other()); }
                else card.state.text = "";
            }

            TeamId mine = me == null ? TeamId.None : me.team;
            string line;
            Color color = UITheme.Text;
            if (state.phase == MatchPhase.MatchEnd) line = state.winner == TeamId.None ? "Draw" : TeamName(state.winner) + " wins";
            else if (state.phase == MatchPhase.Deploy) line = "Push with your minions: destroy the enemy Forward Bastion, then their Core";
            else
            {
                var target = o.Bastion(mine.Other(), BastionTier.Forward);
                if (target != null && !target.alive) target = o.Bastion(mine.Other(), BastionTier.Core);
                var ours = o.Bastion(mine, BastionTier.Forward);
                if (ours != null && !ours.alive) ours = o.Bastion(mine, BastionTier.Core);
                if (mine != TeamId.None && target != null && target.openToHeroes)
                {
                    line = "Your minions are at the enemy " + TierName(target.tier) + " Bastion: hit it now";
                    color = UITheme.ForTeam(mine);
                }
                else if (mine != TeamId.None && ours != null && ours.alive && ours.openToHeroes)
                {
                    line = "Enemy minions at your " + TierName(ours.tier) + " Bastion: clear them";
                    color = UITheme.Danger;
                }
                else
                {
                    float next = snapshot.lanes == null ? 0f : snapshot.lanes.nextWaveIn;
                    line = "Next wave in " + Mathf.Max(0, Mathf.CeilToInt(next)) + "   Heroes hurt a Bastion only with their own minions near it";
                }
            }
            SetStatus(line, color);
        }

        string CenterMessage(MatchSnapshot snapshot, PlayerState me)
        {
            if (snapshot == null || me == null) return "";
            var state = snapshot.match;
            if (state.phase == MatchPhase.Deploy) return "FIGHT IN  " + Mathf.CeilToInt(state.phaseTimeLeft);
            if (state.IsPlaying && !me.alive && me.connected)
            {
                // The wait rounds up to the next wave, which the state copy carries.
                float at = me.respawnAt;
                float wave = state.nextWaveAt;
                float every = SessionRunner.Instance.ClientRules.respawnWaveSeconds;
                if (every > 0f) while (wave < at - 1e-4f) wave += every;
                else wave = at;
                return "BACK IN  " + Mathf.Max(1, Mathf.CeilToInt(wave - state.elapsed));
            }
            return "";
        }
    }
}

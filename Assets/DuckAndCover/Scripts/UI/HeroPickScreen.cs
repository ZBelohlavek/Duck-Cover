using System.Collections.Generic;
using DuckAndCover.Core;
using DuckAndCover.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckAndCover.UI
{
    /// <summary>
    /// The hero pick screen, in the same helmet-display style as the HUD: one slanted card per pickable
    /// hero in its identity colour (name, role, health, weapon, abilities), and under them the team's
    /// 4v4 lineup (tank, healer, DPS, utility or flex) showing which roles are covered and which are
    /// still open. The lineup is advice, never a lock: any hero can be picked, duplicates included.
    /// Shows in the lobby and the pick phase. Keys 1 to 4 or a click pick a hero; it sends the same
    /// <see cref="PickHeroCommand"/> the server already checks.
    /// </summary>
    public class HeroPickScreen : MonoBehaviour
    {
        const float CardWidth = 270f, CardHeight = 330f, CardBottom = 330f, CardSlant = 22f, CardStep = 310f;
        const float LineupY = 196f, ChipWidth = 176f, ChipStep = 196f;

        Canvas canvas;
        GameObject screen;
        VisorGraphic lines;
        Text title, subtitle, hint, lineupTitle;

        sealed class Card
        {
            public HeroDef hero;
            public float left;
            public RectTransform hit;
            public Text name, role, tag, others;
        }
        readonly List<Card> cards = new List<Card>();
        readonly List<Text> chipLabels = new List<Text>(), chipNames = new List<Text>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (NetSmoke.Arg("-duckSmoke") != null) return; // the network smoke test draws nothing
            var go = new GameObject("HeroPickScreen");
            DontDestroyOnLoad(go);
            go.AddComponent<HeroPickScreen>();
        }

        void Awake()
        {
            canvas = UIKit.Canvas("PickCanvas", transform, 12);
            var layer = UIKit.Layer(canvas.transform, "Pick");
            screen = layer.gameObject;

            var glass = UIKit.Lines(layer, "Glass");
            Color dark = UITheme.WithAlpha(UITheme.Glass, 0.55f), clear = UITheme.WithAlpha(UITheme.Glass, 0.15f);
            glass.Quad(new Vector2(0f, 0f), new Vector2(1920f, 0f), new Vector2(1920f, 1080f), new Vector2(0f, 1080f), dark, dark, clear, clear);
            glass.Segment(new Vector2(560f, 836f), new Vector2(1360f, 836f), 1.2f, UITheme.VisorFaint, 3f);
            glass.Fill(VisorGraphic.Regular(960f, 836f, 5f, 4, 90f), UITheme.Visor, 3f);
            glass.Commit();
            lines = UIKit.Lines(layer, "Lines");

            title = UIKit.Readout(layer, "Title", new Vector2(960f, 880f), new Vector2(800f, 56f), "SELECT YOUR HERO", 42, UITheme.Visor, TextAnchor.MiddleCenter, true);
            subtitle = UIKit.Readout(layer, "Subtitle", new Vector2(960f, 806f), new Vector2(900f, 28f), "", 20, UITheme.Text, TextAnchor.MiddleCenter);
            lineupTitle = UIKit.Readout(layer, "LineupTitle", new Vector2(960f, LineupY + 56f), new Vector2(900f, 22f), "", 16, UITheme.TextDim, TextAnchor.MiddleCenter, true);
            hint = UIKit.Readout(layer, "Hint", new Vector2(960f, 96f), new Vector2(1200f, 26f), "", 19, UITheme.Warning, TextAnchor.MiddleCenter, true);
            UIKit.Readout(layer, "Keys", new Vector2(960f, 62f), new Vector2(900f, 22f), "PRESS 1 - 4 OR CLICK A HERO", 15, UITheme.TextDim, TextAnchor.MiddleCenter);
            for (int j = 0; j < Roles.Lineup.Length; j++)
            {
                float x = ChipX(j);
                chipLabels.Add(UIKit.Readout(layer, "Role" + j, new Vector2(x + 4f, LineupY + 18f), new Vector2(ChipWidth, 24f), "", 18, UITheme.Text, TextAnchor.MiddleCenter, true));
                chipNames.Add(UIKit.Readout(layer, "RoleNames" + j, new Vector2(x, LineupY - 16f), new Vector2(ChipWidth + 10f, 20f), "", 14, UITheme.TextDim, TextAnchor.MiddleCenter));
            }
            screen.SetActive(false);
        }

        static float ChipX(int j) => 960f + (j - (Roles.Lineup.Length - 1) * 0.5f) * ChipStep;

        void BuildCards(Transform layer)
        {
            var heroes = HeroData.Pickable();
            for (int i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                float cx = 960f + (i - (heroes.Count - 1) * 0.5f) * CardStep;
                var card = new Card { hero = hero, left = cx - CardWidth * 0.5f - CardSlant * 0.5f };
                card.hit = UIKit.Rect(layer, "Hit" + hero.id, Vector2.zero, new Vector2(card.left, CardBottom), new Vector2(CardWidth + CardSlant, CardHeight));
                float mid = cx + CardSlant * 0.5f;
                UIKit.Readout(layer, "Key" + hero.id, new Vector2(card.left + CardSlant + 22f, CardBottom + CardHeight - 22f), new Vector2(30f, 26f), (i + 1).ToString(), 20, UITheme.Text, TextAnchor.MiddleCenter, true);
                card.tag = UIKit.Readout(layer, "Tag" + hero.id, new Vector2(card.left + CardWidth - 40f, CardBottom + CardHeight - 22f), new Vector2(140f, 22f), "", 15, UITheme.Warning, TextAnchor.MiddleRight, true);
                card.name = UIKit.Readout(layer, "Name" + hero.id, new Vector2(mid, CardBottom + 236f), new Vector2(CardWidth, 46f), hero.displayName.ToUpperInvariant(), 38, UITheme.ForHero(hero.id), TextAnchor.MiddleCenter, true);
                card.role = UIKit.Readout(layer, "RoleTag" + hero.id, new Vector2(mid, CardBottom + 188f), new Vector2(CardWidth, 24f), Roles.Label(hero.role).ToUpperInvariant(), 18, UITheme.Text, TextAnchor.MiddleCenter, true);
                UIKit.Readout(layer, "Stats" + hero.id, new Vector2(mid - 4f, CardBottom + 146f), new Vector2(CardWidth, 20f),
                              "HEALTH " + Mathf.RoundToInt(hero.maxHealth) + "   //   " + hero.weaponId.ToUpperInvariant(), 14, UITheme.TextDim, TextAnchor.MiddleCenter);
                var kit = UIKit.Readout(layer, "Kit" + hero.id, new Vector2(mid - 8f, CardBottom + 82f), new Vector2(CardWidth - 30f, 90f), Kit(hero), 15, UITheme.Text, TextAnchor.UpperCenter);
                kit.horizontalOverflow = HorizontalWrapMode.Wrap;
                card.others = UIKit.Readout(layer, "Others" + hero.id, new Vector2(mid - 10f, CardBottom + 18f), new Vector2(CardWidth - 20f, 20f), "", 14, UITheme.TextDim, TextAnchor.MiddleCenter);
                cards.Add(card);
            }
        }

        /// <summary>The hero's abilities, one a line, the ultimate last.</summary>
        static string Kit(HeroDef hero)
        {
            var lib = HeroData.Abilities;
            var text = new System.Text.StringBuilder();
            string[] keys = { "E", "F" };
            for (int i = 0; i < hero.abilities.Length && i < keys.Length; i++)
            {
                var a = lib.Ability(hero.abilities[i]);
                if (a != null) text.Append(keys[i]).Append("   ").Append(a.displayName).Append('\n');
            }
            var ult = lib.Ability(hero.ultimateId);
            if (ult != null) text.Append("X   ").Append(ult.displayName);
            return text.ToString();
        }

        void LateUpdate()
        {
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            var runner = SessionRunner.Instance;
            var client = runner == null ? null : runner.Client;
            var state = client == null ? null : client.State;
            var me = client == null ? null : client.Me;
            bool show = state != null && me != null && !ScreenshotDirector.HideHud &&
                        (state.phase == MatchPhase.Lobby || state.phase == MatchPhase.HeroPick);
            if (screen.activeSelf != show) screen.SetActive(show);
            if (!show) return;
            if (cards.Count == 0) BuildCards(screen.transform);

            int picked = ReadPick(client);
            if (picked >= 0 && picked < cards.Count && cards[picked].hero.id != me.heroId)
                client.Send(new PickHeroCommand { heroId = cards[picked].hero.id });

            subtitle.text = state.phase == MatchPhase.HeroPick
                ? "PICK PHASE  //  " + Mathf.CeilToInt(state.phaseTimeLeft) + " s"
                : client.IsHostPlayer ? "LOBBY  //  start the match from the panel (F1) when your team is ready" : "LOBBY  //  waiting for the host to start";
            Draw(client, state, me);
        }

        /// <summary>The card the player picked this frame (a number key or a click), or -1.</summary>
        int ReadPick(SessionClient client)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && GUIUtility.keyboardControl == 0)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) return 0;
                if (keyboard.digit2Key.wasPressedThisFrame) return 1;
                if (keyboard.digit3Key.wasPressedThisFrame) return 2;
                if (keyboard.digit4Key.wasPressedThisFrame) return 3;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return CardUnder(mouse.position.ReadValue());
            return -1;
        }

        int CardUnder(Vector2 screenPoint)
        {
            // Clicks on the developer panel are the panel's.
            if (SessionDebugPanel.Showing && screenPoint.x < SessionDebugPanel.Width + 24f) return -1;
            for (int i = 0; i < cards.Count; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(cards[i].hit, screenPoint, canvas.worldCamera)) return i;
            return -1;
        }

        void Draw(SessionClient client, MatchState state, PlayerState me)
        {
            var mouse = Mouse.current;
            int hover = mouse == null ? -1 : CardUnder(mouse.position.ReadValue());
            var g = lines;
            g.Clear();

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                Color tone = UITheme.ForHero(card.hero.id);
                bool mine = me.heroId == card.hero.id;
                var shape = VisorGraphic.Slanted(card.left, CardBottom, CardWidth, CardHeight, CardSlant);
                g.Fill(shape, mine ? UITheme.WithAlpha(tone, 0.22f) : UITheme.Glass);
                g.Line(shape, mine ? 2.6f : hover == i ? 2f : 1.4f, mine || hover == i ? tone : UITheme.WithAlpha(tone, 0.6f), true, mine ? 8f : hover == i ? 5f : 0f);
                // A band of the hero's colour across the top, and the key's tag in the corner.
                const float band = 14f;
                g.Fill(VisorGraphic.Slanted(card.left + CardSlant * (1f - band / CardHeight), CardBottom + CardHeight - band, CardWidth, band, CardSlant * band / CardHeight),
                       UITheme.WithAlpha(tone, mine ? 0.9f : 0.5f));
                var key = VisorGraphic.Slanted(card.left + CardSlant + 8f, CardBottom + CardHeight - 36f, 28f, 26f, 3f);
                g.Fill(key, UITheme.Glass);
                g.Line(key, 1.2f, UITheme.Visor, true, 0f);
                // The role on a small slanted chip under the name.
                float mid = card.left + CardSlant + CardWidth * 0.5f;
                var chip = VisorGraphic.Slanted(mid - 70f, CardBottom + 176f, 128f, 26f, 4f);
                g.Line(chip, 1.2f, tone, true, 3f);
                g.Segment(new Vector2(card.left + 30f, CardBottom + 128f), new Vector2(card.left + CardWidth - 10f, CardBottom + 128f), 1f, UITheme.VisorFaint, 0f);

                card.tag.text = mine ? "SELECTED" : "";
                var others = new List<string>();
                foreach (var p in state.players)
                    if (p.team == me.team && p.id != me.id && p.connected && p.heroId == card.hero.id) others.Add(client.NameOf(p.id));
                card.others.text = others.Count == 0 ? "" : "ALSO PICKED: " + string.Join(", ", others);
            }

            // The team's lineup: covered roles in the team's colour, open roles outlined, the flex slot dimmed once filled elsewhere.
            Color team = UITheme.ForTeam(me.team);
            var open = Roles.Open(state, me.team, HeroData.Catalog);
            lineupTitle.text = (me.team == TeamId.A ? "TEAM A" : me.team == TeamId.B ? "TEAM B" : "YOUR TEAM") + "  LINEUP  //  RECOMMENDED, NEVER REQUIRED";
            lineupTitle.color = team;
            for (int j = 0; j < Roles.Lineup.Length; j++)
            {
                string role = Roles.Lineup[j];
                var names = new List<string>();
                foreach (var p in state.players)
                {
                    if (p.team != me.team || !p.connected || string.IsNullOrEmpty(p.heroId)) continue;
                    var hero = HeroData.Catalog.Get(p.heroId);
                    if (hero != null && hero.role == role) names.Add(p.id == me.id ? "You" : client.NameOf(p.id));
                }
                bool covered = names.Count > 0, isOpen = open.Contains(role);
                float x = ChipX(j);
                var chip = VisorGraphic.Slanted(x - ChipWidth * 0.5f - 5f, LineupY, ChipWidth, 36f, 10f);
                if (covered) g.Fill(chip, UITheme.WithAlpha(team, 0.4f), 4f);
                else g.Fill(chip, UITheme.Glass);
                g.Line(chip, covered || isOpen ? 1.8f : 1.2f, covered ? team : isOpen ? UITheme.Warning : UITheme.VisorFaint, true, isOpen ? 4f : 0f);
                string label = Roles.Label(role).ToUpperInvariant();
                chipLabels[j].text = role == Roles.Utility ? label + " / FLEX" : label;
                chipLabels[j].color = covered ? UITheme.Text : isOpen ? UITheme.Warning : UITheme.TextDim;
                chipNames[j].text = covered ? string.Join(", ", names) : isOpen ? "OPEN" : "";
                chipNames[j].color = covered ? UITheme.Text : UITheme.Warning;
            }
            g.Commit();

            hint.text = Roles.Hint(state, me.team, HeroData.Catalog);
        }
    }
}

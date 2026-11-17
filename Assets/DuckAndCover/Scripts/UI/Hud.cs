using System.Collections.Generic;
using DuckAndCover.Core;
using DuckAndCover.Game;
using UnityEngine;
using UnityEngine.UI;

namespace DuckAndCover.UI
{
    /// <summary>
    /// The fighting HUD: crosshair and hit marker, health and shield, the weapon and its ammo, the
    /// respawn countdown, and who took whom down. It reads the latest state and the local player;
    /// it decides nothing. Score, zones and the level-up prompt come with later packages (WP6, WP12).
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
        Text healthText, weaponText, ammoText, stanceText, centerText, feedText;
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
            if (!(e is PlayerEliminatedEvent down) || down.disconnected) return;
            string victim = Name(down.playerId);
            string line = down.killerId >= 0 ? Name(down.killerId) + "  took down  " + victim : victim + "  went down";
            feed.Add(new KeyValuePair<float, string>(Time.unscaledTime, line));
            if (feed.Count > 5) feed.RemoveAt(0);
        }

        string Name(int playerId) =>
            client.Snapshot != null && client.Snapshot.IsDummy(playerId) ? "Practice dummy" : client.NameOf(playerId);

        void LateUpdate()
        {
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
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
            stanceText.text = local.Vaulting ? "Vaulting" : local.Crouched ? "Crouched" : local.Sprinting ? "Sprinting" : "";

            bool hit = Time.time - local.LastHitAt < HitMarkerSeconds;
            if (hitMarker.activeSelf != hit) hitMarker.SetActive(hit);
            if (hit)
            {
                Color c = local.LastHitEliminated ? UITheme.Danger : local.LastHitWasHead ? UITheme.Warning : UITheme.Text;
                foreach (var tick in hitTicks) tick.color = c;
            }
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

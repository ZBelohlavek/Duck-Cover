using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The stand-in hero body until real models arrive: legs, torso and head built from the same
    /// numbers as the hitboxes (so what you see is what you can hit), in the team's colour, with a
    /// dark visor and a gun to show which way it faces, and a health and shield bar overhead.
    /// It has no colliders: hits are decided by the rules, not by physics.
    /// </summary>
    public class PlaceholderBody : MonoBehaviour
    {
        public static readonly Color TeamA = new Color(0.24f, 0.60f, 0.95f);
        public static readonly Color TeamB = new Color(0.95f, 0.55f, 0.20f);
        static readonly Color Dark = new Color(0.08f, 0.09f, 0.11f);
        static readonly Color BarBack = new Color(0.05f, 0.05f, 0.06f);
        static readonly Color HealthColor = new Color(0.35f, 0.90f, 0.40f);
        static readonly Color ShieldColor = new Color(0.45f, 0.85f, 1.00f);
        const float BarWidth = 0.9f;

        Transform figure;
        Transform bar;
        Transform healthFill;
        Transform shieldFill;
        float crouchScale = 1f;

        public static Color ColorFor(TeamId team) => team == TeamId.B ? TeamB : TeamA;

        public static PlaceholderBody Create(string name, TeamId team, Ruleset r, bool dummy)
        {
            var go = new GameObject(name);
            var body = go.AddComponent<PlaceholderBody>();
            body.Build(team, r, dummy);
            return body;
        }

        void Build(TeamId team, Ruleset r, bool dummy)
        {
            Color main = ColorFor(team);
            Color trim = dummy ? new Color(0.85f, 0.85f, 0.88f) : Color.Lerp(main, Color.white, 0.45f);
            crouchScale = r.crouchHeight / r.standHeight;

            var figureObject = new GameObject("Figure");
            figure = figureObject.transform;
            figure.SetParent(transform, false);

            Capsule("Legs", r.legsBottom, r.legsTop, r.legsRadius, trim);
            Capsule("Torso", r.torsoBottom, r.torsoTop, r.torsoRadius, main);
            Paint.Shape(PrimitiveType.Sphere, figure, "Head", new Vector3(0f, r.headHeight, 0f), Vector3.one * (r.headRadius * 2f), main, false);
            // A full-face visor and a gun, both pointing the way the body faces (+z).
            Paint.Block(figure, "Visor", new Vector3(0f, r.headHeight + 0.02f, r.headRadius * 0.75f), new Vector3(r.headRadius * 1.5f, r.headRadius * 0.7f, r.headRadius * 0.7f), Dark, false);
            Paint.Block(figure, "Gun", new Vector3(r.torsoRadius * 0.8f, r.torsoTop - 0.12f, r.torsoRadius + 0.25f), new Vector3(0.09f, 0.12f, 0.7f), Dark, false);

            var barObject = new GameObject("Bar");
            bar = barObject.transform;
            bar.SetParent(transform, false);
            bar.localPosition = new Vector3(0f, r.standHeight + 0.35f, 0f);
            Paint.Block(bar, "Back", new Vector3(0f, 0f, 0.01f), new Vector3(BarWidth + 0.06f, 0.2f, 0.01f), BarBack, false);
            healthFill = Paint.Block(bar, "Health", new Vector3(0f, -0.035f, 0f), new Vector3(BarWidth, 0.09f, 0.012f), HealthColor, false).transform;
            shieldFill = Paint.Block(bar, "Shield", new Vector3(0f, 0.05f, 0f), new Vector3(BarWidth, 0.05f, 0.012f), ShieldColor, false).transform;

            SetLayer(transform, PhysicsWorld.PlayerLayer);
        }

        void Capsule(string name, float bottom, float top, float radius, Color color)
        {
            // Unity's capsule is 2 high and 1 wide at scale one; the hitbox capsule runs between the two centres.
            float height = (top - bottom) + radius * 2f;
            Paint.Shape(PrimitiveType.Capsule, figure, name, new Vector3(0f, (top + bottom) * 0.5f, 0f), new Vector3(radius * 2f, height * 0.5f, radius * 2f), color, false);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }

        /// <summary>Put the body somewhere, facing <paramref name="yaw"/>, standing or crouched.</summary>
        public void Pose(Vector3 feet, float yaw, bool crouched)
        {
            transform.position = feet;
            figure.localRotation = Quaternion.Euler(0f, yaw, 0f);
            figure.localScale = new Vector3(1f, crouched ? crouchScale : 1f, 1f);
        }

        /// <summary>Health and shield as shares of their maximum (0..1), and turn the bar to face a camera.</summary>
        public void ShowBars(float health, float shield, Camera viewer)
        {
            Fill(healthFill, health);
            Fill(shieldFill, shield);
            if (viewer != null) bar.rotation = Quaternion.LookRotation(bar.position - viewer.transform.position);
        }

        static void Fill(Transform fill, float share)
        {
            share = Mathf.Clamp01(share);
            var scale = fill.localScale;
            fill.localScale = new Vector3(Mathf.Max(0.0001f, BarWidth * share), scale.y, scale.z);
            var p = fill.localPosition;
            fill.localPosition = new Vector3(-BarWidth * (1f - share) * 0.5f, p.y, p.z);
        }
    }
}

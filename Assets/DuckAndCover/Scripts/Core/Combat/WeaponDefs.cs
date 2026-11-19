using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    public enum WeaponKind
    {
        /// <summary>Instant rays, checked against hitboxes as the shooter saw them (lag compensation).</summary>
        Hitscan = 0,
        /// <summary>A travelling shot with gravity that hits directly and splashes.</summary>
        Projectile = 1,
        /// <summary>
        /// A swing: every enemy within <see cref="WeaponDef.range"/> metres and <see cref="WeaponDef.arcDegrees"/>
        /// in front, as the swinger saw them. No ammo and no reload: the magazine is never spent.
        /// </summary>
        Melee = 2,
    }

    /// <summary>Hitbox regions. <see cref="HitInfo.bodyPart"/> carries one of these.</summary>
    public static class BodyParts
    {
        public const int None = 0;
        public const int Head = 1;
        public const int Body = 2;
        public const int Legs = 3;
    }

    /// <summary>
    /// One weapon. Plain data: a hero's data names its weapons (WP11), and talents may change
    /// the numbers later. Damage is per pellet for hitscan weapons.
    /// </summary>
    [Serializable]
    public class WeaponDef
    {
        public string id = "";
        public string displayName = "";
        public WeaponKind kind = WeaponKind.Hitscan;
        /// <summary>Hold the trigger to keep firing.</summary>
        public bool automatic = true;
        /// <summary>Seconds between shots.</summary>
        public float fireInterval = 0.1f;
        public int magazine = 30;
        public float reloadSeconds = 1.6f;
        /// <summary>
        /// Spends no ammo and never reloads (a hero who throws from their own hands): the fire rate is the only
        /// limit. Melee weapons are always like this.
        /// </summary>
        public bool endless;

        // ---- Hitscan ----
        public float damage = 10f;
        public float headMultiplier = 1.5f;
        public float legsMultiplier = 0.85f;
        /// <summary>Rays per shot. Pellets go out in a fixed pattern, so client and server agree.</summary>
        public int pellets = 1;
        /// <summary>Cone half-angle in degrees of the outer pellet ring (0 = dead straight).</summary>
        public float spreadDegrees = 0f;
        public float range = 120f;
        /// <summary>Full damage up to this distance, then down to <see cref="falloffMinShare"/> at <see cref="falloffEnd"/>.</summary>
        public float falloffStart = 25f;
        public float falloffEnd = 50f;
        public float falloffMinShare = 0.6f;
        /// <summary>
        /// Health a hit gives a teammate (with falloff and body part). Above 0 the weapon's shots stop
        /// on teammates and heal them instead of passing through: a healing sidearm. Hitscan only.
        /// </summary>
        public float allyHeal = 0f;

        // ---- Melee (uses damage, range and the multipliers' body share is ignored: every hit is a body hit) ----
        /// <summary>Width of the swing in degrees, left to right, around the swinger's aim.</summary>
        public float arcDegrees = 0f;
        /// <summary>Most enemies one swing hits, nearest first. 0 = everyone in the swing.</summary>
        public int maxTargets = 0;

        // ---- Projectile ----
        public float projectileSpeed = 30f;
        /// <summary>Downward pull on the projectile, m/s². 0 flies straight.</summary>
        public float projectileGravity = 9f;
        /// <summary>Full splash damage at the centre, falling to <see cref="splashMinShare"/> at the edge.</summary>
        public float splashRadius = 0f;
        public float splashDamage = 0f;
        public float splashMinShare = 0.3f;

        public WeaponDef Clone() => (WeaponDef)MemberwiseClone();

        /// <summary>Spends no ammo and has nothing to reload: <see cref="endless"/>, or a melee weapon.</summary>
        public bool NoAmmo() => endless || kind == WeaponKind.Melee;

        /// <summary>Damage share left after travelling <paramref name="distance"/> metres.</summary>
        public float Falloff(float distance)
        {
            if (distance <= falloffStart || falloffEnd <= falloffStart) return 1f;
            if (distance >= falloffEnd) return falloffMinShare;
            float t = (distance - falloffStart) / (falloffEnd - falloffStart);
            return 1f + (falloffMinShare - 1f) * t;
        }

        public float PartMultiplier(int bodyPart) =>
            bodyPart == BodyParts.Head ? headMultiplier : bodyPart == BodyParts.Legs ? legsMultiplier : 1f;

        /// <summary>Splash damage at <paramref name="distance"/> from the blast, 0 outside it.</summary>
        public float SplashAt(float distance)
        {
            if (splashRadius <= 0f || splashDamage <= 0f || distance > splashRadius) return 0f;
            float t = distance / splashRadius;
            return splashDamage * (1f + (splashMinShare - 1f) * t);
        }

        public List<string> Validate()
        {
            var e = new List<string>();
            string me = "weapon '" + id + "'";
            if (string.IsNullOrEmpty(id)) e.Add("a weapon has no id");
            if (fireInterval <= 0f) e.Add(me + ": fireInterval must be positive");
            if (magazine < 1) e.Add(me + ": magazine must be at least 1");
            if (reloadSeconds < 0f) e.Add(me + ": reloadSeconds can't be negative");
            if (kind == WeaponKind.Melee)
            {
                if (damage <= 0f) e.Add(me + ": damage must be positive");
                if (range <= 0f || range > 8f) e.Add(me + ": a swing's range must be 0..8 m");
                if (arcDegrees <= 0f || arcDegrees > 360f) e.Add(me + ": arcDegrees must be 0..360");
                if (maxTargets < 0) e.Add(me + ": maxTargets can't be negative");
                if (allyHeal != 0f) e.Add(me + ": allyHeal works on hitscan weapons only");
            }
            else if (kind == WeaponKind.Hitscan)
            {
                if (damage <= 0f) e.Add(me + ": damage must be positive");
                if (pellets < 1) e.Add(me + ": pellets must be at least 1");
                if (spreadDegrees < 0f || spreadDegrees > 45f) e.Add(me + ": spreadDegrees must be 0..45");
                if (range <= 0f) e.Add(me + ": range must be positive");
                if (falloffMinShare < 0f || falloffMinShare > 1f) e.Add(me + ": falloffMinShare must be 0..1");
                if (allyHeal < 0f) e.Add(me + ": allyHeal can't be negative");
            }
            else
            {
                if (projectileSpeed <= 0f) e.Add(me + ": projectileSpeed must be positive");
                if (damage < 0f || splashDamage < 0f || splashRadius < 0f) e.Add(me + ": damage numbers can't be negative");
                if (damage <= 0f && splashDamage <= 0f) e.Add(me + ": a projectile needs damage or splash");
                if (splashMinShare < 0f || splashMinShare > 1f) e.Add(me + ": splashMinShare must be 0..1");
                if (allyHeal != 0f) e.Add(me + ": allyHeal works on hitscan weapons only");
            }
            if (headMultiplier < 0f || legsMultiplier < 0f) e.Add(me + ": multipliers can't be negative");
            return e;
        }
    }

    /// <summary>The weapons a hero carries, in slot order (slot 0 is drawn on spawn).</summary>
    [Serializable]
    public class WeaponLoadout
    {
        /// <summary>The hero this is for. Empty = every hero without a loadout of their own.</summary>
        public string heroId = "";
        public string[] weapons = new string[0];
    }

    /// <summary>What a weapons data file holds (Data/Weapons/*.json).</summary>
    [Serializable]
    public class WeaponDataFile
    {
        public List<WeaponDef> weapons = new List<WeaponDef>();
        public List<WeaponLoadout> loadouts = new List<WeaponLoadout>();
    }

    /// <summary>Every weapon and loadout in a match, by id.</summary>
    public sealed class WeaponLibrary
    {
        readonly List<WeaponDef> weapons = new List<WeaponDef>();
        readonly List<WeaponLoadout> loadouts = new List<WeaponLoadout>();

        public IReadOnlyList<WeaponDef> Weapons => weapons;
        public IReadOnlyList<WeaponLoadout> Loadouts => loadouts;

        public static WeaponLibrary From(params WeaponDataFile[] files)
        {
            var lib = new WeaponLibrary();
            foreach (var f in files)
            {
                if (f == null) continue;
                if (f.weapons != null) foreach (var w in f.weapons) if (w != null) lib.weapons.Add(w.Clone());
                if (f.loadouts != null) foreach (var l in f.loadouts) if (l != null) lib.loadouts.Add(new WeaponLoadout { heroId = l.heroId ?? "", weapons = (string[])(l.weapons ?? new string[0]).Clone() });
            }
            return lib;
        }

        public WeaponDef Weapon(string id)
        {
            foreach (var w in weapons) if (w.id == id) return w;
            return null;
        }

        /// <summary>The hero's weapons, or the default loadout. Never null.</summary>
        public string[] LoadoutFor(string heroId)
        {
            WeaponLoadout fallback = null;
            foreach (var l in loadouts)
            {
                if (!string.IsNullOrEmpty(heroId) && l.heroId == heroId) return l.weapons;
                if (string.IsNullOrEmpty(l.heroId)) fallback = l;
            }
            return fallback != null ? fallback.weapons : new string[0];
        }

        public List<string> Validate()
        {
            var e = new List<string>();
            var ids = new HashSet<string>();
            foreach (var w in weapons)
            {
                e.AddRange(w.Validate());
                if (!ids.Add(w.id ?? "")) e.Add("weapon '" + w.id + "' is defined twice");
            }
            bool hasDefault = false;
            var heroes = new HashSet<string>();
            foreach (var l in loadouts)
            {
                if (string.IsNullOrEmpty(l.heroId)) hasDefault = true;
                if (!heroes.Add(l.heroId)) e.Add("loadout for '" + l.heroId + "' is defined twice");
                if (l.weapons.Length == 0) e.Add("loadout for '" + l.heroId + "' has no weapons");
                foreach (var id in l.weapons) if (Weapon(id) == null) e.Add("loadout for '" + l.heroId + "' names unknown weapon '" + id + "'");
            }
            if (!hasDefault) e.Add("no default loadout (heroId \"\")");
            return e;
        }
    }

    /// <summary>
    /// The weapons and the starter heroes' loadouts (WP11): a rifle, an SMG, a shotgun, a pistol, a
    /// grenade launcher (no hero carries it yet), the Mender's sidearm (it heals only with the healing rounds his
    /// Mending Rounds load), the war chief's greataxe (a melee weapon) and the vampire's Blood Darts (endless
    /// thrown darts: no ammo, no reload). Loadouts are keyed by
    /// hero id; heroId "" is the default. Data/Weapons/starter_weapons.json is the same table as data; a
    /// test keeps them equal.
    /// </summary>
    public static class StarterWeapons
    {
        public static WeaponDataFile File() => new WeaponDataFile
        {
            weapons =
            {
                new WeaponDef { id = "rifle", displayName = "Rifle", automatic = true, fireInterval = 0.11f, magazine = 28, reloadSeconds = 1.7f,
                    damage = 18f, headMultiplier = 1.6f, legsMultiplier = 0.85f, range = 150f, falloffStart = 30f, falloffEnd = 60f, falloffMinShare = 0.6f },
                new WeaponDef { id = "smg", displayName = "SMG", automatic = true, fireInterval = 0.07f, magazine = 40, reloadSeconds = 1.5f,
                    damage = 11f, headMultiplier = 1.5f, legsMultiplier = 0.85f, range = 80f, falloffStart = 12f, falloffEnd = 30f, falloffMinShare = 0.5f },
                new WeaponDef { id = "shotgun", displayName = "Shotgun", automatic = false, fireInterval = 0.8f, magazine = 6, reloadSeconds = 2.2f,
                    damage = 9f, headMultiplier = 1.25f, legsMultiplier = 0.85f, pellets = 9, spreadDegrees = 4.5f, range = 40f, falloffStart = 7f, falloffEnd = 18f, falloffMinShare = 0.3f },
                new WeaponDef { id = "pistol", displayName = "Pistol", automatic = false, fireInterval = 0.22f, magazine = 12, reloadSeconds = 1.2f,
                    damage = 26f, headMultiplier = 1.75f, legsMultiplier = 0.85f, range = 100f, falloffStart = 20f, falloffEnd = 45f, falloffMinShare = 0.55f },
                new WeaponDef { id = "launcher", displayName = "Grenade launcher", kind = WeaponKind.Projectile, automatic = false, fireInterval = 0.75f, magazine = 4, reloadSeconds = 2.4f,
                    damage = 45f, headMultiplier = 1f, legsMultiplier = 1f, projectileSpeed = 32f, projectileGravity = 9f,
                    splashRadius = 3.5f, splashDamage = 70f, splashMinShare = 0.3f },
                new WeaponDef { id = "greataxe", displayName = "Greataxe", kind = WeaponKind.Melee, automatic = true, fireInterval = 0.75f, magazine = 1, reloadSeconds = 0f,
                    damage = 60f, headMultiplier = 1f, legsMultiplier = 1f, range = 3.2f, arcDegrees = 100f },
                new WeaponDef { id = "mender_sidearm", displayName = "Mending sidearm", automatic = true, fireInterval = 0.16f, magazine = 18, reloadSeconds = 1.4f,
                    damage = 15f, headMultiplier = 1.5f, legsMultiplier = 0.85f, range = 60f, falloffStart = 15f, falloffEnd = 35f, falloffMinShare = 0.5f },
                new WeaponDef { id = "blood_darts", displayName = "Blood Darts", kind = WeaponKind.Projectile, automatic = true, fireInterval = 0.25f, magazine = 1, reloadSeconds = 0f, endless = true,
                    damage = 28f, headMultiplier = 1.75f, legsMultiplier = 0.85f, projectileSpeed = 75f, projectileGravity = 0f },
            },
            loadouts =
            {
                new WeaponLoadout { heroId = "", weapons = new[] { "rifle", "pistol" } },
                new WeaponLoadout { heroId = "bulwark", weapons = new[] { "shotgun", "pistol" } },
                new WeaponLoadout { heroId = "lancer", weapons = new[] { "rifle", "pistol" } },
                new WeaponLoadout { heroId = "mender", weapons = new[] { "mender_sidearm" } },
                new WeaponLoadout { heroId = "rigger", weapons = new[] { "smg", "pistol" } },
                new WeaponLoadout { heroId = "warchief", weapons = new[] { "greataxe" } },
                new WeaponLoadout { heroId = "vampire", weapons = new[] { "blood_darts" } },
            },
        };

        public static WeaponLibrary Library() => WeaponLibrary.From(File());
    }
}

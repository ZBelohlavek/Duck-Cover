using System.Collections.Generic;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// An environment theme: the palette, sky, light and dressing that <see cref="MapDresser"/> lays over
    /// any map's blocks. Themes dress blocks by their kind, never by their names, so one theme fits any
    /// layout and one layout can wear any theme. A map picks its theme with <c>MapDef.theme</c>.
    ///
    /// Readability rule: environments stay off the team colours (saturated blue and orange) on anything
    /// big, so heroes and team markings always stand out against the world.
    /// </summary>
    public class EnvTheme
    {
        public string id;
        public string displayName;
        public int seed = 1;

        // ---- Surfaces ----
        public Color groundA, groundB, groundSpeck;
        public Color patch;                 // moss, frost or ash patches on the ground
        public Color paving, pavingMortar;  // flat stone under each zone
        public Color stone, stoneMortar;    // building walls
        public Color trimBase;              // base and cap courses on building walls, corner posts
        public Color glow;                  // glowing trim strips
        public Color rock, rockCrack;       // outer walls and the cliffs around the map
        public Color[] containers;          // big crates (shipping containers), picked per block
        public Color wood, woodDark;        // small crates
        public Color cover, coverStripe;    // waist-high cover
        public Color deck, deckDark;        // raised platforms and ramps
        public Color shutter, shutterBand;  // doors that open with the map shift
        public Color steel;                 // overhead gantries

        // ---- Plants ----
        public Color trunk, trunkDark;
        public Color[] canopy;              // big tree caps, picked per tree
        public Color leaf;                  // ferns and hanging vines
        public Color bulb;                  // glowing fruit under the caps

        // ---- Sky and light ----
        public Color skyZenith, skyMid, skyHorizon, skyBelow;
        public Color cloud, cloudShade;
        public Color planet, planetBand, planetRing, moon;
        /// <summary>Planet direction as compass yaw and elevation in degrees, and its radius in degrees.</summary>
        public float planetYaw = 200f, planetElevation = 24f, planetSize = 16f;
        public Color sun;
        public Vector3 sunEuler = new Vector3(42f, -140f, 0f);
        public float sunIntensity = 1.35f;
        public Color ambientSky, ambientEquator, ambientGround;
        public Color fog;
        public float fogStart = 45f, fogEnd = 420f;

        // ---- Dressing switches ----
        public bool mushroomTrees = true;
        public bool overheadGantries = true;
        public bool vines = true;
        /// <summary>Lay flagstones over the whole floor of a region in this theme (a temple courtyard).</summary>
        public bool courtyard;
        /// <summary>A carved glyph band near the top of building walls, in <see cref="glyph"/> on the stone.</summary>
        public bool glyphs;
        public Color glyph;

        static readonly Dictionary<string, EnvTheme> all = new Dictionary<string, EnvTheme>();

        static EnvTheme()
        {
            Add(JungleMoon());
            Add(JungleTemple());
            Add(SunforgeQuarry());
            Add(CoralReactor());
        }

        static void Add(EnvTheme t) => all[t.id] = t;

        /// <summary>The theme with this id, or null (null keeps the plain greybox).</summary>
        public static EnvTheme Find(string id) => id != null && all.TryGetValue(id, out var t) ? t : null;

        public static IEnumerable<string> Ids => all.Keys;

        static Color C(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

        /// <summary>
        /// A freight yard overgrown by a jungle moon: mossy violet stone, green earth thick with moss,
        /// magenta, teal, lime and violet containers, deep purple cliffs, giant capped trees with glowing
        /// pink fruit, lime trim lights, under a teal-to-gold sky with a ringed lilac planet.
        /// </summary>
        static EnvTheme JungleMoon() => new EnvTheme
        {
            id = "jungle_moon",
            displayName = "Jungle moon",
            seed = 7,
            groundA = C(0x6E7A34), groundB = C(0x4A4A26), groundSpeck = C(0x34301E),
            patch = C(0x7AAE34),
            paving = C(0x8A86A0), pavingMortar = C(0x3A3450),
            stone = C(0x7E6E9E), stoneMortar = C(0x34284A),
            trimBase = C(0x2E2448),
            glow = C(0x7CFF8C),
            rock = C(0x5E3E78), rockCrack = C(0x2A1A3A),
            containers = new[] { C(0xB4235F), C(0x14857C), C(0x8AB82A), C(0x6A44A8) },
            wood = C(0x8A5434), woodDark = C(0x4E2C1E),
            cover = C(0xC4D4B4), coverStripe = C(0xE8418C),
            deck = C(0x3A3C68), deckDark = C(0x20203E),
            shutter = C(0xB81E3E), shutterBand = C(0xF2C230),
            steel = C(0xD9A824),
            trunk = C(0x7A3A6E), trunkDark = C(0x45203E),
            canopy = new[] { C(0x5CBF3A), C(0x2EA88A), C(0x8ACF3E) },
            leaf = C(0x3E9A30),
            bulb = C(0xFF6EB4),
            skyZenith = C(0x2A9AA0), skyMid = C(0xB8D670), skyHorizon = C(0xFFC48A), skyBelow = C(0x8A6A70),
            cloud = C(0xFFF1D8), cloudShade = C(0xE8B49A),
            planet = C(0xB48AE0), planetBand = C(0xF2A6C8), planetRing = C(0xF6DCC8), moon = C(0xF4F0DC),
            sun = C(0xFFE2B8),
            ambientSky = C(0x9AD6C8), ambientEquator = C(0xC8C49A), ambientGround = C(0x4A4A5A),
            fog = C(0xD8DCA8),
        };

        /// <summary>
        /// The temple quarter inside the jungle: warm sandstone courses with plum base and cap courses,
        /// carved glyph bands, lime trim lights and a flagstone courtyard floor. Used as an art region
        /// (see MapDef.artRegions); sky and light come from the map's own theme.
        /// </summary>
        static EnvTheme JungleTemple()
        {
            var t = JungleMoon();
            t.id = "jungle_temple";
            t.displayName = "Jungle temple";
            t.seed = 9;
            t.paving = C(0xB8A898); t.pavingMortar = C(0x5A4858);
            t.stone = C(0xE4C6A4); t.stoneMortar = C(0x6B4A54);
            t.trimBase = C(0x5A2E58);
            t.glyph = C(0x8A5A44);
            t.containers = new[] { C(0xB4235F), C(0x14857C), C(0xD9A21C), C(0x6A44A8) };
            t.cover = C(0xD8CDB4);
            t.wood = C(0x9A5E34); t.woodDark = C(0x5E3420);
            t.deck = C(0x4A3C70); t.deckDark = C(0x2A2244);
            t.courtyard = true;
            t.glyphs = true;
            return t;
        }

        /// <summary>
        /// Proposed second theme: a cliffside quarry of carved red sandstone at golden hour, brass
        /// machinery, turquoise pools, under a dusky violet sky with two moons. No giant trees; cacti-like
        /// crystal growths would come later, so it uses vines only.
        /// </summary>
        static EnvTheme SunforgeQuarry() => new EnvTheme
        {
            id = "sunforge_quarry",
            displayName = "Sunforge quarry",
            seed = 11,
            groundA = C(0xC08654), groundB = C(0x9E623C), groundSpeck = C(0x6A3E2A),
            patch = C(0xD8B070),
            paving = C(0xB8705A), pavingMortar = C(0x5A3434),
            stone = C(0xC4654A), stoneMortar = C(0x5A2A2A),
            trimBase = C(0x3E2A3A),
            glow = C(0x4FF0E0),
            rock = C(0x9A4A3E), rockCrack = C(0x4A2220),
            containers = new[] { C(0x2E8C8C), C(0xC8A040), C(0x7A3A7A), C(0x3A6A3A) },
            wood = C(0x8A5A30), woodDark = C(0x4E301C),
            cover = C(0xE0C8A0), coverStripe = C(0x2EC4B4),
            deck = C(0x3A4A5A), deckDark = C(0x1E2A34),
            shutter = C(0x7A2E6A), shutterBand = C(0xF0D040),
            steel = C(0xB88A3A),
            trunk = C(0x6A4030), trunkDark = C(0x3A2018),
            canopy = new[] { C(0x6AAA4A) },
            leaf = C(0x5A8A3A),
            bulb = C(0x4FF0E0),
            skyZenith = C(0x3A2A6A), skyMid = C(0xC86A8A), skyHorizon = C(0xFFB070), skyBelow = C(0x5A3A4A),
            cloud = C(0xFFD8B0), cloudShade = C(0xB86A80),
            planet = C(0xF0E8D0), planetBand = C(0xD8C8B0), planetRing = C(0xE8D8C0), moon = C(0xC8E0F0),
            planetSize = 9f, planetElevation = 30f,
            sun = C(0xFFC890), sunEuler = new Vector3(22f, -120f, 0f), sunIntensity = 1.5f,
            ambientSky = C(0x8A70B0), ambientEquator = C(0xE8A080), ambientGround = C(0x5A3A3A),
            fog = C(0xE8A888),
            mushroomTrees = false, overheadGantries = true, vines = true,
        };

        /// <summary>
        /// Proposed third theme: a reactor station grown over by a dry coral reef on a tidal world:
        /// bone-white concrete, coral pinks and reds, sea-green glass, under a bright aqua sky with a
        /// huge pale moon. Tree caps become coral fans.
        /// </summary>
        static EnvTheme CoralReactor() => new EnvTheme
        {
            id = "coral_reactor",
            displayName = "Coral reactor",
            seed = 23,
            groundA = C(0xE0D2B0), groundB = C(0xC8B48A), groundSpeck = C(0x8A7A60),
            patch = C(0x5AB8A0),
            paving = C(0xF0E8D8), pavingMortar = C(0x8A8070),
            stone = C(0xEDE4D0), stoneMortar = C(0x8A7E70),
            trimBase = C(0x2E5A5A),
            glow = C(0xFF5A8A),
            rock = C(0xD0706A), rockCrack = C(0x7A3040),
            containers = new[] { C(0x2EA0A0), C(0xE8C840), C(0xB04A8A), C(0x5A8A3A) },
            wood = C(0xA07850), woodDark = C(0x5A4030),
            cover = C(0xB0D8D0), coverStripe = C(0xE84A6A),
            deck = C(0x2E6A6A), deckDark = C(0x1A3A3A),
            shutter = C(0xD84A5A), shutterBand = C(0xFFFFFF),
            steel = C(0xE8E0D0),
            trunk = C(0xE07A7A), trunkDark = C(0x8A3A4A),
            canopy = new[] { C(0xFF8A9A), C(0xD84A7A), C(0xB060C0) },
            leaf = C(0x40A080),
            bulb = C(0xFFE070),
            skyZenith = C(0x3AB0D8), skyMid = C(0x9AE0E0), skyHorizon = C(0xF8F0D8), skyBelow = C(0x6A9AA0),
            cloud = C(0xFFFFFF), cloudShade = C(0xB8D8E0),
            planet = C(0xF0F0E8), planetBand = C(0xD8E0E0), planetRing = C(0xFFFFFF), moon = C(0xFFF0D0),
            planetSize = 22f, planetElevation = 34f,
            sun = C(0xFFF4E0), sunEuler = new Vector3(55f, -160f, 0f), sunIntensity = 1.3f,
            ambientSky = C(0xA0E0F0), ambientEquator = C(0xF0E8D8), ambientGround = C(0x8A8070),
            fog = C(0xD8F0F0),
            mushroomTrees = true, overheadGantries = false, vines = true,
        };
    }
}

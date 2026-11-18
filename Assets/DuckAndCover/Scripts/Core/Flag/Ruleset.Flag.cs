using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Flag Rush numbers (WP21). Plain fields on the shared Ruleset, so Ruleset.Clone (MemberwiseClone)
    // copies them. Validated by ValidateFlag, which FlagServer calls; Ruleset.ValidateModules stays
    // free for another module. What differs between modes (captures to win, the shift at 3) is in
    // ModeDef.FlagRush.

    public partial class Ruleset
    {
        // ---- The flag ----
        /// <summary>The first flag appears this long after Play starts.</summary>
        public float flagFirstSpawnSeconds = 20f;
        /// <summary>After a capture, the next flag appears this long later.</summary>
        public float flagAfterCaptureSeconds = 10f;
        /// <summary>A dropped flag nobody touches for this long vanishes and appears at a new spot.</summary>
        public float flagDropResetSeconds = 15f;
        /// <summary>A vanished flag reappears this long later (0 = at once).</summary>
        public float flagAfterResetSeconds = 0f;
        /// <summary>Feet this close (flat metres) to the flag pick it up.</summary>
        public float flagPickupRadius = 1.5f;
        /// <summary>...and no more than this far above or below it.</summary>
        public float flagPickupHeight = 2f;
        /// <summary>The carrier can't sprint: their top speed is walk speed (they can still shoot and use non-movement abilities).</summary>
        public bool flagCarrierNoSprint = true;

        // ---- Flag experience (Progression pays it; the scale matches xpPerCapture = 100) ----
        public float xpFlagPickup = 25f;
        /// <summary>Per second of carrying.</summary>
        public float xpFlagCarryPerSecond = 4f;
        /// <summary>The carrier who scores. The most a flag pays.</summary>
        public float xpFlagCapture = 150f;
        /// <summary>Each living teammate within <see cref="flagAssistRadius"/> of the carrier when they score.</summary>
        public float xpFlagCaptureAssist = 50f;
        public float flagAssistRadius = 15f;
        /// <summary>Whoever brings the carrier down (on top of the elimination itself).</summary>
        public float xpFlagStopCarrier = 75f;

        /// <summary>Problems with the flag numbers, empty when usable.</summary>
        public List<string> ValidateFlag()
        {
            var e = new List<string>();
            if (flagFirstSpawnSeconds < 0f || flagAfterCaptureSeconds < 0f || flagAfterResetSeconds < 0f) e.Add("flag spawn delays can't be negative");
            if (flagDropResetSeconds <= 0f) e.Add("flagDropResetSeconds must be positive");
            if (flagPickupRadius <= 0f || flagPickupHeight <= 0f) e.Add("the flag pickup size must be positive");
            if (xpFlagPickup < 0f || xpFlagCarryPerSecond < 0f || xpFlagCapture < 0f || xpFlagCaptureAssist < 0f || xpFlagStopCarrier < 0f)
                e.Add("flag experience can't be negative");
            if (flagAssistRadius < 0f) e.Add("flagAssistRadius can't be negative");
            return e;
        }
    }
}

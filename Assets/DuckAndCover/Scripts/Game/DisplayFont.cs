using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The display font shared by the HUD and the bars drawn over heads: a squared, instrument-style face
    /// installed with the system when there is one (Bahnschrift ships with Windows 10 and 11; nothing is
    /// bundled), else Unity's built-in font.
    /// </summary>
    public static class DisplayFont
    {
        static readonly string[] Wanted = { "Bahnschrift", "Bahnschrift SemiBold", "DIN Alternate", "Roboto Condensed", "DejaVu Sans Condensed" };
        static Font font;

        public static Font Font
        {
            get
            {
                if (font == null) font = Pick();
                return font;
            }
        }

        static Font Pick()
        {
            try
            {
                var installed = Font.GetOSInstalledFontNames();
                if (installed != null)
                    foreach (var want in Wanted)
                        if (System.Array.IndexOf(installed, want) >= 0)
                        {
                            var os = Font.CreateDynamicFontFromOSFont(want, 32);
                            if (os != null) return os;
                        }
            }
            catch (System.Exception)
            {
                // Some platforms can't list system fonts; the built-in font is fine.
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}

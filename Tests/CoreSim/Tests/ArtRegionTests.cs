using DuckAndCover.Core;

/// <summary>Art regions pick a theme by footprint; they are art only and must not touch the layout.</summary>
static class ArtRegionTests
{
    [Test]
    static void GantryYardHasATempleHeartInAJungle()
    {
        var map = GantryYard.Build();
        Check.Equal("jungle_moon", map.theme, "map theme");
        Check.Equal("jungle_temple", map.ThemeAt(0f, 0f), "middle square");
        Check.Equal("jungle_temple", map.ThemeAt(-14f, -17f), "team A's hall");
        Check.Equal("jungle_temple", map.ThemeAt(14f, 17f), "team B's hall");
        Check.Equal("jungle_moon", map.ThemeAt(-16f, -32f), "point A");
        Check.Equal("jungle_moon", map.ThemeAt(0f, -58f), "team A spawn");
        Check.Equal("jungle_moon", map.ThemeAt(0f, 58f), "team B spawn");
    }

    [Test]
    static void ArtRegionsAreSymmetric()
    {
        // Both teams' halves should look alike: every region has a turned twin (or is its own).
        var map = GantryYard.Build();
        foreach (var r in map.artRegions)
        {
            bool twin = false;
            foreach (var o in map.artRegions)
                if (o.theme == r.theme && o.minX == -r.maxX && o.maxX == -r.minX && o.minZ == -r.maxZ && o.maxZ == -r.minZ) twin = true;
            Check.True(twin, "region " + r.theme + " has a turned twin");
        }
    }
}

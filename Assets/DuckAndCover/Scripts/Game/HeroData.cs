using System;
using System.Collections.Generic;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The hero and ability data every machine uses, loaded once from Data/Resources/Heroes and
    /// Data/Resources/Abilities (a Resources folder, so builds carry the files). The host runs the
    /// rules on it; every machine reads it to name abilities and draw what they do.
    /// </summary>
    public static class HeroData
    {
        /// <summary>The hero a player with an unknown hero id plays.</summary>
        public const string FallbackHeroId = "test_hero";

        static HeroCatalog catalog;
        static AbilityLibrary abilities;

        public static HeroCatalog Catalog
        {
            get { if (catalog == null) Load(); return catalog; }
        }

        public static AbilityLibrary Abilities
        {
            get { if (abilities == null) Load(); return abilities; }
        }

        /// <summary>The heroes a player can pick: everyone but the test hero, in file order.</summary>
        public static List<HeroDef> Pickable()
        {
            var list = new List<HeroDef>();
            foreach (var h in Catalog.heroes) if (h != null && h.id != FallbackHeroId) list.Add(h);
            return list;
        }

        static void Load()
        {
            var heroes = new List<HeroDef>();
            var heroFiles = Resources.LoadAll<TextAsset>("Heroes") ?? new TextAsset[0];
            Array.Sort(heroFiles, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var file in heroFiles)
            {
                var hero = JsonUtility.FromJson<HeroDef>(file.text);
                if (hero != null) heroes.Add(hero);
            }
            catalog = new HeroCatalog(heroes, FallbackHeroId);

            var files = new List<AbilityDataFile>();
            var abilityFiles = Resources.LoadAll<TextAsset>("Abilities") ?? new TextAsset[0];
            Array.Sort(abilityFiles, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var file in abilityFiles)
            {
                var data = JsonUtility.FromJson<AbilityDataFile>(file.text);
                if (data != null) files.Add(data);
            }
            abilities = AbilityLibrary.From(files.ToArray());

            var problems = abilities.Validate();
            problems.AddRange(catalog.Validate(new ProgressionRuleset()));
            if (problems.Count > 0) Debug.LogError("Duck and Cover: hero data has problems: " + string.Join("; ", problems));
            else Debug.Log("Duck and Cover: loaded " + heroes.Count + " heroes and " + files.Count + " ability files.");
        }
    }
}

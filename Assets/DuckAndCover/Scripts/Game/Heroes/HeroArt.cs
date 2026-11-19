using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// How a hero's model is set up: written next to the model as Resources/HeroArt/&lt;heroId&gt;/art.json.
    /// The numbers come from the Blender rig that made the clips (art/lancer-anim-v1 for Lancer).
    /// </summary>
    [Serializable]
    public class HeroArtDef
    {
        public string heroId = "";
        /// <summary>The rigged model and its clips, the first-person arms, and the painted texture, by file name in the same folder.</summary>
        public string model = "";
        public string firstPerson = "";
        public string texture = "";
        /// <summary>The first-person model has its own paint (it is cut from the full-detail meshes).</summary>
        public string firstPersonTexture = "";
        /// <summary>Scales the model so its head and body line up with the rules' hitboxes.</summary>
        public float scale = 1f;
        /// <summary>Clip names start with this in the model file (after Blender's "Armature|").</summary>
        public string clipPrefix = "";
        /// <summary>Clips that loop.</summary>
        public string[] loops = new string[0];
        /// <summary>The ground speed each moving clip was made at (metres a second, before <see cref="scale"/>).</summary>
        public ClipSpeed[] speeds = new ClipSpeed[0];
        /// <summary>Jump clip: when the feet leave the ground, the airborne pose to hold, and when they land (seconds).</summary>
        public float jumpTakeoff, jumpAir, jumpLand;
        /// <summary>Which clip each ability plays.</summary>
        public AbilityClip[] abilities = new AbilityClip[0];
    }

    [Serializable]
    public class ClipSpeed { public string clip = ""; public float speed; }

    [Serializable]
    public class AbilityClip { public string ability = ""; public string clip = ""; }

    /// <summary>A hero's loaded art: the model to instantiate, its clips by short name, the first-person arms and the paint.</summary>
    public sealed class HeroArtSet
    {
        public HeroArtDef def;
        public GameObject model;
        public GameObject firstPerson;
        public Material paint;
        public Material firstPersonPaint;
        public readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();

        public bool Loops(string clip) => Array.IndexOf(def.loops, clip) >= 0;

        public float Speed(string clip)
        {
            foreach (var s in def.speeds) if (s.clip == clip) return s.speed;
            return 0f;
        }

        public string ClipFor(string abilityId)
        {
            foreach (var a in def.abilities) if (a.ability == abilityId) return a.clip;
            return null;
        }
    }

    /// <summary>
    /// Finds hero models under Resources/HeroArt. A hero without a folder there keeps the stand-in body
    /// (<see cref="PlaceholderBody"/>) and the block gun in first person.
    /// </summary>
    public static class HeroArt
    {
        public const string Folder = "HeroArt/";
        static readonly Dictionary<string, HeroArtSet> loaded = new Dictionary<string, HeroArtSet>();

        /// <summary>The hero's art, or null if it has none (yet).</summary>
        public static HeroArtSet For(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            if (loaded.TryGetValue(heroId, out var found)) return found;
            loaded[heroId] = null;

            var json = Resources.Load<TextAsset>(Folder + heroId + "/art");
            if (json == null) return null;
            var def = JsonUtility.FromJson<HeroArtDef>(json.text);
            string dir = Folder + heroId + "/";
            var set = new HeroArtSet { def = def };
            set.model = Resources.Load<GameObject>(dir + def.model);
            set.firstPerson = string.IsNullOrEmpty(def.firstPerson) ? null : Resources.Load<GameObject>(dir + def.firstPerson);
            var texture = string.IsNullOrEmpty(def.texture) ? null : Resources.Load<Texture2D>(dir + def.texture);
            set.paint = texture != null ? Paint.Textured(texture) : Paint.For(new Color(0.6f, 0.6f, 0.65f));
            var fpTexture = string.IsNullOrEmpty(def.firstPersonTexture) ? null : Resources.Load<Texture2D>(dir + def.firstPersonTexture);
            set.firstPersonPaint = fpTexture != null ? Paint.Textured(fpTexture) : set.paint;
            if (set.model == null)
            {
                Debug.LogError("Duck and Cover: hero art for " + heroId + " names model " + def.model + ", which is not in Resources/" + dir + ".");
                return null;
            }
            // The clips are sub-assets of the model file.
            foreach (var clip in Resources.LoadAll<AnimationClip>(dir + def.model))
            {
                if (clip == null || clip.name.StartsWith("__preview__")) continue;
                string name = clip.name;
                int bar = name.LastIndexOf('|');
                if (bar >= 0) name = name.Substring(bar + 1);
                if (!string.IsNullOrEmpty(def.clipPrefix) && name.StartsWith(def.clipPrefix)) name = name.Substring(def.clipPrefix.Length);
                set.clips[name] = clip;
            }
            if (set.clips.Count == 0) Debug.LogError("Duck and Cover: hero art for " + heroId + " has no animation clips in " + def.model + ".");
            else Debug.Log("Duck and Cover: hero art for " + heroId + ": " + set.clips.Count + " clips (" + string.Join(", ", set.clips.Keys) + ").");
            loaded[heroId] = set;
            return set;
        }

        /// <summary>Put every renderer under <paramref name="root"/> on the hero's painted material.</summary>
        public static void ApplyPaint(GameObject root, Material paint, bool castShadows)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = paint;
                r.sharedMaterials = mats.Length > 0 ? mats : new[] { paint };
                r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>The first transform under <paramref name="root"/> with this name, searching depth first.</summary>
        public static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var hit = Find(child, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DuckAndCover.Game
{
    /// <summary>
    /// A hero's model as other players see it: picks a clip from how the body moves (idle, run, strafes,
    /// backpedal, sprint, crouch, jump, vault), plays one-shots for reloads and abilities, holds the rifle
    /// shouldered while firing, and bends the spine to the aim's pitch. Everything here is looks: the
    /// rules' hitboxes decide hits, and the model is scaled to match them.
    ///
    /// Clips play through a PlayableGraph evaluated by hand each frame, so no Animator Controller asset
    /// is needed and the clip times are ours (loops wrap, moving clips play at the body's speed).
    /// </summary>
    public sealed class HeroModel : MonoBehaviour
    {
        const float Fade = 0.15f;
        const float StillSpeed = 0.4f;
        const float AimHoldSeconds = 0.7f;

        sealed class Track
        {
            public string name;
            public AnimationClipPlayable playable;
            public float length;
            public bool loop;
            public float time;
            public float weight;
        }

        // ---- What the body sets each frame ----
        public Vector3 velocity;
        public float pitch;
        public bool crouched;
        public bool grounded = true;
        public bool sprinting;
        public bool vaulting;
        public bool reloading;

        public HeroArtSet Art { get; private set; }
        /// <summary>The end of the barrel (a bone that rides the rifle), for tracers and the flash.</summary>
        public Transform Muzzle { get; private set; }

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly List<Track> tracks = new List<Track>();
        readonly Dictionary<string, int> index = new Dictionary<string, int>();
        Transform spine, chest, upperChest, weapon;
        Transform flash;
        float flashLeft;

        int target = -1;
        int oneShot = -1;
        float oneShotSpeed = 1f;
        bool wasAirborne, wasVaulting, wasReloading;
        float aimUntil;
        float recoil;
        bool checkedFacing;

        string frozenClip;
        float frozenTime;

        /// <summary>Build the hero's model under <paramref name="parent"/>, or null if the hero has no model.</summary>
        public static HeroModel Create(Transform parent, HeroArtSet art, bool castShadows = true)
        {
            if (art == null || art.model == null) return null;
            var holder = new GameObject("Model");
            holder.transform.SetParent(parent, false);
            holder.transform.localScale = Vector3.one * art.def.scale;
            var instance = Instantiate(art.model, holder.transform, false);
            instance.name = art.def.model;
            HeroArt.ApplyPaint(instance, art.paint, castShadows);
            var model = holder.AddComponent<HeroModel>();
            model.Build(art, instance);
            return model;
        }

        void Build(HeroArtSet art, GameObject instance)
        {
            Art = art;
            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            graph = PlayableGraph.Create("Hero " + art.def.heroId);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Body", animator);
            mixer = AnimationMixerPlayable.Create(graph, art.clips.Count);
            output.SetSourcePlayable(mixer);
            foreach (var pair in art.clips)
            {
                var playable = AnimationClipPlayable.Create(graph, pair.Value);
                playable.SetApplyFootIK(false);
                int i = tracks.Count;
                graph.Connect(playable, 0, mixer, i);
                mixer.SetInputWeight(i, 0f);
                index[pair.Key] = i;
                tracks.Add(new Track { name = pair.Key, playable = playable, length = Mathf.Max(0.01f, pair.Value.length), loop = art.Loops(pair.Key) });
            }

            spine = HeroArt.Find(instance.transform, "Spine");
            chest = HeroArt.Find(instance.transform, "Chest");
            upperChest = HeroArt.Find(instance.transform, "UpperChest");
            weapon = HeroArt.Find(instance.transform, "Weapon");
            Muzzle = HeroArt.Find(instance.transform, "Muzzle");
            if (Muzzle != null) flash = MuzzleFlash.Create(Muzzle, 0.22f).transform;
            target = Index("idle");
            if (target >= 0) tracks[target].weight = 1f;
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        int Index(string clip) => clip != null && index.TryGetValue(clip, out int i) ? i : -1;

        // ---- Things that happen ----

        /// <summary>A shot left this hero's gun: kick, flash, and hold the rifle up for a moment.</summary>
        public void Fired(float flashSeconds = 0.05f)
        {
            recoil = 1f;
            flashLeft = flashSeconds;
            aimUntil = Time.time + AimHoldSeconds;
        }

        /// <summary>An ability went off: play its clip, if the hero has one for it.</summary>
        public void UsedAbility(string abilityId)
        {
            if (Art == null) return;
            PlayOnce(Art.ClipFor(abilityId), 0f, 1f);
        }

        void PlayOnce(string clip, float from, float speed)
        {
            int i = Index(clip);
            if (i < 0) return;
            oneShot = i;
            oneShotSpeed = speed;
            tracks[i].time = from;
        }

        /// <summary>For screenshots: hold one clip at a time (seconds), ignoring movement. Null lets it move again.</summary>
        public void Freeze(string clip, float seconds)
        {
            frozenClip = clip;
            frozenTime = seconds;
        }

        // ---- Each frame ----

        void LateUpdate()
        {
            if (!graph.IsValid() || tracks.Count == 0) return;
            float dt = Time.deltaTime;
            float speedScale = 1f / Mathf.Max(0.01f, Art.def.scale);

            if (frozenClip != null)
            {
                int f = Index(frozenClip);
                if (f >= 0)
                {
                    foreach (var t in tracks) t.weight = 0f;
                    tracks[f].weight = 1f;
                    tracks[f].time = Mathf.Min(frozenTime, tracks[f].length);
                    target = f;
                    Apply();
                    Procedural(dt);
                    return;
                }
            }

            Choose(dt, speedScale);

            // Weights fade toward the clip chosen.
            float total = 0f;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                t.weight = Mathf.MoveTowards(t.weight, i == target ? 1f : 0f, dt / Fade);
                total += t.weight;
            }
            if (total <= 0f) tracks[target].weight = total = 1f;
            foreach (var t in tracks) t.weight /= total;
            Apply();
            Procedural(dt);
        }

        void Choose(float dt, float speedScale)
        {
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 local = Quaternion.Inverse(transform.rotation) * flat;
            float speed = flat.magnitude;

            // One-shots: a vault or a reload starting, or an ability already playing.
            if (vaulting && !wasVaulting) PlayOnce("vault", 0f, 1f);
            if (reloading && !wasReloading && speed < StillSpeed && !crouched) PlayOnce("reload", 0f, 1f);
            wasVaulting = vaulting;
            wasReloading = reloading;

            bool airborne = !grounded && !vaulting;
            int jump = Index("jump");
            if (airborne && jump >= 0 && oneShot < 0)
            {
                var t = tracks[jump];
                t.time = wasAirborne ? Mathf.Min(t.time + dt, Art.def.jumpAir) : Art.def.jumpTakeoff;
                target = jump;
                wasAirborne = true;
                return;
            }
            if (wasAirborne && !airborne && jump >= 0 && oneShot < 0) PlayOnce("jump", Art.def.jumpLand, 1f);
            wasAirborne = airborne;

            if (oneShot >= 0)
            {
                var t = tracks[oneShot];
                t.time += dt * oneShotSpeed;
                if (t.time < t.length - Fade * 0.5f)
                {
                    target = oneShot;
                    return;
                }
                oneShot = -1;
            }

            string clip;
            float clipSpeed = 0f;
            if (speed < StillSpeed)
                clip = Time.time < aimUntil ? "aim" : crouched ? "crouch_idle" : "idle";
            else if (crouched) clip = "crouch_walk";
            else if (sprinting) clip = "sprint";
            else
            {
                float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(angle) <= 50f) clip = "run";
                else if (Mathf.Abs(angle) >= 130f) clip = "backpedal";
                else clip = angle > 0f ? "strafe_right" : "strafe_left";
            }
            if (Index(clip) < 0) clip = "idle";
            target = Index(clip);
            clipSpeed = Art.Speed(clip);

            // Loops run on: moving clips at the body's speed, so feet don't slide.
            float rate = clipSpeed > 0f ? Mathf.Clamp(speed * speedScale / clipSpeed, 0.5f, 1.8f) : 1f;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (!t.loop || (i != target && t.weight <= 0f)) continue;
                t.time = Mathf.Repeat(t.time + dt * (i == target ? rate : 1f), t.length);
            }
        }

        void Apply()
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                mixer.SetInputWeight(i, t.weight);
                t.playable.SetTime(t.time);
            }
            graph.Evaluate(0f);
            if (!checkedFacing) CheckFacing();
        }

        /// <summary>
        /// The model should face +z like the body. If the rifle points backwards after the first pose, the
        /// export's axes were turned: say so once and turn the model round.
        /// </summary>
        void CheckFacing()
        {
            checkedFacing = true;
            if (Muzzle == null) return;
            Vector3 muzzle = transform.InverseTransformPoint(Muzzle.position);
            if (muzzle.z >= 0f) return;
            Debug.LogWarning("Duck and Cover: " + Art.def.heroId + "'s model faced backwards after import; turned it round.");
            foreach (Transform child in transform) child.localRotation = Quaternion.Euler(0f, 180f, 0f) * child.localRotation;
        }

        /// <summary>After the clips: lean the spine to the aim and kick the chest back on each shot.</summary>
        void Procedural(float dt)
        {
            Vector3 right = transform.right;
            float bend = Mathf.Clamp(pitch, -60f, 60f);
            Turn(spine, right, bend * 0.25f);
            Turn(chest, right, bend * 0.3f);
            Turn(upperChest, right, bend * 0.45f - recoil * 4f);
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 9f);

            flashLeft -= dt;
            if (flash == null) return;
            if (flash.gameObject.activeSelf != flashLeft > 0f) flash.gameObject.SetActive(flashLeft > 0f);
            // The bone's own axes come from Blender: point the flash along the barrel instead.
            Vector3 along = weapon != null ? Muzzle.position - weapon.position : transform.forward;
            if (along.sqrMagnitude > 1e-6f) flash.rotation = Quaternion.LookRotation(along);
        }

        static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null && degrees != 0f) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }
    }

    /// <summary>A short glowing star at a muzzle, off until a shot shows it.</summary>
    public static class MuzzleFlash
    {
        static Texture2D glow;

        public static GameObject Create(Transform muzzle, float size)
        {
            if (glow == null)
            {
                glow = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Muzzle flash" };
                glow.SetPixel(0, 0, new Color(1f, 0.78f, 0.35f));
                glow.Apply();
            }
            var root = new GameObject("Flash");
            root.transform.SetParent(muzzle, false);
            var material = Paint.Textured(glow, true);
            for (int i = 0; i < 3; i++)
            {
                var petal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                petal.name = "Petal";
                Object.Destroy(petal.GetComponent<Collider>());
                petal.transform.SetParent(root.transform, false);
                petal.transform.localRotation = Quaternion.Euler(0f, 0f, i * 60f);
                petal.transform.localScale = new Vector3(size, size * 0.12f, size * 0.12f);
                var r = petal.GetComponent<Renderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            root.SetActive(false);
            return root;
        }
    }
}

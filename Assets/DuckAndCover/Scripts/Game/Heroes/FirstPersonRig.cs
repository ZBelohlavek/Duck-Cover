using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The local player's arms and weapon in front of the camera. The hero's first-person model is made in
    /// camera space (in its aim hold, the eye a little up and left of the bore), so it sits at the camera's
    /// origin and this only adds motion: a step bob and sway while moving, lowered and turned while
    /// sprinting, a dip for reloads, a kick and a flash for each shot, and a little lag behind the look.
    /// Heroes without a first-person model get the stand-in block gun.
    /// </summary>
    public sealed class FirstPersonRig : MonoBehaviour
    {
        // ---- What the local player sets each frame ----
        public float speed;
        public bool grounded = true;
        public bool sprinting;
        public bool crouched;
        public bool reloading;
        public float yaw, pitch;

        /// <summary>Where shots leave on screen.</summary>
        public Transform Muzzle { get; private set; }
        public string HeroId { get; private set; } = "";

        Transform model;
        Transform flash;
        float flashLeft;
        float kick;
        float bobPhase;
        float sprintBlend, reloadBlend, moveBlend;
        Vector2 sway;
        float lastYaw, lastPitch;
        bool hasLast;

        /// <summary>The hero's arms and weapon (or the block gun) under the camera.</summary>
        public static FirstPersonRig Create(Transform camera, string heroId)
        {
            var root = new GameObject("ViewModel");
            root.transform.SetParent(camera, false);
            var rig = root.AddComponent<FirstPersonRig>();
            rig.HeroId = heroId ?? "";
            var art = HeroArt.For(heroId);
            if (art != null && art.firstPerson != null)
            {
                var arms = Instantiate(art.firstPerson, root.transform, false);
                arms.name = art.def.firstPerson;
                HeroArt.ApplyPaint(arms, art.firstPersonPaint, false);
                // Made around the model's own eye, so the hero's scale (which puts that eye at the rules' eye height)
                // shrinks it about the camera: the view looks the same and the arms sit the same size as the body.
                arms.transform.localScale = Vector3.one * art.def.scale;
                rig.model = arms.transform;
                rig.Muzzle = HeroArt.Find(arms.transform, "Muzzle");
                // Checked in the camera's space: the file's own nodes may carry Blender's axis turn.
                if (rig.Muzzle != null && root.transform.InverseTransformPoint(rig.Muzzle.position).z < 0f)
                {
                    Debug.LogWarning("Duck and Cover: " + heroId + "'s first-person model faced backwards after import; turned it round.");
                    arms.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }
            else rig.model = BlockGun(root.transform);
            if (rig.Muzzle == null)
            {
                var m = new GameObject("Muzzle").transform;
                m.SetParent(rig.model, false);
                m.localPosition = new Vector3(0.22f, -0.17f, 0.9f);
                rig.Muzzle = m;
            }
            rig.flash = MuzzleFlash.Create(rig.Muzzle, 0.12f).transform;
            return rig;
        }

        /// <summary>The stand-in until a hero has arms: a block rifle low on the right.</summary>
        static Transform BlockGun(Transform parent)
        {
            var root = new GameObject("BlockGun").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(0.22f, -0.2f, 0.45f);
            var dark = new Color(0.10f, 0.11f, 0.13f);
            Paint.Block(root, "Barrel", new Vector3(0f, 0.03f, 0.18f), new Vector3(0.05f, 0.05f, 0.5f), dark, false);
            Paint.Block(root, "Body", new Vector3(0f, 0f, -0.05f), new Vector3(0.07f, 0.11f, 0.32f), new Color(0.93f, 0.78f, 0.25f), false);
            Paint.Block(root, "Grip", new Vector3(0f, -0.1f, -0.12f), new Vector3(0.05f, 0.14f, 0.07f), dark, false);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.03f, 0.45f);
            return root;
        }

        public void Fired(float flashSeconds = 0.04f)
        {
            kick = 1f;
            flashLeft = flashSeconds;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            moveBlend = Mathf.MoveTowards(moveBlend, grounded ? Mathf.Clamp01(speed / 5f) : 0f, dt * 4f);
            sprintBlend = Mathf.MoveTowards(sprintBlend, sprinting && !reloading ? 1f : 0f, dt * 5f);
            reloadBlend = Mathf.MoveTowards(reloadBlend, reloading ? 1f : 0f, dt * 6f);
            bobPhase += dt * Mathf.Lerp(6f, 13f, Mathf.Clamp01(speed / 7.6f)) * (crouched ? 0.7f : 1f);

            // Lag a touch behind the look, then settle.
            if (hasLast)
            {
                float dy = Mathf.DeltaAngle(lastYaw, yaw), dp = pitch - lastPitch;
                sway = Vector2.Lerp(sway, new Vector2(Mathf.Clamp(-dy * 0.004f, -0.03f, 0.03f), Mathf.Clamp(dp * 0.004f, -0.03f, 0.03f)), dt * 10f);
            }
            lastYaw = yaw; lastPitch = pitch; hasLast = true;
            sway = Vector2.Lerp(sway, Vector2.zero, dt * 6f);

            float bobX = Mathf.Sin(bobPhase) * 0.012f * moveBlend;
            float bobY = -Mathf.Abs(Mathf.Cos(bobPhase)) * 0.014f * moveBlend;
            Vector3 position = new Vector3(bobX + sway.x, bobY + sway.y, 0f)
                + new Vector3(0.02f, -0.07f, -0.04f) * sprintBlend
                + new Vector3(0f, -0.09f, -0.02f) * reloadBlend
                + new Vector3(0f, 0.004f, -0.045f) * kick;
            Quaternion rotation = Quaternion.Euler(-4f * kick + 18f * sprintBlend + 26f * reloadBlend, -14f * sprintBlend, 8f * sprintBlend + 18f * reloadBlend);
            transform.localPosition = position;
            transform.localRotation = rotation;
            kick = Mathf.MoveTowards(kick, 0f, dt * 12f);

            flashLeft -= dt;
            if (flash != null && flash.gameObject.activeSelf != flashLeft > 0f) flash.gameObject.SetActive(flashLeft > 0f);
            if (flash != null && flash.gameObject.activeSelf) flash.rotation = Quaternion.LookRotation(transform.forward);
        }
    }
}

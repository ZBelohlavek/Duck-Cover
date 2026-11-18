using DuckAndCover.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The player at this machine: first-person camera, walking, sprinting, crouching, jumping and
    /// vaulting, and the weapon in hand. Movement runs here (the feel comes from Core's
    /// <see cref="MoveMath"/>, collisions from a CharacterController) and is reported to the server
    /// about 20 times a second; the server checks it and may put the player back. Shots are only
    /// requests: this draws the tracer at once, and the server decides what was hit.
    ///
    /// Keys: WASD move, mouse look, Shift sprint, Ctrl or C crouch, Space jump (vaults a low wall
    /// when facing one), left mouse fire, R reload, 1/2 or the wheel or Q switch weapon,
    /// E and F the hero's abilities, X the ultimate, hold Z or V to take the left or right upgrade
    /// on offer (a stand-in until the level-up prompt, WP12), Esc frees the mouse.
    ///
    /// Abilities are requests too: the server decides, and a dash or knockback comes back as a
    /// PushedEvent that this applies to the player's own movement.
    /// </summary>
    public class LocalPlayer : MonoBehaviour
    {
        public static LocalPlayer Instance { get; private set; }

        public float mouseSensitivity = 0.12f;
        /// <summary>Set by the screenshot run, which has no mouse: stops the cursor being locked.</summary>
        public bool scripted;
        /// <summary>The screenshot run moves the camera itself (to look at the scene from outside).</summary>
        public bool freeCamera;

        // ---- What the HUD reads ----
        public bool Active { get; private set; }
        public bool Crouched { get; private set; }
        public bool Sprinting { get; private set; }
        public bool Vaulting => vaultTime >= 0f;
        public int Ammo { get; private set; }
        public int Magazine { get; private set; }
        public string WeaponName { get; private set; } = "";
        public bool Reloading { get; private set; }
        /// <summary>Time.time of the last hit the server confirmed on an enemy, and whether it took them down.</summary>
        public float LastHitAt { get; private set; } = -10f;
        public bool LastHitWasHead { get; private set; }
        public bool LastHitEliminated { get; private set; }
        public float Yaw => yaw;
        public float Pitch => pitch;
        /// <summary>The upgrade key being held (0 left, 1 right, -1 none) and how far along the hold is, 0..1.</summary>
        public int ChoiceHeld { get; private set; } = -1;
        public float ChoiceProgress { get; private set; }

        /// <summary>Seconds a choice key must be held, so a stray tap mid-fight doesn't pick.</summary>
        public const float ChoiceHoldSeconds = 0.35f;

        CharacterController controller;
        Transform viewModel;
        CombatView view;
        SessionClient client;
        Ruleset rules;
        WeaponLibrary weapons;

        float yaw, pitch;
        Vector3 velocity;
        int placement = -1;
        bool placed;
        float sendTimer;
        float eyeHeight;

        float vaultTime = -1f;
        Vector3 vaultFrom, vaultTo;

        /// <summary>The launch pad whose arc the body is flying, or null (WP18).</summary>
        LaunchPad flightPad;
        float flightTime;

        Vector3 pushVelocity;
        float pushLeft;
        float choiceHeldFor;
        int choiceSentLevel = -1;
        float abilityBlockUntil;

        float nextFireAt;
        int shotsSent;
        int shotBaseline = -1;
        float switchBlockUntil;
        int wantedSlot = -1;

        public static LocalPlayer Create(CombatView view)
        {
            var go = new GameObject("LocalPlayer");
            go.layer = PhysicsWorld.PlayerLayer;
            var player = go.AddComponent<LocalPlayer>();
            player.view = view;
            return player;
        }

        void Awake()
        {
            Instance = this;
            controller = gameObject.AddComponent<CharacterController>();
            controller.enabled = false;
        }

        void OnDestroy()
        {
            Bind(null);
            ReleaseCursor();
            if (viewModel != null) Destroy(viewModel.gameObject);
            if (Instance == this) Instance = null;
        }

        /// <summary>Follow a session (or none). Called by <see cref="CombatView"/> when the session changes.</summary>
        public void Bind(SessionClient next)
        {
            if (client == next) return;
            if (client != null) client.Events.Unsubscribe(OnEvent);
            client = next;
            placed = false;
            placement = -1;
            shotBaseline = -1;
            shotsSent = 0;
            SetActive(false);
            if (client != null) client.Events.Subscribe(OnEvent);
        }

        void OnEvent(GameEvent e)
        {
            if (client == null) return;
            switch (e)
            {
                case PlayerPlacedEvent put when put.playerId == client.PlayerId:
                    Teleport(put.position.ToVector3(), put.yaw, put.correction);
                    placement = put.placement;
                    placed = true;
                    break;
                case DamagedEvent hurt when hurt.attackerId == client.PlayerId && hurt.targetId != client.PlayerId:
                    LastHitAt = Time.time;
                    LastHitEliminated = hurt.eliminated;
                    break;
                case ShotFiredEvent shot when shot.playerId == client.PlayerId:
                    LastHitWasHead = System.Array.IndexOf(shot.hitParts, BodyParts.Head) >= 0;
                    break;
                case CommandRejectedEvent refused when refused.command == nameof(FireCommand):
                    shotsSent = Mathf.Max(0, shotsSent - 1);
                    break;
                case PushedEvent pushed when pushed.targetId == client.PlayerId:
                    StartPush(pushed);
                    break;
            }
        }

        void Teleport(Vector3 feet, float newYaw, bool correction)
        {
            bool was = controller.enabled;
            controller.enabled = false;
            transform.position = feet;
            controller.enabled = was;
            velocity = Vector3.zero;
            vaultTime = -1f;
            pushLeft = 0f;
            flightPad = null;
            if (!correction) { yaw = newYaw; pitch = 0f; }
        }

        /// <summary>A dash (along the aim) or a knockback: moved over its seconds, level with the ground.</summary>
        void StartPush(PushedEvent pushed)
        {
            Vector3 dir = pushed.direction.ToVector3();
            if (pushed.mode != PushDirections.Aim && dir.sqrMagnitude < 1e-6f) dir = transform.position - pushed.origin.ToVector3();
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f || pushed.distance <= 0f) return;
            float seconds = Mathf.Max(0.05f, pushed.seconds);
            pushVelocity = dir.normalized * (pushed.distance / seconds);
            pushLeft = seconds;
            vaultTime = -1f;
        }

        void SetActive(bool on)
        {
            if (Active == on) return;
            Active = on;
            controller.enabled = on;
            if (viewModel != null) viewModel.gameObject.SetActive(on && !freeCamera);
            if (!on)
            {
                ReleaseCursor();
                Crouched = false;
                Sprinting = false;
                vaultTime = -1f;
            }
        }

        void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            var runner = SessionRunner.Instance;
            rules = runner != null ? runner.ClientRules : null;
            weapons = runner != null ? runner.Weapons : null;
            var me = client == null ? null : client.Me;
            var snapshot = client == null ? null : client.Snapshot;
            bool alive = me != null && me.alive && placed && rules != null && snapshot != null;
            SetActive(alive);
            if (!alive) return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool focused = scripted || UpdateCursor(keyboard, mouse);

            var unit = snapshot.abilities == null ? null : snapshot.abilities.Unit(me.id);
            bool held = unit != null && (unit.HasStatus(AbilityLibrary.StunStatus) || unit.HasStatus(AbilityLibrary.RootStatus));
            float speedScale = held ? 0f : unit == null ? 1f : Mathf.Max(rules.minMoveSpeed, unit.stats.Get(StatIds.MoveSpeed));

            Look(mouse, focused);
            Move(keyboard, focused, speedScale);
            PlaceCamera();
            Report();
            bool stunned = unit != null && unit.HasStatus(AbilityLibrary.StunStatus);
            fireRate = unit == null ? 1f : Mathf.Max(0.1f, unit.stats.Get(StatIds.FireRate));
            Weapons(keyboard, mouse, focused, snapshot.Weapons(me.id), stunned);
            Abilities(keyboard, focused, unit);
            Choices(keyboard, focused, snapshot, me);
        }

        float fireRate = 1f;

        // ---- Abilities ----

        void Abilities(Keyboard keyboard, bool focused, HeroUnit unit)
        {
            if (!focused || scripted || keyboard == null || unit == null || Time.time < abilityBlockUntil) return;
            int slot = -1;
            if (keyboard.eKey.wasPressedThisFrame) slot = 0;
            else if (keyboard.fKey.wasPressedThisFrame) slot = 1;
            else if (keyboard.xKey.wasPressedThisFrame) slot = unit.slots.Count - 1;
            if (slot < 0 || slot >= unit.slots.Count) return;
            UseAbility(slot, unit.slots[slot].abilityId);
        }

        /// <summary>Ask the server to use the ability in a slot, aimed where the camera looks.</summary>
        public void UseAbility(int slot, string abilityId)
        {
            if (client == null) return;
            abilityBlockUntil = Time.time + 0.15f; // one request per press, even if the state copy is late
            client.Send(new UseAbilityCommand
            {
                slot = slot, abilityId = abilityId ?? "", tick = view != null ? view.RenderTick : 0,
                aimOrigin = (transform.position + Vector3.up * eyeHeight).ToVec3(), aimDirection = MoveMath.AimDirection(yaw, pitch),
            });
        }

        // ---- Upgrade choices (a stand-in until WP12's prompt) ----

        void Choices(Keyboard keyboard, bool focused, MatchSnapshot snapshot, PlayerState me)
        {
            int level = PendingLevel(snapshot, me.id, rules == null ? null : rules.progression);
            int key = -1;
            if (focused && !scripted && keyboard != null && level > 0)
            {
                if (keyboard.zKey.isPressed) key = 0;
                else if (keyboard.vKey.isPressed) key = 1;
            }
            if (key != ChoiceHeld) choiceHeldFor = 0f;
            ChoiceHeld = key;
            if (key < 0) { ChoiceProgress = 0f; return; }
            choiceHeldFor += Time.deltaTime;
            ChoiceProgress = Mathf.Clamp01(choiceHeldFor / ChoiceHoldSeconds);
            if (ChoiceProgress < 1f || choiceSentLevel == level) return;
            choiceSentLevel = level;
            client.Send(new ChooseUpgradeCommand { level = level, option = key });
        }

        /// <summary>The lowest level with a choice whose upgrade the player hasn't chosen yet, or 0.</summary>
        public static int PendingLevel(MatchSnapshot snapshot, int playerId, ProgressionRuleset leveling)
        {
            var mine = snapshot == null || snapshot.progression == null ? null : snapshot.progression.Player(playerId);
            if (mine == null) return 0;
            // Levels without a choice (the automatic boosts) are skipped, by the match's own level layout.
            for (int level = 2; level <= mine.level; level++)
                if (leveling != null && leveling.ChoiceAt(level) != UpgradeKind.None && !mine.HasPicked(level)) return level;
            return 0;
        }

        /// <summary>Click to take the mouse, Esc to give it back. Returns whether the game has it.</summary>
        bool UpdateCursor(Keyboard keyboard, Mouse mouse)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ReleaseCursor();
            else if (Cursor.lockState != CursorLockMode.Locked && mouse != null && mouse.leftButton.wasPressedThisFrame && !OverPanel(mouse))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return false; // the click that takes the mouse doesn't also fire
            }
            return Cursor.lockState == CursorLockMode.Locked;
        }

        /// <summary>The developer panel takes clicks on the left edge of the screen while it shows.</summary>
        static bool OverPanel(Mouse mouse) => SessionDebugPanel.Showing && mouse.position.ReadValue().x < SessionDebugPanel.Width + 24f;

        void Look(Mouse mouse, bool focused)
        {
            if (!focused || mouse == null || scripted) return;
            Vector2 delta = mouse.delta.ReadValue();
            yaw = Mathf.Repeat(yaw + delta.x * mouseSensitivity, 360f);
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -89f, 89f);
        }

        void Move(Keyboard keyboard, bool focused, float speedScale)
        {
            float dt = Time.deltaTime;
            var input = new MoveInput { yaw = yaw };
            if (focused && keyboard != null && !scripted)
            {
                input.forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                input.right = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                input.sprint = keyboard.leftShiftKey.isPressed;
                input.crouch = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed;
                input.jump = keyboard.spaceKey.wasPressedThisFrame;
            }

            // Stand up only where there is headroom.
            if (!input.crouch && Crouched && !HasHeadroom()) input.crouch = true;
            Crouched = input.crouch;
            float height = Crouched ? rules.crouchHeight : rules.standHeight;
            controller.radius = rules.bodyRadius;
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
            controller.stepOffset = Mathf.Min(0.35f, rules.vaultMinHeight);
            eyeHeight = Mathf.MoveTowards(eyeHeight <= 0f ? rules.eyeHeight : eyeHeight, Hitboxes.EyeHeight(rules, Crouched), 6f * dt);

            if (flightPad != null)
            {
                FlyLaunch(dt);
                return;
            }

            if (pushLeft > 0f)
            {
                float step = Mathf.Min(dt, pushLeft);
                pushLeft -= step;
                velocity.y = Mathf.Min(velocity.y, 0f) - rules.gravity * step;
                controller.Move(pushVelocity * step + Vector3.up * (velocity.y * step));
                Sprinting = false;
                return;
            }

            if (vaultTime >= 0f)
            {
                vaultTime += dt / rules.vaultSeconds;
                Vector3 at = MoveMath.VaultPosition(vaultFrom.ToVec3(), vaultTo.ToVec3(), vaultTime).ToVector3();
                controller.enabled = false;
                transform.position = at;
                controller.enabled = true;
                velocity = Vector3.zero;
                if (vaultTime >= 1f) { vaultTime = -1f; Crouched = true; } // lands low, and stands next frame if there is room
                Sprinting = false;
                return;
            }

            if (input.jump && speedScale > 0f && !Crouched && TryStartVault())
            {
                Sprinting = false;
                return;
            }

            bool grounded = controller.isGrounded;
            velocity = MoveMath.Step(rules, velocity.ToVec3(), input, grounded, speedScale, dt).ToVector3();
            Sprinting = grounded && MoveMath.IsSprinting(rules, input) && speedScale > 0f;
            var flags = controller.Move(velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;
            TryLaunch();
        }

        /// <summary>
        /// Standing on one of the map's launch pads starts its flight. This frame's position (on the pad)
        /// goes to the server at once, so the server launches the player too and checks the arc.
        /// </summary>
        void TryLaunch()
        {
            var map = MapScene.Current;
            if (map == null || map.launchPads.Count == 0 || !controller.isGrounded) return;
            var feet = transform.position.ToVec3();
            foreach (var pad in map.launchPads)
                if (pad.Contains(feet))
                {
                    flightPad = pad;
                    flightTime = 0f;
                    sendTimer = 0f;
                    return;
                }
        }

        /// <summary>Follow the pad's arc (the same one the server checks), then land running.</summary>
        void FlyLaunch(float dt)
        {
            flightTime += dt;
            Vector3 at = flightPad.PointAt(flightTime).ToVector3();
            controller.enabled = false;
            transform.position = at;
            controller.enabled = true;
            velocity = flightPad.VelocityAt(flightTime).ToVector3();
            Sprinting = false;
            if (flightTime >= flightPad.seconds)
            {
                velocity = Vector3.zero;
                flightPad = null;
            }
        }

        bool HasHeadroom()
        {
            float r = rules.bodyRadius * 0.95f;
            Vector3 feet = transform.position;
            return !Physics.CheckCapsule(feet + Vector3.up * (r + 0.05f), feet + Vector3.up * (rules.standHeight - r), r, PhysicsWorld.GeometryMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// A vault starts when there is something solid just ahead, its top is between the vault
        /// heights above the feet, and there is room to stand on it.
        /// </summary>
        bool TryStartVault()
        {
            Vector3 feet = transform.position;
            Vector3 forward = MoveMath.Forward(yaw).ToVector3();
            float reach = rules.bodyRadius + rules.vaultReach;
            // Something in the way at shin height?
            if (!Physics.Raycast(feet + Vector3.up * (rules.vaultMinHeight * 0.9f), forward, out RaycastHit wall, reach, PhysicsWorld.GeometryMask, QueryTriggerInteraction.Ignore)) return false;
            // Find its top, a little way in from the face: the player lands on it (a thin wall is then walked off).
            Vector3 over = feet + forward * (wall.distance + Mathf.Max(0.05f, rules.bodyRadius * 0.6f));
            float probe = rules.vaultMaxHeight + 0.2f;
            if (!Physics.Raycast(new Vector3(over.x, feet.y + probe, over.z), Vector3.down, out RaycastHit top, probe, PhysicsWorld.GeometryMask, QueryTriggerInteraction.Ignore)) return false;
            float ledge = top.point.y - feet.y;
            if (!MoveMath.CanVault(rules, ledge)) return false;
            // Room to crouch on top? Checked from just above the ledge, so the ledge itself doesn't count.
            float r = rules.bodyRadius * 0.95f;
            Vector3 land = new Vector3(over.x, top.point.y + 0.02f, over.z);
            if (Physics.CheckCapsule(land + Vector3.up * (r + 0.1f), land + Vector3.up * (rules.crouchHeight - r), r, PhysicsWorld.GeometryMask, QueryTriggerInteraction.Ignore)) return false;
            vaultFrom = feet;
            vaultTo = land;
            vaultTime = 0f;
            return true;
        }

        void PlaceCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (viewModel == null) viewModel = BuildViewModel(cam.transform);
            if (viewModel.gameObject.activeSelf == freeCamera) viewModel.gameObject.SetActive(!freeCamera);
            if (freeCamera) return;
            cam.transform.SetPositionAndRotation(transform.position + Vector3.up * eyeHeight, Quaternion.Euler(pitch, yaw, 0f));
        }

        static Transform BuildViewModel(Transform camera)
        {
            var root = new GameObject("ViewModel").transform;
            root.SetParent(camera, false);
            root.localPosition = new Vector3(0.22f, -0.2f, 0.45f);
            var dark = new Color(0.10f, 0.11f, 0.13f);
            Paint.Block(root, "Barrel", new Vector3(0f, 0.03f, 0.18f), new Vector3(0.05f, 0.05f, 0.5f), dark, false);
            Paint.Block(root, "Body", new Vector3(0f, 0f, -0.05f), new Vector3(0.07f, 0.11f, 0.32f), new Color(0.93f, 0.78f, 0.25f), false);
            Paint.Block(root, "Grip", new Vector3(0f, -0.1f, -0.12f), new Vector3(0.05f, 0.14f, 0.07f), dark, false);
            return root;
        }

        /// <summary>Where shots leave from on screen: the end of the gun in the player's hands.</summary>
        public Vector3 Muzzle
        {
            get
            {
                var cam = Camera.main;
                if (viewModel != null) return viewModel.TransformPoint(new Vector3(0f, 0.03f, 0.45f));
                return cam != null ? cam.transform.position : transform.position;
            }
        }

        void Report()
        {
            sendTimer -= Time.unscaledDeltaTime;
            if (sendTimer > 0f) return;
            var runner = SessionRunner.Instance;
            sendTimer = runner != null ? runner.Settings.TickSeconds : 0.05f;
            client.Send(new MoveCommand
            {
                position = transform.position.ToVec3(), velocity = velocity.ToVec3(), yaw = yaw, pitch = pitch,
                crouched = Crouched, grounded = controller.isGrounded, sprinting = Sprinting, vaulting = Vaulting, placement = placement,
            });
        }

        // ---- Weapons ----

        void Weapons(Keyboard keyboard, Mouse mouse, bool focused, WeaponState state, bool stunned)
        {
            if (state == null || weapons == null) return;
            var def = weapons.Weapon(state.CurrentId);
            if (def == null) return;

            // Shots the server hasn't counted yet come off the ammo it last reported.
            if (shotBaseline < 0) { shotBaseline = state.shotsFired; shotsSent = 0; }
            int unacknowledged = Mathf.Max(0, shotsSent - (state.shotsFired - shotBaseline));
            if (unacknowledged == 0) { shotBaseline = state.shotsFired; shotsSent = 0; }
            if (wantedSlot == state.current) wantedSlot = -1;

            WeaponName = def.displayName;
            Magazine = state.CurrentMagazine > 0 ? state.CurrentMagazine : def.magazine;
            Reloading = state.Reloading;
            Ammo = Reloading ? 0 : Mathf.Max(0, state.CurrentAmmo - unacknowledged);
            if (!focused || scripted || stunned) return;

            int slot = SlotAsked(keyboard, mouse, state);
            if (slot >= 0 && slot != state.current && wantedSlot < 0)
            {
                wantedSlot = slot;
                switchBlockUntil = Time.time + rules.weaponSwitchSeconds;
                client.Send(new SwitchWeaponCommand { slot = slot });
                return;
            }
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame && !Reloading && Ammo < Magazine)
            {
                client.Send(new ReloadCommand());
                return;
            }

            bool trigger = mouse != null && (def.automatic ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame);
            if (trigger) TryFire(def, state);
        }

        static int SlotAsked(Keyboard keyboard, Mouse mouse, WeaponState state)
        {
            int count = state.weapons.Length;
            if (count < 2) return -1;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) return 0;
                if (keyboard.digit2Key.wasPressedThisFrame) return 1;
                if (keyboard.digit3Key.wasPressedThisFrame && count > 2) return 2;
                if (keyboard.qKey.wasPressedThisFrame) return (state.current + 1) % count;
            }
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0.01f) return (state.current + count - 1) % count;
                if (wheel < -0.01f) return (state.current + 1) % count;
            }
            return -1;
        }

        bool TryFire(WeaponDef def, WeaponState state)
        {
            if (Reloading || Ammo <= 0 || wantedSlot >= 0 || state.switchLeft > 0f) return false;
            if (Time.time < nextFireAt || Time.time < switchBlockUntil) return false;
            var cam = Camera.main;
            if (cam == null) return false;
            nextFireAt = Time.time + def.fireInterval / fireRate;
            shotsSent++;

            Vec3 origin = (transform.position + Vector3.up * eyeHeight).ToVec3();
            Vec3 direction = MoveMath.AimDirection(yaw, pitch);
            // The tick this player is looking at: other players are drawn a little in the past.
            client.Send(new FireCommand { weaponId = def.id, origin = origin, direction = direction, tick = view != null ? view.RenderTick : 0 });
            if (view != null && def.kind == WeaponKind.Hitscan) view.DrawOwnShot(def, origin, direction, Muzzle);
            return true;
        }

        // ---- For the screenshot run, which has no keyboard or mouse ----

        public void LookAt(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = newPitch;
        }

        /// <summary>Pull the trigger once. Returns false if the weapon can't fire right now.</summary>
        public bool FireOnce()
        {
            if (!Active || client == null || client.Snapshot == null || weapons == null) return false;
            var state = client.Snapshot.Weapons(client.PlayerId);
            var def = state == null ? null : weapons.Weapon(state.CurrentId);
            if (def == null) return false;
            PlaceCamera();
            return TryFire(def, state);
        }
    }
}

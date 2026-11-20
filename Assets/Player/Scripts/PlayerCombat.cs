using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [SerializeField, InspectorName("Hitbox anzeigen")] private bool showHitbox = true;
        [SerializeField] private LayerMask enemyLayers = 1 << 8;
        [SerializeField] private LayerMask terrainLayers = ~((1 << 8) | (1 << 9));
        [SerializeField, Min(0.1f)] private float lightReach = 1.65f;
        [SerializeField, Min(0.1f), InspectorName("Heavy Reichweite")] private float heavyReach = 2.6f;
        [SerializeField, Min(0.1f), InspectorName("Dash-Strike Reichweite vorne")] private float dashForwardReach = 1.2f;
        [SerializeField] private int lightDamage = 6;
        [SerializeField] private int heavyDamage = 12;
        [SerializeField] private int fullyChargedDamage = 28;
        [SerializeField] private float fullChargeTime = 1.1f;
        [SerializeField, Min(0f), InspectorName("Pause nach Combo (Sekunden)")] private float comboCooldown = 0.12f;
        private float attackCooldownRemaining;

        public bool DebugAttackActive => dashStrikeActive || IsLunging && IsAirDive || IsAttacking && elapsed >= windup && elapsed < windup + activeTime;
        private Vector2 attackAim = Vector2.right;
        public Vector2 AttackDirection => attackAim;
        public bool DirectionalBurstActive => IsDirectionalAttack && !IsRecovering && (IsSnapBite || !IsHeavyAttack && ComboStep == 2 && elapsed >= windup || IsHeavySnap);
        public Vector2 DirectionalBurstVelocity => attackAim * (IsSnapBite ? Mathf.Lerp(12f, 3f, Mathf.Clamp01(elapsed / 0.14f)) : IsHeavySnap ? HeavySnapSpeed : 6f * Mathf.Sin(Mathf.Clamp01((elapsed - windup) / activeTime) * Mathf.PI));
        public float AttackPoseWeight => !IsAttacking ? 0f : Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(0.01f, windup)) * (1f - Mathf.SmoothStep(0f, 1f, (elapsed - windup - activeTime) / Mathf.Max(0.01f, recovery)));

        public bool IsDirectionalAttack => IsAttacking && !IsDashAttack && !IsCrossBite && !IsAirSpin && !IsAirLunge && !IsAirDive;
        public float AttackHitboxAngle => IsDirectionalAttack ? Mathf.Atan2(attackAim.y, attackAim.x) * Mathf.Rad2Deg : 0f;
        public float AttackPoseAngle => IsDirectionalAttack ? Mathf.Atan2(attackAim.y, Mathf.Abs(attackAim.x)) * Mathf.Rad2Deg * AttackFacing : 0f;
        private Vector2 ReadAimInput()
        {
            Vector2 input = Vector2.zero;
            var keys = Keyboard.current;
            if (keys != null) input = new Vector2(
                (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1f : 0f) - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1f : 0f),
                (keys.wKey.isPressed || keys.upArrowKey.isPressed ? 1f : 0f) - (keys.sKey.isPressed || keys.downArrowKey.isPressed ? 1f : 0f));
            var pad = Gamepad.current;
            if (input.sqrMagnitude < 0.1f && pad != null)
            {
                input = pad.leftStick.ReadValue();
                if (input.magnitude < 0.35f) input = pad.dpad.ReadValue();
                if (input.magnitude < 0.35f) input = Vector2.zero;
            }
            if (input.sqrMagnitude < 0.1f) return Vector2.zero;
            float angle = Mathf.Round(Mathf.Atan2(input.y, input.x) / (Mathf.PI / 4f)) * Mathf.PI / 4f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        public float DebugStartup => windup;
        public float DebugActive => activeTime;
        public float DebugRecovery => recovery;
        public float DebugRemainingAttackTime => Mathf.Max(0f, windup + activeTime + recovery - elapsed) + (!IsHeavyAttack && ComboStep == 2 ? comboCooldown : 0f);
        public string DebugAttackName => IsAirDive ? "Dive attack" : IsAirLunge ? "Air bite" : IsCrossBite ? "Cross bite" : IsDashAttack ? "Dash strike" : IsSnapBite ? "Snap bite" : IsAirSpin ? "Air spin" : IsHeavyAttack ? "Heavy bite" : "Light " + (ComboStep + 1);
        public string DebugTimingInfo => $"Startup {windup * 1000f:0} ms ({windup * 60f:0.#}f)\nActive {activeTime * 1000f:0} ms ({activeTime * 60f:0.#}f)\nRecovery {recovery * 1000f:0} ms ({recovery * 60f:0.#}f)\nAttack lock {attackCooldownRemaining * 1000f:0} ms\nFrames use a 60 FPS timing reference.";
        public string DebugAttackPhase => IsCharging ? "Charging" : !IsAttacking ? (attackCooldownRemaining > 0f ? "Cooldown" : "Ready") : IsLunging ? "Approach" : elapsed < windup ? "Startup" : elapsed < windup + activeTime ? "Active" : "Recovery";
        public bool IsAttacking { get; private set; }
        public bool IsCharging { get; private set; }
        public bool IsHeavyAttack { get; private set; }
        public bool IsDashAttack { get; private set; }
        public bool IsCrossBite { get; private set; }
        [SerializeField, Min(1f)] private float airBiteRange = 6f;
        public bool IsAirSpin { get; private set; }
        public bool IsAirLunge { get; private set; }
        public bool IsLunging { get; private set; }
        public bool IsAirDive { get; private set; }
        private bool airActionUsed, airFreeDash, airSpinUsed;
        private readonly List<Collider2D> ignoredPlatforms = new List<Collider2D>();
        public void ResetAirAction() { if (!IsLunging) { airActionUsed = false; airSpinUsed = false; } }
        public Vector2 AirLungeVelocity => IsAirDive ? Vector2.down * 34f : airFreeDash ? Vector2.right * AttackFacing * Mathf.Lerp(21f, 12f, Mathf.Clamp01(lungeTime / 0.16f)) : airTarget == null ? Vector2.zero : ((Vector2)airTarget.bounds.center - (body.position + Vector2.up * 0.75f)).normalized * 24f;
        private Collider2D airTarget;
        private float lungeTime;
        public bool IsSnapBite { get; private set; }
        public float SnapVelocityX => AttackFacing * Mathf.Lerp(12f, 3f, Mathf.Clamp01(elapsed / 0.14f));
        public float CrossBiteVelocityX => !IsCrossBite || elapsed >= 0.4f ? 0f : Mathf.Clamp((CrossPosition((elapsed + Time.fixedDeltaTime) / 0.4f) - body.position.x) / Time.fixedDeltaTime, -28f, 28f);
        public bool IsCrossDashing => IsCrossBite && elapsed < 0.4f;
        public float CrossDashProgress => Mathf.Clamp01(elapsed / 0.4f);
        public int CrossVisualDirection => crossDirection;
        public float CrossTurnYaw => IsCrossBite ? Mathf.SmoothStep(0f, 1f, (elapsed - 0.06f) / 0.18f) * 180f : 0f;
        public int ComboStep { get; private set; }
        public int AttackFacing { get; private set; } = 1;
        public float ChargeAmount => Mathf.Clamp01(chargeTime / fullChargeTime);
        public bool LocksFacing => IsAttacking && !IsRecovering || IsCharging;
        public bool CanDodge => !IsAttacking || IsRecovering;
        public bool IsRecovering => attackCooldownRemaining > 0f || IsAttacking && (!IsDashAttack || !dashStrikeActive) && elapsed >= windup + activeTime;
        public bool CanJump => (!IsAttacking || IsRecovering) && !IsCharging;
        public bool HasHitPause => hitPause > 0f;
        public float MovementMultiplier => IsRecovering ? 1f : HasHitPause ? 0f : IsCharging ? 0.1f : IsAttacking ? 0.3f : 1f;
        public float AttackProgress => Mathf.Clamp01(elapsed / Mathf.Max(0.01f, windup + activeTime + recovery));
        public bool IsHeavySnap => IsAttacking && IsHeavyAttack && !IsDashAttack && !IsCrossBite && !IsSnapBite && !IsAirLunge && releasedCharge > 0.15f && elapsed >= windup && elapsed < windup + activeTime;
        public float HeavySnapPulse => IsHeavySnap ? Mathf.Sin(Mathf.Clamp01((elapsed - windup) / activeTime) * Mathf.PI) : 0f;
        public float HeavySnapSpeed => Mathf.Lerp(2.5f, 5f, releasedCharge) * HeavySnapPulse;
        public float HeavySnapLift => Mathf.Lerp(0.04f, 0.12f, releasedCharge) * HeavySnapPulse;
        public bool CanInterruptEnemy => IsHeavyAttack && releasedCharge >= 0.8f;

        private PlayerMovement movement;
        private Rigidbody2D body;
        private PhysicsScene2D physics;
        private readonly Collider2D[] targets = new Collider2D[16];
        private readonly RaycastHit2D[] walls = new RaycastHit2D[8];
        private readonly HashSet<TestEnemy> hitTargets = new HashSet<TestEnemy>();
        private float chargeTime, elapsed, windup, activeTime, recovery, queuedLight, hitPause;
        private bool heavyHeld, heavyNeedsRelease, strikeStarted, heavyQueued;
        private int damage;
        [SerializeField, Min(1)] private int crossBiteDamage = 18;
        [SerializeField, Min(0.05f), InspectorName("Haltezeit Combo-Variation")] private float crossHoldThreshold = 0.18f;
        private float crossHoldTime;
        private bool crossBiteQueued;
        private bool crossFromDodge;
        private Vector2 dashHeavyOrigin;
        private float crossHopStart;
        public float CrossVisualProgress => Mathf.Lerp(crossHopStart, 1f, CrossDashProgress);
        private float crossStartX;
        private float crossEntryVelocity;
        private int crossDirection;
        private float crossDestinationX;
        private Collider2D crossTarget;
        private Vector2 crossApproachStart;
        private float releasedCharge;
        private bool dashStrikeActive;
        private Vector2 lastDashPosition;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            body = GetComponent<Rigidbody2D>();
            physics = gameObject.scene.GetPhysicsScene2D();
            AttackFacing = movement.InputFacing();
        }

        private void OnEnable()
        {
            movement = GetComponent<PlayerMovement>();
            body = GetComponent<Rigidbody2D>();
            physics = gameObject.scene.GetPhysicsScene2D();
            CancelAttack();
            heavyHeld = heavyNeedsRelease = false;
        }

        private void OnDisable() { CancelAttack(); RestorePlatforms(true); }

        private void Update()
        {
            if (GetComponent<PlayerDebug>() is PlayerDebug debug && debug.PointerOverMenu) { CancelAttack(); heavyHeld = false; Tick(Time.deltaTime); return; }
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame || pad != null && pad.buttonWest.wasPressedThisFrame)
                QueueLightAttack();
            SetHeavyHeld(mouse != null && mouse.rightButton.isPressed || pad != null && pad.buttonNorth.isPressed);
            Tick(Time.deltaTime);
        }

        public void QueueLightAttack()
        {
            if (movement == null || movement.HasBufferedDodge || movement.IsHitStunned || IsCharging || attackCooldownRemaining > 0f) return;
            heavyQueued = false;
            if (movement.IsDodging)
            {
                heavyQueued = false;
                if (!IsDashAttack)
                {
                    StartDashAttack();
                    dashStrikeActive = true;
                    lastDashPosition = body.position;
                    FindDashHits();
                }
                return;
            }
            if (!IsAttacking) StartLight(0);
            else queuedLight = 0.28f;
        }

        public void SetHeavyHeld(bool held)
        {
            if (movement.HasBufferedDodge) { heavyHeld = held; return; }
            if (movement.IsHitStunned) { heavyHeld = held; return; }
            if (held && !heavyHeld && !movement.IsGrounded && !movement.IsDodging && !IsAttacking && attackCooldownRemaining <= 0f)
            {
                if (airActionUsed) { heavyHeld = true; return; }
                StartAirBite();
                heavyHeld = true;
                return;
            }
            if (held && !heavyHeld && (movement.IsDodging || IsAttacking && !IsHeavyAttack))
            {
                crossApproachStart = body.position;
                crossTarget = SelectCrossTarget();
                crossBiteQueued = true;
                crossFromDodge = movement.IsDodging;
                if (crossFromDodge) dashHeavyOrigin = body.position;
                crossHoldTime = 0f;
                queuedLight = 0f;
                heavyQueued = false;
            }
            if (!held && heavyHeld && crossFromDodge && crossBiteQueued && crossHoldTime < crossHoldThreshold)
            {
                crossBiteQueued = crossFromDodge = false;
                heavyHeld = heavyQueued = false;
                movement.ContinueDodgeIntoAttack();
                StartSnapBite();
                return;
            }
            if (crossBiteQueued || IsCrossBite)
            {
                heavyHeld = held;
                return;
            }
            if (!held) { heavyNeedsRelease = false; heavyQueued = false; }
            if (held && attackCooldownRemaining > 0f) heavyQueued = true;
            if (held && !heavyNeedsRelease && (IsAttacking || movement.IsDodging) && !IsCharging)
            {
                heavyQueued = true;
                queuedLight = 0f;
            }
            if (held && attackCooldownRemaining <= 0f && !heavyHeld && !heavyNeedsRelease && !IsAttacking && !IsCharging && movement.IsGrounded && !movement.IsDodging)
            {
                IsCharging = true;
                chargeTime = 0f;
                AttackFacing = movement.InputFacing();
            }
            if (!held && heavyHeld && IsCharging) StartHeavy();
            heavyHeld = held;
        }

        private void Tick(float dt)
        {
            if (movement.IsHitStunned) { CancelAttack(); return; }
            attackCooldownRemaining = Mathf.Max(0f, attackCooldownRemaining - dt);
            if (crossBiteQueued && heavyHeld) crossHoldTime += dt;
            if (movement.IsDodging && crossFromDodge && crossBiteQueued && heavyHeld && crossHoldTime >= crossHoldThreshold)
            {
                float hopProgress = movement.DodgeProgress;
                movement.ContinueDodgeIntoAttack();
                crossFromDodge = false;
                dashStrikeActive = false;
                StartCrossBite();
                crossHopStart = hopProgress;
                return;
            }
            if (movement.IsDodging) return;
            if (movement.HasBufferedDodge && CanDodge) { CancelAttack(); return; }
            if (crossFromDodge && crossBiteQueued && (!heavyHeld || crossHoldTime >= crossHoldThreshold))
            {
                crossFromDodge = false;
                dashStrikeActive = false;
                if (heavyHeld) StartCrossBite();
                else { crossBiteQueued = false; StartSnapBite(); }
                return;
            }
            if (dashStrikeActive)
            {
                FindDashHits();
                dashStrikeActive = false;
                elapsed = windup + activeTime;
            }
            if (attackCooldownRemaining <= 0f && heavyQueued && heavyHeld && movement.IsGrounded && !IsAttacking && !IsCharging)
            {
                heavyQueued = false;
                heavyNeedsRelease = false;
                IsCharging = true;
                chargeTime = 0f;
                AttackFacing = movement.InputFacing();
            }

            if (IsCharging)
            {
                chargeTime = Mathf.Min(chargeTime + dt, fullChargeTime);
                if (chargeTime >= fullChargeTime)
                {
                    StartHeavy();
                    return;
                }
            }
            if (IsLunging)
            {
                if (!IsAirDive && movement.DownHeld)
                {
                    IsAirDive = true;
                    IsSnapBite = airFreeDash = false;
                    airTarget = null;
                    damage = 16;
                    lungeTime = 0f;
                    windup = 0.65f;
                }
                lungeTime += dt;
                Vector2 origin = body.position + Vector2.up * 0.75f;
                Vector2 delta = airTarget == null ? Vector2.zero : (Vector2)airTarget.ClosestPoint(origin) - origin;
                bool blocked = delta.sqrMagnitude > 0.01f && TerrainBlocks(origin, delta);
                if (IsAirDive)
                {
                    FindHits();
                    if (!movement.IsGrounded && lungeTime < 0.65f) return;
                    FindHits();
                }
                else if (airFreeDash)
                {
                    if (lungeTime < 0.16f) return;
                    CancelAttack();
                    return;
                }
                else if (airTarget != null && !blocked && delta.magnitude > 0.85f && lungeTime < 0.3f) return;
                IsLunging = false;
                elapsed = windup;
                body.linearVelocity *= 0.2f;
            }
            queuedLight = Mathf.Max(0f, queuedLight - dt);
            if (hitPause > 0f) { hitPause -= dt; return; }
            if (!IsAttacking) return;
            float previous = elapsed;
            elapsed += dt;
            if (!movement.HasBufferedDodge && crossBiteQueued && elapsed >= windup + activeTime && (!heavyHeld || crossHoldTime >= crossHoldThreshold))
            {
                if (heavyHeld) StartCrossBite();
                else
                {
                    crossBiteQueued = false;
                    StartSnapBite();
                }
                return;
            }
            if (IsCrossBite && elapsed >= 0.24f) AttackFacing = -crossDirection;
            if (!strikeStarted && elapsed >= windup) strikeStarted = true;
            if (elapsed >= windup && previous < windup + activeTime) FindHits();
            if (!movement.HasBufferedDodge && !IsHeavyAttack && !IsAirSpin && queuedLight > 0f && ComboStep < 2 && elapsed >= windup + activeTime + 0.1f)
            {
                StartLight(ComboStep + 1);
                return;
            }
            if (elapsed >= windup + activeTime + recovery)
            {
                if (crossBiteQueued && !movement.HasBufferedDodge) return;
                if (!IsHeavyAttack && ComboStep == 2) attackCooldownRemaining = comboCooldown;
                bool nextHeavy = !movement.HasBufferedDodge && heavyQueued && heavyHeld && !heavyNeedsRelease;
                bool nextLight = !movement.HasBufferedDodge && queuedLight > 0f;
                CancelAttack();
                if (nextHeavy) heavyQueued = true;
                else if (nextLight && attackCooldownRemaining <= 0f) StartLight(0);
            }
        }

        private void StartLight(int step)
        {
            if (!movement.IsGrounded && ReadAimInput().sqrMagnitude < 0.1f)
            {
                if (airSpinUsed) return;
                airSpinUsed = true;
                IsCharging = IsDashAttack = IsHeavyAttack = false;
                ComboStep = 0;
                damage = 10;
                releasedCharge = 0f;
                windup = 0.06f;
                activeTime = 0.18f;
                recovery = 0.1f;
                BeginAttack();
                IsAirSpin = true;
                return;
            }
            if (!movement.IsGrounded) { if (airSpinUsed) return; airSpinUsed = true; }
            ComboStep = step;
            IsHeavyAttack = false;
            IsDashAttack = false;
            releasedCharge = 0f;
            IsCharging = false;
            damage = lightDamage + (step == 2 ? 4 : step);
            windup = step == 2 ? 0.17f : 0.1f;
            activeTime = 0.1f;
            recovery = step == 2 ? 0.12f : 0.1f;
            BeginAttack();
        }

        private void StartHeavy()
        {
            IsDashAttack = false;
            float amount = ChargeAmount;
            releasedCharge = amount;
            IsCharging = false;
            IsHeavyAttack = true;
            ComboStep = 0;
            damage = Mathf.RoundToInt(Mathf.Lerp(heavyDamage, fullyChargedDamage, amount));
            windup = 0.13f;
            activeTime = 0.14f;
            recovery = 0.18f;
            BeginAttack();
        }

        private void StartDashAttack()
        {
            IsCharging = false;
            IsHeavyAttack = true;
            IsDashAttack = true;
            ComboStep = 0;
            releasedCharge = 0f;
            damage = heavyDamage;
            windup = 0.06f;
            activeTime = 0.14f;
            recovery = 0.15f;
            BeginAttack();
        }

        private void StartAirBite()
        {
            airActionUsed = true;
            if (movement.DownHeld)
            {
                StartAirTravel(true);
                return;
            }
            Vector2 origin = body.position + Vector2.up * 0.75f;
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = enemyLayers };
            int count = physics.OverlapCircle(origin, airBiteRange, filter, targets);
            Collider2D nearest = null;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var enemy = targets[i].GetComponentInParent<TestEnemy>();
                if (enemy == null || enemy.IsDead) continue;
                Vector2 delta = (Vector2)targets[i].bounds.center - origin;
                if (delta.sqrMagnitude >= best) continue;
                var terrain = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = terrainLayers };
                if (TerrainBlocks(origin, delta)) continue;
                best = delta.sqrMagnitude;
                nearest = targets[i];
            }
            if (nearest == null) { StartAirTravel(false); return; }
            StartSnapBite();
            IsAirLunge = true;
            airTarget = nearest;
            IgnorePlatforms();
            AttackFacing = nearest.bounds.center.x >= origin.x ? 1 : -1;
            windup = 0.3f;
            IsLunging = true;
            lungeTime = 0f;
        }

        public float DiveNoseAngle
        {
            get
            {
                if (!IsAirDive || movement.IsGrounded || !IsLunging) return 0f;
                Vector2 origin = body.position + Vector2.up * 0.1f;
                int count = physics.Raycast(origin, Vector2.down, 2.5f,
                    new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = terrainLayers }, walls);
                float clearance = 2.4f;
                for (int i = 0; i < count; i++)
                {
                    if (walls[i].normal.y < 0.5f) continue;
                    clearance = Mathf.Min(clearance, Mathf.Max(0f, walls[i].distance - 0.1f));
                }
                return 70f * Mathf.SmoothStep(0f, 1f, clearance / 2.4f);
            }
        }

        private void StartAirTravel(bool dive)
        {
            StartSnapBite();
            IsSnapBite = false;
            IsAirLunge = IsLunging = true;
            IsAirDive = dive;
            airFreeDash = !dive;
            damage = dive ? 16 : 0;
            windup = 0.65f;
            activeTime = 0.12f;
            recovery = 0.08f;
            lungeTime = 0f;
        }

        private static bool IsOneWay(Collider2D collider)
        {
            return collider != null && collider.usedByEffector && collider.GetComponent<PlatformEffector2D>() != null;
        }

        private bool TerrainBlocks(Vector2 origin, Vector2 delta)
        {
            int count = physics.Raycast(origin, delta.normalized, delta.magnitude,
                new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = terrainLayers }, walls);
            for (int i = 0; i < count; i++) if (!IsOneWay(walls[i].collider)) return true;
            return false;
        }

        private void IgnorePlatforms()
        {
            var own = GetComponent<Collider2D>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var platform in root.GetComponentsInChildren<PlatformEffector2D>())
            {
                if (platform.gameObject.scene != gameObject.scene) continue;
                foreach (var collider in platform.GetComponents<Collider2D>())
                {
                    if (!collider.usedByEffector || Physics2D.GetIgnoreCollision(own, collider)) continue;
                    Physics2D.IgnoreCollision(own, collider, true);
                    ignoredPlatforms.Add(collider);
                }
            }
        }

        private void RestorePlatforms(bool force)
        {
            var own = GetComponent<Collider2D>();
            for (int i = ignoredPlatforms.Count - 1; i >= 0; i--)
            {
                var platform = ignoredPlatforms[i];
                if (!force && (IsLunging || platform != null && own.bounds.Intersects(platform.bounds))) continue;
                if (platform != null) Physics2D.IgnoreCollision(own, platform, false);
                ignoredPlatforms.RemoveAt(i);
            }
        }

        private void StartSnapBite()
        {
            crossBiteQueued = crossFromDodge = dashStrikeActive = false;
            IsCharging = IsDashAttack = IsCrossBite = false;
            IsHeavyAttack = true;
            heavyNeedsRelease = true;
            releasedCharge = 0f;
            damage = 15;
            windup = 0.04f;
            activeTime = 0.1f;
            recovery = 0.1f;
            BeginAttack();
            IsSnapBite = true;
        }

        private float CrossPosition(float progress)
        {
            float time = Mathf.Clamp01(progress) * 0.4f;
            if (time >= 0.24f)
            {
                float u = (time - 0.24f) / 0.16f;
                return crossDestinationX + crossDirection * 3f * 0.16f * (u - u * u * 0.5f);
            }
            float t = time / 0.24f;
            float t2 = t * t;
            float t3 = t2 * t;
            float distance = crossDestinationX - crossStartX;
            float entry = Mathf.Max(6f, crossEntryVelocity * crossDirection);
            float tangent = Mathf.Sign(distance) * Mathf.Min(entry * 0.24f, Mathf.Abs(distance) * 3f);
            return (2f * t3 - 3f * t2 + 1f) * crossStartX + (t3 - 2f * t2 + t) * tangent
                + (-2f * t3 + 3f * t2) * crossDestinationX + (t3 - t2) * crossDirection * 3f * 0.24f;
        }
        private void StartCrossBite()
        {
            crossHopStart = 0f;
            crossBiteQueued = false;
            heavyQueued = false;
            IsCharging = IsDashAttack = false;
            IsHeavyAttack = IsCrossBite = true;
            heavyNeedsRelease = true;
            ComboStep = 0;
            damage = crossBiteDamage;
            releasedCharge = 0f;
            windup = 0.24f;
            activeTime = 0.16f;
            recovery = 0.2f;
            BeginAttack();
            crossEntryVelocity = body.linearVelocity.x;
            crossStartX = body.position.x;
            crossDirection = AttackFacing;
            if (crossTarget == null || crossTarget.GetComponentInParent<TestEnemy>().IsDead) crossTarget = SelectCrossTarget();
            crossDestinationX = body.position.x + crossDirection * 2.5f;
            if (crossTarget != null)
            {
                float edge = crossDirection > 0 ? crossTarget.bounds.max.x : crossTarget.bounds.min.x;
                crossDestinationX = edge + crossDirection * 0.65f;
            }
        }

        private Collider2D SelectCrossTarget()
        {
            Vector2 origin = body.position + Vector2.up * 0.8f;
            Vector2 aim = Vector2.right * movement.InputFacing();
            var pad = Gamepad.current;
            if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.2f) aim = pad.leftStick.ReadValue().normalized;
            var keys = Keyboard.current;
            if (keys != null)
            {
                float y = (keys.wKey.isPressed || keys.upArrowKey.isPressed ? 1f : 0f) - (keys.sKey.isPressed || keys.downArrowKey.isPressed ? 1f : 0f);
                if (y != 0f) aim = new Vector2(aim.x, y).normalized;
            }
            int count = physics.OverlapCircle(origin, 5.5f, new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = enemyLayers }, targets);
            Collider2D best = null;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var enemy = targets[i].GetComponentInParent<TestEnemy>();
                if (enemy == null || enemy.IsDead) continue;
                Vector2 delta = (Vector2)targets[i].bounds.center - origin;
                if (delta.x * movement.InputFacing() < -0.3f || Mathf.Abs(delta.y) > 1.6f || TerrainBlocks(origin, delta)) continue;
                float score = Vector2.Dot(aim, delta.normalized) * 2f - delta.magnitude;
                if (score > bestScore) { bestScore = score; best = targets[i]; }
            }
            if (best == null)
            {
                float nearest = 2.6f;
                for (int i = 0; i < count; i++)
                {
                    var enemy = targets[i].GetComponentInParent<TestEnemy>();
                    if (enemy == null || enemy.IsDead) continue;
                    Vector2 delta = (Vector2)targets[i].ClosestPoint(origin) - origin;
                    if (Mathf.Abs(delta.y) > 1.6f || delta.magnitude >= nearest || TerrainBlocks(origin, delta)) continue;
                    nearest = delta.magnitude;
                    best = targets[i];
                }
            }
            return best;
        }

        public float LimitDodgeSpeed(float speed, int direction)
        {
            if (!crossBiteQueued || !crossFromDodge || !heavyHeld || crossTarget == null) return speed;
            float edge = direction > 0 ? crossTarget.bounds.max.x : crossTarget.bounds.min.x;
            float remaining = Mathf.Max(0f, (edge + direction * 0.65f - body.position.x) * direction);
            return Mathf.Min(speed, Mathf.Sqrt(2f * 90f * remaining), remaining / Time.fixedDeltaTime);
        }

        private void BeginAttack()
        {
            IsAirSpin = IsAirLunge = IsLunging = IsAirDive = airFreeDash = false;
            airTarget = null;
            IsSnapBite = false;
            IsAttacking = true;
            elapsed = queuedLight = hitPause = 0f;
            strikeStarted = false;
            attackAim = ReadAimInput();
            if (attackAim.sqrMagnitude < 0.1f) attackAim = Vector2.right * movement.InputFacing();
            AttackFacing = Mathf.Abs(attackAim.x) > 0.1f ? (attackAim.x > 0f ? 1 : -1) : movement.Facing;
            hitTargets.Clear();
            PlayerDebug.ReportAttack();
        }

        private void FindHits()
        {
            Bounds box = AttackHitbox;
            Vector2 origin = body.position + Vector2.up * 0.75f;
            FindHitsAt(box, origin, AttackHitboxAngle);
        }

        private void FixedUpdate()
        {
            RestorePlatforms(false);
            if (dashStrikeActive) FindDashHits();
        }

        private void FindDashHits()
        {
            Vector2 end = body.position;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(lastDashPosition, end) / 0.3f));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 origin = Vector2.Lerp(lastDashPosition, end, (float)i / steps) + Vector2.up * 0.75f;
                FindHitsAt(DashHitbox(origin - Vector2.up * 0.75f), origin);
            }
            lastDashPosition = end;
        }

        private void FindHitsAt(Bounds box, Vector2 origin, float angle = 0f)
        {
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = enemyLayers };
            int count = IsAirSpin ? physics.OverlapCircle(box.center, 1.6f, filter, targets) : physics.OverlapBox(box.center, box.size, angle, filter, targets);
            for (int i = 0; i < count; i++)
            {
                var enemy = targets[i].GetComponentInParent<TestEnemy>();
                if (enemy == null || hitTargets.Contains(enemy)) continue;
                Vector2 contact = targets[i].ClosestPoint(origin);
                Vector2 direction = contact - origin;
                var terrain = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = terrainLayers };
                if (direction.sqrMagnitude > 0.001f && (IsAirLunge ? TerrainBlocks(origin, direction) : physics.Raycast(origin, direction.normalized, direction.magnitude, terrain, walls) > 0)) continue;
                hitTargets.Add(enemy);
                if (!enemy.TakeDamage(damage, IsDirectionalAttack ? attackAim : Vector2.right * AttackFacing, CanInterruptEnemy)) continue;
                CameraFeel.Hit(false, IsHeavyAttack);
            }
        }

        public void CancelAttack()
        {
            if (IsCharging) heavyNeedsRelease = true;
            IsAirSpin = IsAirLunge = IsLunging = IsAirDive = airFreeDash = false;
            airTarget = null;
            IsAttacking = IsCharging = IsDashAttack = IsCrossBite = IsSnapBite = false;
            crossBiteQueued = false;
            dashStrikeActive = false;
            heavyQueued = false;
            elapsed = queuedLight = hitPause = 0f;
            hitTargets.Clear();
        }

        private Bounds DashHitbox(Vector2 feet)
        {
            const float rearReach = 0.6f;
            Vector2 center = feet + new Vector2(AttackFacing * (dashForwardReach - rearReach) * 0.5f, 0.75f);
            return new Bounds(center, new Vector3(dashForwardReach + rearReach, 1.25f, 0f));
        }
        public Bounds AttackHitbox
        {
            get
            {
                bool heavy = IsCharging || IsAttacking && IsHeavyAttack;
                float reach = heavy ? heavyReach : lightReach;
                int direction = IsAttacking ? AttackFacing : GetComponent<PlayerMovement>().Facing;
                Vector2 feet = Application.isPlaying ? GetComponent<Rigidbody2D>().position : (Vector2)transform.position;
                if (IsDashAttack) return DashHitbox(feet);
                if (IsAirDive) return new Bounds(feet + Vector2.up * 0.35f, new Vector3(2.4f, 1.6f, 0f));
                if (IsAirSpin) return new Bounds(feet + Vector2.up * 0.75f, new Vector3(3.2f, 3.2f, 0f));
                if (IsCrossBite) return new Bounds(feet + new Vector2(direction * 0.95f, 0.75f), new Vector3(1.9f, 1.25f, 0f));
                if (IsSnapBite && !IsDirectionalAttack) return new Bounds(feet + new Vector2(direction * 1f, 0.75f), new Vector3(1.9f, 1.1f, 0f));
                if (IsDirectionalAttack) return new Bounds(feet + Vector2.up * 0.75f + attackAim * ((IsSnapBite ? 1.9f : reach) * 0.5f + 0.25f), new Vector3(IsSnapBite ? 1.9f : reach, IsSnapBite ? 1.1f : 1.25f, 0f));
                return new Bounds(feet + new Vector2(direction * (reach * 0.5f + 0.25f), 0.75f), new Vector3(reach, 1.25f, 0f));
            }
        }

        private void OnDrawGizmos()
        {
            if (!showHitbox) return;
            Bounds box = AttackHitbox;
            bool active = IsLunging && IsAirDive || dashStrikeActive || !IsDashAttack && IsAttacking && elapsed >= windup && elapsed < windup + activeTime;
            Gizmos.color = active ? Color.red : IsCharging || IsHeavyAttack && IsAttacking ? new Color(1f, 0.55f, 0.15f) : Color.cyan;
            if (IsAirSpin) Gizmos.DrawWireSphere(box.center, 1.6f);
            else
            {
                var previous = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(box.center, Quaternion.Euler(0f, 0f, AttackHitboxAngle), Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, box.size);
                Gizmos.matrix = previous;
            }
        }
        private void OnApplicationFocus(bool focused) { if (!focused) { CancelAttack(); heavyHeld = false; } }
    }
}

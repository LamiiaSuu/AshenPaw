using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float runSpeed = 10.5f;
        [SerializeField] private float walkSpeed = 6f;
        [SerializeField, Min(0.01f)] private float runBuildUpTime = 0.75f;
        [SerializeField, Min(0f)] private float walkAnimationTime = 0.25f;
        [SerializeField] private float crouchSpeed = 2.8f;
        [SerializeField] private float acceleration = 95f;
        [SerializeField] private float braking = 120f;
        [SerializeField] private float airAcceleration = 48f;
        [Header("Jump")]
        [SerializeField] private float jumpSpeed = 15.5f;
        [SerializeField] private float risingGravity = 30f;
        [SerializeField] private float fallingGravity = 48f;
        [SerializeField] private float maximumFallSpeed = 24f;
        [SerializeField] private float jumpCut = 0.48f;
        [SerializeField] private float coyoteTime = 0.1f;
        [SerializeField] private float jumpBuffer = 0.12f;
        [Header("Wall")]
        [SerializeField] private float wallSlideSpeed = 3f;
        [SerializeField] private Vector2 wallJumpSpeed = new Vector2(13.5f, 14.5f);
        [SerializeField] private float wallJumpLock = 0.14f;
        [Header("Dodge")]
        [SerializeField] private float dodgeSpeed = 13f;
        [SerializeField, Min(0f)] private float runningDashBonus = 0.6f;
        [SerializeField] private float dodgeDuration = 0.30f;
        [SerializeField] private float dodgeCooldown = 0.4f;
        [SerializeField, Min(0f), InspectorName("i-Frames Start (Sekunden)")] private float invulnerabilityStart = 0.03f;
        [SerializeField, Min(0f), InspectorName("i-Frames Ende (Sekunden)")] private float invulnerabilityEnd = 0.25f;
        [Header("Body")]
        [SerializeField] private float width = 0.75f;
        [SerializeField] private float standingHeight = 1.6f;
        [SerializeField] private float crouchingHeight = 0.9f;
        [SerializeField] private LayerMask groundLayers = ~0;

        public float DebugRunSpeed { get => runSpeed; set => runSpeed = Mathf.Clamp(value, 1f, 20f); }
        public float DebugJumpSpeed { get => jumpSpeed; set => jumpSpeed = Mathf.Clamp(value, 3f, 30f); }
        public float DebugFallGravity { get => fallingGravity; set => fallingGravity = Mathf.Clamp(value, 10f, 100f); }
        public float DebugMaxFallSpeed { get => maximumFallSpeed; set => maximumFallSpeed = Mathf.Clamp(value, 5f, 60f); }
        public bool DownHeld => duckHeld;
        public bool IsGrounded { get; private set; }
        public bool UseRunAnimation => groundRunTime >= walkAnimationTime;
        public bool IsCrouching { get; private set; }
        public bool IsDuckHeld => duckHeld;
        public bool IsDodging { get; private set; }
        public bool IsWallSliding { get; private set; }
        public int WallSlideFacing => wallDirection;
        public bool IsWallJumping => !IsGrounded && !IsHitStunned && clock < wallLockUntil;
        public int Facing => facing;
        public bool HasBufferedDodge => dodgeQueued;
        private float dodgeBufferUntil = float.NegativeInfinity;
        public bool IsHitStunned => hitStunRemaining > 0f;
        private float hitStunRemaining;
        private float hitPush;
        public float DodgeProgress => Mathf.Clamp01(dodgeElapsed / dodgeDuration);
        public float CrouchScale => crouchingHeight / standingHeight;
        public float CurrentHeight => IsCrouching ? crouchingHeight : standingHeight;
        public bool IsInvulnerable => IsDodging && dodgeElapsed >= invulnerabilityStart && dodgeElapsed <= invulnerabilityEnd;

        private Rigidbody2D body;
        private CapsuleCollider2D capsule;
        private ContactFilter2D filter;
        private PhysicsScene2D physicsWorld;
        private PlayerCombat combat;
        private float clock;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[12];
        private readonly Collider2D[] overlaps = new Collider2D[12];
        private float groundRunTime;
        private int groundRunDirection;
        private float move, lastGrounded = float.NegativeInfinity, jumpQueued = float.NegativeInfinity;
        private float wallLockUntil, dodgeElapsed, dodgeReadyAt;
        private float dodgeSpeedMultiplier = 1f;
        private int facing = 1, wallDirection, dodgeDirection;
        private bool duckHeld, jumpHeld, jumpReleased, dodgeQueued;
        private bool airDodgeUsed, airDodge;
        private Vector2 spawnPosition;

        private void Awake()
        {
            if (GetComponent<PlayerDebug>() == null) gameObject.AddComponent<PlayerDebug>();
            body = GetComponent<Rigidbody2D>();
            combat = GetComponent<PlayerCombat>();
            capsule = GetComponent<CapsuleCollider2D>();
            physicsWorld = gameObject.scene.GetPhysicsScene2D();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = groundLayers };
            spawnPosition = body.position;
            SetHeight(false);
        }

        private void OnEnable()
        {
            hitStunRemaining = 0f;
            body = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            combat = GetComponent<PlayerCombat>();
            physicsWorld = gameObject.scene.GetPhysicsScene2D();
            filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = groundLayers };
            IsDodging = IsWallSliding = false;
            move = 0f;
            duckHeld = jumpHeld = jumpReleased = dodgeQueued = false;
            wallLockUntil = dodgeReadyAt = clock;
            jumpQueued = lastGrounded = float.NegativeInfinity;
            SetHeight(false);
        }

        public int InputFacing()
        {
            var keyboard = Keyboard.current;
            float direction = 0f;
            if (keyboard != null)
                direction = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            var pad = Gamepad.current;
            if (direction == 0f && pad != null)
            {
                direction = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(direction) < 0.15f) direction = pad.dpad.x.ReadValue();
            }
            return Mathf.Abs(direction) > 0.15f ? (int)Mathf.Sign(direction) : facing;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            float keyboardMove = 0f;
            if (keyboard != null)
            {
                keyboardMove = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            }
            float stick = pad == null ? 0f : pad.leftStick.x.ReadValue();
            if (pad != null && Mathf.Abs(stick) < 0.15f) stick = pad.dpad.x.ReadValue();
            move = keyboardMove != 0f ? keyboardMove : Mathf.Abs(stick) > 0.15f ? stick : 0f;

            duckHeld = keyboard != null && (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed || keyboard.leftCtrlKey.isPressed)
                || pad != null && (pad.leftStick.y.ReadValue() < -0.5f || pad.dpad.down.isPressed);
            bool held = keyboard != null && keyboard.spaceKey.isPressed || pad != null && pad.buttonSouth.isPressed;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame || pad != null && pad.buttonSouth.wasPressedThisFrame)
                jumpQueued = clock;
            jumpReleased |= jumpHeld && !held;
            jumpHeld = held;
            if (keyboard != null && (keyboard.leftShiftKey.wasPressedThisFrame || keyboard.kKey.wasPressedThisFrame)
                || pad != null && pad.buttonEast.wasPressedThisFrame)
            {
                dodgeQueued = true;
                dodgeBufferUntil = clock + 0.16f;
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            clock += dt;
            IsWallSliding = false;
            if (body.position.y < -20f) { Respawn(); return; }
            IsGrounded = body.linearVelocity.y <= 0.1f && FindGround();
            if (IsGrounded)
            {
                combat?.ResetAirAction();
                lastGrounded = clock;
                airDodgeUsed = false;
            }
            wallDirection = FindWall();
            if (dodgeQueued && combat != null && !combat.CanDodge) dodgeBufferUntil = clock + 0.16f;

            UpdateRunBuildUp(dt);

            if (IsHitStunned)
            {
                hitStunRemaining = Mathf.Max(0f, hitStunRemaining - dt);
                hitPush = Mathf.MoveTowards(hitPush, 0f, 25f * dt);
                body.linearVelocity = new Vector2(hitPush, Mathf.Max(body.linearVelocity.y - fallingGravity * dt, -maximumFallSpeed));
                dodgeQueued = jumpReleased = false;
                return;
            }

            if (IsDodging)
            {
                if (!float.IsNegativeInfinity(jumpQueued)) jumpQueued = clock;
                dodgeElapsed += dt;
                float speed = dodgeSpeed * dodgeSpeedMultiplier * Mathf.Lerp(1.25f, 0.8f, DodgeProgress);
                if (combat != null) speed = combat.LimitDodgeSpeed(speed, dodgeDirection);
                float vertical = airDodge && !IsGrounded ? 0f : Mathf.Max(body.linearVelocity.y - fallingGravity * dt, -maximumFallSpeed);
                body.linearVelocity = new Vector2(dodgeDirection * speed, vertical);
                if (dodgeElapsed >= dodgeDuration)
                {
                    IsDodging = false;
                    SetHeight((duckHeld && IsGrounded) || !CanStand());
                    if (combat != null && combat.IsDashAttack)
                        body.linearVelocity = new Vector2(0f, vertical);
                    else if (move * dodgeDirection <= 0f)
                        body.linearVelocity = new Vector2(dodgeDirection * runSpeed, vertical);
                }
                if (combat == null || !combat.IsDashAttack) dodgeQueued = false;
                jumpReleased = false;
                return;
            }

            if (combat != null && combat.IsLunging)
            {
                facing = combat.AttackFacing;
                body.linearVelocity = combat.AirLungeVelocity;
                return;
            }

            if (combat != null && (combat.IsCrossBite || combat.IsSnapBite && !combat.IsDirectionalAttack) && !combat.IsRecovering)
            {
                facing = combat.AttackFacing;
                Vector2 strikeVelocity = body.linearVelocity;
                strikeVelocity.x = combat.IsSnapBite && !combat.IsAirLunge ? combat.SnapVelocityX : combat.CrossBiteVelocityX;
                strikeVelocity.y = Mathf.Max(strikeVelocity.y - fallingGravity * dt, -maximumFallSpeed);
                body.linearVelocity = strikeVelocity;
                jumpReleased = false;
                return;
            }

            SetHeight(duckHeld && IsGrounded || IsCrouching && !CanStand());
            if (dodgeQueued && (IsGrounded || !airDodgeUsed) && clock >= dodgeReadyAt && (combat == null || combat.CanDodge))
            {
                combat?.CancelAttack();
                airDodge = !IsGrounded;
                if (airDodge) airDodgeUsed = true;
                IsDodging = true;
                dodgeElapsed = 0f;
                dodgeReadyAt = clock + dodgeCooldown;
                dodgeDirection = InputFacing();
                float forwardSpeed = body.linearVelocity.x * dodgeDirection;
                float dashRunProgress = runSpeed > walkSpeed ? Mathf.InverseLerp(walkSpeed, runSpeed, forwardSpeed) : 0f;
                dodgeSpeedMultiplier = 1f + runningDashBonus * dashRunProgress;
                facing = dodgeDirection;
                SetHeight(true);
                if (!HasJump()) jumpQueued = float.NegativeInfinity;
                body.linearVelocity = new Vector2(dodgeDirection * dodgeSpeed * dodgeSpeedMultiplier * 1.25f, 0f);
                dodgeQueued = false;
                return;
            }
            if (clock > dodgeBufferUntil) dodgeQueued = false;

            Vector2 velocity = body.linearVelocity;
            if (clock >= wallLockUntil)
            {
                float runProgress = Mathf.Clamp01(groundRunTime / runBuildUpTime);
                float groundSpeed = Mathf.Lerp(Mathf.Min(walkSpeed, runSpeed), runSpeed, runProgress);
                float speed = IsCrouching ? crouchSpeed : groundSpeed;
                if (combat != null) speed *= combat.MovementMultiplier;
                float rate = IsGrounded ? move == 0f ? braking : acceleration : airAcceleration;
                velocity.x = Mathf.MoveTowards(velocity.x, move * speed, rate * dt);
            }
            if (combat != null && combat.IsCritical)
                velocity.x = combat.StepCriticalRecoil(dt);
            if (combat != null && combat.DirectionalBurstActive)
            {
                Vector2 burst = combat.DirectionalBurstVelocity;
                velocity.x = burst.x;
                if (Mathf.Abs(combat.AttackDirection.y) > 0.1f) velocity.y = burst.y;
            }
            if (combat != null && combat.LocksFacing) facing = combat.AttackFacing;
            else if (move != 0f && clock >= wallLockUntil) facing = (int)Mathf.Sign(move);

            if (HasJump() && CanStand() && (combat == null || combat.CanJump))
            {
                if (clock - lastGrounded <= coyoteTime)
                {
                    SetHeight(false);
                    velocity.y = jumpSpeed;
                    ConsumeJump();
                }
                else if (wallDirection != 0 && clock >= wallLockUntil)
                {
                    body.linearVelocity = velocity;
                    JumpFromWall();
                    velocity = body.linearVelocity;
                }
            }
            if (jumpReleased && velocity.y > 0f) velocity.y *= jumpCut;
            jumpReleased = false;
            velocity.y = Mathf.Max(velocity.y - (velocity.y > 0f ? risingGravity : fallingGravity) * dt, -maximumFallSpeed);
            if (!IsGrounded && wallDirection != 0 && move * wallDirection > 0f && velocity.y < 0f && clock >= wallLockUntil)
            {
                IsWallSliding = true;
                velocity.y = Mathf.Max(velocity.y, -wallSlideSpeed);
            }

            body.linearVelocity = velocity;
        }

        private void UpdateRunBuildUp(float dt)
        {
            int direction = move == 0f || duckHeld || (IsCrouching && !IsDodging) || IsHitStunned
                ? 0 : move > 0f ? 1 : -1;
            if (direction != groundRunDirection || direction == 0)
                groundRunTime = 0f;
            groundRunDirection = direction;

            if (IsGrounded && !IsDodging && direction != 0)
                groundRunTime = Mathf.Min(groundRunTime + dt, runBuildUpTime);
        }

        private bool HasJump() => clock - jumpQueued <= jumpBuffer;

        private void ConsumeJump()
        {
            jumpQueued = lastGrounded = float.NegativeInfinity;
            IsGrounded = false;
        }

        private bool FindGround()
        {
            int count = capsule.Cast(Vector2.down, filter, hits, 0.06f);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].normal.y < 0.65f) continue;
                if (hits[i].collider.usedByEffector && capsule.bounds.min.y < hits[i].collider.bounds.max.y - 0.08f) continue;
                return true;
            }
            return false;
        }

        private int FindWall()
        {
            int first = move == 0f ? facing : (int)Mathf.Sign(move);
            for (int side = 0; side < 2; side++)
            {
                int direction = side == 0 ? first : -first;
                int count = capsule.Cast(Vector2.right * direction, filter, hits, 0.07f);
                for (int i = 0; i < count; i++)
                    if (!hits[i].collider.usedByEffector && hits[i].collider.bounds.max.y > capsule.bounds.min.y + 0.2f
                        && hits[i].normal.x * direction < -0.7f && Mathf.Abs(hits[i].normal.y) < 0.3f)
                        return direction;
            }
            return 0;
        }

        private bool CanStand()
        {
            if (!IsCrouching) return true;
            Vector2 center = body.position + Vector2.up * (standingHeight * 0.5f + 0.03f);
            int count = physicsWorld.OverlapCapsule(center, new Vector2(width - 0.04f, standingHeight - 0.04f), CapsuleDirection2D.Vertical, 0f, filter, overlaps);
            for (int i = 0; i < count; i++)
            {
                Collider2D other = overlaps[i];
                if (other.attachedRigidbody == body) continue;
                if (other.usedByEffector && body.position.y < other.bounds.max.y - 0.08f) continue;
                return false;
            }
            return true;
        }

        private void SetHeight(bool crouch)
        {
            IsCrouching = crouch;
            float height = crouch ? crouchingHeight : standingHeight;
            capsule.size = new Vector2(width, height);
            capsule.offset = Vector2.up * (height * 0.5f);
        }

        private void JumpFromWall()
        {
            int direction = wallDirection;
            SetHeight(false);
            body.linearVelocity = new Vector2(-direction * wallJumpSpeed.x, wallJumpSpeed.y);
            facing = -direction;
            wallLockUntil = clock + wallJumpLock;
            ConsumeJump();
        }

        public void Respawn()
        {
            dodgeQueued = false;
            dodgeBufferUntil = float.NegativeInfinity;
            hitStunRemaining = 0f;
            combat?.CancelAttack();
            combat?.ResetAirAction();
            IsDodging = false;
            airDodgeUsed = airDodge = false;
            groundRunTime = 0f;
            groundRunDirection = 0;
            jumpQueued = lastGrounded = float.NegativeInfinity;
            wallLockUntil = 0f;
            SetHeight(false);
            body.position = spawnPosition;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            move = 0f;
            duckHeld = jumpHeld = jumpReleased = dodgeQueued = false;
            jumpQueued = float.NegativeInfinity;
        }

        public void ContinueDodgeIntoAttack()
        {
            IsDodging = false;
            dodgeQueued = false;
            dodgeBufferUntil = float.NegativeInfinity;
            SetHeight(false);
        }
        public void ReceiveHit(Vector2 direction)
        {
            IsDodging = false;
            hitStunRemaining = 0.14f;
            hitPush = (direction.x == 0f ? -facing : Mathf.Sign(direction.x)) * 4f;
            jumpQueued = float.NegativeInfinity;
            dodgeQueued = false;
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class TestEnemy : MonoBehaviour
    {
        private enum State
        {
            Idle, Chase, Windup, Attack, SecondWindup, SecondAttack,
            LungeWindup, LungeStep, LungeAttack, Recover, Hurt, Dead, Breaking, Reviving, Parried
        }
        [SerializeField, InspectorName("Hitbox anzeigen")] private bool showHitbox = true;
        [SerializeField] private bool dummyMode = true;
        [SerializeField] private float dummyScale = 1.3f;
        [SerializeField] private float skeletonScale = 1.8f;
        private float Size => dummyMode ? dummyScale : skeletonScale;
        [SerializeField, Min(0.2f)] private float parryStun = 1.55f;
        [SerializeField, Min(0f)] private float parryRecoil = 2.8f;
        [SerializeField] private float moveSpeed = 1.5f;
        [SerializeField] private float followRange = 12f;
        [SerializeField] private float attackRange = 1.4f;
        [SerializeField] private float windupDuration = 0.7f;
        [SerializeField] private float attackDuration = 0.22f;
        [SerializeField] private float recoveryDuration = 0.65f;
        [SerializeField] private int attackDamage = 15;
        [SerializeField] private float secondWindupDuration = 0.34f;
        [SerializeField] private float secondAttackDuration = 0.18f;
        [SerializeField, Min(0f)] private float lungeRange = 2.35f;
        [SerializeField, Min(0.02f)] private float lungeWindupDuration = 0.76f;
        [SerializeField, Min(0.02f)] private float lungeStepDuration = 0.22f;
        [SerializeField, Min(0f)] private float lungeSpeed = 8.5f;
        [SerializeField, Min(0.02f)] private float lungeAttackDuration = 0.20f;
        [SerializeField, Min(1)] private int lungeDamage = 22;
        [SerializeField, Min(0f)] private float lungeRecoveryDuration = 0.82f;
        [SerializeField, Min(0f)] private float missRecoveryExtra = 0.30f;
        [SerializeField, Min(0f)] private float interruptRecoveryDuration = 0.44f;
        [SerializeField] private LayerMask terrainLayers = ~((1 << 8) | (1 << 9));
        [SerializeField] private Transform visuals;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite[] skeletonIdle, skeletonWalk, skeletonWindup, skeletonAttack, skeletonRecover, skeletonHit, skeletonDeath;
        [SerializeField] private Sprite[] skeletonLungeWindup;
        [SerializeField] private Sprite[] dummyIdle, dummyHit, dummyBreak;
        [SerializeField] private SpriteRenderer healthFill;
        [SerializeField] private Transform healthBar;
        [SerializeField] private TextMesh status;
        [SerializeField] private Sprite effectSprite;
        [SerializeField] private Material effectMaterial;

        public bool DummyMode => dummyMode;
        public int Health { get; private set; }
        public int MaxHealth => dummyMode ? 500 : 100;
        public bool IsDead => state == State.Dead || state == State.Reviving;

        private Rigidbody2D body;
        private CapsuleCollider2D capsule;
        private PhysicsScene2D physics;
        private PlayerHealth target;
        private Collider2D targetCollider;
        private Rigidbody2D targetBody;
        private readonly RaycastHit2D[] rays = new RaycastHit2D[8];
        private readonly Collider2D[] hitResults = new Collider2D[8];
        private Vector2 spawnPosition;
        private State state;
        private float stateTime, flashTime;
        private int facing = -1;
        private bool hitAttempted;
        private bool attackLanded;
        private bool lungeSelected;
        private float hurtDuration;
        [SerializeField, Min(0f)] private float criticalKnockback = 16f;
        private float parryStunRemaining;
        private PlayerCombat criticalOwner;
        private float criticalRecoilTime;
        public bool IsParried => !dummyMode && state == State.Parried;
        public Vector2 Feet => body.position;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            physics = gameObject.scene.GetPhysicsScene2D();
            spawnPosition = body.position;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            FindPlayer();
            SetDummyMode(dummyMode);
        }

        private void FindPlayer()
        {
            var candidates = FindObjectsByType<PlayerHealth>();
            foreach (var candidate in candidates)
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                target = candidate;
                targetCollider = target.GetComponent<Collider2D>();
                targetBody = target.GetComponent<Rigidbody2D>();
                if (targetCollider != null) Physics2D.IgnoreCollision(capsule, targetCollider);
                break;
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
                SetDummyMode(!dummyMode);
        }

        public void SetDummyMode(bool enabled)
        {
            criticalOwner = null;
            criticalRecoilTime = 0f;
            dummyMode = enabled;
            Health = MaxHealth;
            capsule.enabled = true;
            capsule.size = new Vector2(0.8f, 1.5f) * Size;
            capsule.offset = Vector2.up * (0.75f * Size);
            body.bodyType = enabled ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            body.gravityScale = enabled ? 0f : 4f;
            body.position = spawnPosition;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            facing = -1;
            flashTime = 0f;
            ChangeState(State.Idle);
            UpdatePresentation();
        }

        private void FixedUpdate() => Tick(Time.fixedDeltaTime);

        private bool IsWindingUp => state is State.Windup or State.SecondWindup or State.LungeWindup or State.LungeStep;
        private bool IsSwinging => state is State.Attack or State.SecondAttack or State.LungeAttack;
        private float AttackRecoveryDuration => lungeSelected ? lungeRecoveryDuration : recoveryDuration;

        public float DebugHitStunRemaining => state == State.Hurt ? Mathf.Max(0f, hurtDuration - stateTime) : 0f;
        public bool DebugIsAttacking => IsWindingUp || IsSwinging;
        public bool DebugAttackActive => IsSwinging;
        public string DebugInfo => (dummyMode ? "Dummy" : "Skeleton") + "  " + Health + "/" + MaxHealth + " HP  |  " + state;

        private void Tick(float dt)
        {
            stateTime += dt;
            flashTime = Mathf.Max(0f, flashTime - dt);
            if (dummyMode)
            {
                body.linearVelocity = Vector2.zero;
                if (state == State.Breaking && stateTime >= 0.9f) { Health = MaxHealth; ChangeState(State.Idle); }
                else if (state == State.Hurt && stateTime >= hurtDuration) ChangeState(State.Idle);
                return;
            }
            if (state == State.Dead)
            {
                if (stateTime >= 3f)
                {
                    if (body.position.y < -10f) body.position = spawnPosition;
                    ChangeState(State.Reviving);
                }
                return;
            }
            if (state == State.Reviving)
            {
                if (stateTime >= 1.3f)
                {
                    Health = MaxHealth;
                    capsule.enabled = true;
                    body.bodyType = RigidbodyType2D.Dynamic;
                    body.gravityScale = 4f;
                    ChangeState(State.Idle);
                }
                return;
            }
            if (body.position.y < -10f) { Die(); return; }
            if (IsParried)
            {
                if (criticalOwner != null) { Stop(); return; }
                parryStunRemaining -= dt;
                body.linearVelocity = new Vector2(Mathf.MoveTowards(body.linearVelocity.x, 0f, 22f * dt), body.linearVelocity.y);
                if (parryStunRemaining <= 0f) ChangeState(State.Idle);
                return;
            }
            if (target == null) { FindPlayer(); Stop(); return; }
            if (target.GetComponent<PlayerDebug>() is PlayerDebug debug && debug.HiddenFromEnemies) { Stop(); ChangeState(State.Idle); return; }
            if (state == State.Hurt)
            {
                if (criticalRecoilTime > 0f)
                {
                    criticalRecoilTime = Mathf.Max(0f, criticalRecoilTime - dt);
                    body.linearVelocity = new Vector2(Mathf.MoveTowards(body.linearVelocity.x, 0f, 27f * dt), body.linearVelocity.y);
                }
                else Stop();
                if (stateTime >= hurtDuration && criticalRecoilTime <= 0f) ChangeState(State.Idle);
                return;
            }
            if (state == State.Windup)
            {
                Stop();
                if (stateTime >= windupDuration) ChangeState(State.Attack);
                return;
            }
            if (state == State.Attack)
            {
                Stop();
                TryHitPlayer();
                if (state == State.Attack && stateTime >= attackDuration) ChangeState(State.SecondWindup);
                return;
            }
            if (state == State.SecondWindup)
            {
                Stop();
                if (stateTime >= secondWindupDuration) ChangeState(State.SecondAttack);
                return;
            }
            if (state == State.SecondAttack)
            {
                Stop();
                TryHitPlayer();
                if (state == State.SecondAttack && stateTime >= secondAttackDuration) ChangeState(State.Recover);
                return;
            }
            if (state == State.LungeWindup)
            {
                Stop();
                if (stateTime >= lungeWindupDuration) ChangeState(State.LungeStep);
                return;
            }
            if (state == State.LungeStep)
            {
                if (SafeStep(facing))
                    body.linearVelocity = new Vector2(facing * lungeSpeed, body.linearVelocity.y);
                else Stop();

                if (stateTime >= lungeStepDuration) ChangeState(State.LungeAttack);
                return;
            }
            if (state == State.LungeAttack)
            {
                Stop();
                TryHitPlayer();
                if (state == State.LungeAttack && stateTime >= lungeAttackDuration) ChangeState(State.Recover);
                return;
            }
            if (state == State.Recover)
            {
                Stop();
                float duration = AttackRecoveryDuration + (attackLanded ? 0f : missRecoveryExtra);
                if (stateTime >= duration) ChangeState(State.Idle);
                return;
            }

            Vector2 targetPosition = targetBody != null ? targetBody.position : (Vector2)target.transform.position;
            Vector2 difference = targetPosition - body.position;
            if (difference.sqrMagnitude > followRange * followRange) { Stop(); ChangeState(State.Idle); return; }
            float horizontalDistance = Mathf.Abs(difference.x);
            if (horizontalDistance > 0.05f) facing = difference.x > 0f ? 1 : -1;
            if (horizontalDistance <= Mathf.Max(attackRange, lungeRange) * Size &&
                Mathf.Abs(difference.y) < 1.3f * Size && ClearSight())
            {
                Stop();
                attackLanded = false;
                lungeSelected = horizontalDistance > attackRange * Size;
                ChangeState(lungeSelected ? State.LungeWindup : State.Windup);
                return;
            }
            if (horizontalDistance <= 0.65f || !SafeStep(facing))
            {
                Stop();
                ChangeState(State.Idle);
                return;
            }
            ChangeState(State.Chase);
            body.linearVelocity = new Vector2(facing * moveSpeed, body.linearVelocity.y);
        }

        private void Stop() => body.linearVelocity = new Vector2(0f, body.linearVelocity.y);

        private bool SafeStep(int direction)
        {
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = terrainLayers };
            Vector2 ahead = body.position + new Vector2(direction * 0.6f, 0.4f) * Size;
            if (physics.Raycast(ahead, Vector2.down, 0.85f * Size, filter, rays) == 0) return false;
            Vector2 chest = body.position + Vector2.up * (0.7f * Size);
            return physics.Raycast(chest, Vector2.right * direction, 0.65f * Size, filter, rays) == 0;
        }

        private bool ClearSight()
        {
            if (targetCollider == null) return false;
            Vector2 origin = body.position + Vector2.up * (0.75f * Size);
            Vector2 delta = (Vector2)targetCollider.bounds.center - origin;
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = terrainLayers };
            return physics.Raycast(origin, delta.normalized, delta.magnitude, filter, rays) == 0;
        }

        private void TryHitPlayer()
        {
            if (hitAttempted || target == null || !ClearSight()) return;
            Bounds box = AttackHitbox;
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = 1 << target.gameObject.layer };
            int count = physics.OverlapBox(box.center, box.size, 0f, filter, hitResults);
            for (int i = 0; i < count; i++)
            {
                if (hitResults[i].GetComponentInParent<PlayerHealth>() != target) continue;
                hitAttempted = true;
                Vector2 direction = Vector2.right * facing;
                var combat = target.GetComponent<PlayerCombat>();
                if (combat != null && combat.TryParry(this, direction)) break;
                int damage = state == State.LungeAttack ? lungeDamage : attackDamage;
                if (target.TakeDamage(damage, direction)) attackLanded = true;
                break;
            }
        }

        public bool TryStaggerFromParry(float direction)
        {
            if (dummyMode || !IsSwinging) return false;
            ChangeState(State.Parried);
            flashTime = 0.12f;
            parryStunRemaining = parryStun;
            body.linearVelocity = new Vector2(direction * parryRecoil, body.linearVelocity.y);
            return true;
        }

        public bool LockForCritical(PlayerCombat owner)
        {
            if (!IsParried || criticalOwner != null) return false;
            criticalOwner = owner;
            Stop();
            return true;
        }

        public void ReleaseCritical(PlayerCombat owner)
        {
            if (criticalOwner == owner) criticalOwner = null;
        }

        public bool TakeCritical(PlayerCombat owner, int damage, int direction)
        {
            if (!IsParried || criticalOwner != owner) return false;
            criticalOwner = null;
            if (!ApplyDamage(damage, true, "CRITICAL")) return false;
            if (!IsDead)
            {
                criticalRecoilTime = 0.56f;
                body.linearVelocity = new Vector2(direction * criticalKnockback, 3.6f);
            }
            return true;
        }

        public bool TakeDamage(int damage, Vector2 direction, bool heavy)
            => ApplyDamage(damage, heavy && !IsParried, "ENEMY HIT");

        private bool ApplyDamage(int damage, bool heavy, string outcome)
        {
            if (damage <= 0 || state == State.Dead || state == State.Reviving || state == State.Breaking) return false;
            int actualDamage = Mathf.Min(Health, damage);
            Health = Mathf.Max(0, Health - damage);
            flashTime = 0.12f;
            if (Health == 0)
            {
                if (dummyMode) ChangeState(State.Breaking);
                else Die();
            }
            else if (dummyMode || heavy || state == State.Idle || state == State.Chase || state == State.Hurt)
            {
                bool interrupted = heavy && (IsWindingUp || IsSwinging);
                float duration = dummyMode ? 0.58f : interrupted ? interruptRecoveryDuration : 0.28f;
                hurtDuration = Mathf.Max(duration, DebugHitStunRemaining);
                if (interrupted) Stop();
                ChangeState(State.Hurt);
            }
            PlayerDebug.Report(actualDamage, false, capsule.bounds.center + Vector3.up * (capsule.bounds.extents.y + 0.3f), outcome, this);
            return true;
        }

        private void Die()
        {
            Health = 0;
            criticalOwner = null;
            criticalRecoilTime = 0f;
            body.linearVelocity = Vector2.zero;
            body.bodyType = RigidbodyType2D.Kinematic;
            capsule.enabled = false;
            ChangeState(State.Dead);
        }

        private void ChangeState(State next)
        {
            if (state == next && stateTime > 0f && (next == State.Idle || next == State.Chase)) return;
            state = next;
            stateTime = 0f;
            hitAttempted = false;
            if (next == State.SecondWindup && targetBody != null && Mathf.Abs(targetBody.position.x - body.position.x) > 0.05f)
                facing = targetBody.position.x > body.position.x ? 1 : -1;
        }

        public Bounds AttackHitbox
        {
            get
            {
                Vector2 feet = body != null ? body.position : (Vector2)transform.position;
                return new Bounds(feet + new Vector2(facing * 0.74f, 0.8f) * Size, new Vector3(1.18f, 1.1f, 0f) * Size);
            }
        }

        private void OnDrawGizmos()
        {
            if (!showHitbox) return;
            if (dummyMode || Application.isPlaying && !DebugIsAttacking) return;
            Bounds box = AttackHitbox;
            Gizmos.color = IsSwinging ? Color.red : new Color(1f, 0.65f, 0.15f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        private void LateUpdate() => UpdatePresentation();

        private void UpdatePresentation()
        {
            if (spriteRenderer != null)
            {
                Sprite[] frames;
                float duration;
                bool loop = false;
                int frameStart = 0, frameCount = 0;
                if (dummyMode)
                {
                    frames = state == State.Breaking ? dummyBreak : state == State.Hurt ? dummyHit : dummyIdle;
                    duration = state == State.Breaking ? 0.9f : state == State.Hurt ? hurtDuration : 0.58f;
                    loop = state == State.Idle;
                }
                else
                {
                    switch (state)
                    {
                        case State.Chase: frames = skeletonWalk; duration = 1.2f; loop = true; break;
                        case State.Windup: frames = skeletonWindup; duration = windupDuration; break;
                        case State.Attack: frames = skeletonAttack; duration = attackDuration; frameCount = 2; break;
                        case State.SecondWindup: frames = skeletonAttack; duration = secondWindupDuration; frameStart = 2; frameCount = 2; break;
                        case State.SecondAttack: frames = skeletonAttack; duration = secondAttackDuration; frameStart = 4; frameCount = 1; break;
                        case State.LungeWindup: frames = skeletonLungeWindup; duration = lungeWindupDuration; break;
                        case State.LungeStep: frames = skeletonLungeWindup; duration = lungeStepDuration; frameStart = Mathf.Max(0, (frames?.Length ?? 0) - 1); frameCount = 1; break;
                        case State.LungeAttack: frames = skeletonAttack; duration = lungeAttackDuration; frameStart = 4; frameCount = 1; break;
                        case State.Recover: frames = skeletonRecover; duration = AttackRecoveryDuration; break;
                        case State.Parried: frames = skeletonHit; duration = 0.28f; break;
                        case State.Hurt: frames = skeletonHit; duration = hurtDuration; break;
                        case State.Dead:
                        case State.Reviving: frames = skeletonDeath; duration = 1.3f; break;
                        default: frames = skeletonIdle; duration = 0.6f; loop = true; break;
                    }
                }
                if (frames != null && frames.Length > 0)
                {
                    frameStart = Mathf.Min(frameStart, frames.Length - 1);
                    int count = frameCount > 0 ? Mathf.Min(frameCount, frames.Length - frameStart) : frames.Length;
                    int index = Mathf.FloorToInt(stateTime / Mathf.Max(0.01f, duration) * count);
                    index = frameStart + (loop ? index % count : Mathf.Min(index, count - 1));
                    if (state == State.Reviving) index = frames.Length - 1 - index;
                    spriteRenderer.sprite = frames[index];
                }
                Color color = Color.Lerp(Color.white, new Color(4f, 3.6f, 3.6f, 1f), Mathf.Clamp01(flashTime / 0.12f));
                if (IsParried && criticalOwner == null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(stateTime * 12f);
                    color *= Color.Lerp(Color.white, new Color(1.45f, 1.18f, 0.80f, 1f), 0.55f + 0.45f * pulse);
                }
                spriteRenderer.color = color;
            }
            if (visuals != null)
            {
                visuals.localScale = new Vector3(Size, Size * (dummyMode && state == State.Idle ? 1f + Mathf.Sin(stateTime * 3f) * 0.015f : 1f), 1f);
                if (spriteRenderer != null) spriteRenderer.flipX = !dummyMode && facing < 0;
                visuals.localRotation = Quaternion.Euler(0f, 0f, dummyMode && state == State.Idle ? Mathf.Sin(stateTime * 2f) * 1.2f : 0f);
            }
            if (healthBar != null) healthBar.gameObject.SetActive(PlayerDebug.ShowEnemyHealth);
            if (status != null) status.gameObject.SetActive(PlayerDebug.ShowEnemyHealth);
            if (healthBar != null) healthBar.position = transform.position + Vector3.up * (1.7f * Size + 0.45f);
            if (healthFill != null)
            {
                float amount = (float)Health / MaxHealth;
                healthFill.transform.localScale = new Vector3(Mathf.Max(0.001f, amount) * 1.8f, 0.08f, 1f);
                healthFill.transform.localPosition = new Vector3(-0.9f + amount * 0.9f, 0f, 0f);
                healthFill.color = dummyMode ? new Color(0.9f, 0.7f, 0.3f) : new Color(0.9f, 0.3f, 0.3f);
            }
            if (status != null)
            {
                bool open = IsParried && criticalOwner == null;
                status.text = (dummyMode ? "DUMMY " : "SKELETON ") + Health + "/" + MaxHealth
                    + (open ? "  <color=#FFC578>OPEN</color>" : "");
            }
        }
    }
}

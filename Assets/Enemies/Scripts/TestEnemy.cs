using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class TestEnemy : MonoBehaviour
    {
        private enum State { Idle, Chase, Windup, Attack, Recover, Hurt, Dead, Breaking, Reviving, SecondWindup, SecondAttack }
        [SerializeField, InspectorName("Hitbox anzeigen")] private bool showHitbox = true;
        [SerializeField] private bool dummyMode = true;
        [SerializeField] private float dummyScale = 1.3f;
        [SerializeField] private float skeletonScale = 1.8f;
        private float Size => dummyMode ? dummyScale : skeletonScale;
        [SerializeField] private float moveSpeed = 1.5f;
        [SerializeField] private float followRange = 12f;
        [SerializeField] private float attackRange = 1.65f;
        [SerializeField] private float windupDuration = 0.7f;
        [SerializeField] private float attackDuration = 0.22f;
        [SerializeField] private float recoveryDuration = 0.65f;
        [SerializeField] private int attackDamage = 15;
        [SerializeField] private float secondWindupDuration = 0.34f;
        [SerializeField] private float secondAttackDuration = 0.18f;
        [SerializeField] private LayerMask terrainLayers = ~((1 << 8) | (1 << 9));
        [SerializeField] private Transform visuals;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite[] skeletonIdle, skeletonWalk, skeletonWindup, skeletonAttack, skeletonRecover, skeletonHit, skeletonDeath;
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

        private void Tick(float dt)
        {
            stateTime += dt;
            flashTime = Mathf.Max(0f, flashTime - dt);
            if (dummyMode)
            {
                body.linearVelocity = Vector2.zero;
                if (state == State.Breaking && stateTime >= 0.9f) { Health = MaxHealth; ChangeState(State.Idle); }
                else if (state == State.Hurt && stateTime >= 0.58f) ChangeState(State.Idle);
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
            if (target == null) { FindPlayer(); Stop(); return; }
            if (state == State.Hurt)
            {
                Stop();
                if (stateTime >= 0.28f) ChangeState(State.Idle);
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
                if (stateTime >= attackDuration) ChangeState(State.SecondWindup);
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
                if (stateTime >= secondAttackDuration) ChangeState(State.Recover);
                return;
            }
            if (state == State.Recover)
            {
                Stop();
                if (stateTime >= recoveryDuration) ChangeState(State.Idle);
                return;
            }

            Vector2 targetPosition = targetBody != null ? targetBody.position : (Vector2)target.transform.position;
            Vector2 difference = targetPosition - body.position;
            if (difference.sqrMagnitude > followRange * followRange) { Stop(); ChangeState(State.Idle); return; }
            if (Mathf.Abs(difference.x) > 0.05f) facing = difference.x > 0f ? 1 : -1;
            if (Mathf.Abs(difference.x) <= attackRange * Size && Mathf.Abs(difference.y) < 1.3f * Size && ClearSight())
            {
                Stop();
                ChangeState(State.Windup);
                return;
            }
            if (Mathf.Abs(difference.x) <= 0.65f || !SafeStep(facing))
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
                target.TakeDamage(attackDamage, Vector2.right * facing);
                break;
            }
        }

        public bool TakeDamage(int damage, Vector2 direction, bool heavy)
        {
            if (damage <= 0 || state == State.Dead || state == State.Reviving || state == State.Breaking) return false;
            Health = Mathf.Max(0, Health - damage);
            flashTime = 0.12f;
            if (Health == 0)
            {
                if (dummyMode) ChangeState(State.Breaking);
                else Die();
            }
            else if (dummyMode || heavy || state == State.Idle || state == State.Chase || state == State.Hurt)
                ChangeState(State.Hurt);
            return true;
        }

        private void Die()
        {
            Health = 0;
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
                Vector2 feet = Application.isPlaying && body != null ? body.position : (Vector2)transform.position;
                return new Bounds(feet + new Vector2(facing * 1.05f, 0.8f) * Size, new Vector3(1.6f, 1.25f, 0f) * Size);
            }
        }

        private void OnDrawGizmos()
        {
            if (!showHitbox) return;
            if (dummyMode) return;
            Bounds box = AttackHitbox;
            Gizmos.color = state == State.Attack || state == State.SecondAttack ? Color.red : new Color(1f, 0.65f, 0.15f);
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
                    duration = state == State.Breaking ? 0.9f : 0.58f;
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
                        case State.Recover: frames = skeletonRecover; duration = recoveryDuration; break;
                        case State.Hurt: frames = skeletonHit; duration = 0.28f; break;
                        case State.Dead:
                        case State.Reviving: frames = skeletonDeath; duration = 1.3f; break;
                        default: frames = skeletonIdle; duration = 0.6f; loop = true; break;
                    }
                }
                if (frames != null && frames.Length > 0)
                {
                    int count = frameCount > 0 ? Mathf.Min(frameCount, frames.Length - frameStart) : frames.Length;
                    int index = Mathf.FloorToInt(stateTime / Mathf.Max(0.01f, duration) * count);
                    index = frameStart + (loop ? index % count : Mathf.Min(index, count - 1));
                    if (state == State.Reviving) index = frames.Length - 1 - index;
                    spriteRenderer.sprite = frames[index];
                }
                Color color = Color.white;
                spriteRenderer.color = color;
            }
            if (visuals != null)
            {
                visuals.localScale = new Vector3(Size, Size * (dummyMode && state == State.Idle ? 1f + Mathf.Sin(stateTime * 3f) * 0.015f : 1f), 1f);
                if (spriteRenderer != null) spriteRenderer.flipX = !dummyMode && facing < 0;
                visuals.localRotation = Quaternion.Euler(0f, 0f, dummyMode && state == State.Idle ? Mathf.Sin(stateTime * 2f) * 1.2f : 0f);
            }
            if (healthBar != null) healthBar.position = transform.position + Vector3.up * (1.7f * Size + 0.45f);
            if (healthFill != null)
            {
                float amount = (float)Health / MaxHealth;
                healthFill.transform.localScale = new Vector3(Mathf.Max(0.001f, amount) * 1.8f, 0.08f, 1f);
                healthFill.transform.localPosition = new Vector3(-0.9f + amount * 0.9f, 0f, 0f);
                healthFill.color = dummyMode ? new Color(0.9f, 0.7f, 0.3f) : new Color(0.9f, 0.3f, 0.3f);
            }
            if (status != null) status.text = (dummyMode ? "DUMMY " : "SKELETT ") + Health + "/" + MaxHealth;
        }
    }
}

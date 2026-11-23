using System.Collections.Generic;
using UnityEngine;

namespace AshenPaw
{
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class PlayerVisuals : MonoBehaviour
    {
        [SerializeField] private Transform visuals;
        [SerializeField] private Animator animator;
        [SerializeField] private bool placeholderMotion = true;
        [SerializeField] private bool feetPivot;
        [SerializeField] private Material contactMaterial;
        private PlayerMovement movement;
        private Rigidbody2D body;
        private PlayerCombat combat;
        private PlayerHealth health;
        private Vector3 originalScale;
        private float landingSquash;
        [SerializeField] private AnimationClip[] upwardAttacks;
        [SerializeField] private AnimationClip[] downwardAttacks;
        private bool spinning;
        private Vector3 spinPivot;
        private SpriteRenderer bodySprite;
        private Color originalColor;
        private const int TrailSamples = 6;
        private const int DodgeEchoIndex = TrailSamples;
        private static readonly int FlashColor = Shader.PropertyToID("_FlashColor");
        private static readonly Color TrailColor = new Color(0.65f, 0.85f, 1f, 0.6f);
        private static readonly Color DodgeEchoColor = new Color(1f, 1f, 1f, 0.8f);
        private static readonly Color DustColor = new Color32(166, 137, 117, 195);
        private readonly SpriteRenderer[] trail = new SpriteRenderer[TrailSamples + 1];
        private readonly float[] trailAge = new float[TrailSamples + 1];
        private int trailIndex;
        private float nextTrail;
        private ParticleSystem contactParticles;
        [SerializeField] private AnimationClip parryStance;
        [SerializeField] private AnimationClip criticalBite;
        private SpriteRenderer biteTorso, biteHead, biteJaw;
        private Sprite biteSource;
        private RuntimeAnimatorController cachedController;
        private readonly Dictionary<int, AnimatorControllerParameterType> parameters = new Dictionary<int, AnimatorControllerParameterType>();

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            body = GetComponent<Rigidbody2D>();
            combat = GetComponent<PlayerCombat>();
            health = GetComponent<PlayerHealth>();
            if (visuals == null) visuals = transform.Find("Visuals");
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                foreach (var candidate in GetComponentsInChildren<Animator>())
                    if (candidate.runtimeAnimatorController != null) { animator = candidate; break; }
            if (visuals != null) originalScale = visuals.localScale;
            if (visuals != null) bodySprite = visuals.GetComponentInChildren<SpriteRenderer>();
            if (bodySprite != null)
            {
                originalColor = bodySprite.color;
                for (int i = 0; i < trail.Length; i++) EnsureTrail(i);
            }
        }

        private void OnEnable()
        {
            movement = GetComponent<PlayerMovement>();
            body = GetComponent<Rigidbody2D>();
            combat = GetComponent<PlayerCombat>();
            health = GetComponent<PlayerHealth>();
            cachedController = null;
            parameters.Clear();
            if (animator != null) animator.speed = 1f;
            if (originalScale == Vector3.zero) originalScale = Vector3.one;
        }

        private void UpdateBiteHead()
        {
            Sprite sprite = bodySprite.sprite;
            bool useHead = combat != null && combat.IsCritical && sprite != null && sprite.name == "Bite_2";
            if (!useHead)
            {
                ShowBiteParts(false);
                return;
            }

            if (biteSource != sprite)
            {
                ClearBiteParts();
                biteSource = sprite;
                // Sprite rectangles use pixels from the bottom left. Split at the neck and under the muzzle.
                biteTorso = CreateBitePart("Bite torso", sprite, 0, new Vector2[]
                {
                    new(0, 0), new(64, 0), new(64, 35), new(0, 35),
                    new(0, 35), new(51, 35), new(44, 51), new(0, 51),
                    new(0, 51), new(44, 51), new(44, 64), new(0, 64),
                });
                biteHead = CreateBitePart("Bite head", sprite, 1, new Vector2[]
                {
                    new(49, 35), new(54, 35), new(54, 44), new(45, 44),
                    new(45, 44), new(64, 44), new(64, 51), new(42, 51),
                    new(42, 51), new(64, 51), new(64, 64), new(42, 64),
                });
                biteJaw = CreateBitePart("Bite jaw", sprite, 2, new Vector2[]
                {
                    new(54, 35), new(64, 35), new(64, 44), new(54, 44),
                });
            }

            ShowBiteParts(true);
            float progress = combat.AttackProgress;
            float pull = Mathf.SmoothStep(0f, 1f, (progress - 0.30f) / 0.40f);
            float clench = Mathf.SmoothStep(0f, 1f, (progress - 0.24f) / 0.11f);
            float release = Mathf.SmoothStep(0f, 1f, (progress - 0.62f) / 0.08f);
            Quaternion headRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-5f, 8f, pull));
            Vector3 neck = (new Vector2(48f, 43f) - sprite.pivot) / sprite.pixelsPerUnit;
            Vector3 jaw = (new Vector2(54f, 43f) - sprite.pivot) / sprite.pixelsPerUnit;
            Vector3 headOffset = neck - headRotation * neck;
            Quaternion jawRotation = Quaternion.Euler(0f, 0f, -11f * (1f - clench + release));
            biteHead.transform.SetLocalPositionAndRotation(headOffset, headRotation);
            biteJaw.transform.SetLocalPositionAndRotation(headOffset + headRotation * (jaw - jawRotation * jaw),
                headRotation * jawRotation);
            biteTorso.color = biteHead.color = biteJaw.color = bodySprite.color;
        }

        private SpriteRenderer CreateBitePart(string name, Sprite source, int order, Vector2[] vertices)
        {
            var part = new GameObject(name).AddComponent<SpriteRenderer>();
            part.transform.SetParent(bodySprite.transform, false);
            part.sharedMaterial = bodySprite.sharedMaterial;
            part.sortingLayerID = bodySprite.sortingLayerID;
            part.sortingOrder = bodySprite.sortingOrder + order;
            part.sprite = Sprite.Create(source.texture, source.textureRect, source.pivot / source.rect.size,
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            var triangles = new ushort[vertices.Length / 4 * 6];
            for (int vertex = 0, index = 0; vertex < vertices.Length; vertex += 4, index += 6)
            {
                triangles[index] = (ushort)vertex;
                triangles[index + 1] = (ushort)(vertex + 2);
                triangles[index + 2] = (ushort)(vertex + 1);
                triangles[index + 3] = (ushort)vertex;
                triangles[index + 4] = (ushort)(vertex + 3);
                triangles[index + 5] = (ushort)(vertex + 2);
            }
            part.sprite.OverrideGeometry(vertices, triangles);
            return part;
        }

        private void ShowBiteParts(bool visible)
        {
            if (biteTorso == null) return;
            if (bodySprite != null) bodySprite.forceRenderingOff = visible;
            biteTorso.enabled = biteHead.enabled = biteJaw.enabled = visible;
        }

        private void ClearBiteParts()
        {
            ShowBiteParts(false);
            foreach (var part in new[] { biteTorso, biteHead, biteJaw })
            {
                if (part == null) continue;
                Destroy(part.sprite);
                Destroy(part.gameObject);
            }
        }

        private void EnsureTrail(int index)
        {
            if (trail[index] != null || bodySprite == null) return;
            bool echo = index == DodgeEchoIndex;
            var obj = new GameObject(echo ? "Dodge echo" : "Dodge trail", typeof(SpriteRenderer));
            obj.hideFlags = HideFlags.DontSave;
            var renderer = obj.GetComponent<SpriteRenderer>();
            renderer.sharedMaterial = bodySprite.sharedMaterial;
            renderer.sortingLayerID = bodySprite.sortingLayerID;
            renderer.sortingOrder = bodySprite.sortingOrder - 1;
            if (echo)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetColor(FlashColor, new Color(1f, 0.91f, 0.82f, 0.68f));
                renderer.SetPropertyBlock(properties);
            }
            renderer.enabled = false;
            trail[index] = renderer;
            trailAge[index] = 1f;
        }

        public void PlayDodgeSuccess()
        {
            if (!isActiveAndEnabled || bodySprite == null || bodySprite.sprite == null) return;
            StampTrail(DodgeEchoIndex, DodgeEchoColor);
            trail[DodgeEchoIndex].transform.position += (Vector3)(body.position - (Vector2)transform.position);
        }

        public void PlayLanding(Vector2 point, float speed)
        {
            if (!isActiveAndEnabled || speed < 4f) return;
            float force = Mathf.InverseLerp(4f, 24f, speed);
            landingSquash = Mathf.Lerp(0.07f, 0.2f, force);
            Vector2 origin = point + Vector2.up * 0.07f;
            float spreadSpeed = Mathf.Lerp(1.45f, 3.45f, force);
            int count = force > 0.65f ? 5 : 3;
            Vector2 pawOffset = Vector2.right * 0.22f;
            EmitContact(origin - pawOffset, new Vector2(-spreadSpeed, 0.95f), DustColor, count, 22f);
            EmitContact(origin + pawOffset, new Vector2(spreadSpeed, 0.95f), DustColor, count, 22f);
        }

        public void PlayWallJump(Vector2 point, int wallSide)
        {
            Vector2 origin = point + new Vector2(-wallSide * 0.08f, 0.04f);
            EmitContact(origin, new Vector2(-wallSide * 2.6f, 0.8f), DustColor, 6, 42f);
        }

        public void PlayParry(Vector2 feet, int facing)
        {
            Vector2 origin = feet + new Vector2(facing * 0.9f, 1.0f);
            EmitContact(origin, new Vector2(facing * 4.5f, 1.15f), new Color32(255, 212, 152, 250), 6, 56f);
        }

        private void EmitContact(Vector2 origin, Vector2 velocity, Color color, int count, float spread)
        {
            if (!isActiveAndEnabled || contactMaterial == null || bodySprite == null) return;
            if (contactParticles == null) CreateContactParticles();
            if (!contactParticles.isPlaying) contactParticles.Play();
            var particle = new ParticleSystem.EmitParams { position = origin, startColor = color };
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(-spread, spread);
                particle.velocity = Quaternion.Euler(0f, 0f, angle) * velocity * Random.Range(0.65f, 1.25f);
                particle.rotation = Random.Range(0f, 90f);
                particle.startSize = Random.Range(0.06f, 0.12f);
                particle.startLifetime = Random.Range(0.13f, 0.25f);
                contactParticles.Emit(particle, 1);
            }
        }

        private void CreateContactParticles()
        {
            var effect = new GameObject("Contact particles", typeof(ParticleSystem));
            effect.hideFlags = HideFlags.DontSave;
            effect.transform.SetParent(transform, false);
            contactParticles = effect.GetComponent<ParticleSystem>();
            contactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = contactParticles.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;
            main.gravityModifier = 0.65f;
            var emission = contactParticles.emission;
            emission.enabled = false;
            var shape = contactParticles.shape;
            shape.enabled = false;
            var fade = contactParticles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = contactMaterial;
            renderer.sortingLayerID = bodySprite.sortingLayerID;
            renderer.sortingOrder = bodySprite.sortingOrder - 1;
        }

        private void LateUpdate()
        {
            if (visuals == null) return;
            bool animated = animator != null && animator.runtimeAnimatorController != null;
            int visualFacing = combat != null && combat.IsCrossBite ? combat.CrossVisualDirection : combat != null && (combat.IsDirectionalAttack || combat.IsParrying || combat.IsCritical) ? combat.AttackFacing : movement.Facing;
            if (animated && combat != null && combat.IsDirectionalAttack && Mathf.Abs(combat.AttackDirection.y) > 0.25f)
            {
                var clips = combat.AttackDirection.y > 0f ? upwardAttacks : downwardAttacks;
                int index = combat.IsHeavyAttack ? 3 : combat.ComboStep;
                if (clips != null && index < clips.Length && clips[index] != null)
                    clips[index].SampleAnimation(animator.gameObject, combat.AttackProgress * clips[index].length);
            }
            if (movement.IsWallSliding && (combat == null || !combat.IsAttacking && !combat.IsCharging))
                visualFacing = movement.WallSlideFacing;
            Vector2 stretch = Vector2.one;
            float rotation = 0f;
            landingSquash = Mathf.MoveTowards(landingSquash, 0f, Time.deltaTime * 1.5f);
            if (!animated)
            {
                stretch.y = movement.IsCrouching ? movement.CrouchScale : 1f;
                if (placeholderMotion)
                {
                    if (movement.IsDodging) rotation = -visualFacing * 360f * movement.DodgeProgress;
                    else if (!movement.IsCrouching)
                    {
                        float run = movement.IsGrounded ? Mathf.Sin(Time.time * 25f) * Mathf.Min(Mathf.Abs(body.linearVelocity.x) * 0.006f, 0.04f) : 0f;
                        float jump = !movement.IsGrounded ? Mathf.Clamp(body.linearVelocity.y * 0.006f, -0.04f, 0.09f) : 0f;
                        stretch.x = 1f + landingSquash - jump - run;
                        stretch.y *= 1f - landingSquash + jump + run;
                    }
                }
            }
            visuals.localScale = new Vector3(originalScale.x * stretch.x * visualFacing, originalScale.y * stretch.y, originalScale.z);
            bool dashVisual = movement.IsDodging || combat != null && (combat.IsCrossDashing || combat.IsLunging);
            if (animated && feetPivot)
            {
                if (dashVisual)
                {
                    stretch.x = 1.12f;
                    stretch.y = 0.94f;
                    rotation = movement.IsGrounded ? 0f : visualFacing * -8f;
                }
                else if (combat != null && combat.IsCharging)
                {
                    stretch.x = 1f + combat.ChargeAmount * 0.06f;
                    stretch.y = 1f - Mathf.SmoothStep(0f, 1f, combat.ChargeAmount) * 0.12f;
                }
                else if (combat != null && combat.IsSnapBite)
                {
                    stretch.x = 1.15f;
                    stretch.y = 0.94f;
                    rotation = -visualFacing * 8f;
                }
                else if (combat != null && combat.IsAttacking)
                {
                    stretch.x = combat.IsHeavyAttack ? 1.12f : combat.ComboStep == 0 ? 1.08f : combat.ComboStep == 1 ? 0.98f : 1.12f;
                    stretch.y = combat.IsHeavyAttack ? 0.94f : combat.ComboStep == 0 ? 0.96f : combat.ComboStep == 1 ? 1.04f : 0.94f;
                }
                if (movement.IsCrouching && !dashVisual && (combat == null || !combat.IsAttacking && !combat.IsCharging))
                    stretch.y *= 0.9f;
                if (movement.IsGrounded && landingSquash > 0f)
                {
                    stretch.x *= 1f + landingSquash;
                    stretch.y *= 1f - landingSquash;
                }
                if (combat != null && combat.IsAirDive && movement.IsGrounded)
                {
                    stretch.x = 1f + landingSquash;
                    stretch.y = 1f - landingSquash;
                }
                visuals.localScale = new Vector3(originalScale.x * stretch.x * visualFacing, originalScale.y * stretch.y, originalScale.z);
            }
            float dashProgress = movement.IsDodging ? movement.DodgeProgress : combat != null ? combat.CrossVisualProgress : 0f;
            float hop = animated && feetPivot && dashVisual ? Mathf.Sin(dashProgress * Mathf.PI) * 0.3f : 0f;
            float attackHop = combat != null && combat.IsAttacking && !combat.IsHeavyAttack && combat.ComboStep == 1 ? Mathf.Sin(combat.AttackProgress * Mathf.PI) * 0.10f : 0f;
            float finisherHop = 0f;
            if (combat != null && combat.IsAttacking && !combat.IsHeavyAttack && combat.ComboStep == 2)
            {
                finisherHop = Mathf.Sin(combat.AttackProgress * Mathf.PI) * 0.18f;
                rotation += visualFacing * Mathf.Max(0f, Mathf.Sin(combat.AttackProgress * Mathf.PI * 2f)) * 25f;
            }
            if (combat != null && combat.IsAirDive) rotation = -visualFacing * combat.DiveNoseAngle;
            bool airSpin = combat != null && combat.IsAirSpin;
            if (airSpin)
            {
                if (!spinning && bodySprite != null && bodySprite.sprite != null)
                    spinPivot = visuals.InverseTransformPoint(bodySprite.transform.TransformPoint(bodySprite.sprite.bounds.center));
                float t = combat.AttackProgress;
                float curlIn = Mathf.SmoothStep(0f, 1f, t / 0.18f);
                float unfold = Mathf.SmoothStep(0f, 1f, (t - 0.76f) / 0.24f);
                float curl = curlIn * (1f - unfold);
                visuals.localScale = new Vector3(
                    originalScale.x * visualFacing * Mathf.Lerp(1f, 0.78f, curl),
                    originalScale.y * Mathf.Lerp(1f, 1.02f, curl), originalScale.z);
                float turn = Mathf.SmoothStep(0f, 1f, (t - 0.18f) / 0.58f);
                rotation = -visualFacing * 360f * turn;
            }
            bool aimedAttack = combat != null && combat.IsDirectionalAttack;
            if (aimedAttack)
            {
                rotation = Mathf.Clamp(combat.AttackPoseAngle * 0.35f, -25f, 25f) * combat.AttackPoseWeight;
                float snap = Mathf.Sin(combat.AttackProgress * Mathf.PI);
                visuals.localScale = new Vector3(originalScale.x * visualFacing * (1f + snap * 0.06f), originalScale.y * (1f - snap * 0.04f), originalScale.z);
            }
            spinning = airSpin;
            float nosePivot = 0f;
            visuals.localPosition = new Vector3(nosePivot, (feetPivot ? 0f : movement.CurrentHeight * 0.5f) + hop + attackHop + finisherHop + (combat != null ? combat.HeavySnapLift : 0f), 0f);
            if (aimedAttack)
            {
                float directionalHop = attackHop + finisherHop + combat.HeavySnapLift;
                visuals.localPosition -= Vector3.up * directionalHop;
                visuals.localPosition += (Vector3)combat.AttackDirection * directionalHop;
            }
            if (combat != null && (combat.IsParrying || combat.IsCritical || combat.ParryFlashing && !combat.IsAttacking))
            {
                bool critical = combat.IsCritical;
                float progress = critical ? combat.AttackProgress : combat.ParryFlashing ? 0.32f : combat.ParryProgress;
                AnimationClip clip = critical ? criticalBite : parryStance;
                if (animated && clip != null)
                    clip.SampleAnimation(animator.gameObject, progress * clip.length);

                float brace = Mathf.SmoothStep(0f, 1f, progress / 0.12f)
                    * (1f - Mathf.SmoothStep(0f, 1f, (progress - 0.55f) / 0.45f));
                if (critical)
                {
                    float approach = Mathf.Clamp01((progress - 0.07f) / 0.21f);
                    float retreat = Mathf.Clamp01((progress - 0.705f) / 0.255f);
                    float jumpIn = Mathf.Sin(approach * Mathf.PI);
                    float jumpOut = Mathf.Sin(retreat * Mathf.PI);
                    float contact = Mathf.SmoothStep(0f, 1f, approach)
                        * (1f - Mathf.SmoothStep(0f, 1f, retreat));
                    float x = contact * combat.CriticalLungeDistance;
                    float y = jumpIn * 0.27f + jumpOut * 0.63f;
                    visuals.localPosition += new Vector3(visualFacing * x, y, 0f);
                    visuals.localScale = new Vector3(originalScale.x * visualFacing, originalScale.y, originalScale.z);
                    rotation = 0f;
                }
                else
                {
                    visuals.localPosition += Vector3.right * (visualFacing * brace * 0.13f);
                    visuals.localScale = new Vector3(originalScale.x * visualFacing * (1f + brace * 0.09f),
                        originalScale.y * (1f - brace * 0.13f), originalScale.z);
                    rotation = -visualFacing * brace * 5f;
                }
            }
            visuals.localRotation = Quaternion.Euler(0f, combat != null ? combat.CrossTurnYaw : 0f, rotation);
            if (aimedAttack && bodySprite != null && bodySprite.sprite != null)
            {
                Vector3 aimPivot = visuals.InverseTransformPoint(bodySprite.transform.TransformPoint(bodySprite.sprite.bounds.center));
                Vector3 pivot = Vector3.Scale(aimPivot, visuals.localScale);
                visuals.localPosition += pivot - visuals.localRotation * pivot;
            }
            if (airSpin)
            {
                Vector3 pivot = Vector3.Scale(spinPivot, visuals.localScale);
                Vector3 center = Vector3.Scale(spinPivot, new Vector3(originalScale.x * visualFacing, originalScale.y, originalScale.z));
                visuals.localPosition += center - visuals.localRotation * pivot;
            }
            if (animated) UpdateAnimator();
            UpdateDodgeTrail();
        }

        private void UpdateDodgeTrail()
        {
            if (bodySprite == null) return;
            bodySprite.color = (movement.IsDodging || combat != null && combat.IsCrossDashing) ? originalColor * new Color(1.15f, 1.25f, 1.4f, 1f) : originalColor;
            if (combat != null && (combat.ParryFlashing || combat.CriticalFlashing)) bodySprite.color = new Color(1.8f, 1.8f, 1.8f, originalColor.a);
            if (health != null && health.IsHitFlashing)
                bodySprite.color = Color.Lerp(originalColor, new Color(2f, 0.3f, 0.3f, originalColor.a), health.HitFlashRemaining / 0.14f);
            UpdateBiteHead();
            for (int i = 0; i < trail.Length; i++)
            {
                EnsureTrail(i);
                trailAge[i] += Time.deltaTime;
                bool echo = i == DodgeEchoIndex;
                float life = echo ? 0.23f : 0.2f;
                trail[i].enabled = trailAge[i] < life;
                if (!trail[i].enabled) continue;
                Color color = echo ? DodgeEchoColor : TrailColor;
                color.a *= 1f - trailAge[i] / life;
                trail[i].color = color;
            }
            if (!(movement.IsDodging || combat != null && combat.IsCrossDashing) || Time.time < nextTrail) return;
            nextTrail = Time.time + 0.035f;
            StampTrail(trailIndex, TrailColor);
            trailIndex = (trailIndex + 1) % TrailSamples;
        }

        private void StampTrail(int index, Color color)
        {
            EnsureTrail(index);
            var ghost = trail[index];
            ghost.sprite = bodySprite.sprite;
            ghost.flipX = bodySprite.flipX;
            ghost.flipY = bodySprite.flipY;
            ghost.transform.SetPositionAndRotation(bodySprite.transform.position, bodySprite.transform.rotation);
            ghost.transform.localScale = bodySprite.transform.lossyScale;
            ghost.color = color;
            ghost.enabled = true;
            trailAge[index] = 0f;
        }

        private void OnDisable()
        {
            ShowBiteParts(false);
            if (bodySprite != null) bodySprite.color = originalColor;
            for (int i = 0; i < trail.Length; i++)
            {
                if (trail[i] != null) trail[i].enabled = false;
                trailAge[i] = 1f;
            }
            landingSquash = 0f;
            if (contactParticles != null) contactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDestroy()
        {
            ClearBiteParts();
            foreach (var ghost in trail) if (ghost != null) Destroy(ghost.gameObject);
        }

        private void UpdateAnimator()
        {
            if (cachedController != animator.runtimeAnimatorController)
            {
                cachedController = animator.runtimeAnimatorController;
                parameters.Clear();
                foreach (var parameter in animator.parameters) parameters[parameter.nameHash] = parameter.type;
                animator.applyRootMotion = false;
            }
            SetFloat("Speed", Mathf.Abs(body.linearVelocity.x));
            SetBool("RunReady", movement.UseRunAnimation);
            SetFloat("VerticalSpeed", body.linearVelocity.y);
            SetBool("Grounded", movement.IsGrounded);
            SetBool("Hurt", health != null && health.IsHitFlashing);
            SetBool("AirSpin", combat != null && combat.IsAirSpin);
            SetBool("Crouching", movement.IsCrouching);
            SetBool("Rolling", movement.IsDodging && (combat == null || !combat.IsDashAttack));
            SetBool("WallSliding", movement.IsWallSliding);
            SetBool("WallJumping", movement.IsWallJumping);
            SetBool("Invulnerable", movement.IsInvulnerable);
            if (combat != null)
            {
                SetBool("Attacking", combat.IsAttacking);
                SetBool("Charging", combat.IsCharging);
                SetBool("HeavyAttack", combat.IsHeavyAttack);
                SetBool("DashAttack", combat.IsDashAttack);
                SetBool("CrossBite", combat.IsCrossBite);
                SetInt("AttackStep", combat.ComboStep);
                SetFloat("ChargeAmount", combat.ChargeAmount);
                animator.speed = combat.HasHitPause ? 0f : 1f;
            }
        }

        private void SetFloat(string name, float value)
        {
            int id = Animator.StringToHash(name);
            if (parameters.TryGetValue(id, out var type) && type == AnimatorControllerParameterType.Float) animator.SetFloat(id, value);
        }

        private void SetBool(string name, bool value)
        {
            int id = Animator.StringToHash(name);
            if (parameters.TryGetValue(id, out var type) && type == AnimatorControllerParameterType.Bool) animator.SetBool(id, value);
        }

        private void SetInt(string name, int value)
        {
            int id = Animator.StringToHash(name);
            if (parameters.TryGetValue(id, out var type) && type == AnimatorControllerParameterType.Int) animator.SetInteger(id, value);
        }
    }
}

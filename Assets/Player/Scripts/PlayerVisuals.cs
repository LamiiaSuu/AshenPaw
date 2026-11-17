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
        private PlayerMovement movement;
        private Rigidbody2D body;
        private PlayerCombat combat;
        private PlayerHealth health;
        private Vector3 originalScale;
        private float landingSquash;
        private bool spinning;
        private Vector3 spinPivot;
        private bool wasGrounded;
        private SpriteRenderer bodySprite;
        private Color originalColor;
        private readonly SpriteRenderer[] trail = new SpriteRenderer[6];
        private readonly float[] trailAge = new float[6];
        private int trailIndex;
        private float nextTrail;
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
                for (int i = 0; i < trail.Length; i++)
                {
                    var ghost = new GameObject("Dodge trail", typeof(SpriteRenderer));
                    ghost.hideFlags = HideFlags.DontSave;
                    trail[i] = ghost.GetComponent<SpriteRenderer>();
                    trail[i].sharedMaterial = bodySprite.sharedMaterial;
                    trail[i].sortingLayerID = bodySprite.sortingLayerID;
                    trail[i].sortingOrder = bodySprite.sortingOrder - 1;
                    trail[i].enabled = false;
                    trailAge[i] = 1f;
                }
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

        private void EnsureTrail(int index)
        {
            if (trail[index] != null || bodySprite == null) return;
            var obj = new GameObject("Dodge trail", typeof(SpriteRenderer));
            obj.hideFlags = HideFlags.DontSave;
            var renderer = obj.GetComponent<SpriteRenderer>();
            renderer.sharedMaterial = bodySprite.sharedMaterial;
            renderer.sortingLayerID = bodySprite.sortingLayerID;
            renderer.sortingOrder = bodySprite.sortingOrder - 1;
            renderer.enabled = false;
            trail[index] = renderer;
            trailAge[index] = 1f;
        }

        private void LateUpdate()
        {
            if (visuals == null) return;
            bool animated = animator != null && animator.runtimeAnimatorController != null;
            int visualFacing = combat != null && combat.IsCrossBite ? combat.CrossVisualDirection : movement.Facing;
            Vector2 stretch = Vector2.one;
            float rotation = 0f;
            if (!wasGrounded && movement.IsGrounded) landingSquash = 0.16f;
            wasGrounded = movement.IsGrounded;
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
                if (movement.IsCrouching && !movement.IsDodging) stretch.y = movement.CrouchScale;
                if (movement.IsWallSliding) rotation = visualFacing * 25f;
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
            float attackHop = combat != null && combat.IsAttacking && !combat.IsHeavyAttack && combat.ComboStep == 1 ? Mathf.Sin(combat.AttackProgress * Mathf.PI) * 0.28f : 0f;
            float finisherHop = 0f;
            if (combat != null && combat.IsAttacking && !combat.IsHeavyAttack && combat.ComboStep == 2)
            {
                finisherHop = Mathf.Sin(combat.AttackProgress * Mathf.PI) * 0.55f;
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
            spinning = airSpin;
            float nosePivot = 0f;
            visuals.localPosition = new Vector3(nosePivot, (feetPivot ? 0f : movement.CurrentHeight * 0.5f) + hop + attackHop + finisherHop + (combat != null ? combat.HeavySnapLift : 0f), 0f);
            visuals.localRotation = Quaternion.Euler(0f, combat != null ? combat.CrossTurnYaw : 0f, rotation);
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
            if (health != null && health.IsHitFlashing) bodySprite.color = new Color(2f, 0.3f, 0.3f, originalColor.a);
            for (int i = 0; i < trail.Length; i++)
            {
                EnsureTrail(i);
                trailAge[i] += Time.deltaTime;
                trail[i].enabled = trailAge[i] < 0.2f;
                if (trail[i].enabled) trail[i].color = new Color(0.65f, 0.85f, 1f, (1f - trailAge[i] / 0.2f) * 0.6f);
            }
            if (!(movement.IsDodging || combat != null && combat.IsCrossDashing) || Time.time < nextTrail) return;
            nextTrail = Time.time + 0.035f;
            var ghost = trail[trailIndex];
            ghost.sprite = bodySprite.sprite;
            ghost.transform.SetPositionAndRotation(bodySprite.transform.position, bodySprite.transform.rotation);
            ghost.transform.localScale = bodySprite.transform.lossyScale;
            ghost.color = new Color(0.65f, 0.85f, 1f, 0.6f);
            ghost.enabled = true;
            trailAge[trailIndex] = 0f;
            trailIndex = (trailIndex + 1) % trail.Length;
        }

        private void OnDisable()
        {
            if (bodySprite != null) bodySprite.color = originalColor;
            foreach (var ghost in trail) if (ghost != null) ghost.enabled = false;
        }

        private void OnDestroy()
        {
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
            SetFloat("VerticalSpeed", body.linearVelocity.y);
            SetBool("Grounded", movement.IsGrounded);
            SetBool("Hurt", health != null && health.IsHitFlashing);
            SetBool("AirSpin", combat != null && combat.IsAirSpin);
            SetBool("Crouching", movement.IsCrouching);
            SetBool("Rolling", movement.IsDodging);
            SetBool("WallSliding", movement.IsWallSliding);
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

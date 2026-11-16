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
        private Vector3 originalScale;
        private float landingSquash;
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

        private void LateUpdate()
        {
            if (visuals == null) return;
            bool animated = animator != null && animator.runtimeAnimatorController != null;
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
                    if (movement.IsDodging) rotation = -movement.Facing * 360f * movement.DodgeProgress;
                    else if (!movement.IsCrouching)
                    {
                        float run = movement.IsGrounded ? Mathf.Sin(Time.time * 25f) * Mathf.Min(Mathf.Abs(body.linearVelocity.x) * 0.006f, 0.04f) : 0f;
                        float jump = !movement.IsGrounded ? Mathf.Clamp(body.linearVelocity.y * 0.006f, -0.04f, 0.09f) : 0f;
                        stretch.x = 1f + landingSquash - jump - run;
                        stretch.y *= 1f - landingSquash + jump + run;
                    }
                }
            }
            visuals.localScale = new Vector3(originalScale.x * stretch.x * movement.Facing, originalScale.y * stretch.y, originalScale.z);
            if (animated && feetPivot)
            {
                if (movement.IsCrouching && !movement.IsDodging) stretch.y = movement.CrouchScale;
                if (movement.IsWallSliding) rotation = movement.Facing * 25f;
                if (movement.IsDodging)
                {
                    stretch.x = 1.3f;
                    stretch.y = 0.85f;
                    rotation = movement.IsGrounded ? 0f : movement.Facing * -8f;
                }
                visuals.localScale = new Vector3(originalScale.x * stretch.x * movement.Facing, originalScale.y * stretch.y, originalScale.z);
            }
            float hop = animated && feetPivot && movement.IsDodging ? Mathf.Sin(movement.DodgeProgress * Mathf.PI) * 0.3f : 0f;
            visuals.localPosition = new Vector3(0f, (feetPivot ? 0f : movement.CurrentHeight * 0.5f) + hop, 0f);
            visuals.localRotation = Quaternion.Euler(0f, 0f, rotation);
            if (animated) UpdateAnimator();
            UpdateDodgeTrail();
        }

        private void UpdateDodgeTrail()
        {
            if (bodySprite == null) return;
            bodySprite.color = movement.IsDodging ? originalColor * new Color(1.15f, 1.25f, 1.4f, 1f) : originalColor;
            for (int i = 0; i < trail.Length; i++)
            {
                trailAge[i] += Time.deltaTime;
                trail[i].enabled = trailAge[i] < 0.2f;
                if (trail[i].enabled) trail[i].color = new Color(0.65f, 0.85f, 1f, (1f - trailAge[i] / 0.2f) * 0.6f);
            }
            if (!movement.IsDodging || Time.time < nextTrail) return;
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
            SetBool("Crouching", movement.IsCrouching);
            SetBool("Rolling", movement.IsDodging);
            SetBool("WallSliding", movement.IsWallSliding);
            SetBool("Invulnerable", movement.IsInvulnerable);
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
    }
}

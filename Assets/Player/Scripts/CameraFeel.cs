using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFeel : MonoBehaviour
    {
        [SerializeField] private PlayerMovement target;
        [SerializeField, Min(0f)] private float followDelay = 0.16f;
        [SerializeField] private Vector2 framing = new Vector2(0f, 1.5f);
        [SerializeField, Min(0f)] private float crouchLookDown;
        [SerializeField, Min(0f)] private float lookUpDistance;
        [SerializeField, Min(0f)] private float crouchLookTime = 0.48f;
        [SerializeField, Range(3f, 12f)] private float zoom = 7f;
        [SerializeField] private bool limitView = true;
        [SerializeField] private Vector2 levelMin = new Vector2(-21f, -5f);
        [SerializeField] private Vector2 levelMax = new Vector2(25f, 19f);
        [SerializeField, Range(0f, 2f)] private float screenShake = 1f;
        [SerializeField, Range(0f, 1f)] private float movementFeedback = 0.3f;
        [SerializeField, Range(0f, 0.1f)] private float hitFreeze = 0.035f;
        [SerializeField, Range(0f, 0.12f)] private float criticalFreeze = 0.065f;
        [SerializeField, Range(0f, 0.1f)] private float hurtFreeze = 0.05f;
        [SerializeField, Range(0f, 0.2f)] private float hitFlash = 0f;
        private Camera view;
        private HitCameraShake shake;
        private Rigidbody2D targetBody;
        private PlayerCombat targetCombat;
        private float criticalFocusBlend;
        private Vector2 criticalFocusPoint;
        private Vector3 velocity;
        private float lookOffset;
        private float lookVelocity;
        private bool tracking, wasGrounded, wasDodging, frozen;
        private float thawAt, oldTimeScale, flashUntil;
        private float frozenFixedDelta;
        private const float CriticalFrameDuration = 0.14f;
        private float criticalFrameStarted = float.NegativeInfinity;
        private Vector2 criticalFramePosition;
        private int criticalFrameDirection;

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            shake = GetComponent<HitCameraShake>();
            if (shake == null) shake = gameObject.AddComponent<HitCameraShake>();
            tracking = false;
            velocity = Vector3.zero;
            lookOffset = 0f;
            lookVelocity = 0f;
            criticalFocusBlend = 0f;
        }

        public static void Hit(bool hurt, bool heavy = false)
        {
            var camera = Camera.main;
            if (camera == null) return;
            var feel = camera.GetComponent<CameraFeel>();
            if (feel != null) feel.Impact(hurt, heavy);
        }

        public static void CriticalHit(Vector2 position, int direction)
        {
            var camera = Camera.main;
            if (camera == null || !camera.TryGetComponent(out CameraFeel feel)) return;

            feel.criticalFrameStarted = Time.unscaledTime;
            feel.criticalFramePosition = position;
            feel.criticalFrameDirection = direction;
            if (camera.orthographic)
                camera.orthographicSize = Mathf.Min(camera.orthographicSize, feel.zoom * 0.92f);
            feel.ApplyImpact(1.25f, feel.criticalFreeze);
        }

        private void Impact(bool hurt, bool heavy)
        {
            float amount = hurt ? 1f : heavy ? 0.8f : 0.45f;
            float duration = hurt ? hurtFreeze : hitFreeze * (heavy ? 1.3f : 1f);
            ApplyImpact(amount, duration);
        }

        private void ApplyImpact(float shakeAmount, float duration)
        {
            shake.Trigger(screenShake * shakeAmount);
            if (duration > 0f && (frozen || Mathf.Approximately(Time.timeScale, 1f)))
            {
                if (!frozen)
                {
                    oldTimeScale = Time.timeScale;
                    frozenFixedDelta = Time.fixedDeltaTime;
                    Time.timeScale = 0f;
                    frozen = true;
                }
                thawAt = Mathf.Max(thawAt, Time.unscaledTime + duration);
            }
            if (hitFlash > 0f) flashUntil = Time.unscaledTime + 0.06f;
        }

        private void Update()
        {
            if (frozen && Time.unscaledTime >= thawAt) Thaw();
        }

        private void LateUpdate()
        {
            if (frozen) return;
            if (target == null)
            {
                target = FindAnyObjectByType<PlayerMovement>();
                if (target == null) return;
            }
            if (targetBody == null)
            {
                target.TryGetComponent(out targetBody);
                target.TryGetComponent(out targetCombat);
            }

            Vector3 desired = target.transform.position + (Vector3)framing;
            float focusTarget = 0f;
            if (targetCombat != null && targetCombat.TryGetCriticalFocus(out Vector2 point))
            {
                criticalFocusPoint = point;
                focusTarget = Mathf.SmoothStep(0f, 1f, (targetCombat.AttackProgress - 0.08f) / 0.48f);
            }
            float focusRate = focusTarget > criticalFocusBlend ? 11f : 5.5f;
            criticalFocusBlend = Mathf.Lerp(criticalFocusBlend, focusTarget, 1f - Mathf.Exp(-focusRate * Time.deltaTime));
            desired = Vector2.Lerp(desired, criticalFocusPoint, criticalFocusBlend);
            float requestedLookOffset = GetLookOffset();
            lookOffset = Mathf.SmoothDamp(lookOffset, requestedLookOffset, ref lookVelocity, crouchLookTime);
            float minViewY = levelMin.y + Mathf.Min(0f, lookOffset);
            float maxViewY = levelMax.y + Mathf.Max(0f, lookOffset);
            desired.z = transform.position.z;
            float desiredZoom = Mathf.Lerp(zoom, zoom * 0.60f, criticalFocusBlend);
            view.orthographicSize = Mathf.Lerp(view.orthographicSize, desiredZoom, 1f - Mathf.Exp(-10f * Time.deltaTime));
            if (limitView)
            {
                float halfY = view.orthographicSize;
                float halfX = halfY * view.aspect;
                desired.x = Bound(desired.x, levelMin.x, levelMax.x, halfX);
                desired.y = Bound(desired.y, levelMin.y, levelMax.y, halfY);
            }
            desired.y += lookOffset * (1f - criticalFocusBlend);
            bool teleport = tracking && Vector2.Distance(transform.position, desired) > 18f;
            transform.position = !tracking || teleport || followDelay <= 0f ? desired : Vector3.SmoothDamp(transform.position, desired, ref velocity, followDelay, Mathf.Infinity, Mathf.Max(0.0001f, Time.deltaTime));
            if (limitView)
            {
                Vector3 bounded = transform.position;
                bounded.x = Bound(bounded.x, levelMin.x, levelMax.x, view.orthographicSize * view.aspect);
                bounded.y = Bound(bounded.y, minViewY, maxViewY, view.orthographicSize);
                transform.position = bounded;
            }
            if (tracking)
            {
                if (wasGrounded && !target.IsGrounded && targetBody.linearVelocity.y > 1f) shake.Trigger(screenShake * movementFeedback * 0.35f);
                if (!wasDodging && target.IsDodging) shake.Trigger(screenShake * movementFeedback * 0.6f);
            }
            wasGrounded = target.IsGrounded;
            wasDodging = target.IsDodging;
            tracking = true;
        }

        private float GetLookOffset()
        {
            if (!target.IsGrounded || target.IsDodging) return 0f;
            if (target.IsCrouching && target.IsDuckHeld) return -crouchLookDown;

            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            bool lookingUp = keyboard != null && (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                || pad != null && (pad.leftStick.y.ReadValue() > 0.5f || pad.dpad.up.isPressed);
            return lookingUp ? lookUpDistance : 0f;
        }

        private static float Bound(float value, float min, float max, float extent)
            => max - min <= extent * 2f ? (min + max) * 0.5f : Mathf.Clamp(value, min + extent, max - extent);

        private void Thaw()
        {
            if (!frozen) return;
            Time.timeScale = oldTimeScale;
            Time.fixedDeltaTime = frozenFixedDelta;
            frozen = false;
            thawAt = 0f;
        }

        private void OnDisable() { Thaw(); flashUntil = 0f; criticalFrameStarted = float.NegativeInfinity; }
        private void OnApplicationFocus(bool focused) { if (!focused) Thaw(); }

        private void DrawCriticalFrame(float age)
        {
            float exposure = 1f - Mathf.Clamp01(age / CriticalFrameDuration);
            Color previous = GUI.color;
            Matrix4x4 matrix = GUI.matrix;
            GUI.color = new Color(0.018f, 0.008f, 0.015f, 0.65f * exposure);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

            Vector3 screen = view.WorldToScreenPoint(criticalFramePosition);
            if (screen.z > 0f)
            {
                float size = Screen.height / 1080f;
                Vector2 point = new Vector2(screen.x, Screen.height - screen.y);
                GUI.color = new Color(0.85f, 0.07f, 0.14f, exposure);
                GUIUtility.RotateAroundPivot(-18f * criticalFrameDirection, point);
                GUI.DrawTexture(new Rect(point.x - 30f * size, point.y - 2f * size,
                    60f * size, 4f * size), Texture2D.whiteTexture);
                float whiteAccent = 1f - Mathf.Clamp01(age / 0.055f);
                GUI.color = new Color(1f, 0.95f, 0.9f, whiteAccent);
                GUI.DrawTexture(new Rect(point.x - 18f * size, point.y - 1f * size,
                    36f * size, 2f * size), Texture2D.whiteTexture);
                GUI.matrix = matrix;
                GUI.color = new Color(0.66f, 0.035f, 0.08f, exposure);
                GUIUtility.RotateAroundPivot(16f * criticalFrameDirection, point);
                GUI.DrawTexture(new Rect(point.x - 19f * size, point.y + 8f * size,
                    39f * size, 2f * size), Texture2D.whiteTexture);
            }
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        private void OnGUI()
        {
            float criticalAge = Time.unscaledTime - criticalFrameStarted;
            if (criticalAge < CriticalFrameDuration) DrawCriticalFrame(criticalAge);
            if (hitFlash <= 0f || Time.unscaledTime >= flashUntil) return;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, hitFlash * Mathf.Clamp01((flashUntil - Time.unscaledTime) / 0.06f));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class PlayerHealth : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 100;
        public int Health { get; private set; }
        private PlayerMovement movement;
        private PlayerCombat combat;
        private float protectedUntil, flashUntil;
        public bool IsHitFlashing => Time.time < flashUntil;
        private float rumbleUntil;
        private Gamepad rumblingPad;
        private GUIStyle labelStyle;
        private HitCameraShake cameraShake;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            combat = GetComponent<PlayerCombat>();
            Health = maxHealth;
        }

        public bool TakeDamage(int damage)
            => TakeDamage(damage, Vector2.right * -movement.Facing);

        public bool TakeDamage(int damage, Vector2 direction)
        {
            if (damage <= 0 || movement.IsInvulnerable || Time.time < protectedUntil) return false;
            Health = Mathf.Max(0, Health - damage);
            protectedUntil = Time.time + 0.45f;
            flashUntil = Time.time + 0.22f;
            if (cameraShake == null && Camera.main != null)
            {
                cameraShake = Camera.main.GetComponent<HitCameraShake>();
                if (cameraShake == null) cameraShake = Camera.main.gameObject.AddComponent<HitCameraShake>();
            }
            if (Camera.main != null && Camera.main.GetComponent<CameraFeel>() != null) CameraFeel.Hit(true);
            else if (cameraShake != null) cameraShake.Trigger();
            combat?.CancelAttack();
            rumblingPad = Gamepad.current;
            if (rumblingPad != null)
            {
                rumblingPad.SetMotorSpeeds(0.35f, 0.65f);
                rumbleUntil = Time.unscaledTime + 0.12f;
            }
            if (Health == 0)
            {
                movement.Respawn();
                Health = maxHealth;
            }
            else movement.ReceiveHit(direction);
            return true;
        }

        private void Update()
        {
            if (rumblingPad != null && Time.unscaledTime >= rumbleUntil) StopRumble();
        }

        private void StopRumble()
        {
            if (rumblingPad != null && rumblingPad.added) rumblingPad.SetMotorSpeeds(0f, 0f);
            rumblingPad = null;
        }

        private void OnDisable() => StopRumble();
        private void OnApplicationFocus(bool focused) { if (!focused) StopRumble(); }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            Color previous = GUI.color;
            GUI.color = IsHitFlashing ? new Color(1f, 0.35f, 0.3f) : Color.white;
            GUI.Label(new Rect(18f, 12f, 180f, 25f), "HP " + Health + " / " + maxHealth, labelStyle);
            GUI.color = new Color(0.12f, 0.08f, 0.1f, 0.9f);
            GUI.DrawTexture(new Rect(18f, 40f, 150f, 8f), Texture2D.whiteTexture);
            GUI.color = IsHitFlashing ? Color.white : new Color(0.85f, 0.23f, 0.22f);
            GUI.DrawTexture(new Rect(18f, 40f, 150f * Health / Mathf.Max(1, maxHealth), 8f), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}

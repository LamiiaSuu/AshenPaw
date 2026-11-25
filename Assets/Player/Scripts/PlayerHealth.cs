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
        public float HitFlashRemaining => Mathf.Max(0f, flashUntil - Time.time);
        private float rumbleUntil;
        private Gamepad rumblingPad;
        private GUIStyle labelStyle, valueStyle;
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
            => TakeDamage(damage, direction, false);

        public bool TakeDamage(int damage, Vector2 direction, bool heavy)
        {
            if (damage <= 0) return false;
            if (movement.IsInvulnerable)
            {
                movement.ConfirmDodge();
                PlayerDebug.Report(0, true, transform.position + Vector3.up * 2f, "DODGED");
                return false;
            }
            if (GetComponent<PlayerDebug>() is PlayerDebug debug && debug.Invincible) { PlayerDebug.Report(0, true, transform.position + Vector3.up * 2f, "INVINCIBLE"); return false; }
            if (Time.time < protectedUntil) { PlayerDebug.Report(0, true, transform.position + Vector3.up * 2f, "PROTECTED"); return false; }
            int actualDamage = Mathf.Min(Health, damage);
            Health = Mathf.Max(0, Health - damage);
            PlayerDebug.Report(actualDamage, true, transform.position + Vector3.up * 2f, "PLAYER HIT");
            protectedUntil = Time.time + 0.45f;
            flashUntil = Time.time + 0.22f;
            if (cameraShake == null && Camera.main != null)
            {
                cameraShake = Camera.main.GetComponent<HitCameraShake>();
                if (cameraShake == null) cameraShake = Camera.main.gameObject.AddComponent<HitCameraShake>();
            }
            if (Camera.main != null && Camera.main.GetComponent<CameraFeel>() != null) CameraFeel.Hit(true, heavy);
            else if (cameraShake != null) cameraShake.Trigger(heavy ? 1.4f : 1f);
            combat?.CancelAttack();
            rumblingPad = Gamepad.current;
            if (rumblingPad != null)
            {
                rumblingPad.SetMotorSpeeds(heavy ? 0.65f : 0.35f, heavy ? 0.9f : 0.65f);
                rumbleUntil = Time.unscaledTime + (heavy ? 0.16f : 0.12f);
            }
            if (Health == 0)
            {
                movement.Respawn();
                Health = maxHealth;
            }
            else movement.ReceiveHit(direction, heavy);
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
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUIStyle.none)
                {
                    font = UiTheme.HudFont, fontSize = 23, alignment = TextAnchor.MiddleLeft
                };
                labelStyle.normal.textColor = Color.white;
                valueStyle = new GUIStyle(labelStyle) { fontSize = 26 };
            }
            Color previous = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = PlayerDebug.ScaleFor(Screen.width, Screen.height);
            GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.color = UiTheme.Gold;
            GUI.Label(new Rect(18f, 10f, 32f, 32f), "HP", labelStyle);
            GUI.color = IsHitFlashing ? UiTheme.Hurt : UiTheme.Ivory;
            GUI.Label(new Rect(53f, 10f, 160f, 32f), Health + " / " + maxHealth, valueStyle);
            GUI.color = UiTheme.Surface;
            GUI.DrawTexture(new Rect(18f, 45f, 170f, 9f), Texture2D.whiteTexture);
            GUI.color = IsHitFlashing ? UiTheme.Ivory : new Color32(183, 71, 72, 255);
            GUI.DrawTexture(new Rect(18f, 45f, 170f * Health / Mathf.Max(1, maxHealth), 9f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.matrix = previousMatrix;
        }
    }
}

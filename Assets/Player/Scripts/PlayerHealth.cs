using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AshenPaw
{
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class PlayerHealth : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private PlayerVitalsHud hudPrefab;
        [Header("Stamina")]
        [SerializeField, Min(1)] private int maxStamina = 100;
        [SerializeField, Min(0f)] private float staminaRegenPerSecond = 55f;
        [SerializeField, Min(0f)] private float staminaRegenDelay = 0.40f;
        [SerializeField, Min(0f)] private float exhaustedRegenDelay = 0.60f;
        [SerializeField, Range(0.01f, 1f)] private float exhaustionRecoveryFraction = 0.20f;
        [SerializeField, Min(0f)] private float stamina = 100f;

        public int Health { get; private set; }
        public int MaxHealth => Mathf.Max(1, maxHealth);
        public int MaxStamina => Mathf.Max(1, maxStamina);
        public float Stamina => Mathf.Clamp(stamina, 0f, MaxStamina);
        public bool IsExhausted { get; private set; }
        public float StaminaRegenDelayRemaining { get; private set; }
        public float StaminaRegenPerSecond => staminaRegenPerSecond;
        private bool HasUnlimitedStamina => TryGetComponent(out PlayerDebug debug) && debug.UnlimitedStamina;
        private PlayerMovement movement;
        private PlayerCombat combat;
        private float protectedUntil, flashUntil;
        public bool IsHitFlashing => Time.time < flashUntil;
        public float HitFlashRemaining => Mathf.Max(0f, flashUntil - Time.time);
        private float rumbleUntil;
        private Gamepad rumblingPad;
        private PlayerVitalsHud hud;
        private HitCameraShake cameraShake;

        public event System.Action EnemyHitLanded;
        public event System.Action Death;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            combat = GetComponent<PlayerCombat>();
            RestoreVitals();
            if (Application.isPlaying && hudPrefab != null)
            {
                hud = Instantiate(hudPrefab);
                SceneManager.MoveGameObjectToScene(hud.gameObject, gameObject.scene);
                hud.Bind(this);
            }
        }

        public bool TakeDamage(int damage)
            => TakeDamage(damage, Vector2.right * -movement.Facing);

        public void SetMaxHealth(int value)
        {
            maxHealth = Mathf.Max(1, value);
            Health = Mathf.Min(Health, maxHealth);
        }

        public void SetMaxStamina(int value)
        {
            maxStamina = Mathf.Max(1, value);
            stamina = Stamina;
        }

        public bool CanSpendStamina(float amount)
            => amount <= 0f || HasUnlimitedStamina || amount > 0f && HasStaminaToSpend;

        private bool HasStaminaToSpend => !IsExhausted && Stamina > 0f;

        public bool TrySpendStamina(float amount)
        {
            if (amount <= 0f || HasUnlimitedStamina) return true;
            if (!(amount > 0f) || !HasStaminaToSpend) return false;

            stamina = Mathf.Max(0f, Stamina - amount);
            IsExhausted = stamina <= 0f;
            StaminaRegenDelayRemaining = IsExhausted ? Mathf.Max(staminaRegenDelay, exhaustedRegenDelay) : staminaRegenDelay;
            return true;
        }

        internal void RestoreVitals()
        {
            Health = MaxHealth;
            stamina = MaxStamina;
            IsExhausted = false;
            StaminaRegenDelayRemaining = 0f;
        }

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
            EnemyHitLanded?.Invoke();
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
            if (Health == 0) movement.Respawn();
            else movement.ReceiveHit(direction, heavy);
            if (Health == 0)
            {   
                Death?.Invoke();
                movement.Respawn();
                Health = maxHealth;
            }
            else movement.ReceiveHit(direction, heavy);
            return true;
        }

        private void Update()
        {
            Health = Mathf.Min(Health, MaxHealth);
            UpdateStamina(Time.deltaTime);
            if (rumblingPad != null && Time.unscaledTime >= rumbleUntil) StopRumble();
        }

        private void UpdateStamina(float dt)
        {
            if (movement != null && movement.IsDodging) return;
            if (combat != null && (combat.IsAttacking || combat.IsCharging || combat.IsParrying)) return;

            float regenTime = Mathf.Max(0f, dt - StaminaRegenDelayRemaining);
            StaminaRegenDelayRemaining = Mathf.Max(0f, StaminaRegenDelayRemaining - dt);
            stamina = Mathf.MoveTowards(Stamina, MaxStamina, regenTime * staminaRegenPerSecond);
            float recoveryThreshold = MaxStamina * exhaustionRecoveryFraction;
            if (IsExhausted && (stamina >= recoveryThreshold || Mathf.Approximately(stamina, recoveryThreshold)))
                IsExhausted = false;
        }

        private void StopRumble()
        {
            if (rumblingPad != null && rumblingPad.added) rumblingPad.SetMotorSpeeds(0f, 0f);
            rumblingPad = null;
        }

        private void OnEnable()
        {
            if (hud != null) hud.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (hud != null) hud.gameObject.SetActive(false);
            StopRumble();
        }

        private void OnDestroy()
        {
            if (hud != null) Destroy(hud.gameObject);
        }

        private void OnApplicationFocus(bool focused) { if (!focused) StopRumble(); }
    }
}

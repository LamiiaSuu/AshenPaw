using UnityEngine;
using UnityEngine.UI;

namespace AshenPaw
{
    public sealed class PlayerVitalsHud : MonoBehaviour
    {
        // Existing prefabs can reference either the RectTransform or the RawImage.
        [SerializeField] private Component healthFill;
        [SerializeField] private RectTransform damageTrail;
        [SerializeField] private Text healthValue;
        [SerializeField] private RectTransform healthMeter;
        [SerializeField] private RectTransform staminaMeter;
        [SerializeField] private Component staminaFill;
        [SerializeField] private RectTransform abilityIcons;
        [Header("Meter Scale")]
        [Tooltip("HP represented by the prefab's default bar width. Set the player's max HP on Player Health.")]
        [SerializeField, Min(1f)] private float baseHealth = 100f;
        [Tooltip("Stamina represented by the prefab's default bar width. Set the player's max stamina on Player Health.")]
        [SerializeField, Min(1f)] private float baseStamina = 100f;

        private PlayerHealth health;
        private RectTransform canvasRect;
        private RawImage healthFillImage, staminaFillImage;
        private int shownHealth, shownMaxHealth;
        private float healthTileWidth, staminaTileWidth;
        private float healthUnitsPerPoint, staminaUnitsPerPoint;
        private float healthFramePadding, staminaFramePadding;
        private float healthValueInset;
        private float trailUnitsPerSecond;
        private float trailWaitUntil;
        public RectTransform AbilityIcons => abilityIcons;

        private void OnEnable()
        {
            healthFillImage = FindFillImage(healthFill, healthMeter);
            staminaFillImage = FindFillImage(staminaFill, staminaMeter);
            if (canvasRect != null) return;

            canvasRect = (RectTransform)transform;
            healthTileWidth = healthFillImage.rectTransform.rect.width;
            staminaTileWidth = staminaFillImage.rectTransform.rect.width;
            healthUnitsPerPoint = healthTileWidth / Mathf.Max(1f, baseHealth);
            staminaUnitsPerPoint = staminaTileWidth / Mathf.Max(1f, baseStamina);
            healthFramePadding = healthMeter.rect.width - healthTileWidth;
            staminaFramePadding = staminaMeter.rect.width - staminaTileWidth;
            healthValueInset = healthMeter.rect.width - healthValue.rectTransform.anchoredPosition.x;
            trailUnitsPerSecond = healthTileWidth * 0.95f;
        }

        public void Bind(PlayerHealth target)
        {
            health = target;
            UpdateHealth();
            SetWidth(damageTrail, healthFillImage.rectTransform.rect.width);
            UpdateFillTexture(healthFillImage, healthTileWidth, 4f, Time.time);
            UpdateStamina();
            PositionHealthValue();
        }

        public void SetStamina(float current, float maximum)
        {
            maximum = Mathf.Max(0f, maximum);
            current = Mathf.Clamp(current, 0f, maximum);
            SetWidth(staminaMeter, staminaFramePadding + maximum * staminaUnitsPerPoint);
            SetWidth(staminaFillImage.rectTransform, current * staminaUnitsPerPoint);
            UpdateFillTexture(staminaFillImage, staminaTileWidth, 2.5f, Time.time);
        }

        private void LateUpdate()
        {
            if (health == null) return;
            bool capacityChanged = health.MaxHealth != shownMaxHealth;
            if (capacityChanged || health.Health != shownHealth)
            {
                int previousHealth = shownHealth;
                UpdateHealth();
                if (capacityChanged || shownHealth >= previousHealth)
                    SetWidth(damageTrail, healthFillImage.rectTransform.rect.width);
                else
                    trailWaitUntil = Time.time + 0.18f;
            }

            if (Time.time >= trailWaitUntil)
            {
                float healthWidth = healthFillImage.rectTransform.rect.width;
                float trailWidth = Mathf.MoveTowards(damageTrail.rect.width, healthWidth, Time.deltaTime * trailUnitsPerSecond);
                SetWidth(damageTrail, trailWidth);
            }

            UpdateFillTexture(healthFillImage, healthTileWidth, 4f, Time.time);
            UpdateStamina();
            PositionHealthValue();
            healthValue.color = health.IsHitFlashing ? UiTheme.Hurt : UiTheme.Ivory;
        }

        private void UpdateHealth()
        {
            shownHealth = health.Health;
            shownMaxHealth = health.MaxHealth;
            SetWidth(healthMeter, healthFramePadding + shownMaxHealth * healthUnitsPerPoint);
            SetWidth(healthFillImage.rectTransform, shownHealth * healthUnitsPerPoint);
            healthValue.text = shownHealth + " / " + shownMaxHealth;
        }

        private void UpdateStamina() => SetStamina(health.Stamina, health.MaxStamina);

        private static RawImage FindFillImage(Component reference, RectTransform meter)
        {
            return reference != null
                ? reference.GetComponent<RawImage>()
                : meter.GetComponentInChildren<RawImage>(true);
        }

        private static void UpdateFillTexture(RawImage fill, float tileWidth, float scrollSpeed, float time)
        {
            // Mirrored wrapping repeats after two texture widths.
            float offset = Mathf.Repeat(-time * scrollSpeed / tileWidth, 2f);
            float width = fill.rectTransform.rect.width / tileWidth;
            fill.uvRect = new Rect(offset, 0f, width, 1f);
        }

        private void PositionHealthValue()
        {
            RectTransform valueRect = healthValue.rectTransform;
            float meterLeft = canvasRect.InverseTransformPoint(healthMeter.position).x;
            float widthOnScreen = canvasRect.rect.xMax - meterLeft;
            Vector2 position = valueRect.anchoredPosition;
            position.x = Mathf.Min(Mathf.Max(162f, healthMeter.rect.width - healthValueInset), widthOnScreen - healthValueInset);
            valueRect.anchoredPosition = position;
        }

        private static void SetWidth(RectTransform rect, float width)
        {
            if (!Mathf.Approximately(rect.rect.width, width))
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }
}

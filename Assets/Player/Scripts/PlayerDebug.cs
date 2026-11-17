using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    public sealed class PlayerDebug : MonoBehaviour
    {
        public static event Action DrawExtra;
        public bool Visible { get; private set; }
        public bool Invincible { get; private set; }
        public bool HiddenFromEnemies { get; private set; }
        public bool UnlimitedStamina { get; private set; }
        public static bool ShowEnemyHealth => active != null && active.Visible;
        private static PlayerDebug active;
        private PlayerMovement movement;
        private PlayerCombat combat;
        private PlayerHealth health;
        private Rigidbody2D body;
        private TestEnemy[] enemies = Array.Empty<TestEnemy>();
        private float refreshAt;
        private Vector2 scroll;
        private readonly Rect panel = new Rect(12, 78, 370, 620);
        public bool PointerOverMenu => Visible && Mouse.current != null && panel.Contains(new Vector2(Mouse.current.position.x.ReadValue(), Screen.height - Mouse.current.position.y.ReadValue()));
        private void Awake()
        {
            movement = GetComponent<PlayerMovement>(); combat = GetComponent<PlayerCombat>();
            health = GetComponent<PlayerHealth>(); body = GetComponent<Rigidbody2D>();
        }
        private void OnEnable() => active = this;
        private void OnDisable() { if (active == this) active = null; Invincible = HiddenFromEnemies = UnlimitedStamina = false; foreach (var line in lines) if (line != null) line.enabled = false; }
        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) Visible = !Visible;
            if (Visible && Time.unscaledTime >= refreshAt)
            {
                enemies = FindObjectsByType<TestEnemy>();
                refreshAt = Time.unscaledTime + 0.5f;
            }
        }
        private string State => movement.IsHitStunned ? "Hit Stun" : movement.IsDodging ? "Dash" : combat != null && combat.IsLunging ? "Air Dash" : combat != null && combat.IsAirSpin ? "Air Spin" : combat != null && combat.IsRecovering ? "Recovery" : combat != null && combat.IsAttacking ? "Attack" : combat != null && combat.IsCharging ? "Charging Heavy" : movement.IsWallSliding ? "Wall Slide" : !movement.IsGrounded ? (body.linearVelocity.y > 0 ? "Jump" : "Falling") : movement.IsCrouching ? "Crouch" : Mathf.Abs(body.linearVelocity.x) > 0.1f ? "Run" : "Idle";
        private void OnGUI()
        {
            if (!Visible) return;
            GUILayout.BeginArea(panel, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("DEBUG  [H]");
            GUILayout.Label("Cyan: player | Green: enemies\nYellow/orange: attacks | Red: active");
            GUILayout.Label("State: " + State);
            GUILayout.Label($"Position: {body.position.x:F2}, {body.position.y:F2}");
            GUILayout.Label($"Velocity: {body.linearVelocity.x:F2}, {body.linearVelocity.y:F2}  |  {body.linearVelocity.magnitude:F2}");
            if (health != null) GUILayout.Label("Player HP: " + health.Health);
            GUILayout.Label($"Grounded: {movement.IsGrounded}   i-Frames: {movement.IsInvulnerable}");
            if (combat != null) GUILayout.Label($"Combo: {combat.ComboStep + 1}   Heavy: {combat.ChargeAmount:P0}   Buffered dash: {movement.HasBufferedDodge}");
            Invincible = GUILayout.Toggle(Invincible, "Invincible");
            HiddenFromEnemies = GUILayout.Toggle(HiddenFromEnemies, "Hidden from enemies");
            UnlimitedStamina = GUILayout.Toggle(UnlimitedStamina, "Unlimited stamina (placeholder)");
            GUILayout.Space(8);
            if (GUILayout.Button("Return to spawn")) movement.Respawn();
            GUILayout.Space(8); GUILayout.Label("Enemies");
            foreach (var enemy in enemies) if (enemy != null) GUILayout.Label(enemy.DebugInfo);
            DrawExtra?.Invoke();
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private Material lineMaterial;
        private int lineCount;
        private void LateUpdate()
        {
            lineCount = 0;
            if (Visible)
            {
                var own = GetComponent<Collider2D>();
                if (own != null && own.enabled) Outline(own.bounds, Color.cyan);
                if (combat != null && combat.IsAttacking) Outline(combat.AttackHitbox, combat.DebugAttackActive ? Color.red : Color.yellow, combat.IsAirSpin, combat.AttackHitboxAngle);
                foreach (var enemy in enemies)
                {
                    if (enemy == null) continue;
                    foreach (var collider in enemy.GetComponentsInChildren<Collider2D>())
                        if (collider.enabled && !collider.isTrigger) Outline(collider.bounds, Color.green);
                    if (enemy.DebugIsAttacking) Outline(enemy.AttackHitbox, enemy.DebugAttackActive ? Color.red : new Color(1f, 0.6f, 0f));
                }
            }
            for (int i = lineCount; i < lines.Count; i++) lines[i].enabled = false;
        }
        private void Outline(Bounds bounds, Color color, bool circle = false, float angle = 0f)
        {
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
            if (lineCount == lines.Count)
            {
                var go = new GameObject("Debug hitbox", typeof(LineRenderer));
                go.transform.SetParent(transform, false);
                var renderer = go.GetComponent<LineRenderer>();
                renderer.useWorldSpace = true;
                renderer.loop = true;
                renderer.startWidth = renderer.endWidth = 0.025f;
                renderer.sharedMaterial = lineMaterial;
                renderer.sortingOrder = 1000;
                lines.Add(renderer);
            }
            var line = lines[lineCount++];
            line.enabled = true;
            line.startColor = line.endColor = color;
            line.positionCount = circle ? 40 : 4;
            if (circle)
            {
                for (int i = 0; i < 40; i++)
                {
                    float circleAngle = i * Mathf.PI * 2f / 40f;
                    line.SetPosition(i, bounds.center + new Vector3(Mathf.Cos(circleAngle), Mathf.Sin(circleAngle), 0f) * bounds.extents.x);
                }
            }
            else
            {
                float z = bounds.center.z;
                line.SetPosition(0, new Vector3(bounds.min.x, bounds.min.y, z));
                line.SetPosition(1, new Vector3(bounds.max.x, bounds.min.y, z));
                line.SetPosition(2, new Vector3(bounds.max.x, bounds.max.y, z));
                line.SetPosition(3, new Vector3(bounds.min.x, bounds.max.y, z));
                if (angle != 0f)
                    for (int i = 0; i < 4; i++) line.SetPosition(i, bounds.center + Quaternion.Euler(0f, 0f, angle) * (line.GetPosition(i) - bounds.center));
            }
        }
        private void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}

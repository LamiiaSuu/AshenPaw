using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace AshenPaw
{
    [DefaultExecutionOrder(200)]
    public sealed partial class PlayerDebug : MonoBehaviour
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
        private float refreshAt, frameSeconds;
        private Collider2D playerCollider;
        private readonly Dictionary<TestEnemy, Collider2D[]> enemyColliders = new Dictionary<TestEnemy, Collider2D[]>();
        private readonly Dictionary<TestEnemy, Rigidbody2D> enemyBodies = new Dictionary<TestEnemy, Rigidbody2D>();
        private Vector2 scroll;
        private Rect panel => new Rect(12, 78, 430, Mathf.Min(620f, Screen.height / UiScale - 90f));
        public bool PointerOverMenu => Visible && Mouse.current != null && (panel.Contains(new Vector2(Mouse.current.position.x.ReadValue(), Screen.height - Mouse.current.position.y.ReadValue()) / UiScale) || CombatPanel.Contains(new Vector2(Mouse.current.position.x.ReadValue(), Screen.height - Mouse.current.position.y.ReadValue()) / UiScale));
        private void Awake()
        {
            movement = GetComponent<PlayerMovement>(); combat = GetComponent<PlayerCombat>();
            health = GetComponent<PlayerHealth>(); body = GetComponent<Rigidbody2D>(); playerCollider = GetComponent<Collider2D>();
        }
        private void OnEnable()
        {
            active = this;
            frameSeconds = 0f;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            Camera.onPreCull += BeforeBuiltinCamera;
        }
        private void BeforeCamera(ScriptableRenderContext context, Camera camera) { if (camera.CompareTag("MainCamera")) DrawDebugGeometry(); }
        private void BeforeBuiltinCamera(Camera camera) { if (GraphicsSettings.currentRenderPipeline == null && camera.CompareTag("MainCamera")) DrawDebugGeometry(); }
        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= BeforeCamera; Camera.onPreCull -= BeforeBuiltinCamera; if (active == this) active = null; Invincible = HiddenFromEnemies = UnlimitedStamina = false; foreach (var line in lines) if (line != null) line.enabled = false; }
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) frameSeconds = frameSeconds <= 0f ? dt : Mathf.Lerp(frameSeconds, dt, 1f - Mathf.Exp(-dt * 4f));
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) { Visible = !Visible; if (!Visible) popups.Clear(); }
            if (Visible && Time.unscaledTime >= refreshAt)
            {
                enemies = FindObjectsByType<TestEnemy>();
                enemyColliders.Clear(); enemyBodies.Clear();
                foreach (var enemy in enemies)
                {
                    enemyColliders[enemy] = enemy.GetComponentsInChildren<Collider2D>();
                    enemyBodies[enemy] = enemy.GetComponent<Rigidbody2D>();
                }
                refreshAt = Time.unscaledTime + 0.5f;
            }
        }
        private string State => movement.IsHitStunned ? "Hit Stun" : movement.IsDodging ? "Dash" : combat != null && combat.IsLunging ? "Air Dash" : combat != null && combat.IsAirSpin ? "Air Spin" : combat != null && combat.IsRecovering ? "Recovery" : combat != null && combat.IsAttacking ? "Attack" : combat != null && combat.IsCharging ? "Charging Heavy" : movement.IsWallSliding ? "Wall Slide" : !movement.IsGrounded ? (body.linearVelocity.y > 0 ? "Jump" : "Falling") : movement.IsCrouching ? "Crouch" : Mathf.Abs(body.linearVelocity.x) > 0.1f ? "Run" : "Idle";
        private void DrawPanel()
        {
            if (!Visible) return;
            GUILayout.BeginArea(panel, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("DEBUG  [H]");
            GUILayout.Label(frameSeconds > 0f ? $"FPS  {1f / frameSeconds:0}   |   {frameSeconds * 1000f:0.0} ms" : "FPS  --");
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
        private void LateUpdate() => DrawDebugGeometry();
        private void DrawDebugGeometry()
        {
            lineCount = 0;
            if (Visible)
            {
                if (playerCollider != null && playerCollider.enabled) OutlineCollider(playerCollider, Color.cyan);
                if (combat != null && combat.IsAttacking)
                {
                    Bounds box = combat.AttackHitbox;
                    if (Application.isPlaying && body != null) box.center += transform.position - new Vector3(body.position.x, body.position.y, transform.position.z);
                    Outline(box, combat.DebugAttackActive ? Color.red : Color.yellow, combat.IsAirSpin, combat.AttackHitboxAngle);
                }
                foreach (var enemy in enemies)
                {
                    if (enemy == null) continue;
                    if (enemyColliders.TryGetValue(enemy, out var colliders))
                        foreach (var collider in colliders)
                            if (collider != null && collider.enabled && !collider.isTrigger) OutlineCollider(collider, Color.green);
                    if (enemy.DebugIsAttacking)
                    {
                        Bounds box = enemy.AttackHitbox;
                        if (Application.isPlaying && enemyBodies.TryGetValue(enemy, out var rb) && rb != null)
                            box.center += enemy.transform.position - new Vector3(rb.position.x, rb.position.y, enemy.transform.position.z);
                        Outline(box, enemy.DebugAttackActive ? Color.red : new Color(1f, 0.6f, 0f));
                    }
                }
            }
            for (int i = lineCount; i < lines.Count; i++) lines[i].enabled = false;
        }
        private void OutlineCollider(Collider2D collider, Color color)
        {
            if (collider is CapsuleCollider2D capsule)
            {
                var line = NextLine(color, 26);
                bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
                float radius = (vertical ? capsule.size.x : capsule.size.y) * 0.5f;
                float straight = Mathf.Max(0f, (vertical ? capsule.size.y : capsule.size.x) * 0.5f - radius);
                for (int half = 0; half < 2; half++)
                    for (int i = 0; i <= 12; i++)
                    {
                        float angle = -Mathf.PI * 0.5f + half * Mathf.PI + i * Mathf.PI / 12f;
                        Vector2 point = new Vector2(Mathf.Cos(angle) * radius + (half == 0 ? straight : -straight), Mathf.Sin(angle) * radius);
                        if (vertical) point = new Vector2(-point.y, point.x);
                        line.SetPosition(half * 13 + i, capsule.transform.TransformPoint(point + capsule.offset));
                    }
            }
            else if (collider is BoxCollider2D box)
            {
                var line = NextLine(color, 4);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 point = box.offset + new Vector2((i == 0 || i == 3 ? -1 : 1) * box.size.x * 0.5f, (i < 2 ? -1 : 1) * box.size.y * 0.5f);
                    line.SetPosition(i, box.transform.TransformPoint(point));
                }
            }
            else Outline(collider.bounds, color);
        }
        private LineRenderer NextLine(Color color, int count)
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
            line.positionCount = count;
            return line;
        }
        private void Outline(Bounds bounds, Color color, bool circle = false, float angle = 0f)
        {
            var line = NextLine(color, circle ? 40 : 4);
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
            if (debugSkin != null) Destroy(debugSkin);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace AshenPaw
{
    public sealed partial class PlayerDebug
    {
        private sealed class Popup
        {
            public Vector3 position;
            public float born;
            public int amount, lane;
            public string outcome;
            public bool received;
        }
        private readonly List<Popup> popups = new List<Popup>();
        private readonly Queue<string> recentHits = new Queue<string>();
        private int dealt, taken, landedHits, receivedHits, dodged, attacks, popupLane;
        private int singleDamage, comboDamage, comboHits, lastTaken;
        private float lastComboHit = float.NegativeInfinity;
        private Object comboTarget;
        private string lastMove = "-", lastAdvantage = "-";
        private float lastStartup, lastActive, lastRecovery;
        private Rect CombatPanel => new Rect(Screen.width / UiScale - 434f, Screen.height / UiScale - 380f, 418f, 364f);
        private GUISkin debugSkin;
        private GUIStyle numberStyle, captionStyle, outcomeStyle;
        private GUIStyle titleStyle, valueStyle, noteStyle;
        public static float ScaleFor(int width, int height) => Mathf.Max(0.5f, Mathf.Min(width / 1920f, height / 1080f));
        private float UiScale => ScaleFor(Screen.width, Screen.height);

        public static void ReportAttack()
        {
            if (active != null && active.Visible) active.attacks++;
        }
        public static void Report(int amount, bool received, Vector3 position, string outcome, Object receiver = null)
        {
            if (active == null || (!active.Visible && amount <= 0)) return;
            active.AddPopup(amount, received, position, outcome);
            if (active.Visible) active.AddResult(amount, received, outcome, receiver);
        }

        private void AddPopup(int amount, bool received, Vector3 position, string outcome)
        {
            if (popups.Count >= 48) popups.RemoveAt(0);
            popups.Add(new Popup { amount = amount, received = received, position = position,
                outcome = outcome, born = Time.unscaledTime, lane = popupLane++ % 3 - 1 });
        }

        private void AddResult(int amount, bool received, string outcome, Object receiver)
        {
            if (outcome == "DODGED") dodged++;
            else if (amount > 0)
            {
                if (received) { taken += amount; receivedHits++; lastTaken = amount; lastComboHit = float.NegativeInfinity; }
                else
                {
                    dealt += amount; landedHits++; singleDamage = amount;
                    if (Time.time - lastComboHit >= 1f || comboTarget != receiver) { comboDamage = 0; comboHits = 0; }
                    comboDamage += amount; comboHits++;
                    lastComboHit = Time.time; comboTarget = receiver;
                    var enemy = receiver as TestEnemy;
                    lastAdvantage = enemy != null && enemy.DebugHitStunRemaining > 0f && combat != null
                        ? ((enemy.DebugHitStunRemaining - combat.DebugRemainingAttackTime) * 60f).ToString("+0.#;-0.#;0") + "f"
                        : "No hit stun";
                }
            }
            recentHits.Enqueue(outcome + (amount > 0 ? "  -" + amount : ""));
            while (recentHits.Count > 4) recentHits.Dequeue();
        }
        private void DrawCombatWindow()
        {
            if (combat != null && combat.IsAttacking)
            {
                lastMove = combat.DebugAttackName;
                lastStartup = combat.DebugStartup; lastActive = combat.DebugActive; lastRecovery = combat.DebugRecovery;
            }
            Rect rect = CombatPanel;
            DrawPanelBackground(rect);
            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 10f, rect.width - 28f, rect.height - 18f));
            GUILayout.Label("COMBAT DATA", titleStyle);
            DataRow("Single damage", singleDamage.ToString(), UiTheme.Gold);
            DataRow("Combo damage", comboDamage + "  (" + comboHits + " hits)", UiTheme.Gold);
            DataRow("Damage taken", lastTaken.ToString(), UiTheme.Hurt);
            GUILayout.Space(6);
            DataRow("Move", lastMove, UiTheme.Ivory);
            DataRow("Phase", combat != null ? combat.DebugAttackPhase : "Ready", UiTheme.Ivory);
            DataRow("Startup", Frames(lastStartup), UiTheme.Ivory);
            DataRow("Active", Frames(lastActive), UiTheme.Ivory);
            DataRow("Recovery", Frames(lastRecovery), UiTheme.Ivory);
            DataRow("Hit advantage", lastAdvantage, UiTheme.Ivory);
            GUILayout.Label("Frames @ 60 FPS | Combo window: 1s", noteStyle);
            if (GUILayout.Button("Reset combat data"))
            {
                dealt = taken = landedHits = receivedHits = dodged = attacks = 0;
                singleDamage = comboDamage = comboHits = lastTaken = 0;
                lastComboHit = float.NegativeInfinity; comboTarget = null;
                lastMove = lastAdvantage = "-";
                lastStartup = lastActive = lastRecovery = 0f;
                recentHits.Clear(); popups.Clear();
            }
            GUILayout.EndArea();
        }

        private static void DrawPanelBackground(Rect rect)
        {
            Color background = UiTheme.Surface;
            background.a = 0.94f;
            GUI.color = background;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = UiTheme.Gold;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static string Frames(float seconds) => (seconds * 60f).ToString("0.#") + "f  /  " + (seconds * 1000f).ToString("0") + " ms";
        private void DataRow(string label, string value, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(155f));
            GUI.color = color; GUILayout.Label(value, valueStyle); GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }
        private void OnGUI()
        {
            if (PauseMenu.BlocksGameplayInput || (!Visible && popups.Count == 0)) return;
            if (debugSkin == null)
            {
                debugSkin = Instantiate(GUI.skin);
                debugSkin.font = UiTheme.DebugFont;
                debugSkin.label.font = UiTheme.DebugFont;
                debugSkin.label.normal.textColor = UiTheme.Ivory;
                debugSkin.label.fontSize = 16;
                debugSkin.label.wordWrap = true;
                debugSkin.toggle.font = UiTheme.DebugFont;
                debugSkin.toggle.fontSize = 16;
                debugSkin.toggle.wordWrap = true;
                debugSkin.button.font = UiTheme.DebugFont;
                debugSkin.button.fontSize = 16;
                debugSkin.button.padding = new RectOffset(8, 8, 6, 6);
                titleStyle = new GUIStyle(debugSkin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                titleStyle.normal.textColor = UiTheme.Gold;
                noteStyle = new GUIStyle(debugSkin.label) { fontSize = 14 };
                noteStyle.normal.textColor = UiTheme.Muted;
                valueStyle = new GUIStyle(debugSkin.label) { alignment = TextAnchor.MiddleRight, wordWrap = false };
                valueStyle.normal.textColor = Color.white;
                numberStyle = new GUIStyle(GUIStyle.none)
                {
                    font = UiTheme.DamageFont, alignment = TextAnchor.MiddleCenter
                };
                numberStyle.normal.textColor = Color.white;
                captionStyle = new GUIStyle(numberStyle) { font = UiTheme.HudFont, fontSize = 14 };
                outcomeStyle = new GUIStyle(captionStyle);
            }
            Matrix4x4 previousMatrix = GUI.matrix;
            GUISkin previousSkin = GUI.skin;
            Color previousColor = GUI.color;
            try
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * UiScale);
                GUI.skin = debugSkin;
                GUI.color = Color.white;
                DrawPopups();
                if (Visible)
                {
                    DrawPanel();
                    DrawCombatWindow();
                }
            }
            finally { GUI.matrix = previousMatrix; GUI.skin = previousSkin; GUI.color = previousColor; }
        }
        private void DrawPopups()
        {
            Camera camera = Camera.main;
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                Popup popup = popups[i];
                float age = Time.unscaledTime - popup.born;
                if (age >= 1.15f) { popups.RemoveAt(i); continue; }
                if (camera == null) continue;
                Vector3 point = camera.WorldToScreenPoint(popup.position);
                if (point.z <= 0f) continue;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, (age - 0.7f) / 0.45f);
                float x = point.x / UiScale + popup.lane * 32f;
                float y = (Screen.height - point.y) / UiScale - 38f * (1f - Mathf.Exp(-age * 3f));
                Rect rect = new Rect(x - 90f, y - 26f, 180f, 48f);
                float bounce = 5f * Mathf.Exp(-age * 18f);
                if (popup.amount <= 0)
                {
                    Color outcome = popup.outcome switch
                    {
                        "DODGED" => UiTheme.Dodged,
                        "PARRY" => UiTheme.Gold,
                        _ => UiTheme.Ivory
                    };
                    outcomeStyle.fontSize = Mathf.RoundToInt(20f + bounce);
                    DrawOutlined(rect, popup.outcome, outcomeStyle, outcome, fade);
                    continue;
                }

                Color color = popup.received ? UiTheme.Hurt : UiTheme.Gold;
                numberStyle.fontSize = Mathf.RoundToInt((popup.outcome == "CRITICAL" ? 39f : 33f) + bounce);
                DrawOutlined(rect, "−" + popup.amount, numberStyle, color, fade);
                if (Visible)
                    DrawOutlined(new Rect(x - 90f, y + 19f, 180f, 22f), popup.outcome, captionStyle, color, fade);
            }
        }
        private static void DrawOutlined(Rect rect, string text, GUIStyle style, Color color, float alpha)
        {
            GUI.color = new Color(0.02f, 0.02f, 0.04f, alpha);
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    if (x != 0 || y != 0) GUI.Label(new Rect(rect.x + x * 1.5f, rect.y + y * 1.5f, rect.width, rect.height), text, style);
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [DefaultExecutionOrder(-100)]
    public sealed class PauseMenu : MonoBehaviour
    {
        private static readonly string[] Options = { "Continue", "Settings", "Quit Game" };
        private const float MenuWidth = 368f;
        private const float MenuHeight = 292f;
        private const float Center = MenuWidth * 0.5f;
        private const float FirstRow = 94f;
        private const float RowSpacing = 66f;
        private static PauseMenu owner;
        private static int resumeFrame = -1;

        internal static bool IsPaused => owner != null;
        internal static bool BlocksGameplayInput => IsPaused || Time.frameCount == resumeFrame;

        [SerializeField] private AudioClip selectionSound;
        [SerializeField, Range(0f, 1f)] private float selectionVolume = 0.12f;

        private AudioSource selectionSource;
        private bool settingsOpen;
        private bool stickHeld;
        private int selection;
        private float previousTimeScale;
        private bool previousAudioPause;
        private float openedAt;
        private float markerY, markerVelocity;
        private Texture2D diamond;
        private GUIStyle heading, button, caption;

        private void Update()
        {
            var keys = Keyboard.current;
            var pad = Gamepad.current;
            bool toggle = keys?.escapeKey.wasPressedThisFrame == true || pad?.startButton.wasPressedThisFrame == true;
            if (!IsPaused)
            {
                if (toggle) Open();
                return;
            }
            if (owner != this) return;

            if (toggle || pad?.buttonEast.wasPressedThisFrame == true)
            {
                if (settingsOpen) settingsOpen = false;
                else Continue();
                return;
            }

            float stick = pad?.leftStick.y.ReadValue() ?? 0f;
            bool stickStep = Mathf.Abs(stick) >= 0.5f && !stickHeld;
            stickHeld = Mathf.Abs(stick) >= 0.5f;
            bool up = keys?.upArrowKey.wasPressedThisFrame == true || keys?.wKey.wasPressedThisFrame == true
                || pad?.dpad.up.wasPressedThisFrame == true || stickStep && stick > 0f;
            bool down = keys?.downArrowKey.wasPressedThisFrame == true || keys?.sKey.wasPressedThisFrame == true
                || pad?.dpad.down.wasPressedThisFrame == true || stickStep && stick < 0f;
            if (!settingsOpen)
            {
                int nextSelection = selection;
                var mouse = Mouse.current;
                if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.01f)
                {
                    float scale = MenuScale();
                    Vector2 mousePosition = mouse.position.ReadValue();
                    mousePosition.y = Screen.height - mousePosition.y;
                    Vector2 local = (mousePosition - MenuOrigin(scale)) / scale;
                    for (int i = 0; i < Options.Length; i++)
                        if (Row(FirstRow + i * RowSpacing).Contains(local)) nextSelection = i;
                }
                nextSelection = (nextSelection + (down ? 1 : 0) - (up ? 1 : 0) + Options.Length) % Options.Length;
                SetSelection(nextSelection);
                markerY = Mathf.SmoothDamp(markerY, FirstRow + selection * RowSpacing,
                    ref markerVelocity, 0.045f, Mathf.Infinity, Time.unscaledDeltaTime);
            }

            if (keys?.enterKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true)
            {
                if (settingsOpen) settingsOpen = false;
                else Select(selection);
            }
        }

        private void SetSelection(int option)
        {
            if (selection == option) return;
            selection = option;
            if (selectionSound == null) return;

            if (selectionSource == null)
            {
                selectionSource = gameObject.AddComponent<AudioSource>();
                selectionSource.playOnAwake = false;
                selectionSource.ignoreListenerPause = true;
            }

            selectionSource.clip = selectionSound;
            selectionSource.volume = selectionVolume;
            selectionSource.Play();
        }

        private void Open()
        {
            if (IsPaused) return;
            GetComponent<CameraFeel>()?.Thaw();
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            selection = 0;
            settingsOpen = false;
            openedAt = Time.unscaledTime;
            markerY = FirstRow;
            markerVelocity = 0f;
            stickHeld = Mathf.Abs(Gamepad.current?.leftStick.y.ReadValue() ?? 0f) >= 0.5f;
            owner = this;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }

        private void Continue()
        {
            if (owner != this) return;
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            owner = null;
            resumeFrame = Time.frameCount;
        }

        private void OnDisable() => Continue();

        private void OnDestroy()
        {
            if (diamond != null) Destroy(diamond);
        }

        private void Select(int option)
        {
            switch (option)
            {
                case 0:
                    Continue();
                    break;
                case 1:
                    settingsOpen = true;
                    break;
                case 2:
                    Continue();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
        }

        private void OnGUI()
        {
            if (owner != this) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUIStyle.none) { font = UiTheme.MenuFont, fontSize = 14, alignment = TextAnchor.MiddleCenter };
                button = new GUIStyle(heading) { fontSize = 30 };
                caption = new GUIStyle(heading) { fontSize = 19 };
                heading.normal.textColor = UiTheme.Gold;
                caption.normal.textColor = UiTheme.Ivory;
                diamond = CreateDiamond();
            }

            var matrix = GUI.matrix;
            var tint = GUI.color;
            int depth = GUI.depth;
            GUI.depth = -100;
            GUI.matrix = Matrix4x4.identity;
            float fade = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - openedAt) / 0.18f);
            GUI.color = new Color(1f, 1f, 1f, fade);
            Color backdrop = UiTheme.Surface;
            backdrop.a = 205f / 255f;
            Fill(new Rect(0, 0, Screen.width, Screen.height), backdrop);
            float scale = MenuScale();
            Vector2 origin = MenuOrigin(scale);
            GUI.matrix = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(scale, scale, 1f));
            GUI.Label(new Rect(0f, 8f, MenuWidth, 24f), settingsOpen ? "S  E  T  T  I  N  G  S" : "P  A  U  S  E  D", heading);
            DrawDivider(44f, 144f, 19f, 12f, new Color32(116, 94, 76, 255), new Color32(191, 160, 125, 255));
            DrawDivider(273f, 72f, 11f, 10f, new Color32(87, 71, 60, 255), new Color32(146, 118, 92, 255));

            if (settingsOpen)
            {
                GUI.Label(Row(FirstRow + RowSpacing * 0.5f), "Settings coming soon.", caption);
                DrawSelection(FirstRow + 2f * RowSpacing);
                if (DrawButton(Row(FirstRow + 2f * RowSpacing), "Back", true)) settingsOpen = false;
            }
            else
            {
                DrawSelection(markerY);
                for (int i = 0; i < Options.Length; i++)
                {
                    float y = FirstRow + i * RowSpacing;
                    if (!DrawButton(Row(y), Options[i], selection == i)) continue;
                    SetSelection(i);
                    Select(i);
                    break;
                }
            }

            GUI.matrix = matrix;
            GUI.color = tint;
            GUI.depth = depth;
        }

        private static float MenuScale() => Mathf.Min(Screen.width / 500f, Screen.height / 525f);

        private static Vector2 MenuOrigin(float scale)
            => new Vector2((Screen.width - MenuWidth * scale) * 0.5f, (Screen.height - MenuHeight * scale) * 0.5f);

        private static Rect Row(float y) => new Rect(Center - 142f, y - 24f, 284f, 48f);

        private bool DrawButton(Rect rect, string label, bool selected)
        {
            button.normal.textColor = button.hover.textColor = button.active.textColor = selected ? UiTheme.Gold : UiTheme.Ivory;
            return GUI.Button(rect, label, button);
        }

        private void DrawSelection(float y)
        {
            DrawDiamond(Center - 114f, y, 16f, UiTheme.Gold);
            DrawDiamond(Center + 114f, y, 16f, UiTheme.Gold);
        }

        private void DrawDivider(float y, float extent, float gap, float diamondSize, Color line, Color jewel)
        {
            float length = extent - gap;
            Fill(new Rect(Center - extent, y - 0.5f, length, 1f), line);
            Fill(new Rect(Center + gap, y - 0.5f, length, 1f), line);
            DrawDiamond(Center, y, diamondSize, jewel);
        }

        private void DrawDiamond(float x, float y, float size, Color color)
            => Fill(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), color, diamond);

        private static Texture2D CreateDiamond()
        {
            const int size = 32;
            const float center = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Abs(x - center) + Mathf.Abs(y - center);
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(size * 0.5f + 0.5f - distance));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void Fill(Rect rect, Color color, Texture texture = null)
        {
            Color previous = GUI.color;
            GUI.color = previous * color;
            GUI.DrawTexture(rect, texture != null ? texture : Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}

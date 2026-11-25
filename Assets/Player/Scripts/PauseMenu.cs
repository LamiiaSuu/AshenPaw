using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [DefaultExecutionOrder(-100)]
    public sealed class PauseMenu : MonoBehaviour
    {
        private static readonly string[] Options = { "Continue", "Settings", "Quit Game" };
        private static readonly Color Gold = new Color32(224, 178, 126, 255);
        private static readonly Color Ivory = new Color32(239, 224, 208, 255);
        private static PauseMenu owner;
        private static int resumeFrame = -1;

        internal static bool IsPaused => owner != null;
        internal static bool BlocksGameplayInput => IsPaused || Time.frameCount == resumeFrame;

        private bool settingsOpen;
        private bool stickHeld;
        private int selection;
        private float previousTimeScale;
        private bool previousAudioPause;
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
                selection = (selection + (down ? 1 : 0) - (up ? 1 : 0) + Options.Length) % Options.Length;

            if (keys?.enterKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true)
            {
                if (settingsOpen) settingsOpen = false;
                else Select(selection);
            }
        }

        private void Open()
        {
            if (IsPaused) return;
            GetComponent<CameraFeel>()?.Thaw();
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            selection = 0;
            settingsOpen = false;
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
                heading = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                button = new GUIStyle(GUIStyle.none) { fontSize = 22, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(20, 16, 0, 0) };
                caption = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            }

            var matrix = GUI.matrix;
            var tint = GUI.color;
            int depth = GUI.depth;
            GUI.depth = -100;
            GUI.matrix = Matrix4x4.identity;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.035f, 0.045f, 0.78f));
            float scale = Mathf.Min(Screen.width / 640f, Screen.height / 620f);
            float panelHeight = settingsOpen ? 290f : 214f;
            var origin = new Vector3((Screen.width - 364f * scale) * 0.5f, (Screen.height - panelHeight * scale) * 0.5f, 0f);
            GUI.matrix = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(scale, scale, 1f));
            Fill(new Rect(0, 0, 364, panelHeight), new Color32(28, 24, 29, 250));
            Fill(new Rect(0, 0, 364, 2), Gold);
            heading.normal.textColor = Ivory;
            if (settingsOpen) GUI.Label(new Rect(24, 18, 316, 60), "Settings", heading);

            if (settingsOpen)
            {
                caption.normal.textColor = Ivory;
                GUI.Label(new Rect(24, 98, 316, 40), "Settings coming soon.", caption);
                if (DrawButton(new Rect(24, 214, 316, 48), "Back", true)) settingsOpen = false;
            }
            else
            {
                for (int i = 0; i < Options.Length; i++)
                {
                    if (!DrawButton(new Rect(24, 24 + i * 58, 316, 48), Options[i], selection == i)) continue;
                    selection = i;
                    Select(i);
                    break;
                }
            }

            GUI.matrix = matrix;
            GUI.color = tint;
            GUI.depth = depth;
        }

        private bool DrawButton(Rect rect, string label, bool selected)
        {
            bool highlighted = selected || rect.Contains(Event.current.mousePosition);
            Fill(rect, highlighted ? new Color32(63, 46, 41, 255) : new Color32(37, 31, 36, 255));
            if (selected) Fill(new Rect(rect.x, rect.y, 2, rect.height), Gold);
            button.normal.textColor = button.hover.textColor = button.active.textColor = highlighted ? Gold : Ivory;
            return GUI.Button(rect, label, button);
        }

        private static void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenPaw
{
    [DefaultExecutionOrder(-100)]
    public sealed class PauseMenu : MonoBehaviour
    {
        private enum Page { Pause, Settings, Sound }

        private static readonly string[] PauseOptions = { "Continue", "Settings", "Quit Game" };
        private static readonly string[] SettingsOptions = { "Sound Settings", "Back" };
        private static readonly (AudioCategory Category, string Label)[] AudioOptions =
        {
            (AudioCategory.Ambience, "Ambience"),
            (AudioCategory.Bgm, "BGM"),
            (AudioCategory.Sfx, "SFX"),
            (AudioCategory.Voices, "Voices")
        };
        private const float MenuWidth = 368f;
        private const float MenuHeight = 292f;
        private const float SettingsHeight = 226f;
        private const float SoundWidth = 416f;
        private const float SoundHeight = 321f;
        private const float Center = MenuWidth * 0.5f;
        private const float FirstRow = 94f;
        private const float RowSpacing = 66f;
        private const float FirstAudioRow = 76f;
        private const float AudioRowSpacing = 46f;
        private const float BackRow = 267f;
        private const float VolumeStep = 0.05f;
        private static PauseMenu owner;
        private static int resumeFrame = -1;

        internal static bool IsPaused => owner != null;
        internal static bool BlocksGameplayInput => IsPaused || Time.frameCount == resumeFrame;

        [SerializeField] private AudioClip selectionSound;
        [SerializeField, Range(0f, 1f)] private float selectionVolume = 0.12f;

        private AudioSource selectionSource;
        private Page page;
        private bool stickHeld;
        private int selection;
        private int volumeDirection;
        private float nextVolumeStep;
        private float previousTimeScale;
        private bool previousAudioPause;
        private float openedAt;
        private float markerY, markerVelocity;
        private Texture2D diamond;
        private GUIStyle heading, button, volumeLabel, volumeValue, sliderThumb;

        private void Start() => GameAudioSettings.ApplySavedVolumes();

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
                GoBack();
                return;
            }

            float stick = pad?.leftStick.y.ReadValue() ?? 0f;
            bool stickStep = Mathf.Abs(stick) >= 0.5f && !stickHeld;
            stickHeld = Mathf.Abs(stick) >= 0.5f;
            bool up = keys?.upArrowKey.wasPressedThisFrame == true || keys?.wKey.wasPressedThisFrame == true
                || pad?.dpad.up.wasPressedThisFrame == true || stickStep && stick > 0f;
            bool down = keys?.downArrowKey.wasPressedThisFrame == true || keys?.sKey.wasPressedThisFrame == true
                || pad?.dpad.down.wasPressedThisFrame == true || stickStep && stick < 0f;
            bool confirm = keys?.enterKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true;
            int vertical = (down ? 1 : 0) - (up ? 1 : 0);
            if (page == Page.Sound)
            {
                UpdateAudioSettings(keys, pad, vertical, confirm);
                return;
            }

            string[] options = page == Page.Settings ? SettingsOptions : PauseOptions;
            int nextSelection = selection;
            var mouse = Mouse.current;
            if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.01f)
            {
                float scale = MenuScale();
                Vector2 mousePosition = mouse.position.ReadValue();
                mousePosition.y = Screen.height - mousePosition.y;
                Vector2 local = (mousePosition - MenuOrigin(scale)) / scale;
                for (int i = 0; i < options.Length; i++)
                    if (Row(FirstRow + i * RowSpacing).Contains(local)) nextSelection = i;
            }
            nextSelection = (nextSelection + vertical + options.Length) % options.Length;
            SetSelection(nextSelection);
            markerY = Mathf.SmoothDamp(markerY, FirstRow + selection * RowSpacing,
                ref markerVelocity, 0.045f, Mathf.Infinity, Time.unscaledDeltaTime);
            if (confirm) Select(selection);
        }

        private void UpdateAudioSettings(Keyboard keys, Gamepad pad, int vertical, bool confirm)
        {
            int rows = AudioOptions.Length + 1;
            int nextSelection = selection;
            var mouse = Mouse.current;
            if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.01f)
            {
                float scale = MenuScale();
                Vector2 mousePosition = mouse.position.ReadValue();
                mousePosition.y = Screen.height - mousePosition.y;
                Vector2 local = (mousePosition - MenuOrigin(scale)) / scale;
                for (int i = 0; i < AudioOptions.Length; i++)
                    if (AudioRow(i).Contains(local)) nextSelection = i;
                if (SoundBackButton().Contains(local)) nextSelection = AudioOptions.Length;
            }

            SetSelection((nextSelection + vertical + rows) % rows);
            if (selection == AudioOptions.Length)
            {
                if (confirm) GoBack();
                return;
            }

            float stick = pad?.leftStick.x.ReadValue() ?? 0f;
            bool left = keys?.leftArrowKey.isPressed == true || keys?.aKey.isPressed == true
                || pad?.dpad.left.isPressed == true || stick < -0.5f;
            bool right = keys?.rightArrowKey.isPressed == true || keys?.dKey.isPressed == true
                || pad?.dpad.right.isPressed == true || stick > 0.5f;
            int direction = (right ? 1 : 0) - (left ? 1 : 0);
            bool newDirection = direction != volumeDirection;
            volumeDirection = direction;
            if (direction == 0 || (!newDirection && Time.unscaledTime < nextVolumeStep)) return;

            var category = AudioOptions[selection].Category;
            GameAudioSettings.SetVolume(category, Mathf.Clamp01(GameAudioSettings.GetVolume(category) + direction * VolumeStep));
            nextVolumeStep = Time.unscaledTime + (newDirection ? 0.3f : 0.07f);
        }

        private void SetSelection(int option)
        {
            if (selection == option) return;
            selection = option;
            volumeDirection = 0;
            PlaySelectionSound();
        }

        private void PlaySelectionSound()
        {
            if (selectionSound == null) return;

            if (selectionSource == null)
            {
                selectionSource = gameObject.AddComponent<AudioSource>();
                selectionSource.playOnAwake = false;
                selectionSource.ignoreListenerPause = true;
                selectionSource.outputAudioMixerGroup = GameAudioSettings.GetGroup(AudioCategory.Sfx);
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
            ChangePage(Page.Pause);
            openedAt = Time.unscaledTime;
            stickHeld = Mathf.Abs(Gamepad.current?.leftStick.y.ReadValue() ?? 0f) >= 0.5f;
            owner = this;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }

        private void Continue()
        {
            if (owner != this) return;
            if (page == Page.Sound) GameAudioSettings.Save();
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
            if (page == Page.Settings)
            {
                if (option == 0) ChangePage(Page.Sound);
                else GoBack();
                return;
            }

            switch (option)
            {
                case 0:
                    Continue();
                    break;
                case 1:
                    ChangePage(Page.Settings);
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

        private void GoBack()
        {
            switch (page)
            {
                case Page.Sound:
                    ChangePage(Page.Settings);
                    break;
                case Page.Settings:
                    ChangePage(Page.Pause, 1);
                    break;
                default:
                    Continue();
                    break;
            }
        }

        private void ChangePage(Page next, int selected = 0)
        {
            if (page == Page.Sound) GameAudioSettings.Save();
            page = next;
            selection = selected;
            volumeDirection = 0;
            markerY = FirstRow + selection * RowSpacing;
            markerVelocity = 0f;
        }

        private void OnGUI()
        {
            if (owner != this) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUIStyle.none) { font = UiTheme.MenuFont, fontSize = 14, alignment = TextAnchor.MiddleCenter };
                button = new GUIStyle(heading) { fontSize = 30 };
                volumeLabel = new GUIStyle(heading) { fontSize = 19, alignment = TextAnchor.MiddleLeft };
                volumeValue = new GUIStyle(volumeLabel) { fontSize = 16, alignment = TextAnchor.MiddleRight };
                sliderThumb = new GUIStyle(GUIStyle.none) { fixedWidth = 12f, fixedHeight = 20f };
                heading.normal.textColor = UiTheme.Gold;
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
            Vector2 size = MenuSize();
            float width = size.x;
            string title = page switch
            {
                Page.Settings => "S  E  T  T  I  N  G  S",
                Page.Sound => "S  O  U  N  D",
                _ => "P  A  U  S  E  D"
            };
            GUI.Label(new Rect(0f, 8f, width, 24f), title, heading);
            DrawDivider(width * 0.5f, 44f, 144f, 19f, 12f, new Color32(116, 94, 76, 255), new Color32(191, 160, 125, 255));
            DrawDivider(width * 0.5f, size.y - 19f, 72f, 11f, 10f, new Color32(87, 71, 60, 255), new Color32(146, 118, 92, 255));

            if (page == Page.Sound)
            {
                DrawAudioSettings();
            }
            else
            {
                string[] options = page == Page.Settings ? SettingsOptions : PauseOptions;
                DrawSelection(markerY);
                for (int i = 0; i < options.Length; i++)
                {
                    float y = FirstRow + i * RowSpacing;
                    if (!DrawButton(Row(y), options[i], selection == i)) continue;
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

        private Vector2 MenuOrigin(float scale)
        {
            Vector2 size = MenuSize();
            return (new Vector2(Screen.width, Screen.height) - size * scale) * 0.5f;
        }

        private Vector2 MenuSize() => page switch
        {
            Page.Settings => new Vector2(MenuWidth, SettingsHeight),
            Page.Sound => new Vector2(SoundWidth, SoundHeight),
            _ => new Vector2(MenuWidth, MenuHeight)
        };

        private static Rect Row(float y) => new Rect(Center - 142f, y - 24f, 284f, 48f);

        private static float AudioRowY(int index) => FirstAudioRow + AudioRowSpacing * index;

        private static Rect AudioRow(int index) => new Rect(26f, AudioRowY(index) - 18f, 356f, 36f);

        private static Rect SoundBackButton() => new Rect(SoundWidth * 0.5f - 142f, BackRow - 24f, 284f, 48f);

        private void DrawAudioSettings()
        {
            for (int i = 0; i < AudioOptions.Length; i++)
            {
                var option = AudioOptions[i];
                float y = AudioRowY(i);
                Rect slider = new Rect(144f, y - 12f, 171f, 24f);
                if (Event.current.type == EventType.MouseDown && AudioRow(i).Contains(Event.current.mousePosition))
                    SetSelection(i);

                bool selected = selection == i;
                Color color = selected ? UiTheme.Gold : UiTheme.Ivory;
                float volume = GameAudioSettings.GetVolume(option.Category);
                float nextVolume = GUI.HorizontalSlider(slider, volume, 0f, 1f, GUIStyle.none, sliderThumb);
                if (!Mathf.Approximately(nextVolume, volume))
                {
                    GameAudioSettings.SetVolume(option.Category, nextVolume);
                    volume = nextVolume;
                }

                if (selected) DrawDiamond(35f, y, 10f, color);
                volumeLabel.normal.textColor = color;
                volumeValue.normal.textColor = color;
                GUI.Label(new Rect(52f, y - 13f, 88f, 26f), option.Label, volumeLabel);
                GUI.Label(new Rect(325f, y - 12f, 47f, 24f), $"{Mathf.RoundToInt(volume * 100f)}%", volumeValue);

                float trackStart = slider.x + sliderThumb.fixedWidth * 0.5f;
                float trackWidth = slider.width - sliderThumb.fixedWidth;
                Fill(new Rect(trackStart, y - 1f, trackWidth, 2f), new Color32(100, 83, 71, 255));
                Fill(new Rect(trackStart, y - 1f, trackWidth * volume, 2f), color);
                DrawDiamond(trackStart + trackWidth * volume, y, selected ? 13f : 11f, color);
            }

            bool backSelected = selection == AudioOptions.Length;
            if (backSelected) DrawSelection(BackRow, SoundWidth * 0.5f);
            if (DrawButton(SoundBackButton(), "Back", backSelected)) GoBack();
        }

        private bool DrawButton(Rect rect, string label, bool selected)
        {
            button.normal.textColor = button.hover.textColor = button.active.textColor = selected ? UiTheme.Gold : UiTheme.Ivory;
            return GUI.Button(rect, label, button);
        }

        private void DrawSelection(float y, float center = Center)
        {
            DrawDiamond(center - 114f, y, 16f, UiTheme.Gold);
            DrawDiamond(center + 114f, y, 16f, UiTheme.Gold);
        }

        private void DrawDivider(float center, float y, float extent, float gap, float diamondSize, Color line, Color jewel)
        {
            float length = extent - gap;
            Fill(new Rect(center - extent, y - 0.5f, length, 1f), line);
            Fill(new Rect(center + gap, y - 0.5f, length, 1f), line);
            DrawDiamond(center, y, diamondSize, jewel);
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

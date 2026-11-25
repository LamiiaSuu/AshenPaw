using UnityEngine;

namespace AshenPaw
{
    internal static class UiTheme
    {
        internal static readonly Font MenuFont = Resources.Load<Font>("Fonts/Gelasio-Regular");
        internal static readonly Font DamageFont = Resources.Load<Font>("Fonts/Gelasio-Bold");
        internal static readonly Font HudFont = Resources.Load<Font>("Fonts/SourceSans3-Semibold");
        internal static readonly Font DebugFont = Resources.Load<Font>("Fonts/SourceCodePro-Regular");

        internal static readonly Color Ivory = new Color32(216, 204, 192, 255);
        internal static readonly Color Gold = new Color32(215, 170, 122, 255);
        internal static readonly Color Muted = new Color32(172, 156, 144, 255);
        internal static readonly Color Surface = new Color32(28, 24, 29, 255);
        internal static readonly Color Hurt = new Color32(242, 142, 132, 255);
        internal static readonly Color Dodged = new Color32(174, 210, 194, 255);
    }
}

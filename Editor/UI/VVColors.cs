using UnityEngine;

namespace VirtualVenues.Editor.UI
{
    /// <summary>
    /// C# mirror of the VirtualVenues palette (VVTheme.uss) for the handful of colors that
    /// are chosen at runtime — e.g. per-row status icons and success/error labels — where a
    /// USS class can't be applied declaratively. Keep these in sync with VVTheme.uss.
    /// </summary>
    public static class VVColors
    {
        public static readonly Color Text      = new Color(0.929f, 0.949f, 0.980f); // #EDF2FA
        public static readonly Color TextMuted = new Color(0.580f, 0.639f, 0.722f); // #94A3B8
        public static readonly Color Success   = new Color(0.204f, 0.827f, 0.600f); // #34D399
        public static readonly Color Warning   = new Color(0.961f, 0.651f, 0.137f); // #F5A623
        public static readonly Color Danger    = new Color(0.973f, 0.443f, 0.443f); // #F87171
        public static readonly Color Muted     = new Color(0.392f, 0.455f, 0.557f); // #64748B
    }
}

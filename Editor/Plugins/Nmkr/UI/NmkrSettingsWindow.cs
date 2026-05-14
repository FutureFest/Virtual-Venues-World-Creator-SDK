using UnityEditor;
using UnityEngine;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// Standalone window for editing NMKR plugin settings. Thin shell around the
    /// shared <see cref="NmkrSettingsView"/> — the same UI is also embedded in
    /// the NMKR_Plugin prefab's custom Inspector, and both edit the one shared
    /// <see cref="NmkrPluginConfig"/> asset.
    /// </summary>
    public class NmkrSettingsWindow : EditorWindow
    {
        [MenuItem("VirtualVenues/Plugins/NMKR Settings")]
        public static void ShowWindow()
        {
            NmkrSettingsWindow w = GetWindow<NmkrSettingsWindow>("NMKR Settings");
            w.minSize = new Vector2(420, 320);
        }

        private void CreateGUI()
        {
            rootVisualElement.Add(new NmkrSettingsView());
        }
    }
}

using UnityEditor;
using UnityEngine.UIElements;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// Inspector for <see cref="NmkrPluginSettings"/>. Surfaces the same NMKR
    /// settings UI as the NMKR Settings window — both edit the one shared
    /// <see cref="NmkrPluginConfig"/> asset. The component's own serialized
    /// fields are left empty on disk; NmkrBuildProcessor injects the resolved
    /// values into the world bundle at build time.
    /// </summary>
    [CustomEditor(typeof(NmkrPluginSettings))]
    public class NmkrPluginSettingsEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            HelpBox info = new HelpBox(
                "Settings are stored in the shared NmkrPluginConfig asset and injected " +
                "into this component when the world is published — the full set into the " +
                "dedicated-server (UMS) bundle, everything except the API key into the " +
                "player (UPC) bundle. The fields on this component stay empty on disk.",
                HelpBoxMessageType.Info);
            info.style.marginBottom = 6;
            root.Add(info);

            root.Add(new NmkrSettingsView());
            return root;
        }
    }
}

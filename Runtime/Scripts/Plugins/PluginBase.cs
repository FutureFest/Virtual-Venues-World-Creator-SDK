using UnityEngine;

namespace VirtualVenues.Plugins
{
    public abstract class PluginBase : MonoBehaviour
    {
        [SerializeField] private string _pluginId = string.Empty;
        [SerializeField] private string _displayName = string.Empty;

        public string PluginId => _pluginId;
        public string DisplayName => _displayName;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Put one in a world to choose its control style. Default Style is what players start with; Allowed Styles is
    /// what they can switch between (empty = the built-ins plus your default). Turn off Allow Player Choice to force
    /// the default. Without one, worlds use Classic and players can choose any built-in.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldControlSettings : MonoBehaviour
    {
        private static WorldControlSettings _current = null;

        /// <summary>The settings of the loaded world, or null.</summary>
        public static WorldControlSettings Current => _current;

        /// <summary>Raised when <see cref="Current"/> or its values change.</summary>
        public static event Action OnChanged;

        [SerializeField] private ControlStyle _defaultStyle = null;
        [SerializeField] private List<ControlStyle> _allowedStyles = new List<ControlStyle>();
        [SerializeField] private bool _allowPlayerChoice = true;

        public ControlStyle DefaultStyle => _defaultStyle;
        public IReadOnlyList<ControlStyle> AllowedStyles => _allowedStyles;
        public bool AllowPlayerChoice => _allowPlayerChoice;

        /// <summary>Runtime setup, e.g. from a UWE layout.</summary>
        public void Configure(ControlStyle defaultStyle, bool allowPlayerChoice)
        {
            _defaultStyle = defaultStyle;
            _allowPlayerChoice = allowPlayerChoice;
            if (_current == this) { RaiseChanged(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            OnChanged = null;
        }

        private void OnEnable()
        {
            _current = this;
            RaiseChanged();
        }

        private void OnDisable()
        {
            if (_current != this) { return; }
            _current = null;
            RaiseChanged();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && _current == this) { RaiseChanged(); }
        }

        private static void RaiseChanged()
        {
            OnChanged?.Invoke();
            ControlStyles.Refresh();
        }
    }
}

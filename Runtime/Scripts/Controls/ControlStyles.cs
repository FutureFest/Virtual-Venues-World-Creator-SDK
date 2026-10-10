using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// The built-in control styles and the one in use. Active = the player's pick (when the world allows it and
    /// offers that style) &gt; the world's default (<see cref="WorldControlSettings"/>) &gt; Classic.
    /// The core project sets <see cref="PlayerPick"/> from the player's settings; players read <see cref="Active"/>
    /// and listen to <see cref="OnActiveChanged"/>.
    /// </summary>
    public static class ControlStyles
    {
        public const string ResourcesFolder = "ControlStyles";
        public const string ClassicId = "classic";
        public const string ShooterId = "shooter";
        public const string MmoId = "mmo";

        private static ControlStyle[] _builtIn = null;
        private static ControlStyle _fallback = null;
        private static string _playerPick = "";
        private static ControlStyle _active = null;

        /// <summary>Raised when <see cref="Active"/> changes.</summary>
        public static event Action<ControlStyle> OnActiveChanged;

        /// <summary>Classic, Shooter and MMO, loaded from Resources/ControlStyles.</summary>
        public static IReadOnlyList<ControlStyle> BuiltIn
        {
            get
            {
                if (_builtIn == null) { _builtIn = Resources.LoadAll<ControlStyle>(ResourcesFolder); }
                return _builtIn;
            }
        }

        /// <summary>The style id the player chose, or empty for the world default.</summary>
        public static string PlayerPick
        {
            get => _playerPick;
            set
            {
                _playerPick = value ?? "";
                Refresh();
            }
        }

        public static ControlStyle Active
        {
            get
            {
                if (_active == null) { _active = Resolve(_playerPick, WorldControlSettings.Current); }
                return _active;
            }
        }

        /// <summary>The styles the player can choose from in the current world.</summary>
        public static IReadOnlyList<ControlStyle> Available(WorldControlSettings world)
        {
            if (world == null || world.AllowedStyles.Count == 0)
            {
                List<ControlStyle> styles = new List<ControlStyle>(BuiltIn);
                ControlStyle worldDefault = world != null ? world.DefaultStyle : null;
                if (worldDefault != null && !styles.Contains(worldDefault)) { styles.Insert(0, worldDefault); }
                return styles;
            }

            return world.AllowedStyles;
        }

        public static bool PlayerCanChoose(WorldControlSettings world)
        {
            return world == null || world.AllowPlayerChoice;
        }

        /// <summary>Finds a style by id among the built-ins and the given world's styles.</summary>
        public static ControlStyle Find(string id, WorldControlSettings world = null)
        {
            if (string.IsNullOrEmpty(id)) { return null; }

            foreach (ControlStyle style in Available(world))
            {
                if (style != null && style.Id == id) { return style; }
            }
            foreach (ControlStyle style in BuiltIn)
            {
                if (style != null && style.Id == id) { return style; }
            }
            return null;
        }

        /// <summary>Pure resolution, exposed for tests and tools.</summary>
        public static ControlStyle Resolve(string playerPick, WorldControlSettings world)
        {
            if (PlayerCanChoose(world) && !string.IsNullOrEmpty(playerPick))
            {
                foreach (ControlStyle style in Available(world))
                {
                    if (style != null && style.Id == playerPick) { return style; }
                }
            }

            if (world != null && world.DefaultStyle != null) { return world.DefaultStyle; }

            return Find(ClassicId) ?? Fallback;
        }

        /// <summary>Re-resolves <see cref="Active"/> and raises <see cref="OnActiveChanged"/> if it changed.</summary>
        public static void Refresh()
        {
            ControlStyle next = Resolve(_playerPick, WorldControlSettings.Current);
            if (next == _active) { return; }
            _active = next;
            OnActiveChanged?.Invoke(_active);
        }

        // Used only if the Resources presets are missing, so a player is never left without a style.
        private static ControlStyle Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = ScriptableObject.CreateInstance<ControlStyle>();
                    _fallback.name = ClassicId;
                }
                return _fallback;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _builtIn = null;
            _playerPick = "";
            _active = null;
            OnActiveChanged = null;
        }
    }
}

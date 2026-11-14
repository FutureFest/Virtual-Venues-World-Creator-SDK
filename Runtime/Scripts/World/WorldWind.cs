using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// The world's wind, set by the creator in the Inspector. One per world. Built on Unity's own <see cref="WindZone"/>
    /// on the same object: the Wind Zone's arrow (this object's forward) is where the wind blows, and its <b>Main</b> is
    /// the wind power (1 = normal, 0 = no wind, 2 = double). The Wind Zone itself still pushes Particle Systems with
    /// External Forces and Unity terrain grass and trees.
    /// <para>Unity hands Wind Zone values only to its own tree shaders and to particles, so this component passes them
    /// to every shader as two globals, set every frame (also in the Editor, so the creator sees changes live):
    /// <c>_VVWind</c> = (direction x, direction z, power, 1) and <c>_VVWindGust</c> = (turbulence, pulse magnitude,
    /// pulse frequency, 0). Direction = where the wind blows TO, world x / z, length 1. With no World Wind on (none in
    /// the scene, or it is disabled) both are all 0, and <c>_VVWind.w</c> = 0 tells a shader to use its own defaults.
    /// Shaders read them through <c>Runtime/Shaders/VVWind.hlsl</c>.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WindZone))]
    [AddComponentMenu("VirtualVenues/World Wind")]
    public class WorldWind : MonoBehaviour
    {
        /// <summary>Shader global: (direction x, direction z, power, 1), or all 0 with no World Wind.</summary>
        public static readonly int WindId = Shader.PropertyToID("_VVWind");
        /// <summary>Shader global: (turbulence, pulse magnitude, pulse frequency, 0), or all 0 with no World Wind.</summary>
        public static readonly int GustId = Shader.PropertyToID("_VVWindGust");

        // Enabled World Winds, oldest first. The last one enabled drives the globals.
        private static readonly List<WorldWind> _instances = new List<WorldWind>();

        private WindZone _zone = null;

        /// <summary>The World Wind that sets the shader globals now, or null.</summary>
        public static WorldWind Active => _instances.Count > 0 ? _instances[_instances.Count - 1] : null;

        /// <summary>The Wind Zone this reads from.</summary>
        public WindZone Zone => _zone != null ? _zone : (_zone = GetComponent<WindZone>());

        /// <summary>Where the wind blows to, world (x, z), length 1.</summary>
        public Vector2 Direction => FlatDirection(transform.forward);

        /// <summary>Wind power: the Wind Zone's Main, never below 0.</summary>
        public float Power => Zone != null ? Mathf.Max(0f, Zone.windMain) : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instances.Clear();
            Clear();
        }

        // Added from Add Component: the Wind Zone that comes with it blows world-wide.
        private void Reset()
        {
            if (Zone != null) { Zone.mode = WindZoneMode.Directional; }
        }

        private void OnEnable()
        {
            _zone = GetComponent<WindZone>();
            _instances.Remove(this);
            _instances.Add(this);
            if (_instances.Count > 1)
            {
                Debug.LogWarning($"[WorldWind] More than one World Wind is on ('{_instances[0].name}', '{name}'). A world has one; '{name}' is used.", this);
            }
            Push();
        }

        private void OnDisable()
        {
            _instances.Remove(this);
            WorldWind next = Active;
            if (next != null) { next.Push(); }
            else { Clear(); }
        }

        private void Update()
        {
            if (Active == this) { Push(); }
        }

        /// <summary>Sets <c>_VVWind</c> and <c>_VVWindGust</c> from this object and its Wind Zone.</summary>
        public void Push()
        {
            WindZone zone = Zone;
            if (zone == null) { Clear(); return; }
            Shader.SetGlobalVector(WindId, PackWind(transform.forward, zone.windMain));
            Shader.SetGlobalVector(GustId, PackGust(zone.windTurbulence, zone.windPulseMagnitude, zone.windPulseFrequency));
        }

        /// <summary>Both globals to all 0: no World Wind, shaders use their own defaults.</summary>
        public static void Clear()
        {
            Shader.SetGlobalVector(WindId, Vector4.zero);
            Shader.SetGlobalVector(GustId, Vector4.zero);
        }

        /// <summary><c>_VVWind</c> for a Wind Zone facing <paramref name="forward"/> with Main <paramref name="main"/>.</summary>
        public static Vector4 PackWind(Vector3 forward, float main)
        {
            Vector2 direction = FlatDirection(forward);
            return new Vector4(direction.x, direction.y, Mathf.Max(0f, main), 1f);
        }

        /// <summary><c>_VVWindGust</c> from a Wind Zone's Turbulence, Pulse Magnitude and Pulse Frequency.</summary>
        public static Vector4 PackGust(float turbulence, float pulseMagnitude, float pulseFrequency)
        {
            return new Vector4(Mathf.Max(0f, turbulence), Mathf.Max(0f, pulseMagnitude), Mathf.Max(0f, pulseFrequency), 0f);
        }

        // Only the heading counts: a tilted Wind Zone blows level. Pointing straight up or down has no heading, so north.
        private static Vector2 FlatDirection(Vector3 forward)
        {
            Vector2 flat = new Vector2(forward.x, forward.z);
            return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.up;
        }
    }
}

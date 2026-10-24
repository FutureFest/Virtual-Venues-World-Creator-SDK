using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Marker: choose what the solid colliders on this GameObject and its children stop — players, the camera,
    /// vehicles, any mix. The VirtualVenues app sets their layers and per-collider layer overrides when the world
    /// loads; the SDK TestPlayer imitates players + camera in Play mode. Nothing changes at edit time.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Collision Scope")]
    public class CollisionScope : MonoBehaviour
    {
        [Tooltip("Players bump into it and can stand on it. Off: players walk through.\n" +
                 "Covers every solid (non-trigger) collider on this object and its children.")]
        [SerializeField] private bool _stopsPlayers = true;
        [Tooltip("The camera stops at it (zooms in instead of going through). Off: the camera passes through.")]
        [SerializeField] private bool _stopsCamera = false;
        [Tooltip("Vehicles bump into it and can drive on it. Off: vehicles pass through.")]
        [SerializeField] private bool _stopsVehicles = false;

        public bool StopsPlayers => _stopsPlayers;
        public bool StopsCamera => _stopsCamera;
        public bool StopsVehicles => _stopsVehicles;

        private static readonly List<CollisionScope> _instances = new List<CollisionScope>();
        public static List<CollisionScope> Instances => _instances;
        public static Action<CollisionScope> onCollisionScopeAdded = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instances.Clear();
            onCollisionScopeAdded = null;
        }

        private void Awake()
        {
            if (_instances.Contains(this)) { return; }
            _instances.Add(this);
            onCollisionScopeAdded?.Invoke(this);
        }

        private void OnDestroy()
        {
            _instances.Remove(this);
        }

        /// <summary>
        /// Fills <paramref name="covered"/> with the solid colliders this scope owns: non-trigger, nearest
        /// CollisionScope parent is this one. A collider sharing its GameObject with a trigger goes to
        /// <paramref name="skipped"/> instead, because layers are per GameObject and the trigger needs its own.
        /// </summary>
        public void GetColliders(List<Collider> covered, List<Collider> skipped = null)
        {
            covered.Clear();
            skipped?.Clear();
            foreach (Collider col in GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) { continue; }
                if (col.GetComponentInParent<CollisionScope>(true) != this) { continue; }
                if (HasTrigger(col.gameObject)) { skipped?.Add(col); continue; }
                covered.Add(col);
            }
        }

        private static bool HasTrigger(GameObject go)
        {
            foreach (Collider col in go.GetComponents<Collider>())
            {
                if (col.isTrigger) { return true; }
            }
            return false;
        }

#if UNITY_EDITOR
        private static readonly List<Collider> _gizmoCovered = new List<Collider>();
        private static readonly List<Collider> _gizmoSkipped = new List<Collider>();

        private void OnDrawGizmos()
        {
            GetColliders(_gizmoCovered, _gizmoSkipped);
            // Blue = players, yellow = camera, green = vehicles, mixed when several are ticked, grey = none.
            Color sum = Color.clear;
            if (_stopsPlayers) { sum += new Color(0.2f, 0.6f, 1f); }
            if (_stopsCamera) { sum += new Color(1f, 0.8f, 0.1f); }
            if (_stopsVehicles) { sum += new Color(0.3f, 0.9f, 0.4f); }
            Color c = sum.a > 0f ? sum / sum.a : Color.gray;
            foreach (Collider col in _gizmoCovered)
            {
                Bounds b = col.bounds;
                Gizmos.color = new Color(c.r, c.g, c.b, 0.12f);
                Gizmos.DrawCube(b.center, b.size);
                Gizmos.color = c;
                Gizmos.DrawWireCube(b.center, b.size);
            }
            // Red: shares a GameObject with a trigger, so it is left alone. Move the solid collider to its own child.
            Gizmos.color = Color.red;
            foreach (Collider col in _gizmoSkipped)
            {
                Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
            }
        }
#endif
    }
}

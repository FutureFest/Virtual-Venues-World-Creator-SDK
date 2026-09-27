using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public abstract class TriggerZoneData : MonoBehaviour
    {
        public enum TriggerZoneType
        {
            None,
            Voice,
            Area,
            Swim,
            Bounce,
            Push,
            Respawn,
        }

        /// <summary>
        /// Raised when zone data is added or re-configured (e.g. a world layout adds it after the TriggerZone, or
        /// the world editor changes a value), so the runtime can (re)apply it. Voice/Area Configure does not raise it.
        /// </summary>
        public static event Action<TriggerZoneData> OnChanged;

        /// <summary>Raised when zone data is destroyed, so the runtime can remove what it added for it.</summary>
        public static event Action<TriggerZoneData> OnRemoved;

        public abstract TriggerZoneType ZoneType { get; }

        protected virtual void Awake()
        {
            RaiseChanged();
        }

        protected virtual void OnDestroy()
        {
            OnRemoved?.Invoke(this);
        }

        protected void RaiseChanged()
        {
            OnChanged?.Invoke(this);
        }
    }
}

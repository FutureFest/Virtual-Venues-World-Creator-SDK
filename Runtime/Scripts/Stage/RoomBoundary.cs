using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>Authored room volume. Acoustic separation is independent of booking privacy.</summary>
    public sealed class RoomBoundary : MonoBehaviour
    {
        public int stageIndex;
        public Collider volume;
        public GameObject privacyBarrier;
        public static Func<Vector3?> ListenerPosition;
        private static readonly List<RoomBoundary> Rooms = new List<RoomBoundary>();
        private static PrivacySnapshot snapshot;

        [Serializable] public sealed class Booking
        {
            public int stageIndex;
            public long start;
            public long end;
        }
        [Serializable] public sealed class PrivacySnapshot { public Booking[] bookings; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() { Rooms.Clear(); snapshot = null; ListenerPosition = null; }

        public static void SetPrivacySnapshot(string json)
        {
            var next = JsonUtility.FromJson<PrivacySnapshot>(json);
            if (next == null || next.bookings == null) return;
            snapshot = next;
            foreach (var room in Rooms) room.UpdatePrivacy();
        }

        private void OnEnable() { if (!Rooms.Contains(this)) Rooms.Add(this); UpdatePrivacy(); }
        private void OnDisable() { Rooms.Remove(this); }
        private void Update() { UpdatePrivacy(); }

        private void UpdatePrivacy()
        {
            if (!privacyBarrier) return;
            // A missing schedule is not a private booking. Public/unbooked rooms
            // remain open, including while the browser is fetching its first snapshot.
            bool isPrivate = false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (stageIndex > 0 && snapshot != null)
                foreach (var booking in snapshot.bookings)
                    if (booking != null && booking.stageIndex == stageIndex && booking.start <= now && now < booking.end)
                    { isPrivate = true; break; }
            if (privacyBarrier.activeSelf != isPrivate) privacyBarrier.SetActive(isPrivate);
        }

        public bool Contains(Vector3 point)
        {
            return volume && volume.enabled && volume.gameObject.activeInHierarchy &&
                (volume.ClosestPoint(point) - point).sqrMagnitude < 0.000001f;
        }

        public static int RoomAt(Vector3 point, bool includeStageZero = false)
        {
            // Breakout rooms take precedence at a shared boundary.
            foreach (var room in Rooms)
                if (room.stageIndex > 0 && room.Contains(point)) return room.stageIndex;
            if (includeStageZero)
                foreach (var room in Rooms)
                    if (room.stageIndex == 0 && room.Contains(point)) return 0;
            return -1;
        }

        public static bool AllowsAudio(Vector3 listener, Vector3 source)
        {
            return RoomAt(listener) == RoomAt(source);
        }
    }
}

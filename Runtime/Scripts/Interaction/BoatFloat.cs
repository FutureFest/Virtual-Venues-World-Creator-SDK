using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// How a <see cref="VehicleType.Boat"/> floats and drives in a world without Crest water. The core project and the
    /// TestPlayer both use it, so a boat handles the same in Play mode and in the live world.
    /// <para>The boat floats on the top of the <see cref="SwimZoneData"/> trigger volume it is in, raised and tilted by
    /// the <see cref="WaterWaves"/> over it (none = flat): lift = buoyancy × depth³ at the hull probe, a torque that
    /// lines the boat up with the water's normal, and water drag. It drives by thrust a little below the centre of
    /// mass plus a steering torque that leans it into the turn.</para>
    /// </summary>
    public static class BoatFloat
    {
        /// <summary>How deep the hull bottom rides at rest, metres.</summary>
        public const float Draft = 0.15f;
        private const float ForceHeightOffset = -0.1f;
        private const float TurningHeel = 0.35f;
        private const float BrakeThrottle = -0.5f;
        private const float WetTolerance = 0.1f;    // keel this far above the water line still counts as touching it
        private const float MaxLift = 100f;
        private const float UprightTorque = 8f;
        private const float AngularDrag = 0.2f;

        /// <summary>What a <see cref="Step"/> found.</summary>
        public enum Water { InWater, OutOfWater, NoWaterInWorld }

        private static bool _warnedNoWater = false;

        /// <summary>
        /// One physics step against these trigger zones (the world's: <c>TriggerZone.Tracker.Instances</c>).
        /// <paramref name="centerToBottom"/> is the hull probe from <see cref="CenterToBottom"/>. InWater = the keel
        /// touches the water, the only time the boat may <see cref="Drive"/>: lift still eases a boat whose keel is
        /// just above the line, but a beached boat must not thrust itself up the shore. Out of the water the boat
        /// falls normally; with no Swim Zone in the world at all it holds still (no gravity) and warns once instead
        /// of sinking.
        /// </summary>
        public static Water Step(Rigidbody body, IList<TriggerZone> zones, float buoyancy, Vector3 drag, float centerToBottom)
        {
            Transform t = body.transform;
            if (!TrySwimSurface(t.position, zones, out float surface, out bool anySwimZone))
            {
                if (anySwimZone) { body.useGravity = true; return Water.OutOfWater; } // beached / airborne

                // Never touches isKinematic: that flag belongs to whoever decides which peer simulates.
                body.useGravity = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                if (!_warnedNoWater)
                {
                    _warnedNoWater = true;
                    Debug.LogWarning($"[Boat] '{body.name}' is a Boat but this world has no water (no Crest water, no Swim Zone). Add a Swim Zone over the water.", body);
                }
                return Water.NoWaterInWorld;
            }

            body.useGravity = true;
            surface += WaterWaves.Sample(t.position, out Vector3 waterUp);
            float depth = surface - t.position.y - centerToBottom;
            if (depth <= 0f) { return Water.OutOfWater; }

            body.AddForce(Vector3.ClampMagnitude(buoyancy * depth * depth * depth * Vector3.up, MaxLift), ForceMode.Acceleration);
            body.AddTorque(Vector3.Cross(t.up, waterUp) * UprightTorque, ForceMode.Acceleration);
            body.AddTorque(-AngularDrag * body.angularVelocity);
            Vector3 v = body.linearVelocity;
            body.AddForce(drag.x * Vector3.Dot(t.right, -v) * t.right
                + drag.y * Vector3.Dot(Vector3.up, -v) * Vector3.up
                + drag.z * Vector3.Dot(t.forward, -v) * t.forward, ForceMode.Acceleration);

            // The probe rides RestDepth - Draft under the keel (CenterToBottom).
            float keelDepth = depth - (RestDepth(buoyancy) - Draft);
            return keelDepth > -WetTolerance ? Water.InWater : Water.OutOfWater;
        }

        /// <summary>Engine and steering for one step. Only while <see cref="Step"/> returns InWater. Brake wins over accelerate and reverses at half thrust.</summary>
        public static void Drive(Rigidbody body, bool accelerate, bool brake, float turn, float enginePower, float turnPower)
        {
            Transform t = body.transform;
            float throttle = brake ? BrakeThrottle : accelerate ? 1f : 0f;
            body.AddForceAtPosition(enginePower * throttle * t.forward, body.worldCenterOfMass + ForceHeightOffset * Vector3.up, ForceMode.Acceleration);
            body.AddTorque(turnPower * turn * (t.up + TurningHeel * t.forward), ForceMode.Acceleration);
        }

        /// <summary>
        /// The water line under <paramref name="point"/>: the top of the highest Swim Zone trigger volume whose
        /// footprint contains it (and that it is not below). <paramref name="anySwimZone"/> = the world has one at all.
        /// </summary>
        public static bool TrySwimSurface(Vector3 point, IList<TriggerZone> zones, out float surface, out bool anySwimZone)
        {
            surface = float.MinValue;
            anySwimZone = false;
            bool found = false;
            for (int i = 0; zones != null && i < zones.Count; i++)
            {
                TriggerZone zone = zones[i];
                if (zone == null || zone.GetComponent<SwimZoneData>() == null) { continue; }
                Collider volume = zone.Collider != null ? zone.Collider : zone.GetComponent<Collider>();
                if (volume == null) { continue; }
                anySwimZone = true;
                Bounds b = volume.bounds;
                if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z || point.y < b.min.y) { continue; }
                if (b.max.y > surface) { surface = b.max.y; }
                found = true;
            }
            return found;
        }

        /// <summary>
        /// The hull probe offset so the hull floats <see cref="Draft"/> deep. Lift is buoyancy × depth³ (m/s²) at the
        /// probe, which balances gravity at depth ∛(g / buoyancy), so the probe sits that far below the water line.
        /// <paramref name="hullBottom"/> is world metres from the pivot (build-time pose, about upright).
        /// </summary>
        public static float CenterToBottom(float hullBottom, float buoyancy)
        {
            return hullBottom + Draft - RestDepth(buoyancy);
        }

        private static float RestDepth(float buoyancy)
        {
            return Mathf.Pow(-Physics.gravity.y / Mathf.Max(0.01f, buoyancy), 1f / 3f);
        }

        /// <summary>World renderer bounds of the boat. No renderers = a 1×1×2 hull centred on the pivot.</summary>
        public static Bounds HullBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { return new Bounds(root.position, new Vector3(1f, 1f, 2f)); }
            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) { world.Encapsulate(renderers[i].bounds); }
            return world;
        }
    }
}

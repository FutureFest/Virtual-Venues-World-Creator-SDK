using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// TestPlayer stand-in for the core project's boat body: a dynamic Rigidbody floated on its Swim Zone by the shared
    /// <see cref="BoatFloat"/> model, so it bobs, leans into turns and collides like the live boat. Added the first
    /// time the TestPlayer drives a <see cref="VehicleType.Boat"/>; it keeps floating after you get out, as the
    /// server keeps simulating an empty boat in the core project. Raises the vehicle's water events.
    /// </summary>
    [AddComponentMenu("")]
    public class TestBoat : MonoBehaviour
    {
        private Vehicle _vehicle = null;
        private Rigidbody _body = null;
        private float _centerToBottom = 0f;
        private bool _accelerate = false;
        private bool _brake = false;
        private float _turn = 0f;
        private bool _inWater = false;
        private bool _observed = false;

        public Rigidbody Body => _body;
        public float ForwardSpeed => Vector3.Dot(_body.linearVelocity, transform.forward);

        /// <summary>The boat's TestBoat, set up like the core project's boat body on first use.</summary>
        public static TestBoat Attach(Vehicle vehicle)
        {
            if (vehicle.TryGetComponent(out TestBoat boat)) { return boat; }
            boat = vehicle.gameObject.AddComponent<TestBoat>();
            boat._vehicle = vehicle;
            if (!vehicle.TryGetComponent(out boat._body)) { boat._body = vehicle.gameObject.AddComponent<Rigidbody>(); }

            BoatStats stats = vehicle.BoatStats ?? new BoatStats();
            Bounds hull = BoatFloat.HullBounds(vehicle.transform);
            boat._centerToBottom = BoatFloat.CenterToBottom(hull.min.y - vehicle.transform.position.y, stats.buoyancy);
            boat._body.mass = Mathf.Max(1f, vehicle.Stats.mass);
            boat._body.isKinematic = false;
            boat._body.useGravity = true;
            boat._body.interpolation = RigidbodyInterpolation.Interpolate;
            return boat;
        }

        public void SetInput(bool accelerate, bool brake, float turn)
        {
            _accelerate = accelerate;
            _brake = brake;
            _turn = turn;
        }

        private void FixedUpdate()
        {
            Step(TriggerZone.Tracker.Instances);
        }

        /// <summary>One physics step against these trigger zones (FixedUpdate passes the world's).</summary>
        public void Step(IList<TriggerZone> zones)
        {
            if (_vehicle == null || _body == null || _body.isKinematic) { return; }
            BoatStats stats = _vehicle.BoatStats ?? new BoatStats();
            bool inWater = BoatFloat.Step(_body, zones, stats.buoyancy, stats.drag, _centerToBottom) == BoatFloat.Water.InWater;
            if (inWater) { BoatFloat.Drive(_body, _accelerate, _brake, _turn, stats.enginePower, stats.turnPower); }

            if (inWater != _inWater && _observed) { (inWater ? _vehicle.Events.onEnterWater : _vehicle.Events.onExitWater).Invoke(); }
            _inWater = inWater;
            _observed = true;
        }
    }
}

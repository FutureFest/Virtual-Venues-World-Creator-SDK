using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>A one-shot <see cref="RideSounds"/> played. Append only.</summary>
    public enum RideSoundCue { BoosterEnter, BrakeEnter, StationStop, StationLaunch }

    /// <summary>A looping sound that follows the cart's speed inside a speed band.</summary>
    [Serializable]
    public class RideSpeedLayer
    {
        [Tooltip("A looping sound (wheels, wind, rumble). Loop is switched on for you.")]
        [SerializeField] private AudioSource _source = null;
        [Tooltip("Speed (m/s) where the band starts. It fades in from here to Min + Fade.")]
        [Min(0f)] [SerializeField] private float _minSpeed = 0f;
        [Tooltip("Speed (m/s) where the band ends. It fades out from here to Max + Fade.")]
        [Min(0f)] [SerializeField] private float _maxSpeed = 20f;
        [Tooltip("Width (m/s) of the fade at each edge of the band. 0 = hard on/off.")]
        [Min(0f)] [SerializeField] private float _fade = 2f;
        [Tooltip("Pitch at Min Speed.")]
        [Range(0.1f, 3f)] [SerializeField] private float _pitchAtMin = 0.8f;
        [Tooltip("Pitch at Max Speed.")]
        [Range(0.1f, 3f)] [SerializeField] private float _pitchAtMax = 1.3f;
        [Tooltip("Volume inside the band.")]
        [Range(0f, 1f)] [SerializeField] private float _maxVolume = 1f;

        public AudioSource Source => _source;
        public float MinSpeed => _minSpeed;
        public float MaxSpeed => _maxSpeed;
        public float Fade => _fade;
        public float PitchAtMin => _pitchAtMin;
        public float PitchAtMax => _pitchAtMax;
        public float MaxVolume => _maxVolume;

        public RideSpeedLayer() { }

        public RideSpeedLayer(AudioSource source, float minSpeed, float maxSpeed, float fade, float pitchAtMin, float pitchAtMax, float maxVolume = 1f)
        {
            _source = source;
            _minSpeed = minSpeed;
            _maxSpeed = maxSpeed;
            _fade = fade;
            _pitchAtMin = pitchAtMin;
            _pitchAtMax = pitchAtMax;
            _maxVolume = maxVolume;
        }

        /// <summary>Volume and pitch at <paramref name="speed"/> m/s. Same rule as the old FutureFest coaster cart.</summary>
        public void Evaluate(float speed, out float volume, out float pitch)
        {
            pitch = Mathf.Lerp(_pitchAtMin, _pitchAtMax, Mathf.InverseLerp(_minSpeed, _maxSpeed, speed));
            if (speed < RideSounds.StoppedSpeed || speed < _minSpeed || speed > _maxSpeed + _fade) { volume = 0f; return; }
            float v = 1f;
            if (_fade > 0f && speed < _minSpeed + _fade) { v = Mathf.InverseLerp(_minSpeed, _minSpeed + _fade, speed); }
            else if (_fade > 0f && speed > _maxSpeed) { v = Mathf.InverseLerp(_maxSpeed + _fade, _maxSpeed, speed); }
            volume = v * _maxVolume;
        }
    }

    /// <summary>
    /// Ride sounds driven by the <see cref="SplineMover"/> on this object, every frame: speed layers (wheels, wind)
    /// that fade and pitch with speed, a lift chain loop, and one-shots for boosters, brakes and station stops.
    /// Runs on every player's game from the shared ride clock, so no network traffic. A resync (late join, clock
    /// switch) plays no one-shots. Put it on each car you want heard (usually the lead car).
    /// </summary>
    // After SplineMover (-50), so it reads this frame's speed.
    [DefaultExecutionOrder(-30)]
    [RequireComponent(typeof(SplineMover))]
    public class RideSounds : MonoBehaviour
    {
        /// <summary>Below this speed (m/s) the cart counts as stopped: speed layers go silent.</summary>
        public const float StoppedSpeed = 0.05f;
        private const float LaunchSpeed = 0.2f;

        /// <summary>Raised when a one-shot cue plays (also when its sound is empty).</summary>
        public static event Action<RideSounds, RideSoundCue> OnCue;

        [Tooltip("Loops that fade in and out and change pitch with speed. Several bands can overlap (slow rumble, fast roar).")]
        [SerializeField] private List<RideSpeedLayer> _speedLayers = new List<RideSpeedLayer>();
        [Tooltip("Optional loop that plays only while the cart is in a Lift section (chain clack).")]
        [SerializeField] private AudioSource _lift = null;
        [Tooltip("Optional one-shot when the cart enters a Booster section.")]
        [SerializeField] private AudioSource _booster = null;
        [Tooltip("Optional one-shot when the cart enters a Brake section.")]
        [SerializeField] private AudioSource _brake = null;
        [Tooltip("Optional one-shot when the cart stops (station hold).")]
        [SerializeField] private AudioSource _stationStop = null;
        [Tooltip("Optional one-shot when the cart starts moving again (dispatch). Empty = play Station Stop again.")]
        [SerializeField] private AudioSource _stationLaunch = null;

        private SplineMover _mover = null;
        private bool _hasState = false;
        private TrackSection _prevSection = null;
        private bool _stopped = false;

        public List<RideSpeedLayer> SpeedLayers => _speedLayers;
        public AudioSource Lift => _lift;
        public AudioSource Booster => _booster;
        public AudioSource Brake => _brake;
        public AudioSource StationStop => _stationStop;
        public AudioSource StationLaunch => _stationLaunch;
        public SplineMover Mover => _mover != null ? _mover : _mover = GetComponent<SplineMover>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnCue = null;
        }

        private void OnEnable()
        {
            _hasState = false;
            foreach (RideSpeedLayer layer in _speedLayers)
            {
                if (layer.Source == null) { continue; }
                layer.Source.loop = true;
                layer.Source.volume = 0f;
            }
            if (_lift != null) { _lift.loop = true; }
        }

        private void Update()
        {
            Refresh();
        }

        /// <summary>Sets every sound from the mover's current state. Called each frame; call it after <see cref="SplineMover.Apply"/> from code.</summary>
        public void Refresh()
        {
            SplineMover mover = Mover;
            if (mover == null) { return; }
            float speed = mover.Speed;
            TrackSection section = mover.Section;

            foreach (RideSpeedLayer layer in _speedLayers)
            {
                AudioSource source = layer.Source;
                if (source == null) { continue; }
                layer.Evaluate(speed, out float volume, out float pitch);
                source.volume = volume;
                source.pitch = pitch;
                // Loops play from load; retried here so a source enabled later (or unlocked by the browser) starts too.
                if (!source.isPlaying && source.clip != null && source.isActiveAndEnabled) { source.Play(); }
            }

            if (_lift != null && _lift.isActiveAndEnabled)
            {
                bool onLift = section != null && section.Type == TrackSectionType.Lift && speed >= StoppedSpeed;
                if (onLift && !_lift.isPlaying && _lift.clip != null) { _lift.Play(); }
                else if (!onLift && _lift.isPlaying) { _lift.Stop(); }
            }

            // Hysteresis so a cart creeping into the station doesn't flicker between stopped and moving.
            bool stopped = _stopped ? speed < LaunchSpeed : speed < StoppedSpeed;
            bool quiet = !_hasState || mover.Resynced;
            if (!quiet)
            {
                if (section != null && section != _prevSection)
                {
                    if (section.Type == TrackSectionType.Booster) { Cue(RideSoundCue.BoosterEnter, _booster); }
                    else if (section.Type == TrackSectionType.Brake) { Cue(RideSoundCue.BrakeEnter, _brake); }
                }
                if (stopped && !_stopped) { Cue(RideSoundCue.StationStop, _stationStop); }
                else if (!stopped && _stopped) { Cue(RideSoundCue.StationLaunch, _stationLaunch != null ? _stationLaunch : _stationStop); }
            }
            _hasState = true;
            _prevSection = section;
            _stopped = stopped;
        }

        private void Cue(RideSoundCue cue, AudioSource source)
        {
            if (source != null && source.isActiveAndEnabled && source.clip != null)
            {
                source.Stop();
                source.Play();
            }
            OnCue?.Invoke(this, cue);
        }
    }
}

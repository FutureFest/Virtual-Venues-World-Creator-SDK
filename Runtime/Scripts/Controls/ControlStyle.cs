using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>When mouse movement turns the camera. Append only — the value is serialized.</summary>
    public enum LookActivation
    {
        [Tooltip("Turn the camera while a mouse button is held; the cursor is free otherwise (Roblox Classic, WoW).")]
        HoldButton = 0,
        [Tooltip("Click once to lock the cursor; the mouse always turns the camera. Ctrl frees the cursor (Fortnite).")]
        Locked = 1,
    }

    /// <summary>Which mouse buttons a <see cref="LookActivation.HoldButton"/> style listens to.</summary>
    [Flags]
    public enum LookButtons
    {
        Left = 1,
        Right = 2,
    }

    /// <summary>Which way the character faces. Append only — the value is serialized.</summary>
    public enum FacingMode
    {
        [Tooltip("Turn to face the way the character walks (Roblox Classic).")]
        FaceMoveDirection = 0,
        [Tooltip("Always face where the camera looks; sideways keys strafe (Fortnite).")]
        FaceCamera = 1,
        [Tooltip("Face where the camera looks only while the Facing button is held, otherwise face the walk direction (WoW right-drag).")]
        FaceCameraWhileHeld = 2,
    }

    public enum ControlMouseButton
    {
        Left = 0,
        Right = 1,
    }

    [Serializable]
    public class LookModule
    {
        public LookActivation activation = LookActivation.HoldButton;
        [Tooltip("Hold Button only: which buttons turn the camera.")]
        public LookButtons holdButtons = LookButtons.Left | LookButtons.Right;
        [Tooltip("Multiplies the player's mouse sensitivity.")]
        [Min(0.01f)] public float sensitivity = 1f;
        public bool invertY = false;
        [Tooltip("How far the camera can look up, in degrees.")]
        [Range(0f, 89f)] public float topClamp = 70f;
        [Tooltip("How far the camera can look down, in degrees (negative).")]
        [Range(-89f, 0f)] public float bottomClamp = -30f;
    }

    [Serializable]
    public class CameraModule
    {
        [Tooltip("Furthest the camera can zoom out, in metres.")]
        [Min(0.5f)] public float zoomMax = 12f;
        [Tooltip("Starting zoom, 0 = closest, 1 = furthest.")]
        [Range(0f, 1f)] public float defaultZoom = 0.3f;
        [Tooltip("Camera side: 0.5 = centred behind the character, 1 = over the right shoulder.")]
        [Range(0f, 1f)] public float shoulderSide = 0.6f;
        [Range(30f, 110f)] public float fieldOfView = 75f;
        [Tooltip("Let scrolling all the way in switch to first person.")]
        public bool allowFirstPerson = true;
    }

    [Serializable]
    public class FacingModule
    {
        public FacingMode mode = FacingMode.FaceMoveDirection;
        [Tooltip("Face Camera While Held only: the button that turns the character to the camera.")]
        public ControlMouseButton heldButton = ControlMouseButton.Right;
        [Tooltip("Seconds to turn toward the target direction.")]
        [Range(0f, 0.3f)] public float turnSmoothTime = 0.06f;
    }

    /// <summary>
    /// A camera + character control style: how the camera turns, how it frames the character, and which way the
    /// character faces. Data only, so creators can author their own and ship them in a world; the core project and
    /// the TestPlayer both run it through <see cref="ControlStyleMath"/>. Built-in presets live in
    /// Resources/ControlStyles (see <see cref="ControlStyles"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "ControlStyle", menuName = "Virtual Venues/Control Style")]
    public class ControlStyle : ScriptableObject
    {
        [Tooltip("Stable id saved in player settings and world layouts. Lowercase, no spaces. Never change it after publishing.")]
        [SerializeField] private string _id = "";
        [SerializeField] private string _displayName = "";
        [SerializeField] private LookModule _look = new LookModule();
        [SerializeField] private CameraModule _camera = new CameraModule();
        [SerializeField] private FacingModule _facing = new FacingModule();

        public string Id => string.IsNullOrEmpty(_id) ? name.ToLowerInvariant() : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public LookModule Look => _look;
        public CameraModule Camera => _camera;
        public FacingModule Facing => _facing;
    }
}

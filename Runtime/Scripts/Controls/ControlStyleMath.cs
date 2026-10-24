using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// The rules a <see cref="ControlStyle"/> describes, as pure functions. Shared by the core player and the
    /// TestPlayer so a style behaves the same in the Editor and live. Move direction and facing are separate:
    /// that is what lets a style strafe.
    /// </summary>
    public static class ControlStyleMath
    {
        /// <summary>World yaw (degrees) the character should walk toward for a WASD/stick input.</summary>
        public static float MoveYaw(Vector2 input, float cameraYaw)
        {
            return Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg + cameraYaw;
        }

        /// <summary>True while the style turns the character to face the camera.</summary>
        public static bool FacesCamera(FacingModule facing, bool leftHeld, bool rightHeld)
        {
            switch (facing.mode)
            {
                case FacingMode.FaceCamera: return true;
                case FacingMode.FaceCameraWhileHeld: return facing.heldButton == ControlMouseButton.Left ? leftHeld : rightHeld;
                default: return false;
            }
        }

        /// <summary>
        /// World yaw the character should turn toward, or false to keep its current facing
        /// (face-move-direction styles standing still).
        /// </summary>
        public static bool TryGetTargetYaw(FacingModule facing, Vector2 input, float cameraYaw, bool leftHeld, bool rightHeld, out float yaw)
        {
            if (FacesCamera(facing, leftHeld, rightHeld))
            {
                yaw = cameraYaw;
                return true;
            }

            yaw = MoveYaw(input, cameraYaw);
            return input != Vector2.zero;
        }

        /// <summary>True when mouse movement should turn the camera this frame.</summary>
        public static bool ShouldRotateCamera(LookModule look, bool leftHeld, bool rightHeld, bool cursorLocked)
        {
            if (look.activation == LookActivation.Locked) { return cursorLocked; }

            return ((look.holdButtons & LookButtons.Left) != 0 && leftHeld)
                || ((look.holdButtons & LookButtons.Right) != 0 && rightHeld);
        }

        public const float VehicleOrbitMinPitch = -15f;
        public const float VehicleOrbitMaxPitch = 60f;

        /// <summary>
        /// One frame of the driver camera's orbit (x = yaw, y = pitch, degrees, relative to behind the vehicle).
        /// Turns with the mouse while <paramref name="looking"/>; otherwise holds its angle, and eases back behind
        /// the vehicle by <paramref name="recenterStep"/> degrees while it moves forward.
        /// </summary>
        public static Vector2 StepVehicleOrbit(Vector2 orbit, Vector2 lookDelta, bool looking, bool movingForward, LookModule look, float recenterStep)
        {
            if (looking)
            {
                orbit.x = Mathf.DeltaAngle(0f, orbit.x + lookDelta.x * look.sensitivity);
                orbit.y += lookDelta.y * look.sensitivity * (look.invertY ? -1f : 1f);
            }
            else if (movingForward)
            {
                orbit.x = Mathf.MoveTowardsAngle(orbit.x, 0f, recenterStep);
                orbit.y = Mathf.MoveTowards(orbit.y, 0f, recenterStep);
            }
            orbit.y = Mathf.Clamp(orbit.y, VehicleOrbitMinPitch, VehicleOrbitMaxPitch);
            return orbit;
        }
    }
}

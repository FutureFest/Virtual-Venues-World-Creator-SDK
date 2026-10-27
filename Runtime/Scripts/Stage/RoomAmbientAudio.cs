using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Old name of <see cref="AmbientAudio"/>, with the same behaviour. Kept because asset bundles find a script
    /// by its class name, so published worlds that use it still load. Add Ambient Audio to new worlds.
    /// </summary>
    [Obsolete("Use AmbientAudio. RoomAmbientAudio stays only so published worlds keep loading.")]
    [AddComponentMenu("")]
    public sealed class RoomAmbientAudio : AmbientAudio { }
}

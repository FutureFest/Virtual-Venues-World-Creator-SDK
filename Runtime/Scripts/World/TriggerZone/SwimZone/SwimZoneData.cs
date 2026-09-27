using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Players inside this zone swim. Data only — the core project adds the swim behaviour.
    /// Put it next to a <see cref="TriggerZone"/> and a trigger collider covering the water volume.
    /// </summary>
    public class SwimZoneData : TriggerZoneData
    {
        public override TriggerZoneType ZoneType => TriggerZoneType.Swim;

#if UNITY_EDITOR
        [UnityEditor.MenuItem("GameObject/VirtualVenues/Swim Zone", isValidateFunction: false, priority: 0)]
        private static void CreateSwimZone(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("SwimZone", Vector3.zero);
        }
#endif
    }
}

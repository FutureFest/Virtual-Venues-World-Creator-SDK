using UnityEngine;

namespace VirtualVenues.Plugins.Nmkr
{
    /// <summary>
    /// Marks a string field as an NMKR project UID. The Editor draws it as a
    /// dropdown of the creator's NMKR projects (see NmkrProjectUidDrawer in the
    /// VV SDK Editor assembly). The attribute itself carries no data.
    /// </summary>
    public class NmkrProjectUidAttribute : PropertyAttribute { }
}

#if UNITY_EDITOR
namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// The GameObject > VirtualVenues create menu (also the Hierarchy right-click menu) in one place.
    /// Every create [MenuItem] uses a group path below, and a priority of its group's order plus its place in the
    /// group, so each group stays together and the groups show in this order. All priorities are negative (-50 to -2)
    /// so VirtualVenues sorts above Unity's Create Empty (0) at the top of the menu. Don't use -1 (Unity's "no
    /// priority"), and stay under 50 (Center On Children), or items drop out of the Hierarchy right-click menu.
    /// </summary>
    public static class CreateMenu
    {
        public const string Root = "GameObject/VirtualVenues/";

        public const string StageMenu = Root + "Stage/";
        public const string FixturesMenu = StageMenu + "Fixtures/";
        public const string WorldMenu = Root + "World/";
        public const string ZonesMenu = Root + "Zones/";
        public const string RidesMenu = Root + "Rides/";
        public const string VehiclesMenu = Root + "Vehicles/";
        public const string EventsMenu = Root + "Events/";
        public const string AvatarMenu = Root + "Avatar/";
        public const string PluginsMenu = Root + "Plugins/";

        private const int First = -50;

        public const int StageOrder = First;
        public const int WorldOrder = First + 10;
        public const int ZonesOrder = First + 20;
        public const int RidesOrder = First + 30;
        public const int VehiclesOrder = First + 35;
        public const int EventsOrder = First + 40;
        public const int AvatarOrder = First + 44;
        public const int PluginsOrder = First + 47;
    }
}
#endif

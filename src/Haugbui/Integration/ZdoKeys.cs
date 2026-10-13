namespace Haugbui.Integration
{
    /// <summary>Custom ZDO keys. Renaming one orphans the data saved under it.</summary>
    internal static class ZdoKeys
    {
        public const int SchemaVersion = 1;

        public static readonly int Kind = "haugbui_kind".GetStableHashCode();
        public static readonly int Version = "haugbui_ver".GetStableHashCode();
        public static readonly int Guid = "haugbui_guid".GetStableHashCode();
        public static readonly int TombGuid = "haugbui_tomb_guid".GetStableHashCode();
        public static readonly int CreatureState = "haugbui_state".GetStableHashCode();
        public static readonly int DarkAura = "haugbui_dark".GetStableHashCode();

        public const string TombState = "haugbui_tomb";
        public const string Carried = "haugbui_carried";
        public const string Overflow = "haugbui_overflow";
        public const string Requests = "haugbui_requests";
        public const string Job = "haugbui_job";
    }
}

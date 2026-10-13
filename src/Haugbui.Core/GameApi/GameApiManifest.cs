using System.Collections.Generic;
using System.Linq;

namespace Haugbui.Core.GameApi
{
    public enum MemberKind
    {
        Type,
        Field,
        Property,
        Method
    }

    public static class Features
    {
        public const string Core = "Core";
        public const string DayNight = "DayNight";
        public const string Containers = "Containers";
        public const string Smelters = "Production.Smelter";
        public const string Fermenters = "Production.Fermenter";
        public const string Cooking = "Production.Cooking";
        public const string Harvest = "Harvest";
        public const string RemoteWork = "RemoteWork";
    }

    public sealed class ApiMember
    {
        public ApiMember(string assembly, string type, string member, MemberKind kind, string feature, params string[] parameterTypes)
        {
            Assembly = assembly;
            Type = type;
            Member = member;
            Kind = kind;
            Feature = feature;
            ParameterTypes = parameterTypes;
        }

        public string Assembly { get; }
        public string Type { get; }
        public string Member { get; }
        public MemberKind Kind { get; }
        public string Feature { get; }

        /// <summary>Full type names of method parameters; empty means "any overload with this name".</summary>
        public IReadOnlyList<string> ParameterTypes { get; }

        public override string ToString() =>
            Kind == MemberKind.Type ? $"{Assembly}:{Type}" : $"{Assembly}:{Type}.{Member}({string.Join(",", ParameterTypes)})";
    }

    /// <summary>
    /// Every game member the mod depends on, checked by reflection at startup (and against the decompiled reference
    /// assemblies in tests). Add an entry whenever code starts using a new private member or RPC handler.
    /// </summary>
    public static class GameApiManifest
    {
        private const string Valheim = "assembly_valheim";
        private const string GuiUtils = "assembly_guiutils";

        public static IReadOnlyList<ApiMember> Members { get; } = new[]
        {
            M(Valheim, "ZNet", "instance", MemberKind.Property, Features.Core),
            M(Valheim, "ZNet", "GetTimeSeconds", MemberKind.Method, Features.Core),
            M(Valheim, "ZNet", "IsServer", MemberKind.Method, Features.Core),
            M(Valheim, "ZNetScene", "instance", MemberKind.Property, Features.Core),
            M(Valheim, "ZNetScene", "GetPrefab", MemberKind.Method, Features.Core, "System.String"),
            M(Valheim, "ZNetScene", "m_prefabs", MemberKind.Field, Features.Core),
            M(Valheim, "ObjectDB", "instance", MemberKind.Property, Features.Core),
            M(Valheim, "ObjectDB", "GetItemPrefab", MemberKind.Method, Features.Core, "System.String"),
            M(Valheim, "ObjectDB", "m_items", MemberKind.Field, Features.Core),
            M(Valheim, "ObjectDB", "m_recipes", MemberKind.Field, Features.Core),
            M(Valheim, "ZDOMan", "instance", MemberKind.Property, Features.Core),
            M(Valheim, "Version", "CurrentVersion", MemberKind.Property, Features.Core),
            M(GuiUtils, "Localization", "instance", MemberKind.Property, Features.Core),
            M(GuiUtils, "Localization", "Localize", MemberKind.Method, Features.Core, "System.String"),

            M(Valheim, "EnvMan", "instance", MemberKind.Property, Features.DayNight),
            M(Valheim, "EnvMan", "IsNight", MemberKind.Method, Features.DayNight),
            M(Valheim, "EnvMan", "IsDay", MemberKind.Method, Features.DayNight),
            M(Valheim, "EnvMan", "GetDayFraction", MemberKind.Method, Features.DayNight),
            M(Valheim, "EnvMan", "IsTimeSkipping", MemberKind.Method, Features.DayNight),
            M(Valheim, "EnvMan", "m_dayLengthSec", MemberKind.Field, Features.DayNight),

            M(Valheim, "Container", "m_inventory", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "IsInUse", MemberKind.Method, Features.Containers),
            M(Valheim, "Container", "Save", MemberKind.Method, Features.Containers),
            M(Valheim, "Inventory", "Save", MemberKind.Method, Features.Containers, "ZPackage"),
            M(Valheim, "Inventory", "Load", MemberKind.Method, Features.Containers, "ZPackage"),

            M(Valheim, "Smelter", "RPC_AddOre", MemberKind.Method, Features.Smelters),
            M(Valheim, "Smelter", "RPC_AddFuel", MemberKind.Method, Features.Smelters),
            M(Valheim, "Smelter", "RPC_EmptyProcessed", MemberKind.Method, Features.Smelters),
            M(Valheim, "Smelter", "m_conversion", MemberKind.Field, Features.Smelters),
            M(Valheim, "Smelter", "m_maxOre", MemberKind.Field, Features.Smelters),
            M(Valheim, "Smelter", "m_maxFuel", MemberKind.Field, Features.Smelters),
            M(Valheim, "Smelter", "m_fuelItem", MemberKind.Field, Features.Smelters),
            M(Valheim, "Smelter", "m_secPerProduct", MemberKind.Field, Features.Smelters),
            M(Valheim, "Fermenter", "RPC_AddItem", MemberKind.Method, Features.Fermenters),
            M(Valheim, "Fermenter", "RPC_Tap", MemberKind.Method, Features.Fermenters),
            M(Valheim, "Fermenter", "m_conversion", MemberKind.Field, Features.Fermenters),
            M(Valheim, "CookingStation", "RPC_AddItem", MemberKind.Method, Features.Cooking),
            M(Valheim, "CookingStation", "RPC_RemoveDoneItem", MemberKind.Method, Features.Cooking),
            M(Valheim, "CookingStation", "m_conversion", MemberKind.Field, Features.Cooking),
            M(Valheim, "Beehive", "RPC_Extract", MemberKind.Method, Features.Harvest),
            M(Valheim, "Pickable", "RPC_Pick", MemberKind.Method, Features.Harvest),
            M(Valheim, "Pickable", "m_harvestable", MemberKind.Field, Features.Harvest),
        };

        public static IEnumerable<string> FeaturesOf(IEnumerable<ApiMember> members) =>
            members.Select(m => m.Feature).Distinct();

        private static ApiMember M(string assembly, string type, string member, MemberKind kind, string feature, params string[] parameterTypes) =>
            new ApiMember(assembly, type, member, kind, feature, parameterTypes);
    }
}

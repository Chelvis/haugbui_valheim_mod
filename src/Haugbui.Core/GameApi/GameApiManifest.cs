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
        public const string Structures = "Structures";
        public const string Creature = "Creature";
        public const string Combat = "Combat";
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
            M(Valheim, "Smelter", "m_haveRoof", MemberKind.Field, Features.Smelters),
            M(Valheim, "Smelter", "m_blockedSmoke", MemberKind.Field, Features.Smelters),
            M(Valheim, "Smelter", "m_addOreSwitch", MemberKind.Field, Features.Smelters),
            M(Valheim, "ShieldGenerator", "RPC_AddFuel", MemberKind.Method, Features.Smelters),
            M(Valheim, "Character", "m_tamed", MemberKind.Field, Features.Creature),
            M(Valheim, "Character", "m_animator", MemberKind.Field, Features.Creature),
            M(Valheim, "ShieldGenerator", "m_fuelItems", MemberKind.Field, Features.Smelters),
            M(Valheim, "Switch", "Interact", MemberKind.Method, Features.Smelters),
            M(Valheim, "Switch", "GetHoverText", MemberKind.Method, Features.Smelters),
            M(Valheim, "Fermenter", "RPC_AddItem", MemberKind.Method, Features.Fermenters),
            M(Valheim, "Fermenter", "RPC_Tap", MemberKind.Method, Features.Fermenters),
            M(Valheim, "Fermenter", "m_conversion", MemberKind.Field, Features.Fermenters),
            M(Valheim, "Fermenter", "m_fermentationDuration", MemberKind.Field, Features.Fermenters),
            M(Valheim, "Fermenter", "m_roofCheckPoint", MemberKind.Field, Features.Fermenters),
            M(Valheim, "Fermenter", "m_outputPoint", MemberKind.Field, Features.Fermenters),
            M(Valheim, "Fermenter", "Interact", MemberKind.Method, Features.Fermenters),
            M(Valheim, "Fermenter", "GetHoverText", MemberKind.Method, Features.Fermenters),
            M(Valheim, "CookingStation", "RPC_AddItem", MemberKind.Method, Features.Cooking),
            M(Valheim, "CookingStation", "RPC_RemoveDoneItem", MemberKind.Method, Features.Cooking),
            M(Valheim, "CookingStation", "m_conversion", MemberKind.Field, Features.Cooking),
            M(Valheim, "CookingStation", "RPC_AddFuel", MemberKind.Method, Features.Cooking),
            M(Valheim, "CookingStation", "IsFireLit", MemberKind.Method, Features.Cooking),
            M(Valheim, "CookingStation", "m_slots", MemberKind.Field, Features.Cooking),
            M(Valheim, "CookingStation", "m_overCookedItem", MemberKind.Field, Features.Cooking),
            M(Valheim, "CookingStation", "Interact", MemberKind.Method, Features.Cooking),
            M(Valheim, "CookingStation", "GetHoverText", MemberKind.Method, Features.Cooking),
            M(Valheim, "Beehive", "RPC_Extract", MemberKind.Method, Features.Harvest),
            M(Valheim, "Pickable", "RPC_Pick", MemberKind.Method, Features.Harvest),
            M(Valheim, "Pickable", "m_harvestable", MemberKind.Field, Features.Harvest),

            M(Valheim, "Piece", "Awake", MemberKind.Method, Features.Structures),
            M(Valheim, "Piece", "m_resources", MemberKind.Field, Features.Structures),
            M(Valheim, "Player", "UpdatePlacementGhost", MemberKind.Method, Features.Structures),
            M(Valheim, "Player", "TryPlacePiece", MemberKind.Method, Features.Structures, "Piece"),
            M(Valheim, "Player", "SetPlacementGhostValid", MemberKind.Method, Features.Structures),
            M(Valheim, "Player", "m_placementGhost", MemberKind.Field, Features.Structures),
            M(Valheim, "Player", "m_placementStatus", MemberKind.Field, Features.Structures),
            M(Valheim, "Player", "GetPlayerID", MemberKind.Method, Features.Structures),
            M(Valheim, "Bed", "Awake", MemberKind.Method, Features.Structures),
            M(Valheim, "Bed", "GetOwner", MemberKind.Method, Features.Structures),
            M(Valheim, "Container", "GetHoverText", MemberKind.Method, Features.Structures),
            M(Valheim, "Container", "m_name", MemberKind.Field, Features.Structures),
            M(Valheim, "Inventory", "m_name", MemberKind.Field, Features.Structures),
            M(Valheim, "ZDO", "SetPrefab", MemberKind.Method, Features.Structures),
            M(Valheim, "ZDOMan", "m_objectsByID", MemberKind.Field, Features.Structures),
            M(Valheim, "TombStone", "m_floater", MemberKind.Field, Features.Structures),
            M(Valheim, "UnifiedPopup", "Push", MemberKind.Method, Features.Structures),
            M(Valheim, "UnifiedPopup", "Pop", MemberKind.Method, Features.Structures),
            M(Valheim, "WarningPopup", null, MemberKind.Type, Features.Structures),
            M(Valheim, "IPlaced", null, MemberKind.Type, Features.Structures),
            M(Valheim, "StationExtension", "m_connectionPrefab", MemberKind.Field, Features.Structures),
            M(Valheim, "ZoneSystem", "m_globalKeys", MemberKind.Field, Features.Structures),
            M(Valheim, "ZoneSystem", "m_globalKeysValues", MemberKind.Field, Features.Structures),

            M(Valheim, "BaseAI", "UpdateAI", MemberKind.Method, Features.Creature, "System.Single"),
            M(Valheim, "Character", "m_tamed", MemberKind.Field, Features.Creature),
            M(Valheim, "Character", "m_onDeath", MemberKind.Field, Features.Creature),
            M(Valheim, "Character", "m_deathEffects", MemberKind.Field, Features.Creature),
            M(Valheim, "Character", "m_flyFastSpeed", MemberKind.Field, Features.Creature),
            M(Valheim, "Character", "m_flySlowSpeed", MemberKind.Field, Features.Creature),
            M(Valheim, "Character", "SetMaxHealth", MemberKind.Method, Features.Creature),
            M(Valheim, "Character", "SetHealth", MemberKind.Method, Features.Creature),
            M(Valheim, "Character", "SetMoveDir", MemberKind.Method, Features.Creature),
            M(Valheim, "Container", "Interact", MemberKind.Method, Features.Creature),
            M(Valheim, "WearNTear", "m_onDestroyed", MemberKind.Field, Features.Creature),
            M(Valheim, "Inventory", "CanAddItem", MemberKind.Method, Features.Creature),
            M(Valheim, "Inventory", "FindFreeStackSpace", MemberKind.Method, Features.Creature),
            M(Valheim, "ItemDrop", "DropItem", MemberKind.Method, Features.Creature),
            M(Valheim, "Billboard", null, MemberKind.Type, Features.Creature),

            M(Valheim, "ZNetScene", "Awake", MemberKind.Method, Features.Containers),
            M(Valheim, "ZNetScene", "FindInstance", MemberKind.Method, Features.Containers, "ZDO"),
            M(Valheim, "ZNetScene", "InActiveArea", MemberKind.Method, Features.Containers, "UnityEngine.Vector3", "UnityEngine.Vector3"),
            M(Valheim, "ZoneSystem", "IsZoneLoaded", MemberKind.Method, Features.Containers, "UnityEngine.Vector3"),
            M(Valheim, "Container", "Awake", MemberKind.Method, Features.Containers),
            M(Valheim, "Container", "m_piece", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "m_lastRevision", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "m_privacy", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "m_wagon", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "m_rootObjectOverride", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "m_autoDestroyEmpty", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "m_inUse", MemberKind.Field, Features.Containers),
            M(Valheim, "Container", "GetInventory", MemberKind.Method, Features.Containers),
            M(Valheim, "Inventory", "RemoveItem", MemberKind.Method, Features.Containers, "ItemDrop+ItemData", "System.Int32"),
            M(Valheim, "Inventory", "GetAllItems", MemberKind.Method, Features.Containers),
            M(Valheim, "Inventory", "GetEmptySlots", MemberKind.Method, Features.Containers),
            M(Valheim, "Inventory", "AddItem", MemberKind.Method, Features.Containers,
                "ItemDrop+ItemData", "System.Int32", "System.Int32", "System.Int32", "System.Boolean"),
            M(Valheim, "Inventory", "GetItemAt", MemberKind.Method, Features.Containers, "System.Int32", "System.Int32"),
            M(Valheim, "Inventory", "GetWidth", MemberKind.Method, Features.Containers),
            M(Valheim, "Inventory", "GetHeight", MemberKind.Method, Features.Containers),
            M(Valheim, "Inventory", "Changed", MemberKind.Method, Features.Containers),
            M(Valheim, "Player", "m_localPlayerExists", MemberKind.Field, Features.Containers),
            M(Valheim, "Player", "IsMaterialKnown", MemberKind.Method, Features.Containers, "System.String"),
            M(Valheim, "Container", "CheckAccess", MemberKind.Method, Features.Containers, "System.Int64"),
            M(Valheim, "Container", "m_checkGuardStone", MemberKind.Field, Features.Containers),
            M(Valheim, "ZDO", "DataRevision", MemberKind.Property, Features.Containers),
            M(Valheim, "Piece", "GetCreator", MemberKind.Method, Features.Containers),
            M(Valheim, "Piece", "IsPlacedByPlayer", MemberKind.Method, Features.Containers),
            M(Valheim, "PrivateArea", "m_allAreas", MemberKind.Field, Features.Containers),
            M(Valheim, "PrivateArea", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "PrivateArea", "m_piece", MemberKind.Field, Features.Containers),
            M(Valheim, "PrivateArea", "IsEnabled", MemberKind.Method, Features.Containers),
            M(Valheim, "PrivateArea", "IsInside", MemberKind.Method, Features.Containers),
            M(Valheim, "PrivateArea", "IsPermitted", MemberKind.Method, Features.Containers),
            M(Valheim, "Incinerator", null, MemberKind.Type, Features.Containers),
            M(Valheim, "TombStone", null, MemberKind.Type, Features.Containers),
            M(Valheim, "Ship", null, MemberKind.Type, Features.Containers),
            M(Valheim, "ItemDrop", "s_instances", MemberKind.Field, Features.Containers),
            M(Valheim, "Character", "GetAllCharacters", MemberKind.Method, Features.Containers),
            M(Valheim, "Smelter", "Awake", MemberKind.Method, Features.Containers),
            M(Valheim, "Smelter", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "Fermenter", "Awake", MemberKind.Method, Features.Containers),
            M(Valheim, "Fermenter", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "CookingStation", "Awake", MemberKind.Method, Features.Containers),
            M(Valheim, "CookingStation", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "ShieldGenerator", "Start", MemberKind.Method, Features.Containers),
            M(Valheim, "ShieldGenerator", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "Pickable", "Awake", MemberKind.Method, Features.Containers),
            M(Valheim, "Pickable", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "Beehive", "Awake", MemberKind.Method, Features.Containers),
            M(Valheim, "Beehive", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "SapCollector", "Awake", MemberKind.Method, Features.Containers),
            M(Valheim, "SapCollector", "m_nview", MemberKind.Field, Features.Containers),
            M(Valheim, "Bed", "m_nview", MemberKind.Field, Features.Containers),

            M(Valheim, "ItemDrop", "m_nview", MemberKind.Field, Features.Harvest),
            M(Valheim, "ItemDrop", "GetTimeSinceSpawned", MemberKind.Method, Features.Harvest),
            M(Valheim, "ItemDrop", "IsPiece", MemberKind.Method, Features.Harvest),
            M(Valheim, "ItemDrop", "InTar", MemberKind.Method, Features.Harvest),
            M(Valheim, "ItemDrop", "Load", MemberKind.Method, Features.Harvest),
            M(Valheim, "ItemDrop", "SetStack", MemberKind.Method, Features.Harvest, "System.Int32"),
            M(Valheim, "ItemDrop", "CanPickup", MemberKind.Method, Features.Harvest, "System.Boolean"),
            M(Valheim, "ItemDrop", "RequestOwn", MemberKind.Method, Features.Harvest),
            M(Valheim, "Player", "GetAllPlayers", MemberKind.Method, Features.Harvest),
            M(Valheim, "Pickable", "RPC_Pick", MemberKind.Method, Features.Harvest, "System.Int64", "System.Int32"),
            M(Valheim, "Pickable", "GetPicked", MemberKind.Method, Features.Harvest),
            M(Valheim, "Pickable", "CanBePicked", MemberKind.Method, Features.Harvest),
            M(Valheim, "Pickable", "m_harvestable", MemberKind.Field, Features.Harvest),
            M(Valheim, "Pickable", "m_itemPrefab", MemberKind.Field, Features.Harvest),
            M(Valheim, "Pickable", "m_amount", MemberKind.Field, Features.Harvest),
            M(Valheim, "Pickable", "m_aggravateRange", MemberKind.Field, Features.Harvest),
            M(Valheim, "Beehive", "RPC_Extract", MemberKind.Method, Features.Harvest, "System.Int64"),
            M(Valheim, "Beehive", "GetHoneyLevel", MemberKind.Method, Features.Harvest),
            M(Valheim, "Beehive", "m_honeyItem", MemberKind.Field, Features.Harvest),
            M(Valheim, "SapCollector", "RPC_Extract", MemberKind.Method, Features.Harvest, "System.Int64"),
            M(Valheim, "SapCollector", "GetLevel", MemberKind.Method, Features.Harvest),
            M(Valheim, "SapCollector", "m_spawnItem", MemberKind.Field, Features.Harvest),

            M(Valheim, "Character", "RPC_Damage", MemberKind.Method, Features.Combat, "System.Int64", "HitData"),
            M(Valheim, "Character", "m_onDamaged", MemberKind.Field, Features.Combat),
            M(Valheim, "Character", "m_nview", MemberKind.Field, Features.Combat),
            M(Valheim, "Character", "GetFaction", MemberKind.Method, Features.Combat),
            M(Valheim, "Character", "IsBoss", MemberKind.Method, Features.Combat),
            M(Valheim, "Character", "IsTamed", MemberKind.Method, Features.Combat),
            M(Valheim, "Character", "GetBaseAI", MemberKind.Method, Features.Combat),
            M(Valheim, "Character", "GetRadius", MemberKind.Method, Features.Combat),
            M(Valheim, "Character", "GetCenterPoint", MemberKind.Method, Features.Combat),
            M(Valheim, "Character", "GetZDOID", MemberKind.Method, Features.Combat),
            M(Valheim, "Character", "SetLookDir", MemberKind.Method, Features.Combat),
            M(Valheim, "Character", "Damage", MemberKind.Method, Features.Combat, "HitData"),
            M(Valheim, "Humanoid", "StartAttack", MemberKind.Method, Features.Combat, "Character", "System.Boolean"),
            M(Valheim, "Humanoid", "GetCurrentWeapon", MemberKind.Method, Features.Combat),
            M(Valheim, "Humanoid", "EquipItem", MemberKind.Method, Features.Combat),
            M(Valheim, "Humanoid", "GetInventory", MemberKind.Method, Features.Combat),
            M(Valheim, "Humanoid", "InAttack", MemberKind.Method, Features.Combat),
            M(Valheim, "Humanoid", "m_unarmedWeapon", MemberKind.Field, Features.Combat),
            M(Valheim, "Attack", "ModifyDamage", MemberKind.Method, Features.Combat, "HitData", "System.Single"),
            M(Valheim, "Attack", "m_character", MemberKind.Field, Features.Combat),
            M(Valheim, "HitData", "m_damage", MemberKind.Field, Features.Combat),
            M(Valheim, "HitData", "GetTotalDamage", MemberKind.Method, Features.Combat),
            M(Valheim, "HitData", "SetAttacker", MemberKind.Method, Features.Combat),
            M(Valheim, "HitData+DamageTypes", "Modify", MemberKind.Method, Features.Combat, "System.Single"),
            M(Valheim, "ItemDrop+ItemData", "m_lastAttackTime", MemberKind.Field, Features.Combat),
            M(Valheim, "ItemDrop+ItemData+SharedData", "m_aiAttackRange", MemberKind.Field, Features.Combat),
            M(Valheim, "ItemDrop+ItemData+SharedData", "m_aiAttackInterval", MemberKind.Field, Features.Combat),
            M(Valheim, "ItemDrop+ItemData+SharedData", "m_aiAttackMaxAngle", MemberKind.Field, Features.Combat),
            M(Valheim, "ItemDrop+ItemData+SharedData", "m_damages", MemberKind.Field, Features.Combat),
            M(Valheim, "BaseAI", "IsEnemy", MemberKind.Method, Features.Combat, "Character", "Character"),
            M(Valheim, "BaseAI", "GetTargetCreature", MemberKind.Method, Features.Combat),
            M(Valheim, "BaseAI", "m_alertedEffects", MemberKind.Field, Features.Combat),
            M(Valheim, "MonsterAI", null, MemberKind.Type, Features.Combat),
            M(Valheim, "MonsterAI", "m_targetCreature", MemberKind.Field, Features.Combat),
            M(Valheim, "MonsterAI", "m_targetStatic", MemberKind.Field, Features.Combat),
            M(Valheim, "MonsterAI", "GetStaticTarget", MemberKind.Method, Features.Combat),
            M(Valheim, "MonsterAI", "m_attackPlayerObjects", MemberKind.Field, Features.Combat),
            M(Valheim, "BaseAI", "IsAlerted", MemberKind.Method, Features.Combat),
            M(Valheim, "BaseAI", "SetAlerted", MemberKind.Method, Features.Combat, "System.Boolean"),
            M(Valheim, "BaseAI", "m_lastFlee", MemberKind.Field, Features.Combat),
            M(Valheim, "BaseAI", "m_fleeInterval", MemberKind.Field, Features.Combat),
            M(Valheim, "Piece", "IsPlacedByPlayer", MemberKind.Method, Features.Combat),
            M(Valheim, "RandEventSystem", "instance", MemberKind.Property, Features.Combat),
            M(Valheim, "RandEventSystem", "GetCurrentRandomEvent", MemberKind.Method, Features.Combat),
            M(Valheim, "RandEventSystem", "SetRandomEventByName", MemberKind.Method, Features.Combat),
            M(Valheim, "RandEventSystem", "ResetRandomEvent", MemberKind.Method, Features.Combat),
            M(Valheim, "RandEventSystem", "m_events", MemberKind.Field, Features.Combat),
            M(Valheim, "RandomEvent", "m_pos", MemberKind.Field, Features.Combat),
            M(Valheim, "RandomEvent", "m_eventRange", MemberKind.Field, Features.Combat),
            M(Valheim, "RandomEvent", "m_name", MemberKind.Field, Features.Combat),
            M(Valheim, "YesNoPopup", null, MemberKind.Type, Features.Combat),
            M(Valheim, "UnifiedPopup", "IsAvailable", MemberKind.Method, Features.Combat),
        };

        public static IEnumerable<string> FeaturesOf(IEnumerable<ApiMember> members) =>
            members.Select(m => m.Feature).Distinct();

        private static ApiMember M(string assembly, string type, string member, MemberKind kind, string feature, params string[] parameterTypes) =>
            new ApiMember(assembly, type, member, kind, feature, parameterTypes);
    }
}

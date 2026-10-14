using System.Collections.Generic;
using BepInEx.Configuration;
using Haugbui.Core.Progression;
using Haugbui.Core.Recipes;
using Haugbui.Core.Tribute;
using Haugbui.Diagnostics;

namespace Haugbui.Config
{
    internal sealed class HaugbuiConfig
    {
        private const string General = "1 - General";
        private const string Progression = "2 - Progression";
        private const string Timing = "3 - Timing";
        private const string Death = "4 - Death";
        private const string Production = "5 - Production";
        private const string Construction = "6 - Construction";
        private const string Recipes = "7 - Recipes";
        private const string Combat = "8 - Combat";
        private const string AwayMode = "9 - Away mode";
        private const string Harvest = "10 - Harvest";
        private const string Tribute = "11 - Tribute";
        private const string Advanced = "99 - Advanced";

        private readonly ConfigFile _file;
        private readonly Dictionary<string, ConfigEntry<string>> _recipeEntries = new Dictionary<string, ConfigEntry<string>>();

        private ConfigEntry<string> _carryCapacity;
        private ConfigEntry<string> _maxSpeed;
        private ConfigEntry<string> _radius;
        private ConfigEntry<string> _slots;
        private ConfigEntry<string> _maxHealth;

        public HaugbuiConfig(ConfigFile file)
        {
            _file = file;
            Bind();
            ProgressionTable = BuildProgressionTable();
            TributeTable = BuildTributeTable();
        }

        public ProgressionTable ProgressionTable { get; private set; }
        public TributeTable TributeTable { get; private set; }

        public ConfigEntry<bool> DebugLogging { get; private set; }
        public ConfigEntry<string> EffectsQuality { get; private set; }
        public ConfigEntry<bool> StrictItemData { get; private set; }

        public ConfigEntry<float> DawnReturnLeadSeconds { get; private set; }
        public ConfigEntry<float> CollectionIntervalSeconds { get; private set; }
        public ConfigEntry<bool> SleepFastMode { get; private set; }

        public ConfigEntry<float> CooldownDays { get; private set; }
        public ConfigEntry<float> RestFullHealSeconds { get; private set; }

        public ConfigEntry<int> ProductionUnlockLevel { get; private set; }
        public ConfigEntry<float> JobTakeoverMinutes { get; private set; }

        public ConfigEntry<float> BedDistance { get; private set; }
        public ConfigEntry<float> TombSpacing { get; private set; }
        public ConfigEntry<float> TombSpacingSameOwner { get; private set; }
        public ConfigEntry<bool> TerritoryMode { get; private set; }
        public ConfigEntry<int> MaxHaugbuisPerPlayer { get; private set; }
        public ConfigEntry<float> UpgradeRange { get; private set; }

        public ConfigEntry<float> CombatDamageMultiplier { get; private set; }

        public ConfigEntry<bool> HarvestWildPickables { get; private set; }
        public ConfigEntry<bool> HarvestSapCollectors { get; private set; }

        public ConfigEntry<bool> TributeEnabled { get; private set; }
        public ConfigEntry<string> TributeOnTombDestroyed { get; private set; }
        public ConfigEntry<float> WorkCheckSeconds { get; private set; }
        private ConfigEntry<string> _tributeCosts;
        private ConfigEntry<string> _tributeValues;

        public bool RefundTribute => TributeOnTombDestroyed.Value != "Forfeit";

        public ConfigEntry<bool> AwayModeEnabled { get; private set; }
        public ConfigEntry<float> RemoteTickSeconds { get; private set; }

        public ConfigEntry<float> PlanningIntervalSeconds { get; private set; }
        public ConfigEntry<int> MaxTicksPerFrame { get; private set; }
        public ConfigEntry<float> ReservationLeaseSeconds { get; private set; }

        public string GetRecipeIngredients(string pieceId) =>
            _recipeEntries.TryGetValue(pieceId, out ConfigEntry<string> entry) ? entry.Value : null;

        public string GetDefaultRecipeIngredients(string pieceId) =>
            _recipeEntries.TryGetValue(pieceId, out ConfigEntry<string> entry) ? (string)entry.DefaultValue : null;

        private void Bind()
        {
            DebugLogging = _file.Bind(General, "DebugLogging", false, "Verbose Haugbui logging for troubleshooting.");
            DebugLogging.SettingChanged += (_, __) => Log.DebugEnabled = DebugLogging.Value;
            Log.DebugEnabled = DebugLogging.Value;
            EffectsQuality = _file.Bind(General, "EffectsQuality", "Normal",
                new ConfigDescription("Particle density of Haugbui effects.", new AcceptableValueList<string>("Low", "Normal")));
            StrictItemData = BindAdmin(General, "StrictItemData", true,
                "ON: the Haugbui only adds items to stacks with identical data (quality, world level, cheated flag, crafter, " +
                "custom data), so it never makes a clean item cheated or a cheated item clean. OFF: it stacks like a player " +
                "moving items (same item, quality and world level; a cheated item makes the stack cheated, the stack keeps its " +
                "own custom data), but it never clears a cheated flag.");
            Core.Storage.StackingRules.StrictItemData = StrictItemData.Value;
            StrictItemData.SettingChanged += (_, __) => Core.Storage.StackingRules.StrictItemData = StrictItemData.Value;

            _carryCapacity = BindLevels("CarryCapacity", ProgressionTable.ToCsv(ProgressionTable.DefaultCarryCapacity),
                "Carry weight per level 1..6.");
            _maxSpeed = BindLevels("MaxSpeed", ProgressionTable.ToCsv(ProgressionTable.DefaultMaxSpeed),
                "Maximum flight speed (m/s) per level 1..6.");
            _radius = BindLevels("OperationalRadius", ProgressionTable.ToCsv(ProgressionTable.DefaultRadius),
                "Work radius around the tomb (m) per level 1..6.");
            _slots = BindLevels("InventorySlots", ProgressionTable.ToCsv(ProgressionTable.DefaultSlots),
                "Carried inventory slots per level 1..6 (whole numbers).");
            _maxHealth = BindLevels("MaxHealth", ProgressionTable.ToCsv(ProgressionTable.DefaultMaxHealth),
                "Health per level 1..6.");

            DawnReturnLeadSeconds = BindAdmin(Timing, "DawnReturnLeadSeconds", 60f,
                "Real seconds before dawn when the Haugbui starts returning to its tomb.", 0f, 600f);
            CollectionIntervalSeconds = BindAdmin(Timing, "CollectionIntervalSeconds", 10f,
                "How often a working Haugbui checks Collection Chests.", 2f, 120f);
            SleepFastMode = BindAdmin(Timing, "SleepFastMode", true,
                "While the player sleeps, the Haugbui works the skipped night in fast mode.");

            CooldownDays = BindAdmin(Death, "CooldownDays", 1f,
                "In-game days before a dead Haugbui may return. Without the Altar it then waits for the next nightfall.", 0f, 30f);
            RestFullHealSeconds = BindAdmin(Death, "RestFullHealSeconds", 3600f,
                "World-time seconds for a resting Haugbui to heal from zero to full.", 1f, 86400f);

            ProductionUnlockLevel = BindAdmin(Production, "ProductionUnlockLevel", 2,
                "Tomb level that unlocks production automation.", ProgressionTable.MinLevel, ProgressionTable.MaxLevel);
            JobTakeoverMinutes = BindAdmin(Production, "JobTakeoverMinutes", 10f,
                "Minutes of world time after which another Haugbui may take over a station job whose tomb stopped working it.", 1f, 1440f);

            BedDistance = BindAdmin(Construction, "BedDistance", 50f,
                "Maximum distance between a tomb and a bed owned by the builder.", 5f, 500f);
            TombSpacing = BindAdmin(Construction, "TombSpacing", 5f, "Minimum distance between two tombs.", 0f, 100f);
            TombSpacingSameOwner = BindAdmin(Construction, "TombSpacingSameOwner", 100f,
                "Minimum distance between two tombs of the same player.", 0f, 1000f);
            TerritoryMode = BindAdmin(Construction, "TerritoryMode", true,
                "Where the areas of one player's tombs overlap, each chest, station, crop or ground item belongs only to the " +
                "nearest of those tombs; storage chests stay shared as sources for deliveries and production.");
            MaxHaugbuisPerPlayer = BindAdmin(Construction, "MaxHaugbuisPerPlayer", 3,
                "Servant slots per player and world (one tomb each). WARNING: many Haugbuis can cause performance problems and errors.",
                1, 10);
            UpgradeRange = BindAdmin(Construction, "UpgradeRange", 8f, "Maximum distance between an upgrade and its tomb.", 2f, 50f);

            foreach (PieceRecipe recipe in DefaultRecipes.All)
            {
                _recipeEntries[recipe.PieceId] = _file.Bind(Recipes, recipe.PieceId, recipe.IngredientsToConfig(),
                    AdminDescription("Ingredients as Item:Amount,Item:Amount (item prefab names). Invalid text uses the default."));
            }

            CombatDamageMultiplier = BindAdmin(Combat, "DamageMultiplier", 1f, "Multiplier for Haugbui attack damage.", 0.1f, 10f);

            HarvestWildPickables = BindAdmin(Harvest, "WildPickables", false,
                "Also pick wild plants (berries, mushrooms, thistle...), not only cultivated crops.");
            HarvestSapCollectors = BindAdmin(Harvest, "SapCollectors", false, "Also empty sap collectors.");

            TributeEnabled = BindAdmin(Tribute, "Enabled", true,
                "Each shift must be paid with valuables offered at the tomb. Off: the Haugbui works every night for free.");
            _tributeCosts = BindAdmin(Tribute, "CostPerLevel", TributeTable.CostsToCsv(TributeTable.DefaultCosts),
                "Cost of one shift in coin units for levels 1..6.");
            _tributeValues = BindAdmin(Tribute, "Values", TributeTable.ValuesToConfig(TributeTable.DefaultValues),
                "Valuables that can be offered and their worth in coin units, as Item:Value (item prefab names).");
            _tributeCosts.SettingChanged += (_, __) => TributeTable = BuildTributeTable();
            _tributeValues.SettingChanged += (_, __) => TributeTable = BuildTributeTable();
            TributeOnTombDestroyed = _file.Bind(Tribute, "OnTombDestroyed", "DropCoins",
                AdminDescription("What happens to the balance when a tomb is destroyed: dropped as Coins, or lost.",
                    new AcceptableValueList<string>("DropCoins", "Forfeit")));
            WorkCheckSeconds = BindAdmin(Tribute, "WorkCheckSeconds", 10f,
                "How often a dormant Haugbui looks for work (or tribute) during its shift.", 2f, 120f);

            AwayModeEnabled = BindAdmin(AwayMode, "Enabled", true,
                "Tombs keep sorting, delivering and refilling smelters while the player is far away.");
            RemoteTickSeconds = BindAdmin(AwayMode, "TickSeconds", 10f, "Interval of away-mode work.", 2f, 120f);

            PlanningIntervalSeconds = BindAdmin(Advanced, "PlanningIntervalSeconds", 1f,
                "How often each Haugbui plans its next task.", 0.25f, 10f);
            MaxTicksPerFrame = BindAdmin(Advanced, "MaxTicksPerFrame", 1,
                "Maximum Haugbui planning ticks per frame across all tombs.", 1, 10);
            ReservationLeaseSeconds = BindAdmin(Advanced, "ReservationLeaseSeconds", 30f,
                "How long a task reservation lives without renewal.", 5f, 300f);
        }

        private ConfigEntry<string> BindLevels(string key, string defaults, string description)
        {
            ConfigEntry<string> entry = _file.Bind(Progression, key, defaults, AdminDescription(description));
            entry.SettingChanged += (_, __) => ProgressionTable = BuildProgressionTable();
            return entry;
        }

        private TributeTable BuildTributeTable()
        {
            TributeTable table = TributeTable.FromConfig(_tributeCosts.Value, _tributeValues.Value, out List<string> warnings);
            foreach (string warning in warnings) Log.Warning("Config " + Tribute + ": " + warning);
            return table;
        }

        private ProgressionTable BuildProgressionTable()
        {
            ProgressionTable table = ProgressionTable.FromCsv(
                _carryCapacity.Value, _maxSpeed.Value, _radius.Value, _slots.Value, _maxHealth.Value,
                out IReadOnlyList<string> warnings);
            foreach (string warning in warnings)
                Log.Warning("Config " + Progression + ": " + warning);
            return table;
        }

        private ConfigEntry<float> BindAdmin(string section, string key, float value, string description, float min, float max) =>
            _file.Bind(section, key, value, AdminDescription(description, new AcceptableValueRange<float>(min, max)));

        private ConfigEntry<int> BindAdmin(string section, string key, int value, string description, int min, int max) =>
            _file.Bind(section, key, value, AdminDescription(description, new AcceptableValueRange<int>(min, max)));

        private ConfigEntry<bool> BindAdmin(string section, string key, bool value, string description) =>
            _file.Bind(section, key, value, AdminDescription(description));

        private ConfigEntry<string> BindAdmin(string section, string key, string value, string description) =>
            _file.Bind(section, key, value, AdminDescription(description));

        private static ConfigDescription AdminDescription(string description, AcceptableValueBase range = null) =>
            new ConfigDescription(description, range, new ConfigurationManagerAttributes { IsAdminOnly = true });
    }
}

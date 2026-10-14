using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using Haugbui.Config;
using Haugbui.Core.Scheduling;
using Haugbui.Diagnostics;
using Haugbui.Diagnostics.Commands;
using Haugbui.Integration;
using Haugbui.Structures;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Haugbui
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public sealed class HaugbuiPlugin : BaseUnityPlugin
    {
        // The GUID names the config file and the network handshake; changing it orphans existing configs.
        public const string PluginGuid = "com.kelvinmarques.haugbui";
        public const string PluginName = "Haugbui";
        // Keep in sync with HaugbuiVersion in Directory.Build.props.
        public const string PluginVersion = "0.0.6";

        private Harmony _harmony;
        private bool _piecesRegistered;

        internal static HaugbuiPlugin Instance { get; private set; }
        internal HaugbuiConfig Settings { get; private set; }
        internal TickScheduler Scheduler { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            Settings = new HaugbuiConfig(Config);

            Scheduler = new TickScheduler(stopwatchMs: () => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency)
            {
                MaxTicksPerFrame = Settings.MaxTicksPerFrame.Value
            };
            Settings.MaxTicksPerFrame.SettingChanged += (_, __) => Scheduler.MaxTicksPerFrame = Settings.MaxTicksPerFrame.Value;
            Scheduler.TickFailed += (handle, e) => Log.ErrorRateLimited("tick:" + handle.Name, $"Tick {handle.Name} failed: {e}");
            Coordination.Reservations.Table.LeaseSeconds = Settings.ReservationLeaseSeconds.Value;
            Settings.ReservationLeaseSeconds.SettingChanged += (_, __) => Coordination.Reservations.Table.LeaseSeconds = Settings.ReservationLeaseSeconds.Value;

            GameApiSelfCheck.Run();
            ApplyConstructionSettings();
            Settings.UpgradeRange.SettingChanged += (_, __) => ApplyConstructionSettings();
            Settings.BedDistance.SettingChanged += (_, __) => ApplyConstructionSettings();
            Settings.TombSpacing.SettingChanged += (_, __) => ApplyConstructionSettings();
            Settings.TombSpacingSameOwner.SettingChanged += (_, __) => ApplyConstructionSettings();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(HaugbuiPlugin).Assembly);

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
            if (GameApiSelfCheck.IsAvailable(Core.GameApi.Features.Creature)) Creature.CreaturePrefab.Register();
            else Log.Error("Haugbui creature disabled: required game members are missing.");
            ApplyEffectsQuality();
            Settings.EffectsQuality.SettingChanged += (_, __) => ApplyEffectsQuality();

            CommandManager.Instance.AddConsoleCommand(new StatusCommand());
            CommandManager.Instance.AddConsoleCommand(new DumpPrefabsCommand());
            CommandManager.Instance.AddConsoleCommand(new DumpTombCommand());
            CommandManager.Instance.AddConsoleCommand(new ReservationsCommand());
            CommandManager.Instance.AddConsoleCommand(new LedgerCommand());
            CommandManager.Instance.AddConsoleCommand(new RegistryCommand());
#if DEBUG
            CommandManager.Instance.AddConsoleCommand(new Spikes.TransferSaveSpikeCommand());
            CommandManager.Instance.AddConsoleCommand(new Spikes.RemoteWorkSpikeCommand());
            CommandManager.Instance.AddConsoleCommand(new Spikes.HostPrefabSpikeCommand());
            CommandManager.Instance.AddConsoleCommand(new Spikes.FlightSpikeCommand());
            Spikes.FlightSpike.RegisterPrefab();
            CommandManager.Instance.AddConsoleCommand(new SelfTest.SelfTestCommand());
            SelfTest.AutoTest.StartIfRequested(this);
#endif

            Log.Info($"{PluginName} {PluginVersion} loaded (Valheim {Version.GetVersionString()}, Jötunn {Jotunn.Main.Version}).");
        }

        private void Update()
        {
            Scheduler.Update(Time.timeAsDouble);
            StructureRegistry.Update();
            UI.TombPanel.Update();
            UI.DeliveryPanel.Update();
            UI.ProductionPanel.Update();
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
            _harmony?.UnpatchSelf();
        }

        private void OnVanillaPrefabsAvailable()
        {
            StartupValidation.Run(Settings);
            if (_piecesRegistered) return;
            _piecesRegistered = true;
            if (GameApiSelfCheck.IsAvailable(Core.GameApi.Features.Structures)) HostPrefabSystem.RegisterPieces();
            else Log.Error("Haugbui structures disabled: required game members are missing.");
        }

        private void ApplyEffectsQuality() => Creature.CreatureVisuals.LowQuality = Settings.EffectsQuality.Value == "Low";

        private void ApplyConstructionSettings()
        {
            StructureRegistry.UpgradeRange = Settings.UpgradeRange.Value;
            PlacementCheck.BedDistance = Settings.BedDistance.Value;
            PlacementCheck.TombSpacing = Settings.TombSpacing.Value;
            PlacementCheck.SameOwnerSpacing = Settings.TombSpacingSameOwner.Value;
        }
    }
}

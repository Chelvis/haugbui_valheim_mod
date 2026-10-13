using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using Haugbui.Config;
using Haugbui.Core.Scheduling;
using Haugbui.Diagnostics;
using Haugbui.Diagnostics.Commands;
using Haugbui.Integration;
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
        public const string PluginVersion = "0.0.1";

        private Harmony _harmony;

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

            GameApiSelfCheck.Run();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(HaugbuiPlugin).Assembly);

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;

            CommandManager.Instance.AddConsoleCommand(new StatusCommand());
            CommandManager.Instance.AddConsoleCommand(new DumpPrefabsCommand());
#if DEBUG
            CommandManager.Instance.AddConsoleCommand(new Spikes.TransferSaveSpikeCommand());
            CommandManager.Instance.AddConsoleCommand(new Spikes.RemoteWorkSpikeCommand());
            CommandManager.Instance.AddConsoleCommand(new Spikes.HostPrefabSpikeCommand());
#endif

            Log.Info($"{PluginName} {PluginVersion} loaded (Valheim {Version.GetVersionString()}, Jötunn {Jotunn.Main.Version}).");
        }

        private void Update()
        {
            Scheduler.Update(Time.timeAsDouble);
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
            _harmony?.UnpatchSelf();
        }

        private void OnVanillaPrefabsAvailable()
        {
            StartupValidation.Run(Settings);
        }
    }
}

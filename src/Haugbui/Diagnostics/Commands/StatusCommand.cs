using System.Linq;
using Haugbui.Integration;

namespace Haugbui.Diagnostics.Commands
{
    internal sealed class StatusCommand : HaugbuiCommand
    {
        public override string Name => "haugbui_status";
        public override string Help => "Haugbui: plugin, game API and recipe status.";

        protected override void Execute(string[] args)
        {
            Print($"Haugbui {HaugbuiPlugin.PluginVersion} on Valheim {Version.GetVersionString()} with Jötunn {Jotunn.Main.Version}");

            var api = GameApiSelfCheck.Report;
            Print(api.Passed
                ? $"Game API: OK ({api.CheckedCount} members)"
                : $"Game API: {api.Missing.Count} missing; disabled features: {string.Join(", ", api.DisabledFeatures)}");

            var recipes = StartupValidation.Results;
            if (recipes.Count == 0)
            {
                Print("Recipes: not validated yet (vanilla prefabs not loaded)");
            }
            else
            {
                foreach (var group in recipes.GroupBy(r => r.Value.Status).OrderBy(g => g.Key))
                    Print($"Recipes {group.Key}: {string.Join(", ", group.Select(r => r.Key))}");
            }

            var scheduler = HaugbuiPlugin.Instance.Scheduler;
            Print($"Scheduler: {scheduler.Handles.Count} ticks, max {scheduler.MaxTicksPerFrame} per frame");
            foreach (var handle in scheduler.Handles)
                Print($"  {handle.Name}: every {handle.Interval:0.##}s, runs {handle.RunCount}, last {handle.LastDurationMs:0.###} ms");

            Print($"Debug logging: {Log.DebugEnabled}");
        }
    }
}

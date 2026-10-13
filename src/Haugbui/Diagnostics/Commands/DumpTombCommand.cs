using System.Linq;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.Diagnostics.Commands
{
    internal sealed class DumpTombCommand : HaugbuiCommand
    {
        public override string Name => "haugbui_dump_tomb";
        public override string Help => "Haugbui: print the nearest tomb's saved state, level and bound upgrades.";

        protected override void Execute(string[] args)
        {
            Vector3 position = Player.m_localPlayer.transform.position;
            HaugbuiStructure tomb = StructureRegistry.Tombs
                .OrderBy(t => Vector3.Distance(t.transform.position, position))
                .FirstOrDefault();
            if (tomb == null)
            {
                Print("No loaded tomb.");
                return;
            }

            ZDO zdo = tomb.Zdo;
            Print($"Tomb {tomb.Guid} at {tomb.transform.position:F1}, {Vector3.Distance(tomb.transform.position, position):0.0} m away");
            Print($"  saved as prefab {ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name}, kind {zdo.GetInt(ZdoKeys.Kind)}, " +
                  $"schema {zdo.GetInt(ZdoKeys.Version)}");
            Print($"  level {tomb.Progress.Level}, abilities {tomb.Progress.Abilities}");

            var state = tomb.TombState;
            Print(state == null
                ? "  state: none or unreadable"
                : $"  owner {state.OwnerId}, toggles {state.Toggles}, health {state.Health}, death {state.DeathTime}, level cache {state.LevelCache}");

            foreach (HaugbuiStructure upgrade in StructureRegistry.Upgrades.Where(u => u.BoundTombGuid == tomb.Guid))
            {
                float distance = Vector3.Distance(upgrade.transform.position, tomb.transform.position);
                Print($"  upgrade {upgrade.Kind} {upgrade.Guid} at {distance:0.0} m" +
                      (distance > StructureRegistry.UpgradeRange ? " (out of range, not counted)" : ""));
            }
        }
    }
}

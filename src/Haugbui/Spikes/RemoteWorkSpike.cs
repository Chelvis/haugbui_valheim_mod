#if DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Haugbui.Diagnostics.Commands;
using UnityEngine;

namespace Haugbui.Spikes
{
    /// <summary>
    /// HB-T-090: edits a chest and a smelter through their ZDOs while their zone is not loaded, then checks the result
    /// when the player returns.
    /// </summary>
    internal sealed class RemoteWorkSpikeCommand : HaugbuiCommand
    {
        private const string StateFile = "haugbui_spike_remote.txt";
        private const string AddedItem = "Resin";
        private const int AddedAmount = 5;
        private const int AddedOre = 2;
        private const float AddedFuel = 4f;

        public override string Name => "haugbui_spike_remote";
        public override string Help => "SPIKE HB-T-090: 'mark' next to a chest and a smelter, 'apply' far away, 'verify' back at base, 'scan' anywhere.";
        public override bool IsCheat => true;

        protected override void Execute(string[] args)
        {
            switch (args.Length > 0 ? args[0] : "")
            {
                case "mark": Mark(); break;
                case "apply": Apply(); break;
                case "verify": Verify(); break;
                case "scan": Scan(); break;
                default: Print(Help); break;
            }
        }

        private void Mark()
        {
            Vector3 position = Player.m_localPlayer.transform.position;
            Container chest = SpikeUtils.PlayerBuiltContainersNear(position, 10f).FirstOrDefault();
            Smelter smelter = SpikeUtils.NearestComponent<Smelter>(position, 10f);
            if (chest == null || smelter == null || smelter.m_conversion.Count == 0)
            {
                Print("Stand within 10 m of a player-built chest and a smelter-type station.");
                return;
            }

            ZDO chestZdo = chest.m_nview.GetZDO();
            ZDO smelterZdo = smelter.m_nview.GetZDO();
            var state = new[]
            {
                SpikeUtils.Format(chestZdo.m_uid),
                SpikeUtils.Format(smelterZdo.m_uid),
                chest.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab(AddedItem).GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToString(),
                smelterZdo.GetInt(ZDOVars.s_queued).ToString(),
                smelterZdo.GetFloat(ZDOVars.s_fuel).ToString(System.Globalization.CultureInfo.InvariantCulture),
                smelter.m_conversion[0].m_from.name,
            };
            File.WriteAllLines(SpikeUtils.FilePath(StateFile), state);
            Print($"Marked chest {state[0]} ({state[2]} {AddedItem}) and {smelter.name} {state[1]} (queue {state[3]}, fuel {state[4]}). " +
                  "Now travel until both are unloaded (>300 m) and run 'apply'.");
        }

        private void Apply()
        {
            if (!TryLoadState(out string[] state, out ZDO chestZdo, out ZDO smelterZdo)) return;
            if (ZNetScene.instance.FindInstance(chestZdo) != null || ZNetScene.instance.FindInstance(smelterZdo) != null)
            {
                Print("Still loaded; move further away.");
                return;
            }

            GameObject chestPrefab = ZNetScene.instance.GetPrefab(chestZdo.GetPrefab());
            Container template = chestPrefab.GetComponent<Container>();
            var inventory = new Inventory(template.m_name, null, template.m_width, template.m_height);
            byte[] bytes = chestZdo.GetByteArray(ZDOVars.s_items);
            if (bytes != null) inventory.Load(new ZPackage(bytes));
            if (!inventory.AddItem(ObjectDB.instance.GetItemPrefab(AddedItem), AddedAmount))
            {
                Print("Chest full; empty a slot and retry.");
                return;
            }
            var package = new ZPackage();
            inventory.Save(package);
            chestZdo.Set(ZDOVars.s_items, package.GetArray());

            Smelter smelterTemplate = ZNetScene.instance.GetPrefab(smelterZdo.GetPrefab()).GetComponent<Smelter>();
            int queued = smelterZdo.GetInt(ZDOVars.s_queued);
            int toAdd = Mathf.Min(AddedOre, smelterTemplate.m_maxOre - queued);
            for (int i = 0; i < toAdd; i++)
                smelterZdo.Set("item" + (queued + i), state[5]);
            smelterZdo.Set(ZDOVars.s_queued, queued + toAdd);
            float fuel = Mathf.Min(smelterTemplate.m_maxFuel, smelterZdo.GetFloat(ZDOVars.s_fuel) + AddedFuel);
            smelterZdo.Set(ZDOVars.s_fuel, fuel);

            Print($"Applied remotely: +{AddedAmount} {AddedItem} in chest, +{toAdd} {state[5]} queued, fuel {fuel}. " +
                  "Run 'save', optionally quit and reload, then go back and run 'verify'.");
        }

        private void Verify()
        {
            if (!TryLoadState(out string[] state, out ZDO chestZdo, out ZDO smelterZdo)) return;
            ZNetView chestView = ZNetScene.instance.FindInstance(chestZdo);
            if (chestView == null)
            {
                Print("Chest not loaded; go back to the base.");
                return;
            }

            int resin = chestView.GetComponent<Container>().GetInventory()
                .CountItems(ObjectDB.instance.GetItemPrefab(AddedItem).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
            int expected = int.Parse(state[2]) + AddedAmount;
            Print(resin == expected ? $"PASS chest: {resin} {AddedItem}" : $"FAIL chest: expected {expected} got {resin}");
            Print($"Smelter now: queue {smelterZdo.GetInt(ZDOVars.s_queued)}, fuel {smelterZdo.GetFloat(ZDOVars.s_fuel)} " +
                  $"(was {state[3]} / {state[4]}). Watch it process the queued ore.");
        }

        private void Scan()
        {
            var stopwatch = Stopwatch.StartNew();
            var containers = new List<ZDO>();
            int prefabs = 0;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null || prefab.GetComponent<Container>() == null || prefab.GetComponent<Piece>() == null) continue;
                prefabs++;
                int index = 0;
                var batch = new List<ZDO>();
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab.name, batch, ref index)) { }
                containers.AddRange(batch);
            }
            stopwatch.Stop();
            Print($"Per-prefab scan: {containers.Count} container ZDOs across {prefabs} prefabs in {stopwatch.Elapsed.TotalMilliseconds:0.0} ms");

            var hashes = new HashSet<int>(ZNetScene.instance.m_prefabs
                .Where(p => p != null && p.GetComponent<Container>() != null && p.GetComponent<Piece>() != null)
                .Select(p => p.name.GetStableHashCode()));
            stopwatch.Restart();
            int singlePass = ZDOMan.instance.m_objectsByID.Values.Count(z => hashes.Contains(z.GetPrefab()));
            stopwatch.Stop();
            Print($"Single-pass scan: {singlePass} container ZDOs of {ZDOMan.instance.m_objectsByID.Count} total in {stopwatch.Elapsed.TotalMilliseconds:0.0} ms");
        }

        private bool TryLoadState(out string[] state, out ZDO chestZdo, out ZDO smelterZdo)
        {
            chestZdo = smelterZdo = null;
            string path = SpikeUtils.FilePath(StateFile);
            state = File.Exists(path) ? File.ReadAllLines(path) : null;
            if (state == null || state.Length < 6
                || !SpikeUtils.TryParseZdoId(state[0], out ZDOID chestId) || !SpikeUtils.TryParseZdoId(state[1], out ZDOID smelterId))
            {
                Print("Run 'mark' first.");
                return false;
            }
            chestZdo = ZDOMan.instance.GetZDO(chestId);
            smelterZdo = ZDOMan.instance.GetZDO(smelterId);
            if (chestZdo == null || smelterZdo == null)
            {
                Print("Marked objects no longer exist in the world data.");
                return false;
            }
            return true;
        }
    }
}
#endif

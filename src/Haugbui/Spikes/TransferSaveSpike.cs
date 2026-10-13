#if DEBUG
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Haugbui.Diagnostics;
using Haugbui.Diagnostics.Commands;
using UnityEngine;

namespace Haugbui.Spikes
{
    /// <summary>
    /// HB-T-009: moves items between nearby chests every frame (destination first) while forcing world saves, then
    /// checks after a reload that item totals did not drift.
    /// </summary>
    internal sealed class TransferSaveSpikeCommand : HaugbuiCommand
    {
        private const string StateFile = "haugbui_spike_transfer.txt";

        public override string Name => "haugbui_spike_transfer";
        public override string Help => "SPIKE HB-T-009: 'start [seconds] [movesPerFrame]' near 2+ chests, then 'verify' after reload.";
        public override bool IsCheat => true;

        protected override void Execute(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "";
            if (mode == "start") Start(args);
            else if (mode == "verify") Verify();
            else if (mode == "stop") Stop();
            else Print(Help);
        }

        private void Start(string[] args)
        {
            float seconds = args.Length > 1 && float.TryParse(args[1], out float s) ? s : 120f;
            int movesPerFrame = args.Length > 2 && int.TryParse(args[2], out int m) ? m : 5;

            List<Container> chests = SpikeUtils.PlayerBuiltContainersNear(Player.m_localPlayer.transform.position, 10f)
                .Where(c => c.m_nview.IsOwner() && !c.IsInUse()).ToList();
            if (chests.Count < 2 || chests.All(c => c.GetInventory().NrOfItems() == 0))
            {
                Print("Need at least 2 owned, closed chests within 10 m, with items in at least one.");
                return;
            }

            Dictionary<string, int> totals = SpikeUtils.Totals(chests.Select(c => c.GetInventory()));
            string ids = string.Join(",", chests.Select(c => SpikeUtils.Format(c.m_nview.GetZDO().m_uid)));
            File.WriteAllText(SpikeUtils.FilePath(StateFile), ids + "\n" + SpikeUtils.Format(totals) + "\n");

            GameObject host = HaugbuiPlugin.Instance.gameObject;
            TransferSaveSpikeRunner runner = host.GetComponent<TransferSaveSpikeRunner>();
            if (runner == null) runner = host.AddComponent<TransferSaveSpikeRunner>();
            runner.Begin(chests, totals, seconds, movesPerFrame);
            Print($"Spike started on {chests.Count} chests for {seconds}s, {movesPerFrame} moves/frame. Totals: {SpikeUtils.Format(totals)}");
        }

        private void Stop()
        {
            TransferSaveSpikeRunner runner = HaugbuiPlugin.Instance.gameObject.GetComponent<TransferSaveSpikeRunner>();
            if (runner != null) runner.End("stopped by command");
        }

        private void Verify()
        {
            string path = SpikeUtils.FilePath(StateFile);
            if (!File.Exists(path))
            {
                Print("No spike state file; run 'start' first.");
                return;
            }

            string[] lines = File.ReadAllLines(path);
            var inventories = new List<Inventory>();
            foreach (string text in lines[0].Split(','))
            {
                if (!SpikeUtils.TryParseZdoId(text, out ZDOID id)) continue;
                ZNetView view = ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(id));
                Container chest = view != null ? view.GetComponent<Container>() : null;
                if (chest == null)
                {
                    Print($"Chest {text} not loaded; stand next to the chests.");
                    return;
                }
                inventories.Add(chest.GetInventory());
            }

            string now = SpikeUtils.Format(SpikeUtils.Totals(inventories));
            Print(now == lines[1] ? "PASS: totals unchanged after reload." : $"FAIL: expected {lines[1]} got {now}");
        }
    }

    internal sealed class TransferSaveSpikeRunner : MonoBehaviour
    {
        private const float SaveEverySeconds = 5f;

        private List<Container> _chests;
        private Dictionary<string, int> _expected;
        private float _endTime;
        private float _nextSave;
        private int _movesPerFrame;
        private long _moves;
        private int _saves;
        private bool _running;

        public void Begin(List<Container> chests, Dictionary<string, int> expected, float seconds, int movesPerFrame)
        {
            _chests = chests;
            _expected = expected;
            _endTime = Time.time + seconds;
            _nextSave = Time.time + SaveEverySeconds;
            _movesPerFrame = movesPerFrame;
            _moves = 0;
            _saves = 0;
            _running = true;
        }

        public void End(string reason)
        {
            if (!_running) return;
            _running = false;
            string now = SpikeUtils.Format(SpikeUtils.Totals(_chests.Where(c => c != null).Select(c => c.GetInventory())));
            string expected = SpikeUtils.Format(_expected);
            Log.Info($"Transfer spike ended ({reason}): {_moves} moves, {_saves} saves, " +
                     (now == expected ? "totals OK" : $"DRIFT expected {expected} got {now}"));
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Spike ended: {_moves} moves, {_saves} saves");
        }

        private void Update()
        {
            if (!_running) return;
            if (_chests.Any(c => c == null))
            {
                End("a chest was unloaded or destroyed");
                return;
            }
            if (Time.time >= _endTime)
            {
                End("time");
                return;
            }

            for (int i = 0; i < _movesPerFrame; i++)
                MoveOne();

            if (Time.time >= _nextSave)
            {
                _nextSave = Time.time + SaveEverySeconds;
                ZNet.instance.Save(false);
                _saves++;
            }
        }

        private void MoveOne()
        {
            Container source = _chests[Random.Range(0, _chests.Count)];
            Container destination = _chests[Random.Range(0, _chests.Count)];
            if (source == destination || source.IsInUse() || destination.IsInUse()) return;

            List<ItemDrop.ItemData> items = source.GetInventory().GetAllItems();
            if (items.Count == 0) return;
            ItemDrop.ItemData item = items[Random.Range(0, items.Count)];
            int amount = Random.Range(1, item.m_stack + 1);

            ItemDrop.ItemData moving = item.Clone();
            moving.m_stack = amount;
            // AddItem can stack part of the amount and then fail without rollback, so check capacity first.
            if (!destination.GetInventory().CanAddItem(moving)) return;
            if (!destination.GetInventory().AddItem(moving))
            {
                Log.Error("Transfer spike: AddItem failed after CanAddItem succeeded");
                return;
            }
            source.GetInventory().RemoveItem(item, amount);
            _moves++;
        }
    }
}
#endif

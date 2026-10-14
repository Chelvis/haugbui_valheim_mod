using System.Linq;
using Haugbui.Coordination;
using Haugbui.Core.Coordination;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Registry;
using UnityEngine;

namespace Haugbui.Diagnostics.Commands
{
    internal sealed class ReservationsCommand : HaugbuiCommand
    {
        public override string Name => "haugbui_reservations";
        public override string Help => "Haugbui: active task reservations and each Haugbui's current task.";

        protected override void Execute(string[] args)
        {
            var leases = Reservations.Table.Leases;
            Print($"Reservations: {leases.Count}");
            double now = Time.timeAsDouble;
            foreach (Lease lease in leases) Print($"  {lease.Key} by {lease.Holder} ({lease.ExpiresAt - now:0}s left)");
            foreach (TombController tomb in TombController.All.Where(t => t != null && t.State != null))
            {
                Print($"Tomb {tomb.Guid}: {tomb.Machine.State}, task {tomb.Worker.Plan?.ToString() ?? "none"}, " +
                      $"carried {string.Join(", ", tomb.Carried.Stacks)}, waiting {string.Join(", ", tomb.Worker.NoDestination.Keys)}, " +
                      $"loaded {tomb.Worker.Coverage:P0}{(tomb.Worker.LastAbort != null ? ", last stop: " + tomb.Worker.LastAbort : "")}");
            }
        }
    }

    internal sealed class LedgerCommand : HaugbuiCommand
    {
        public override string Name => "haugbui_ledger";
        public override string Help => "Haugbui: the last item transfers (destination-first ledger).";

        protected override void Execute(string[] args)
        {
            TransferRecord[] records = Reservations.Ledger.ToArray();
            Print($"Last {records.Length} transfers (duplicates logged this session: {Reservations.DuplicatesLogged}):");
            foreach (TransferRecord record in records) Print("  " + record);
        }
    }

    internal sealed class RegistryCommand : HaugbuiCommand
    {
        public override string Name => "haugbui_registry";
        public override string Help => "Haugbui: world registry counts compared with a full scene scan.";

        protected override void Execute(string[] args)
        {
            string report = WorldRegistry.Verify(out bool ok);
            Print($"Registry {(ok ? "OK" : "MISMATCH")}: {report}");
        }
    }
}

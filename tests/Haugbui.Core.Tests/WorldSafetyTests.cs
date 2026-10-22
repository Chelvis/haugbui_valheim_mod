using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class WorldSafetyTests
    {
        [Test]
        public void Clean_world_does_not_warn_or_write()
        {
            var marker = WorldGuard.Read(null, null);

            Assert.That(WorldGuard.Compare(marker, 0, false), Is.EqualTo(WorldGuardVerdict.Clean));
            Assert.That(WorldGuard.ShouldWrite(marker, WorldGuardVerdict.Clean, 0), Is.False);
        }

        [Test]
        public void First_sight_of_tombs_records_a_baseline()
        {
            var marker = WorldGuard.Read(null, null);
            WorldGuardVerdict verdict = WorldGuard.Compare(marker, 3, false);

            Assert.That(verdict, Is.EqualTo(WorldGuardVerdict.Baseline));
            Assert.That(WorldGuard.ShouldWrite(marker, verdict, 3), Is.True);
        }

        [Test]
        public void Fewer_tombs_than_recorded_warns_and_keeps_the_count()
        {
            var marker = WorldGuard.Read("1", "4");
            WorldGuardVerdict verdict = WorldGuard.Compare(marker, 3, false);

            Assert.That(verdict, Is.EqualTo(WorldGuardVerdict.Missing));
            Assert.That(WorldGuard.ShouldWrite(marker, verdict, 3), Is.False);
        }

        [Test]
        public void Restoring_the_tomb_clears_the_shortfall()
        {
            var marker = WorldGuard.Read("1", "4");

            Assert.That(WorldGuard.Compare(marker, 4, false), Is.EqualTo(WorldGuardVerdict.Intact));
            Assert.That(WorldGuard.ShouldWrite(marker, WorldGuardVerdict.Intact, 4), Is.False);
        }

        [Test]
        public void A_broken_count_is_rewritten_without_a_warning()
        {
            var marker = WorldGuard.Read("1", "nope");
            WorldGuardVerdict verdict = WorldGuard.Compare(marker, 2, false);

            Assert.That(verdict, Is.EqualTo(WorldGuardVerdict.Baseline));
            Assert.That(WorldGuard.ShouldWrite(marker, verdict, 2), Is.True);
        }

        [Test]
        public void A_future_marker_schema_is_left_alone()
        {
            var marker = WorldGuard.Read("9", "2");

            Assert.That(marker.FutureSchema, Is.True);
            Assert.That(WorldGuard.Compare(marker, 2, false), Is.EqualTo(WorldGuardVerdict.Intact));
            Assert.That(WorldGuard.ShouldWrite(marker, WorldGuardVerdict.Intact, 2), Is.False);
            Assert.That(WorldGuard.Compare(marker, 0, false), Is.EqualTo(WorldGuardVerdict.Missing));
            Assert.That(WorldGuard.ShouldWrite(marker, WorldGuardVerdict.Missing, 0), Is.False);
        }

        [Test]
        public void Servant_slots_alone_are_haugbui_data()
        {
            var marker = WorldGuard.Read(null, null);

            Assert.That(WorldGuard.HasData(false, 0, true), Is.True);
            Assert.That(WorldGuard.Compare(marker, 0, true), Is.EqualTo(WorldGuardVerdict.Baseline));
        }

        [Test]
        public void Backup_stays_off_unless_asked_and_the_world_is_a_local_folder()
        {
            Assert.That(WorldBackup.Choose(false, true, true, false), Is.EqualTo(BackupDecision.Off));
            Assert.That(WorldBackup.Choose(true, false, true, false), Is.EqualTo(BackupDecision.NoData));
            Assert.That(WorldBackup.Choose(true, true, false, false), Is.EqualTo(BackupDecision.NotLocal));
            Assert.That(WorldBackup.Choose(true, true, true, true), Is.EqualTo(BackupDecision.Saving));
            Assert.That(WorldBackup.Choose(true, true, true, false), Is.EqualTo(BackupDecision.Copy));
        }

        [Test]
        public void Backup_folder_name_is_timestamped_and_file_safe()
        {
            string name = WorldBackup.FolderName("My World:1", new DateTime(2026, 10, 19, 23, 5, 6));

            Assert.That(name, Is.EqualTo("haugbui_backup_My World_1_20261019-230506"));
            Assert.That(name.IndexOfAny(Path.GetInvalidFileNameChars()), Is.LessThan(0));
        }

        [Test]
        public void Copy_matches_the_world_folder_and_stops_when_asked()
        {
            string root = Path.Combine(Path.GetTempPath(), "haugbui-backup-" + Guid.NewGuid().ToString("N"));
            string source = Path.Combine(root, "world");
            string dest = Path.Combine(root, "copy");
            try
            {
                Directory.CreateDirectory(Path.Combine(source, "chunks"));
                File.WriteAllText(Path.Combine(source, "world.fwl"), "meta");
                File.WriteAllBytes(Path.Combine(source, "chunks", "a.dat"), new byte[] { 1, 2, 3 });

                Assert.That(WorldBackup.CopyTree(source, dest, () => false), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(dest, "world.fwl")), Is.EqualTo("meta"));
                Assert.That(File.ReadAllBytes(Path.Combine(dest, "chunks", "a.dat")), Is.EqualTo(new byte[] { 1, 2, 3 }));

                string stopped = Path.Combine(root, "stopped");
                Assert.That(WorldBackup.CopyTree(source, stopped, () => true), Is.False);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void Readable_tomb_clamps_the_level_and_the_tribute()
        {
            var state = new TombState { LevelCache = 40, Balance = 3, CheatedBalance = 9, SlotIndex = -5, CreatureState = 99 };

            Assert.That(BlobIntegrity.RepairTomb(state), Is.True);
            Assert.That(state.LevelCache, Is.EqualTo(6));
            Assert.That(state.CheatedBalance, Is.EqualTo(3));
            Assert.That(state.SlotIndex, Is.EqualTo(-1));
            Assert.That(state.CreatureState, Is.EqualTo((int)HaugbuiState.Idle));
            Assert.That(BlobIntegrity.RepairTomb(state), Is.False);
        }

        [Test]
        public void A_request_ahead_of_itself_is_closed_instead_of_reopened()
        {
            var lines = new List<DeliveryRequest>
            {
                new DeliveryRequest { ItemPrefab = "Wood", Requested = 2, Delivered = 5 },
                new DeliveryRequest { ItemPrefab = "", Requested = 1, Delivered = 0 },
            };

            Assert.That(BlobIntegrity.RepairRequests(lines), Is.True);
            Assert.That(lines.Select(l => l.ItemPrefab + ":" + l.Requested + ":" + l.Delivered), Is.EqualTo(new[] { "Wood:5:5" }));
        }

        [Test]
        public void A_job_with_negative_progress_is_clamped()
        {
            var job = new StationJobState { Mode = (JobMode)9, Target = -3, Supplied = -1, Produced = 4, InputPrefab = "CopperOre" };

            Assert.That(BlobIntegrity.RepairJob(job), Is.True);
            Assert.That(job.Mode, Is.EqualTo(JobMode.Finite));
            Assert.That(job.Target, Is.EqualTo(0));
            Assert.That(job.Supplied, Is.EqualTo(0));
            Assert.That(job.Produced, Is.EqualTo(4));
        }

        [Test]
        public void Fuzzed_blobs_never_throw_and_are_not_treated_as_healthy()
        {
            var random = new Random(20261019);
            IBlobSerializer<TombState> tombs = TombStateSerializer.Instance;
            IBlobSerializer<List<DeliveryRequest>> requests = DeliveryRequestsSerializer.Instance;
            IBlobSerializer<StationJobState> jobs = StationJobSerializer.Instance;
            var buffer = new byte[64];
            for (int i = 0; i < 200; i++)
            {
                random.NextBytes(buffer);
                int length = random.Next(0, buffer.Length + 1);
                byte[] data = buffer.Take(length).ToArray();

                Assert.That(() => tombs.TryDeserialize(data, out _), Throws.Nothing);
                Assert.That(() => requests.TryDeserialize(data, out _), Throws.Nothing);
                Assert.That(() => jobs.TryDeserialize(data, out _), Throws.Nothing);

                BlobReadStatus status = tombs.TryDeserialize(data, out TombState state);
                if (status == BlobReadStatus.Ok) BlobIntegrity.RepairTomb(state);
                else Assert.That(status, Is.Not.EqualTo(BlobReadStatus.Ok));
            }
        }

        [Test]
        public void Corrupt_and_future_blobs_stay_disabled()
        {
            byte[] corrupt = { 9, 9, 9, 9 };
            byte[] future = new BlobWriter(99).Write("later").ToArray();

            Assert.That(TombStateSerializer.Instance.TryDeserialize(corrupt, out _), Is.EqualTo(BlobReadStatus.Corrupt));
            Assert.That(TombStateSerializer.Instance.TryDeserialize(future, out _), Is.EqualTo(BlobReadStatus.FutureVersion));
            Assert.That(DeliveryRequestsSerializer.Instance.TryDeserialize(corrupt, out _), Is.EqualTo(BlobReadStatus.Corrupt));
            Assert.That(StationJobSerializer.Instance.TryDeserialize(future, out _), Is.EqualTo(BlobReadStatus.FutureVersion));
        }
    }
}

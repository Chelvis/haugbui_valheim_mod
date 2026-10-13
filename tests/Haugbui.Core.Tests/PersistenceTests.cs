using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Persistence;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class PersistenceTests
    {
        private sealed class MemoryStore : IBlobStore
        {
            public readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
            public int Writes;

            public byte[] Get(string key) => Data.TryGetValue(key, out byte[] value) ? value : null;

            public void Set(string key, byte[] data)
            {
                Writes++;
                Data[key] = data;
            }
        }

        [Test]
        public void Tomb_state_round_trips()
        {
            var state = new TombState
            {
                Guid = "abc", OwnerId = 123456789012L, Health = 37.5f, DeathTime = 98765.25, LevelCache = 4,
                Toggles = TombToggles.Combat | TombToggles.CombatAcknowledged
            };

            TombStateSerializer.Instance.TryDeserialize(TombStateSerializer.Instance.Serialize(state), out TombState read);

            Assert.That(read.Guid, Is.EqualTo("abc"));
            Assert.That(read.OwnerId, Is.EqualTo(123456789012L));
            Assert.That(read.Health, Is.EqualTo(37.5f));
            Assert.That(read.DeathTime, Is.EqualTo(98765.25));
            Assert.That(read.LevelCache, Is.EqualTo(4));
            Assert.That(read.Toggles, Is.EqualTo(TombToggles.Combat | TombToggles.CombatAcknowledged));
        }

        [Test]
        public void New_tomb_defaults_follow_the_requirements()
        {
            var state = new TombState();

            Assert.That(state.Has(TombToggles.GroundItems), Is.True);
            Assert.That(state.Has(TombToggles.Harvesting), Is.True);
            Assert.That(state.Has(TombToggles.Combat), Is.False);
            Assert.That(double.IsNaN(state.DeathTime), Is.True);
        }

        [Test]
        public void Delivery_requests_round_trip()
        {
            var requests = new List<DeliveryRequest>
            {
                new DeliveryRequest { ItemPrefab = "Wood", Requested = 40, Delivered = 12 },
                new DeliveryRequest { ItemPrefab = "Resin", Requested = 20, Delivered = 20 },
            };

            DeliveryRequestsSerializer.Instance.TryDeserialize(DeliveryRequestsSerializer.Instance.Serialize(requests),
                out List<DeliveryRequest> read);

            Assert.That(read.Select(r => $"{r.ItemPrefab}:{r.Requested}:{r.Delivered}:{r.Remaining}"),
                Is.EqualTo(new[] { "Wood:40:12:28", "Resin:20:20:0" }));
        }

        [Test]
        public void Station_job_round_trips()
        {
            var job = new StationJobState
            {
                Enabled = true, InputPrefab = "IronOre", OutputPrefab = "Iron", Mode = JobMode.Infinite,
                Target = 100, Supplied = 7, Produced = 5, TombGuid = "t1"
            };

            StationJobSerializer.Instance.TryDeserialize(StationJobSerializer.Instance.Serialize(job), out StationJobState read);

            Assert.That(read.Enabled, Is.True);
            Assert.That(read.InputPrefab, Is.EqualTo("IronOre"));
            Assert.That(read.OutputPrefab, Is.EqualTo("Iron"));
            Assert.That(read.Mode, Is.EqualTo(JobMode.Infinite));
            Assert.That(new[] { read.Target, read.Supplied, read.Produced }, Is.EqualTo(new[] { 100, 7, 5 }));
            Assert.That(read.TombGuid, Is.EqualTo("t1"));
        }

        [TestCase(null, BlobReadStatus.Missing)]
        [TestCase(new byte[0], BlobReadStatus.Missing)]
        [TestCase(new byte[] { 1, 2, 3 }, BlobReadStatus.Corrupt)]
        [TestCase(new byte[] { 1, 2, 3, 4, 1, 0, 0, 0 }, BlobReadStatus.Corrupt)]
        public void Invalid_data_is_reported(byte[] data, BlobReadStatus expected)
        {
            Assert.That(TombStateSerializer.Instance.TryDeserialize(data, out _), Is.EqualTo(expected));
        }

        [Test]
        public void Truncated_payload_is_corrupt()
        {
            byte[] data = TombStateSerializer.Instance.Serialize(new TombState { Guid = "x" });

            Assert.That(TombStateSerializer.Instance.TryDeserialize(data.Take(data.Length - 3).ToArray(), out _),
                Is.EqualTo(BlobReadStatus.Corrupt));
        }

        [Test]
        public void Newer_schema_is_reported_as_future_version()
        {
            byte[] data = new BlobWriter(schemaVersion: 99).Write("guid").ToArray();

            Assert.That(TombStateSerializer.Instance.TryDeserialize(data, out _), Is.EqualTo(BlobReadStatus.FutureVersion));
        }

        [Test]
        public void Slot_refuses_to_write_before_reading()
        {
            var store = new MemoryStore();
            var slot = new PersistentSlot<TombState>(store, "haugbui_tomb", TombStateSerializer.Instance);

            Assert.That(slot.TrySave(new TombState()), Is.False);
            Assert.That(store.Writes, Is.Zero);
        }

        [Test]
        public void Slot_writes_after_reading_an_empty_key()
        {
            var store = new MemoryStore();
            var slot = new PersistentSlot<TombState>(store, "haugbui_tomb", TombStateSerializer.Instance);

            Assert.That(slot.Load(), Is.Null);
            Assert.That(slot.State, Is.EqualTo(SlotState.Empty));
            Assert.That(slot.TrySave(new TombState { Guid = "g" }), Is.True);
            Assert.That(slot.Load().Guid, Is.EqualTo("g"));
        }

        [Test]
        public void Corrupt_or_future_data_is_never_overwritten()
        {
            var store = new MemoryStore();
            byte[] future = new BlobWriter(schemaVersion: 2_000).Write("from the future").ToArray();
            store.Data["future"] = future;
            store.Data["corrupt"] = new byte[] { 9, 9, 9 };

            foreach (string key in new[] { "future", "corrupt" })
            {
                byte[] before = store.Data[key];
                var slot = new PersistentSlot<TombState>(store, key, TombStateSerializer.Instance);

                Assert.That(slot.Load(), Is.Null);
                Assert.That(slot.State, Is.EqualTo(SlotState.ReadOnly));
                Assert.That(slot.TrySave(new TombState()), Is.False);
                Assert.That(store.Data[key], Is.SameAs(before));
            }
        }

        private sealed class NameV2Serializer : IBlobSerializer<string>
        {
            public int CurrentVersion => 2;

            public void Write(string value, BlobWriter writer) => writer.Write(value).Write(value.Length);

            public string Read(BlobReader reader)
            {
                string name = reader.ReadString();
                if (reader.SchemaVersion >= 2) reader.ReadInt();
                return name;
            }
        }

        [Test]
        public void Older_schema_is_migrated_by_the_serializer()
        {
            byte[] v1 = new BlobWriter(schemaVersion: 1).Write("old").ToArray();

            Assert.That(new NameV2Serializer().TryDeserialize(v1, out string value), Is.EqualTo(BlobReadStatus.Ok));
            Assert.That(value, Is.EqualTo("old"));
        }

        [Test]
        public void Request_count_out_of_range_is_corrupt()
        {
            byte[] data = new BlobWriter(1).Write(-5).ToArray();

            Assert.That(DeliveryRequestsSerializer.Instance.TryDeserialize(data, out _), Is.EqualTo(BlobReadStatus.Corrupt));
        }
    }
}

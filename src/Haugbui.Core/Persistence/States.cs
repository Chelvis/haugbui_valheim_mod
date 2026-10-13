using System;
using System.Collections.Generic;

namespace Haugbui.Core.Persistence
{
    [Flags]
    public enum TombToggles
    {
        None = 0,
        GroundItems = 1 << 0,
        Harvesting = 1 << 1,
        Combat = 1 << 2,
        Production = 1 << 3,
        Resting = 1 << 4,
        CombatAcknowledged = 1 << 5,
        AwayMode = 1 << 6,

        Defaults = GroundItems | Harvesting | Production | AwayMode
    }

    public sealed class TombState
    {
        public string Guid { get; set; } = string.Empty;
        public long OwnerId { get; set; }
        public TombToggles Toggles { get; set; } = TombToggles.Defaults;

        /// <summary>Negative means "full health" (not damaged since the value was last set).</summary>
        public float Health { get; set; } = -1f;

        /// <summary>World time of the last death; NaN while alive.</summary>
        public double DeathTime { get; set; } = double.NaN;

        /// <summary>Display cache for when upgrades are unloaded; the real level is recomputed from upgrades.</summary>
        public int LevelCache { get; set; } = 1;

        /// <summary>Persisted creature state: Dormant, Idle (out) or Dead (see HaugbuiStateMachine.Persisted).</summary>
        public int CreatureState { get; set; }

        /// <summary>Last known creature position while out; NaN X when unknown.</summary>
        public float PositionX { get; set; } = float.NaN;
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        /// <summary>World time when <see cref="Health"/> was recorded, so dormant regeneration covers unloaded time.</summary>
        public double HealthTime { get; set; } = double.NaN;

        public bool Has(TombToggles toggle) => (Toggles & toggle) == toggle;

        public void Set(TombToggles toggle, bool on) => Toggles = on ? Toggles | toggle : Toggles & ~toggle;
    }

    public sealed class TombStateSerializer : IBlobSerializer<TombState>
    {
        public static readonly TombStateSerializer Instance = new TombStateSerializer();

        public int CurrentVersion => 2;

        public void Write(TombState value, BlobWriter writer)
        {
            writer.Write(value.Guid).Write(value.OwnerId).Write((int)value.Toggles).Write(value.Health)
                .Write(value.DeathTime).Write(value.LevelCache)
                .Write(value.CreatureState).Write(value.PositionX).Write(value.PositionY).Write(value.PositionZ)
                .Write(value.HealthTime);
        }

        public TombState Read(BlobReader reader)
        {
            var state = new TombState
            {
                Guid = reader.ReadString(),
                OwnerId = reader.ReadLong(),
                Toggles = (TombToggles)reader.ReadInt(),
                Health = reader.ReadFloat(),
                DeathTime = reader.ReadDouble(),
                LevelCache = reader.ReadInt(),
            };
            if (reader.SchemaVersion < 2) return state;
            state.CreatureState = reader.ReadInt();
            state.PositionX = reader.ReadFloat();
            state.PositionY = reader.ReadFloat();
            state.PositionZ = reader.ReadFloat();
            state.HealthTime = reader.ReadDouble();
            return state;
        }
    }

    public sealed class DeliveryRequest
    {
        public string ItemPrefab { get; set; } = string.Empty;
        public int Requested { get; set; }
        public int Delivered { get; set; }

        public int Remaining => Math.Max(0, Requested - Delivered);
    }

    public sealed class DeliveryRequestsSerializer : IBlobSerializer<List<DeliveryRequest>>
    {
        public static readonly DeliveryRequestsSerializer Instance = new DeliveryRequestsSerializer();

        public int CurrentVersion => 1;

        public void Write(List<DeliveryRequest> value, BlobWriter writer)
        {
            writer.Write(value.Count);
            foreach (DeliveryRequest request in value)
                writer.Write(request.ItemPrefab).Write(request.Requested).Write(request.Delivered);
        }

        public List<DeliveryRequest> Read(BlobReader reader)
        {
            int count = reader.ReadInt();
            if (count < 0 || count > 1000) throw new ArgumentException("request count out of range");
            var result = new List<DeliveryRequest>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(new DeliveryRequest
                {
                    ItemPrefab = reader.ReadString(),
                    Requested = reader.ReadInt(),
                    Delivered = reader.ReadInt(),
                });
            }
            return result;
        }
    }

    public enum JobMode
    {
        Finite,
        Infinite
    }

    public sealed class StationJobState
    {
        public bool Enabled { get; set; }
        public string InputPrefab { get; set; } = string.Empty;
        public string OutputPrefab { get; set; } = string.Empty;
        public JobMode Mode { get; set; } = JobMode.Finite;
        public int Target { get; set; }
        public int Supplied { get; set; }
        public int Produced { get; set; }
        public string TombGuid { get; set; } = string.Empty;
    }

    public sealed class StationJobSerializer : IBlobSerializer<StationJobState>
    {
        public static readonly StationJobSerializer Instance = new StationJobSerializer();

        public int CurrentVersion => 1;

        public void Write(StationJobState value, BlobWriter writer)
        {
            writer.Write(value.Enabled).Write(value.InputPrefab).Write(value.OutputPrefab).Write((int)value.Mode)
                .Write(value.Target).Write(value.Supplied).Write(value.Produced).Write(value.TombGuid);
        }

        public StationJobState Read(BlobReader reader) => new StationJobState
        {
            Enabled = reader.ReadBool(),
            InputPrefab = reader.ReadString(),
            OutputPrefab = reader.ReadString(),
            Mode = (JobMode)reader.ReadInt(),
            Target = reader.ReadInt(),
            Supplied = reader.ReadInt(),
            Produced = reader.ReadInt(),
            TombGuid = reader.ReadString(),
        };
    }
}

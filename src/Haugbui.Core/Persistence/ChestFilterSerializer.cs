using System;
using System.Collections.Generic;

namespace Haugbui.Core.Persistence
{
    /// <summary>One prefab the chest accepts. <see cref="MaxStacks"/> of 0 means the chest's own room is the only limit.</summary>
    public sealed class ChestFilterLine
    {
        public ChestFilterLine(string prefab, int maxStacks)
        {
            Prefab = prefab ?? string.Empty;
            MaxStacks = maxStacks;
        }

        public string Prefab { get; }
        public int MaxStacks { get; }
    }

    /// <summary>
    /// Chest filter blob. Version 1 is a list of prefab names, all unlimited.
    /// Version 2 stores a stack cap after each name. 0 is unlimited.
    /// </summary>
    public sealed class ChestFilterSerializer : IBlobSerializer<List<ChestFilterLine>>
    {
        public static readonly ChestFilterSerializer Instance = new ChestFilterSerializer();

        public int CurrentVersion => 2;

        public void Write(List<ChestFilterLine> value, BlobWriter writer)
        {
            List<ChestFilterLine> items = value ?? new List<ChestFilterLine>();
            writer.Write(items.Count);
            foreach (ChestFilterLine item in items)
                writer.Write(item.Prefab).Write(item.MaxStacks);
        }

        public List<ChestFilterLine> Read(BlobReader reader)
        {
            int count = reader.ReadInt();
            if (count < 0 || count > 256) throw new ArgumentException("chest filter count out of range");
            var result = new List<ChestFilterLine>(count);
            bool capped = reader.SchemaVersion >= 2;
            for (int i = 0; i < count; i++)
            {
                string prefab = reader.ReadString();
                int stacks = capped ? reader.ReadInt() : 0;
                if (stacks < 0) stacks = 0;
                result.Add(new ChestFilterLine(prefab, stacks));
            }
            return result;
        }
    }
}

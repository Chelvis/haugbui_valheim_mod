using System;
using System.Collections.Generic;

namespace Haugbui.Core.Persistence
{
    /// <summary>A short list of prefab names, used by the chest filter.</summary>
    public sealed class StringListSerializer : IBlobSerializer<List<string>>
    {
        public static readonly StringListSerializer Instance = new StringListSerializer();

        public int CurrentVersion => 1;

        public void Write(List<string> value, BlobWriter writer)
        {
            List<string> items = value ?? new List<string>();
            writer.Write(items.Count);
            foreach (string item in items) writer.Write(item);
        }

        public List<string> Read(BlobReader reader)
        {
            int count = reader.ReadInt();
            if (count < 0 || count > 256) throw new ArgumentException("string list count out of range");
            var result = new List<string>(count);
            for (int i = 0; i < count; i++) result.Add(reader.ReadString());
            return result;
        }
    }
}

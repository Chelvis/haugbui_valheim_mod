using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;

namespace Haugbui.Integration
{
    /// <summary>The prefab list on a vanilla chest. Missing and empty both mean "no filter".</summary>
    internal static class ChestFilterStore
    {
        public static List<ChestFilterLine> Read(ZDO zdo)
        {
            if (zdo == null) return new List<ChestFilterLine>();
            var slot = new PersistentSlot<List<ChestFilterLine>>(new ZdoBlobStore(zdo), ZdoKeys.Filter, ChestFilterSerializer.Instance);
            return ChestFilterRules.Normalize(slot.Load());
        }

        public static bool Write(ZDO zdo, IEnumerable<string> prefabs) =>
            Write(zdo, prefabs?.Select(name => new ChestFilterLine(name, 0)));

        public static bool Write(ZDO zdo, IEnumerable<ChestFilterLine> lines)
        {
            if (zdo == null) return false;
            var slot = new PersistentSlot<List<ChestFilterLine>>(new ZdoBlobStore(zdo), ZdoKeys.Filter, ChestFilterSerializer.Instance);
            slot.Load();
            return slot.TrySave(ChestFilterRules.Normalize(lines));
        }
    }
}

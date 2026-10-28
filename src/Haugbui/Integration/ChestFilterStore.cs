using System.Collections.Generic;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;

namespace Haugbui.Integration
{
    /// <summary>The prefab list on a vanilla chest. Missing and empty both mean "no filter".</summary>
    internal static class ChestFilterStore
    {
        public static List<string> Read(ZDO zdo)
        {
            if (zdo == null) return new List<string>();
            var slot = new PersistentSlot<List<string>>(new ZdoBlobStore(zdo), ZdoKeys.Filter, StringListSerializer.Instance);
            List<string> stored = slot.Load();
            return ChestFilterRules.Normalize(stored);
        }

        public static bool Write(ZDO zdo, IEnumerable<string> prefabs)
        {
            if (zdo == null) return false;
            var slot = new PersistentSlot<List<string>>(new ZdoBlobStore(zdo), ZdoKeys.Filter, StringListSerializer.Instance);
            slot.Load();
            return slot.TrySave(ChestFilterRules.Normalize(prefabs));
        }
    }
}

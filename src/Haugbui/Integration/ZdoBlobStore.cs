using Haugbui.Core.Persistence;

namespace Haugbui.Integration
{
    internal sealed class ZdoBlobStore : IBlobStore
    {
        private readonly ZDO _zdo;

        public ZdoBlobStore(ZDO zdo) => _zdo = zdo;

        public byte[] Get(string key) => _zdo.GetByteArray(key.GetStableHashCode());

        public void Set(string key, byte[] data) => _zdo.Set(key.GetStableHashCode(), data);
    }
}

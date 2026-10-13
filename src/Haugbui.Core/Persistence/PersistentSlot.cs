namespace Haugbui.Core.Persistence
{
    public interface IBlobStore
    {
        byte[] Get(string key);
        void Set(string key, byte[] data);
    }

    public enum SlotState
    {
        Unread,
        Empty,
        Loaded,
        ReadOnly
    }

    /// <summary>
    /// One versioned blob under one key. Writes are refused until the key was read successfully, and forever when the
    /// stored blob is corrupt or from a newer schema, so a failed or partial load can never overwrite saved data.
    /// </summary>
    public sealed class PersistentSlot<T>
    {
        private readonly IBlobStore _store;
        private readonly IBlobSerializer<T> _serializer;

        public PersistentSlot(IBlobStore store, string key, IBlobSerializer<T> serializer)
        {
            _store = store;
            Key = key;
            _serializer = serializer;
        }

        public string Key { get; }
        public SlotState State { get; private set; } = SlotState.Unread;
        public BlobReadStatus LastReadStatus { get; private set; }

        public bool CanWrite => State == SlotState.Empty || State == SlotState.Loaded;

        /// <returns>The stored value, or default when missing, corrupt or from a newer schema.</returns>
        public T Load()
        {
            LastReadStatus = _serializer.TryDeserialize(_store.Get(Key), out T value);
            switch (LastReadStatus)
            {
                case BlobReadStatus.Ok:
                    State = SlotState.Loaded;
                    return value;
                case BlobReadStatus.Missing:
                    State = SlotState.Empty;
                    return default;
                default:
                    State = SlotState.ReadOnly;
                    return default;
            }
        }

        public bool TrySave(T value)
        {
            if (!CanWrite) return false;
            _store.Set(Key, _serializer.Serialize(value));
            State = SlotState.Loaded;
            return true;
        }
    }
}

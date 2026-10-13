using System;
using System.IO;
using System.Text;

namespace Haugbui.Core.Persistence
{
    public sealed class BlobWriter
    {
        private readonly MemoryStream _stream = new MemoryStream();
        private readonly BinaryWriter _writer;

        public BlobWriter(int schemaVersion)
        {
            _writer = new BinaryWriter(_stream, Encoding.UTF8);
            _writer.Write(Blob.Magic);
            _writer.Write(schemaVersion);
        }

        public BlobWriter Write(int value) { _writer.Write(value); return this; }
        public BlobWriter Write(long value) { _writer.Write(value); return this; }
        public BlobWriter Write(float value) { _writer.Write(value); return this; }
        public BlobWriter Write(double value) { _writer.Write(value); return this; }
        public BlobWriter Write(bool value) { _writer.Write(value); return this; }
        public BlobWriter Write(string value) { _writer.Write(value ?? string.Empty); return this; }

        public byte[] ToArray()
        {
            _writer.Flush();
            return _stream.ToArray();
        }
    }

    public sealed class BlobReader
    {
        private readonly BinaryReader _reader;

        internal BlobReader(BinaryReader reader, int schemaVersion)
        {
            _reader = reader;
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }

        public int ReadInt() => _reader.ReadInt32();
        public long ReadLong() => _reader.ReadInt64();
        public float ReadFloat() => _reader.ReadSingle();
        public double ReadDouble() => _reader.ReadDouble();
        public bool ReadBool() => _reader.ReadBoolean();
        public string ReadString() => _reader.ReadString();
    }

    public enum BlobReadStatus
    {
        Missing,
        Ok,
        FutureVersion,
        Corrupt
    }

    public static class Blob
    {
        internal const int Magic = 0x48424742; // "HBGB"

        /// <summary>Validates the header; the reader is positioned on the payload when the status is Ok or FutureVersion.</summary>
        public static BlobReadStatus Open(byte[] data, int currentVersion, out BlobReader reader)
        {
            reader = null;
            if (data == null || data.Length == 0) return BlobReadStatus.Missing;
            try
            {
                var binary = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8);
                if (binary.ReadInt32() != Magic) return BlobReadStatus.Corrupt;
                int version = binary.ReadInt32();
                if (version <= 0) return BlobReadStatus.Corrupt;
                reader = new BlobReader(binary, version);
                return version > currentVersion ? BlobReadStatus.FutureVersion : BlobReadStatus.Ok;
            }
            catch (EndOfStreamException)
            {
                return BlobReadStatus.Corrupt;
            }
        }
    }

    public interface IBlobSerializer<T>
    {
        int CurrentVersion { get; }
        void Write(T value, BlobWriter writer);

        /// <summary>Reads any version from 1 to <see cref="CurrentVersion"/>, migrating older layouts.</summary>
        T Read(BlobReader reader);
    }

    public static class BlobSerializerExtensions
    {
        public static byte[] Serialize<T>(this IBlobSerializer<T> serializer, T value)
        {
            var writer = new BlobWriter(serializer.CurrentVersion);
            serializer.Write(value, writer);
            return writer.ToArray();
        }

        public static BlobReadStatus TryDeserialize<T>(this IBlobSerializer<T> serializer, byte[] data, out T value)
        {
            value = default;
            BlobReadStatus status = Blob.Open(data, serializer.CurrentVersion, out BlobReader reader);
            if (status != BlobReadStatus.Ok) return status;
            try
            {
                value = serializer.Read(reader);
                return BlobReadStatus.Ok;
            }
            catch (Exception e) when (e is EndOfStreamException || e is IOException || e is ArgumentException)
            {
                value = default;
                return BlobReadStatus.Corrupt;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace Haugbui.Core.Geometry
{
    /// <summary>
    /// Uniform grid over the horizontal plane for objects that do not move (HB-T-041). Queries fill a caller-owned list,
    /// so they do not allocate.
    /// </summary>
    public sealed class SpatialHash<T> where T : class
    {
        private readonly struct Entry
        {
            public Entry(T item, Vec3 position, long cell)
            {
                Item = item;
                Position = position;
                Cell = cell;
            }

            public T Item { get; }
            public Vec3 Position { get; }
            public long Cell { get; }
        }

        private readonly Dictionary<long, List<Entry>> _cells = new Dictionary<long, List<Entry>>();
        private readonly Dictionary<T, Entry> _entries = new Dictionary<T, Entry>();

        public SpatialHash(float cellSize = 16f)
        {
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
            CellSize = cellSize;
        }

        public float CellSize { get; }
        public int Count => _entries.Count;
        public IEnumerable<T> Items => _entries.Keys;

        public bool Contains(T item) => _entries.ContainsKey(item);

        public bool TryGetPosition(T item, out Vec3 position)
        {
            bool found = _entries.TryGetValue(item, out Entry entry);
            position = found ? entry.Position : Vec3.Zero;
            return found;
        }

        /// <summary>Adds the item, or moves it when it is already indexed.</summary>
        public void Add(T item, Vec3 position)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            Remove(item);
            long cell = CellOf(position.X, position.Z);
            if (!_cells.TryGetValue(cell, out List<Entry> list))
            {
                list = new List<Entry>();
                _cells[cell] = list;
            }
            var entry = new Entry(item, position, cell);
            list.Add(entry);
            _entries[item] = entry;
        }

        public bool Remove(T item)
        {
            if (item == null || !_entries.TryGetValue(item, out Entry entry)) return false;
            _entries.Remove(item);
            List<Entry> list = _cells[entry.Cell];
            for (int i = 0; i < list.Count; i++)
            {
                if (!ReferenceEquals(list[i].Item, item)) continue;
                list[i] = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                break;
            }
            if (list.Count == 0) _cells.Remove(entry.Cell);
            return true;
        }

        /// <summary>Removes every entry the predicate rejects (e.g. destroyed objects); returns how many.</summary>
        public int Prune(Func<T, bool> keep)
        {
            var dead = new List<T>();
            foreach (T item in _entries.Keys)
                if (!keep(item)) dead.Add(item);
            foreach (T item in dead) Remove(item);
            return dead.Count;
        }

        public void Clear()
        {
            _cells.Clear();
            _entries.Clear();
        }

        /// <summary>Appends every item within <paramref name="radius"/> (3D distance) of <paramref name="center"/>.</summary>
        public void Query(Vec3 center, float radius, List<T> results)
        {
            if (radius < 0) return;
            float radiusSq = radius * radius;
            int minX = Coord(center.X - radius), maxX = Coord(center.X + radius);
            int minZ = Coord(center.Z - radius), maxZ = Coord(center.Z + radius);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!_cells.TryGetValue(Key(x, z), out List<Entry> list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Vec3 p = list[i].Position;
                        float dx = p.X - center.X, dy = p.Y - center.Y, dz = p.Z - center.Z;
                        if (dx * dx + dy * dy + dz * dz <= radiusSq) results.Add(list[i].Item);
                    }
                }
            }
        }

        private int Coord(float value) => (int)Math.Floor(value / CellSize);

        private long CellOf(float x, float z) => Key(Coord(x), Coord(z));

        private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    }
}

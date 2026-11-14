using System;
using System.Collections;
using System.Collections.Generic;

namespace Fluorine
{
    /// <summary>
    /// An ordered list published as immutable versions.
    /// <see cref="Value"/>, <see cref="Count"/>, <see cref="IndexOf"/> and enumeration subscribe to every change.
    /// The indexer getter subscribes to the value at that position, including a position past the end.
    /// <see cref="Contains"/> subscribes to whether that value occurs at least once.
    /// A second copy does not change presence, and removing one of several copies does not either.
    /// Insert and <see cref="RemoveAt"/> shift later positions. A position whose value is still
    /// <see cref="object.Equals(object, object)"/> to what it held is not triggered.
    /// A version shares unchanged structure with the version it was built from
    /// and does not point back at it, so a version nobody holds can be collected.
    /// Presence uses <see cref="object.Equals(object, object)"/>. <see cref="object.GetHashCode"/>
    /// must agree with that equality and stay stable while the value is present.
    /// </summary>
    public sealed class ReactiveList<T> : Register, IEnumerable<T>
    {
        readonly Dictionary<T, int> _counts = new Dictionary<T, int>(ObjectEquals.Instance);
        readonly Dictionary<T, Presence> _presences = new Dictionary<T, Presence>(ObjectEquals.Instance);
        readonly Dictionary<int, IndexWatch> _indexes = new Dictionary<int, IndexWatch>();
        readonly bool _allowsNull = default(T) == null;
        readonly Version _empty;
        Version _version;
        int _nullCount;
        Presence _nullPresence;

        public ReactiveList()
        {
            _empty = Version.CreateEmpty();
            _version = _empty;
        }

        public ReactiveList(IEnumerable<T> items)
            : this()
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            var buffer = new List<T>();
            foreach (var item in items)
            {
                buffer.Add(item);
                NoteAdded(item, out _);
            }

            _version = buffer.Count == 0 ? _empty : Version.FromItems(buffer.ToArray());
        }

        public Version Value
        {
            get
            {
                RecordRead();
                lock (this)
                    return _version;
            }
        }

        public int Count
        {
            get
            {
                RecordRead();
                lock (this)
                    return _version.Count;
            }
        }

        public T this[int index]
        {
            get
            {
                if (index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));
                if (!Reaction.IsRecording)
                {
                    lock (this)
                    {
                        if ((uint)index >= (uint)_version.Count)
                            throw new ArgumentOutOfRangeException(nameof(index));
                        return _version[index];
                    }
                }

                // Subscribe before reading, and keep that pair atomic with publication.
                lock (this)
                {
                    GetOrCreateIndex(index).RecordRead();
                    if ((uint)index >= (uint)_version.Count)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    return _version[index];
                }
            }
            set => SetValue(index, value);
        }

        public bool Contains(T item)
        {
            if (!Reaction.IsRecording)
            {
                lock (this)
                    return CountOf(item) > 0;
            }

            lock (this)
            {
                GetOrCreatePresence(item).RecordRead();
                return CountOf(item) > 0;
            }
        }

        public int IndexOf(T item)
        {
            return Value.IndexOf(item);
        }

        public void Add(T item)
        {
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                var next = current.Inserted(current.Count, item);
                if (PublishInsert(current, next, current.Count, item))
                    return;
            }
        }

        public void Insert(int index, T item)
        {
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                if ((uint)index > (uint)current.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                var next = current.Inserted(index, item);
                if (PublishInsert(current, next, index, item))
                    return;
            }
        }

        public bool Remove(T item)
        {
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                var index = current.IndexOf(item);
                if (index < 0)
                {
                    if (Still(current))
                        return false;
                    continue;
                }

                var stored = current[index];
                var next = current.RemovedAt(index);
                if (next.Count == 0)
                    next = _empty;
                if (PublishRemove(current, next, index, stored))
                    return true;
            }
        }

        public void RemoveAt(int index)
        {
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                if ((uint)index >= (uint)current.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                var stored = current[index];
                var next = current.RemovedAt(index);
                if (next.Count == 0)
                    next = _empty;
                if (PublishRemove(current, next, index, stored))
                    return;
            }
        }

        public bool SetValue(int index, T value)
        {
            return SetValue(index, value, out _);
        }

        public bool SetValue(int index, T value, out T oldValue)
        {
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                if ((uint)index >= (uint)current.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                var next = current.Set(index, value, out oldValue, out var changed);
                if (!changed)
                {
                    if (Still(current))
                        return false;
                    continue;
                }

                if (PublishSet(current, next, index, oldValue, value))
                    return true;
            }
        }

        public void Clear()
        {
            Version previous;
            IndexWatch[] indexes;
            Presence[] presences;
            lock (this)
            {
                previous = _version;
                if (previous.Count == 0)
                    return;
                indexes = WatchesBefore(previous.Count);
                presences = SnapshotPresences();
                _version = _empty;
                _counts.Clear();
                _nullCount = 0;
            }

            TriggerReactions();
            for (var i = 0; i < indexes.Length; i++)
                indexes[i].TriggerReactions();
            for (var i = 0; i < presences.Length; i++)
            {
                if (previous.Contains(presences[i].Item))
                    presences[i].TriggerReactions();
            }
        }

        public IEnumerator<T> GetEnumerator() => Value.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal int WatchedIndexes
        {
            get
            {
                lock (this)
                    return _indexes.Count;
            }
        }

        internal int WatchedPresences
        {
            get
            {
                lock (this)
                    return _presences.Count + (_nullPresence == null ? 0 : 1);
            }
        }

        internal void CheckInvariants()
        {
            lock (this)
            {
                _version.CheckTree();
                var expected = new Dictionary<T, int>(ObjectEquals.Instance);
                var nulls = 0;
                foreach (var item in _version)
                {
                    if (IsNull(item))
                    {
                        nulls++;
                        continue;
                    }

                    expected.TryGetValue(item, out var count);
                    expected[item] = count + 1;
                }

                if (nulls != _nullCount || expected.Count != _counts.Count)
                    throw new InvalidOperationException("Presence counts do not match the picture.");
                foreach (var pair in expected)
                {
                    if (!_counts.TryGetValue(pair.Key, out var count) || count != pair.Value)
                        throw new InvalidOperationException("Presence counts do not match the picture.");
                }
            }
        }

        bool IsNull(T item)
        {
            return _allowsNull && item == null;
        }

        int CountOf(T item)
        {
            if (IsNull(item))
                return _nullCount;
            return _counts.TryGetValue(item, out var count) ? count : 0;
        }

        bool Still(Version current)
        {
            lock (this)
                return ReferenceEquals(_version, current);
        }

        bool PublishInsert(Version current, Version next, int index, T item)
        {
            Presence presence;
            bool presenceChanged;
            IndexWatch[] watches;
            lock (this)
            {
                if (!ReferenceEquals(_version, current))
                    return false;
                presenceChanged = NoteAdded(item, out presence);
                _version = next;
                watches = WatchesFrom(index);
            }

            TriggerReactions();
            if (presenceChanged && presence != null)
                presence.TriggerReactions();
            TriggerWhereChanged(current, next, watches);
            return true;
        }

        bool PublishRemove(Version current, Version next, int index, T item)
        {
            Presence presence;
            bool presenceChanged;
            IndexWatch[] watches;
            lock (this)
            {
                if (!ReferenceEquals(_version, current))
                    return false;
                presenceChanged = NoteRemoved(item, out presence);
                _version = next;
                watches = WatchesFrom(index);
            }

            TriggerReactions();
            if (presenceChanged && presence != null)
                presence.TriggerReactions();
            TriggerWhereChanged(current, next, watches);
            return true;
        }

        bool PublishSet(Version current, Version next, int index, T previous, T value)
        {
            Presence removedPresence;
            Presence addedPresence;
            bool removedChanged;
            bool addedChanged;
            IndexWatch watch;
            lock (this)
            {
                if (!ReferenceEquals(_version, current))
                    return false;
                ReplaceCounts(previous, value, out removedPresence, out removedChanged, out addedPresence, out addedChanged);
                _version = next;
                _indexes.TryGetValue(index, out watch);
            }

            TriggerReactions();
            if (watch != null)
                watch.TriggerReactions();
            if (removedChanged && removedPresence != null)
                removedPresence.TriggerReactions();
            if (addedChanged && addedPresence != null)
                addedPresence.TriggerReactions();
            return true;
        }

        static void TriggerWhereChanged(Version older, Version newer, IndexWatch[] watches)
        {
            for (var i = 0; i < watches.Length; i++)
            {
                if (!SameSlot(older, newer, watches[i].Index))
                    watches[i].TriggerReactions();
            }
        }

        static bool SameSlot(Version older, Version newer, int index)
        {
            var inOld = older != null && (uint)index < (uint)older.Count;
            var inNew = newer != null && (uint)index < (uint)newer.Count;
            if (inOld != inNew)
                return false;
            if (!inOld)
                return true;
            return Equals(older[index], newer[index]);
        }

        bool NoteAdded(T item, out Presence presence)
        {
            presence = null;
            if (IsNull(item))
            {
                if (_nullCount == 0)
                {
                    _nullCount = 1;
                    presence = _nullPresence;
                    return true;
                }

                _nullCount++;
                return false;
            }

            if (_counts.TryGetValue(item, out var count))
            {
                _counts[item] = count + 1;
                return false;
            }

            _counts.Add(item, 1);
            _presences.TryGetValue(item, out presence);
            return true;
        }

        bool NoteRemoved(T item, out Presence presence)
        {
            presence = null;
            if (IsNull(item))
            {
                if (_nullCount == 0)
                    throw new InvalidOperationException("The list lost count of a removed value.");
                presence = _nullPresence;
                if (_nullCount == 1)
                {
                    _nullCount = 0;
                    return true;
                }

                _nullCount--;
                return false;
            }

            if (!_counts.TryGetValue(item, out var count))
                throw new InvalidOperationException("The list lost count of a removed value.");
            _presences.TryGetValue(item, out presence);
            if (count == 1)
            {
                _counts.Remove(item);
                return true;
            }

            _counts[item] = count - 1;
            return false;
        }

        void ReplaceCounts(T previous, T value, out Presence removedPresence, out bool removedChanged,
            out Presence addedPresence, out bool addedChanged)
        {
            if (IsNull(previous) || IsNull(value))
            {
                removedChanged = NoteRemoved(previous, out removedPresence);
                addedChanged = NoteAdded(value, out addedPresence);
                return;
            }

            if (!_counts.TryGetValue(previous, out var oldCount))
                throw new InvalidOperationException("The list lost count of a replaced value.");
            _counts.TryGetValue(value, out var newCount);
            _presences.TryGetValue(previous, out removedPresence);
            _presences.TryGetValue(value, out addedPresence);
            if (oldCount == 1)
                _counts.Remove(previous);
            else
                _counts[previous] = oldCount - 1;
            if (newCount == 0)
                _counts.Add(value, 1);
            else
                _counts[value] = newCount + 1;
            removedChanged = oldCount == 1;
            addedChanged = newCount == 0;
        }

        IndexWatch[] WatchesFrom(int index)
        {
            var matches = new List<IndexWatch>();
            foreach (var watch in _indexes.Values)
            {
                if (watch.Index >= index)
                    matches.Add(watch);
            }

            return matches.ToArray();
        }

        IndexWatch[] WatchesBefore(int count)
        {
            var matches = new List<IndexWatch>();
            foreach (var watch in _indexes.Values)
            {
                if ((uint)watch.Index < (uint)count)
                    matches.Add(watch);
            }

            return matches.ToArray();
        }

        Presence[] SnapshotPresences()
        {
            var extra = _nullPresence == null ? 0 : 1;
            var matches = new Presence[_presences.Count + extra];
            _presences.Values.CopyTo(matches, 0);
            if (extra == 1)
                matches[matches.Length - 1] = _nullPresence;
            return matches;
        }

        IndexWatch GetOrCreateIndex(int index)
        {
            if (!_indexes.TryGetValue(index, out var watch))
            {
                watch = new IndexWatch(this, index);
                _indexes.Add(index, watch);
            }

            return watch;
        }

        Presence GetOrCreatePresence(T item)
        {
            if (IsNull(item))
            {
                if (_nullPresence == null)
                    _nullPresence = new Presence(this, item);
                return _nullPresence;
            }

            if (!_presences.TryGetValue(item, out var presence))
            {
                presence = new Presence(this, item);
                _presences.Add(item, presence);
            }

            return presence;
        }

        void RemoveIndexLocked(IndexWatch watch)
        {
            if (_indexes.TryGetValue(watch.Index, out var current) && ReferenceEquals(current, watch))
                _indexes.Remove(watch.Index);
        }

        void RemovePresenceLocked(Presence presence)
        {
            if (IsNull(presence.Item))
            {
                if (ReferenceEquals(_nullPresence, presence))
                    _nullPresence = null;
                return;
            }

            if (_presences.TryGetValue(presence.Item, out var current) && ReferenceEquals(current, presence))
                _presences.Remove(presence.Item);
        }

        sealed class IndexWatch(ReactiveList<T> owner, int index) : Register
        {
            int _listeners;

            public int Index { get; } = index;

            protected override object SyncRoot => owner;

            internal override bool AddListener(Reaction reaction)
            {
                if (reaction == null)
                    return false;
                lock (SyncRoot)
                {
                    if (!AddListenerLocked(reaction))
                        return false;
                    _listeners++;
                    return true;
                }
            }

            internal override bool RemoveListener(Reaction reaction)
            {
                if (reaction == null)
                    return false;
                lock (SyncRoot)
                {
                    if (!RemoveListenerLocked(reaction))
                        return false;
                    _listeners--;
                    if (_listeners == 0)
                        owner.RemoveIndexLocked(this);
                    return true;
                }
            }
        }

        sealed class Presence(ReactiveList<T> owner, T item) : Register
        {
            int _listeners;

            public T Item { get; } = item;

            protected override object SyncRoot => owner;

            internal override bool AddListener(Reaction reaction)
            {
                if (reaction == null)
                    return false;
                lock (SyncRoot)
                {
                    if (!AddListenerLocked(reaction))
                        return false;
                    _listeners++;
                    return true;
                }
            }

            internal override bool RemoveListener(Reaction reaction)
            {
                if (reaction == null)
                    return false;
                lock (SyncRoot)
                {
                    if (!RemoveListenerLocked(reaction))
                        return false;
                    _listeners--;
                    if (_listeners == 0)
                        owner.RemovePresenceLocked(this);
                    return true;
                }
            }
        }

        sealed class ObjectEquals : IEqualityComparer<T>
        {
            public static readonly ObjectEquals Instance = new ObjectEquals();

            public bool Equals(T x, T y)
            {
                return object.Equals(x, y);
            }

            public int GetHashCode(T obj)
            {
                if (ReferenceEquals(obj, null))
                    return 0;
                return obj.GetHashCode();
            }
        }

        /// <summary>
        /// One published picture of the list. Diff compares two pictures by index.
        /// It does not mark either of them as seen.
        /// </summary>
        public sealed class Version : IEnumerable<T>
        {
            readonly IndexTree<T>.Node _root;

            Version(IndexTree<T>.Node root, int count)
            {
                _root = root;
                Count = count;
            }

            internal static Version CreateEmpty()
            {
                return new Version(null, 0);
            }

            internal static Version FromItems(T[] items)
            {
                return new Version(IndexTree<T>.FromItems(items), items.Length);
            }

            public int Count { get; }

            public T this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)Count)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    return IndexTree<T>.Get(_root, index);
                }
            }

            public bool Contains(T item)
            {
                return IndexOf(item) >= 0;
            }

            public int IndexOf(T item)
            {
                for (var i = 0; i < Count; i++)
                {
                    if (Equals(this[i], item))
                        return i;
                }

                return -1;
            }

            public Change[] Diff(Version previous)
            {
                var older = previous == null ? null : previous._root;
                var olderCount = previous == null ? 0 : previous.Count;
                if (ReferenceEquals(_root, older))
                    return new Change[0];
                var changes = new List<Change>();
                var limit = olderCount < Count ? olderCount : Count;
                var index = 0;
                while (index < limit)
                {
                    var shared = IndexTree<T>.LargestShared(older, _root, index);
                    if (shared > 0)
                    {
                        index += shared;
                        continue;
                    }

                    var before = IndexTree<T>.Get(older, index);
                    var after = IndexTree<T>.Get(_root, index);
                    if (!Equals(before, after))
                        changes.Add(new Change(ChangeKind.Updated, index, after, before));
                    index++;
                }

                for (var added = limit; added < Count; added++)
                    changes.Add(new Change(ChangeKind.Added, added, IndexTree<T>.Get(_root, added), default(T)));
                for (var removed = limit; removed < olderCount; removed++)
                    changes.Add(new Change(ChangeKind.Removed, removed, IndexTree<T>.Get(older, removed), default(T)));
                return changes.Count == 0 ? new Change[0] : changes.ToArray();
            }

            public IEnumerator<T> GetEnumerator() => new IndexTree<T>.Enumerator(_root);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            internal int NodeCount => IndexTree<T>.CountNodes(_root);

            internal static int Shared(Version left, Version right)
            {
                var older = left == null ? null : left._root;
                var newer = right == null ? null : right._root;
                return IndexTree<T>.CountShared(older, newer);
            }

            internal void CheckTree()
            {
                IndexTree<T>.Check(_root);
                if (_root == null)
                {
                    if (Count != 0)
                        throw new InvalidOperationException("An empty picture reported a count.");
                    return;
                }

                if (_root.Count != Count)
                    throw new InvalidOperationException("Picture count does not match its blocks.");
            }

            internal Version Inserted(int index, T item)
            {
                return new Version(IndexTree<T>.Insert(_root, index, item), Count + 1);
            }

            internal Version RemovedAt(int index)
            {
                return new Version(IndexTree<T>.RemoveAt(_root, index), Count - 1);
            }

            internal Version Set(int index, T value, out T previous, out bool changed)
            {
                var root = IndexTree<T>.Set(_root, index, value, out previous, out changed);
                if (!changed)
                    return this;
                return new Version(root, Count);
            }
        }

        public enum ChangeKind
        {
            Added,
            Removed,
            Updated
        }

        public readonly struct Change
        {
            public Change(ChangeKind kind, int index, T value, T previous)
            {
                Kind = kind;
                Index = index;
                Value = value;
                Previous = previous;
            }

            public ChangeKind Kind { get; }
            public int Index { get; }
            public T Value { get; }
            public T Previous { get; }
        }
    }
}

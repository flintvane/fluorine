using System;
using System.Collections;
using System.Collections.Generic;

namespace Fluorine
{
    /// <summary>
    /// A fixed-length sequence published as immutable versions.
    /// <see cref="Value"/>, <see cref="Count"/> and <see cref="Length"/> subscribe to every change.
    /// The indexer getter subscribes only to that slot's value.
    /// A version shares unchanged structure with the version it was built from
    /// and does not point back at it, so a version nobody holds can be collected.
    /// Values use <see cref="object.Equals(object, object)"/>.
    /// There is no membership test: an index is a slot, and a slot always holds a value.
    /// </summary>
    public sealed class ReactiveArray<T> : Register, IEnumerable<T>
    {
        readonly int _length;
        readonly Dictionary<int, IndexWatch> _indexes = new Dictionary<int, IndexWatch>();
        readonly Version _empty;
        Version _version;

        public ReactiveArray(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            _length = length;
            _empty = Version.CreateEmpty();
            _version = length == 0 ? _empty : Version.FromItems(new T[length]);
        }

        public ReactiveArray(IEnumerable<T> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            List<T> buffer = [.. items];
            _length = buffer.Count;
            _empty = Version.CreateEmpty();
            _version = _length == 0 ? _empty : Version.FromItems([.. buffer]);
        }

        public int Length => Count;

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
                if ((uint)index >= (uint)_length)
                    throw new ArgumentOutOfRangeException(nameof(index));
                if (!Reaction.IsRecording)
                {
                    lock (this)
                        return _version[index];
                }

                // Subscribe before reading, and keep that pair atomic with publication.
                lock (this)
                {
                    GetOrCreate(index).RecordRead();
                    return _version[index];
                }
            }
            set => SetValue(index, value);
        }

        public bool SetValue(int index, T value)
        {
            return SetValue(index, value, out _);
        }

        public bool SetValue(int index, T value, out T oldValue)
        {
            if ((uint)index >= (uint)_length)
                throw new ArgumentOutOfRangeException(nameof(index));
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                var next = current.Set(index, value, out oldValue, out var changed);
                if (!changed)
                {
                    if (Still(current))
                        return false;
                    continue;
                }

                if (Publish(current, next, index))
                    return true;
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

        internal void CheckInvariants()
        {
            lock (this)
            {
                if (_version.Count != _length)
                    throw new InvalidOperationException("The picture length left the array length.");
                _version.CheckTree();
            }
        }

        bool Still(Version current)
        {
            lock (this)
                return ReferenceEquals(_version, current);
        }

        bool Publish(Version current, Version next, int index)
        {
            IndexWatch watch;
            lock (this)
            {
                if (!ReferenceEquals(_version, current))
                    return false;
                _version = next;
                _indexes.TryGetValue(index, out watch);
            }

            TriggerReactions();
            if (watch != null)
                watch.TriggerReactions();
            return true;
        }

        IndexWatch GetOrCreate(int index)
        {
            if (!_indexes.TryGetValue(index, out var watch))
            {
                watch = new IndexWatch(this, index);
                _indexes.Add(index, watch);
            }

            return watch;
        }

        void RemoveIndexLocked(IndexWatch watch)
        {
            if (_indexes.TryGetValue(watch.Index, out var current) && ReferenceEquals(current, watch))
                _indexes.Remove(watch.Index);
        }

        sealed class IndexWatch(ReactiveArray<T> owner, int index) : Register
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

        /// <summary>
        /// One published picture of the array. Diff compares two pictures by index.
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

            public Change[] Diff(Version previous)
            {
                var older = previous == null ? null : previous._root;
                var olderCount = previous == null ? 0 : previous.Count;
                if (ReferenceEquals(_root, older))
                    return [];
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
                return changes.Count == 0 ? [] : [.. changes];
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

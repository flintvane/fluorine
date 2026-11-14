using System;
using System.Collections;
using System.Collections.Generic;

namespace Fluorine
{
    /// <summary>
    /// A sorted dictionary published as immutable versions.
    /// <see cref="Value"/> subscribes to every change.
    /// <see cref="ContainsKey"/> subscribes only to that key's presence.
    /// <see cref="TryGetValue"/> and the indexer getter subscribe to that key's value.
    /// A version shares unchanged blocks with the version it was built from
    /// and does not point back at it, so a version nobody holds can be collected.
    /// Keys use <see cref="IComparer{T}"/>. Compare returning zero means the same key.
    /// Values use <see cref="object.Equals(object, object)"/>.
    /// Enumeration and <see cref="Version.Diff"/> run in comparer order.
    /// Comparison results must stay stable while a key is present.
    /// A null key is rejected. Replacing a value keeps the stored key instance.
    /// </summary>
    public sealed class ReactiveSortedDictionary<TKey, TValue> : Register, IEnumerable<KeyValuePair<TKey, TValue>>
    {
        const string MissingKeyMessage = "The given key was not present in the dictionary.";
        static readonly bool KeyCanBeNull = default(TKey) == null;

        readonly IComparer<TKey> _keys;
        readonly KeyOrder _order;
        readonly SortedDictionary<TKey, Slot> _slots;
        readonly Version _empty;
        Version _version;

        public ReactiveSortedDictionary()
            : this((IComparer<TKey>)null)
        {
        }

        public ReactiveSortedDictionary(IComparer<TKey> comparer)
        {
            _keys = comparer ?? Comparer<TKey>.Default;
            _order = new KeyOrder(_keys);
            _empty = Version.CreateEmpty(_order);
            _version = _empty;
            _slots = new SortedDictionary<TKey, Slot>(_keys);
        }

        public ReactiveSortedDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items)
            : this(items, null)
        {
        }

        public ReactiveSortedDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items, IComparer<TKey> comparer)
            : this(comparer)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            var version = _empty;
            foreach (var item in items)
            {
                RequireKey(item.Key);
                version = version.Added(item.Key, item.Value);
            }

            _version = version.Count == 0 ? _empty : version;
        }

        public IComparer<TKey> Comparer => _keys;

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

        public TValue this[TKey key]
        {
            get
            {
                if (!TryGetValue(key, out var value))
                    throw new KeyNotFoundException(MissingKeyMessage);
                return value;
            }
            set => SetValue(key, value);
        }

        public bool ContainsKey(TKey key)
        {
            RequireKey(key);
            Version version;
            if (!Reaction.IsRecording)
            {
                lock (this)
                    version = _version;
                return version.ContainsKey(key);
            }

            // Subscribe before reading, and keep that pair atomic with publication.
            lock (this)
            {
                GetOrCreatePresence(key).RecordRead();
                version = _version;
            }

            return version.ContainsKey(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            RequireKey(key);
            Version version;
            if (!Reaction.IsRecording)
            {
                lock (this)
                    version = _version;
                return version.TryGetValue(key, out value);
            }

            lock (this)
            {
                GetOrCreateValue(key).RecordRead();
                version = _version;
            }

            return version.TryGetValue(key, out value);
        }

        public bool Add(TKey key, TValue value)
        {
            RequireKey(key);
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                var next = current.Added(key, value);
                if (ReferenceEquals(next, current))
                {
                    if (Still(current))
                        return false;
                    continue;
                }

                if (Publish(current, next, key, true, true))
                    return true;
            }
        }

        public bool SetValue(TKey key, TValue value)
        {
            return SetValue(key, value, out _);
        }

        public bool SetValue(TKey key, TValue value, out TValue oldValue)
        {
            RequireKey(key);
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                var next = current.Set(key, value, out oldValue, out var changed, out var inserted);
                if (!changed)
                {
                    if (Still(current))
                        return false;
                    continue;
                }

                if (Publish(current, next, key, inserted, true))
                    return true;
            }
        }

        public bool Remove(TKey key)
        {
            RequireKey(key);
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                var next = current.Removed(key);
                if (ReferenceEquals(next, current))
                {
                    if (Still(current))
                        return false;
                    continue;
                }

                if (next.Count == 0)
                    next = _empty;
                if (Publish(current, next, key, true, true))
                    return true;
            }
        }

        public void Clear()
        {
            Version previous;
            lock (this)
            {
                previous = _version;
                if (previous.Count == 0)
                    return;
                _version = _empty;
            }

            TriggerReactions();
            foreach (var item in previous)
            {
                Presence presence;
                ValueWatch valueWatch;
                lock (this)
                    TryGetWatches(item.Key, out presence, out valueWatch);
                if (presence != null)
                    presence.TriggerReactions();
                if (valueWatch != null)
                    valueWatch.TriggerReactions();
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return Value.GetEnumerator();
        }

        public IEnumerable<TKey> Keys => Value.Keys;

        public IEnumerable<TValue> Values => Value.Values;

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal int WatchedPresences
        {
            get
            {
                lock (this)
                    return CountWatches(true);
            }
        }

        internal int WatchedValues
        {
            get
            {
                lock (this)
                    return CountWatches(false);
            }
        }

        internal void CheckInvariants()
        {
            Version version;
            lock (this)
                version = _version;
            version.Check();
        }

        static void RequireKey(TKey key)
        {
            if (KeyCanBeNull && key == null)
                throw new ArgumentNullException(nameof(key));
        }

        bool Still(Version current)
        {
            lock (this)
                return ReferenceEquals(_version, current);
        }

        bool Publish(Version current, Version next, TKey key, bool presenceChanged, bool valueChanged)
        {
            Presence presence;
            ValueWatch valueWatch;
            lock (this)
            {
                if (!ReferenceEquals(_version, current))
                    return false;
                _version = next;
                TryGetWatches(key, out presence, out valueWatch);
            }

            TriggerReactions();
            if (presenceChanged && presence != null)
                presence.TriggerReactions();
            if (valueChanged && valueWatch != null)
                valueWatch.TriggerReactions();
            return true;
        }

        int CountWatches(bool presence)
        {
            var count = 0;
            foreach (var slot in _slots.Values)
            {
                if (presence ? slot.Presence != null : slot.Value != null)
                    count++;
            }

            return count;
        }

        Presence GetOrCreatePresence(TKey key)
        {
            var slot = GetOrCreateSlot(key);
            if (slot.Presence == null)
                slot.Presence = new Presence(this, key);
            return slot.Presence;
        }

        ValueWatch GetOrCreateValue(TKey key)
        {
            var slot = GetOrCreateSlot(key);
            if (slot.Value == null)
                slot.Value = new ValueWatch(this, key);
            return slot.Value;
        }

        Slot GetOrCreateSlot(TKey key)
        {
            if (!_slots.TryGetValue(key, out var slot))
            {
                slot = new Slot();
                _slots.Add(key, slot);
            }

            return slot;
        }

        bool TryGetWatches(TKey key, out Presence presence, out ValueWatch valueWatch)
        {
            presence = null;
            valueWatch = null;
            if (!_slots.TryGetValue(key, out var slot))
                return false;
            presence = slot.Presence;
            valueWatch = slot.Value;
            return presence != null || valueWatch != null;
        }

        // Caller holds the dictionary lock.
        void RemovePresenceLocked(Presence presence)
        {
            if (!_slots.TryGetValue(presence.Key, out var slot) || !ReferenceEquals(slot.Presence, presence))
                return;
            slot.Presence = null;
            if (slot.Value == null)
                _slots.Remove(presence.Key);
        }

        // Caller holds the dictionary lock.
        void RemoveValueLocked(ValueWatch valueWatch)
        {
            if (!_slots.TryGetValue(valueWatch.Key, out var slot) || !ReferenceEquals(slot.Value, valueWatch))
                return;
            slot.Value = null;
            if (slot.Presence == null)
                _slots.Remove(valueWatch.Key);
        }

        sealed class Slot
        {
            public Presence Presence;
            public ValueWatch Value;
        }

        sealed class Presence(ReactiveSortedDictionary<TKey, TValue> owner, TKey key) : Register
        {
            int _listeners;

            public TKey Key { get; } = key;

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

        sealed class ValueWatch(ReactiveSortedDictionary<TKey, TValue> owner, TKey key) : Register
        {
            int _listeners;

            public TKey Key { get; } = key;

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
                        owner.RemoveValueLocked(this);
                    return true;
                }
            }
        }

        internal readonly struct Entry(TKey key, TValue value)
        {
            public readonly TKey Key = key;
            public readonly TValue Value = value;
        }

        internal sealed class KeyOrder(IComparer<TKey> keys) : IComparer<Entry>
        {
            public int Compare(Entry x, Entry y)
            {
                return keys.Compare(x.Key, y.Key);
            }
        }

        readonly struct ValueUpdate : BlockTree<Entry>.IReplace
        {
            public bool Replace(Entry existing, Entry incoming, out Entry stored)
            {
                if (Equals(existing.Value, incoming.Value))
                {
                    stored = existing;
                    return false;
                }

                stored = new Entry(existing.Key, incoming.Value);
                return true;
            }
        }

        /// <summary>
        /// One published picture of the dictionary. Enumeration and <see cref="Diff"/>
        /// run in comparer order. Diff compares two pictures. It does not mark
        /// either of them as seen.
        /// </summary>
        public sealed class Version : IEnumerable<KeyValuePair<TKey, TValue>>
        {
            readonly KeyOrder _order;
            readonly BlockTree<Entry>.Node _root;

            Version(BlockTree<Entry>.Node root, KeyOrder order)
            {
                _root = root;
                _order = order;
            }

            internal static Version CreateEmpty(KeyOrder order)
            {
                return new Version(null, order);
            }

            public int Count => _root == null ? 0 : _root.Count;

            public TValue this[TKey key]
            {
                get
                {
                    if (!TryGetValue(key, out var value))
                        throw new KeyNotFoundException(MissingKeyMessage);
                    return value;
                }
            }

            public bool ContainsKey(TKey key)
            {
                RequireKey(key);
                return BlockTree<Entry>.TryGet(_root, new Entry(key, default), _order, out _);
            }

            public bool TryGetValue(TKey key, out TValue value)
            {
                RequireKey(key);
                if (!BlockTree<Entry>.TryGet(_root, new Entry(key, default), _order, out var stored))
                {
                    value = default;
                    return false;
                }

                value = stored.Value;
                return true;
            }

            internal int IndexOf(TKey key) => BlockTree<Entry>.IndexOf(_root, new Entry(key, default), _order);

            public Change[] Diff(Version previous)
            {
                var older = previous == null ? null : previous._root;
                if (ReferenceEquals(_root, older))
                    return [];
                var changes = new List<Change>();
                BlockTree<Entry>.Diff(older, _root, _order, new Sink(changes));
                return changes.Count == 0 ? [] : changes.ToArray();
            }

            public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
            {
                return new PairEnumerator(new BlockTree<Entry>.Enumerator(_root));
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerable<TKey> Keys => new KeyView(_root);

            public IEnumerable<TValue> Values => new ValueView(_root);

            internal int NodeCount => BlockTree<Entry>.CountNodes(_root);

            internal int SharedWith(Version other)
            {
                return BlockTree<Entry>.CountShared(_root, other == null ? null : other._root);
            }

            internal void Check()
            {
                BlockTree<Entry>.Check(_root, _order);
            }

            internal Version Added(TKey key, TValue value)
            {
                var root = BlockTree<Entry>.Add(_root, new Entry(key, value), _order, out var added);
                if (!added)
                    return this;
                return new Version(root, _order);
            }

            internal Version Set(TKey key, TValue value, out TValue oldValue, out bool changed, out bool inserted)
            {
                var root = BlockTree<Entry>.Update(_root, new Entry(key, value), _order, default(ValueUpdate),
                    out var previous, out changed, out inserted);
                oldValue = inserted ? default : previous.Value;
                if (!changed)
                    return this;
                return new Version(root, _order);
            }

            internal Version Removed(TKey key)
            {
                var root = BlockTree<Entry>.Remove(_root, new Entry(key, default), _order, out var removed);
                if (!removed)
                    return this;
                if (root == null)
                    return new Version(null, _order);
                return new Version(root, _order);
            }

            readonly struct Sink(List<Change> changes) : BlockTree<Entry>.IDiff
            {
                public void Added(Entry item)
                {
                    changes.Add(new Change(ChangeKind.Added, item.Key, item.Value, default));
                }

                public void Removed(Entry item)
                {
                    changes.Add(new Change(ChangeKind.Removed, item.Key, item.Value, default));
                }

                public void Matched(Entry older, Entry newer)
                {
                    if (!Equals(older.Value, newer.Value))
                        changes.Add(new Change(ChangeKind.Updated, older.Key, newer.Value, older.Value));
                }
            }
        }

        public enum ChangeKind
        {
            Added,
            Removed,
            Updated
        }

        public readonly struct Change(ChangeKind kind, TKey key, TValue value, TValue previousValue)
        {
            public ChangeKind Kind { get; } = kind;
            public TKey Key { get; } = key;
            public TValue Value { get; } = value;
            public TValue PreviousValue { get; } = previousValue;
        }

        sealed class PairEnumerator(BlockTree<Entry>.Enumerator inner) : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            public KeyValuePair<TKey, TValue> Current
            {
                get
                {
                    var entry = inner.Current;
                    return new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                }
            }

            object IEnumerator.Current => Current;

            public bool MoveNext() => inner.MoveNext();

            public void Reset() => inner.Reset();

            public void Dispose() => inner.Dispose();
        }

        sealed class KeyView(BlockTree<Entry>.Node root) : IEnumerable<TKey>
        {
            public IEnumerator<TKey> GetEnumerator() => new KeyEnumerator(new BlockTree<Entry>.Enumerator(root));

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        sealed class ValueView(BlockTree<Entry>.Node root) : IEnumerable<TValue>
        {
            public IEnumerator<TValue> GetEnumerator() => new ValueEnumerator(new BlockTree<Entry>.Enumerator(root));

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        sealed class KeyEnumerator(BlockTree<Entry>.Enumerator inner) : IEnumerator<TKey>
        {
            public TKey Current => inner.Current.Key;

            object IEnumerator.Current => Current;

            public bool MoveNext() => inner.MoveNext();

            public void Reset() => inner.Reset();

            public void Dispose() => inner.Dispose();
        }

        sealed class ValueEnumerator(BlockTree<Entry>.Enumerator inner) : IEnumerator<TValue>
        {
            public TValue Current => inner.Current.Value;

            object IEnumerator.Current => Current;

            public bool MoveNext() => inner.MoveNext();

            public void Reset() => inner.Reset();

            public void Dispose() => inner.Dispose();
        }
    }
}

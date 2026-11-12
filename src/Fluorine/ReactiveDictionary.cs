using System;
using System.Collections;
using System.Collections.Generic;

namespace Fluorine
{
    /// <summary>
    /// A hash dictionary published as immutable versions.
    /// <see cref="Value"/> subscribes to every change.
    /// <see cref="ContainsKey"/> subscribes only to that key's presence.
    /// <see cref="TryGetValue"/> and the indexer getter subscribe to that key's value.
    /// A version shares unchanged structure with the version it was built from
    /// and does not point back at it, so a version nobody holds can be collected.
    /// Keys use <see cref="IEqualityComparer{T}"/>. Values use <see cref="object.Equals(object, object)"/>.
    /// A key's hash code must stay stable while the key is present.
    /// A null key is rejected.
    /// </summary>
    public sealed class ReactiveDictionary<TKey, TValue> : Register, IEnumerable<KeyValuePair<TKey, TValue>>
    {
        const string MissingKeyMessage = "The given key was not present in the dictionary.";
        static readonly bool KeyCanBeNull = (object)default(TKey) == null;

        readonly IEqualityComparer<TKey> _comparer;
        readonly Dictionary<TKey, Slot> _slots;
        readonly Version _empty;
        Version _version;

        public ReactiveDictionary()
            : this((IEqualityComparer<TKey>)null)
        {
        }

        public ReactiveDictionary(IEqualityComparer<TKey> comparer)
        {
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            _empty = Version.CreateEmpty(_comparer);
            _version = _empty;
            _slots = new Dictionary<TKey, Slot>(_comparer);
        }

        public ReactiveDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items)
            : this(items, null)
        {
        }

        public ReactiveDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items, IEqualityComparer<TKey> comparer)
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

        public IEqualityComparer<TKey> Comparer => _comparer;

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
                var next = current.Removed(key, out _, out var removed);
                if (!removed)
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

        static void RequireKey(TKey key)
        {
            if (KeyCanBeNull && (object)key == null)
                throw new ArgumentNullException(nameof(key));
        }

        static bool ValuesEqual(TValue left, TValue right)
        {
            return object.Equals(left, right);
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

        sealed class Presence : Register
        {
            readonly ReactiveDictionary<TKey, TValue> _owner;
            int _listeners;

            public Presence(ReactiveDictionary<TKey, TValue> owner, TKey key)
            {
                _owner = owner;
                Key = key;
            }

            public TKey Key { get; }

            // Publication and the listener list share the dictionary lock.
            protected override object SyncRoot => _owner;

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
                        _owner.RemovePresenceLocked(this);
                    return true;
                }
            }
        }

        sealed class ValueWatch : Register
        {
            readonly ReactiveDictionary<TKey, TValue> _owner;
            int _listeners;

            public ValueWatch(ReactiveDictionary<TKey, TValue> owner, TKey key)
            {
                _owner = owner;
                Key = key;
            }

            public TKey Key { get; }

            // Publication and the listener list share the dictionary lock.
            protected override object SyncRoot => _owner;

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
                        _owner.RemoveValueLocked(this);
                    return true;
                }
            }
        }

        /// <summary>
        /// One published picture of the dictionary. Diff compares two pictures.
        /// It does not mark either of them as seen.
        /// </summary>
        public sealed class Version : IEnumerable<KeyValuePair<TKey, TValue>>
        {
            readonly IEqualityComparer<TKey> _comparer;
            readonly Node _root;

            Version(Node root, int count, IEqualityComparer<TKey> comparer)
            {
                _root = root;
                Count = count;
                _comparer = comparer ?? EqualityComparer<TKey>.Default;
            }

            internal static Version CreateEmpty(IEqualityComparer<TKey> comparer)
            {
                return new Version(null, 0, comparer);
            }

            public int Count { get; }

            public TValue this[TKey key]
            {
                get
                {
                    if (!TryGetValue(key, out var value))
                        throw new KeyNotFoundException(MissingKeyMessage);
                    return value;
                }
            }

            public bool SameAs(Version other)
            {
                if (ReferenceEquals(this, other))
                    return true;
                return ReferenceEquals(_root, other == null ? null : other._root);
            }

            public bool ContainsKey(TKey key)
            {
                RequireKey(key);
                if (_root == null)
                    return false;
                return _root.Contains(_comparer.GetHashCode(key), key, 0, _comparer);
            }

            public bool TryGetValue(TKey key, out TValue value)
            {
                RequireKey(key);
                if (_root == null)
                {
                    value = default(TValue);
                    return false;
                }

                return _root.TryGet(_comparer.GetHashCode(key), key, 0, _comparer, out value);
            }

            public Change[] Diff(Version previous)
            {
                var older = previous == null ? null : previous._root;
                if (ReferenceEquals(_root, older))
                    return Array.Empty<Change>();
                var changes = new List<Change>();
                DiffNodes(older, _root, _comparer, changes);
                if (changes.Count == 0)
                    return Array.Empty<Change>();
                return changes.ToArray();
            }

            public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
            {
                if (_root == null)
                    return Empty();
                var items = new List<KeyValuePair<TKey, TValue>>(Count);
                _root.Collect(items);
                return items.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            static IEnumerator<KeyValuePair<TKey, TValue>> Empty()
            {
                yield break;
            }

            internal Version Added(TKey key, TValue value)
            {
                var hash = _comparer.GetHashCode(key);
                if (_root == null)
                    return new Version(new Leaf(hash, key, value), 1, _comparer);
                var root = _root.Add(hash, key, value, 0, _comparer, out var added);
                if (!added)
                    return this;
                return new Version(root, Count + 1, _comparer);
            }

            internal Version Set(TKey key, TValue value, out TValue previous, out bool changed, out bool inserted)
            {
                var hash = _comparer.GetHashCode(key);
                if (_root == null)
                {
                    previous = default(TValue);
                    changed = true;
                    inserted = true;
                    return new Version(new Leaf(hash, key, value), 1, _comparer);
                }

                var root = _root.Set(hash, key, value, 0, _comparer, out previous, out changed, out inserted);
                if (!changed)
                    return this;
                return new Version(root, Count + (inserted ? 1 : 0), _comparer);
            }

            internal Version Removed(TKey key, out TValue removedValue, out bool removed)
            {
                if (_root == null)
                {
                    removedValue = default(TValue);
                    removed = false;
                    return this;
                }

                var hash = _comparer.GetHashCode(key);
                var root = _root.Remove(hash, key, 0, _comparer, out removed, out removedValue);
                if (!removed)
                    return this;
                if (root == null)
                    return new Version(null, 0, _comparer);
                return new Version(root, Count - 1, _comparer);
            }

            static void DiffNodes(Node older, Node newer, IEqualityComparer<TKey> comparer, List<Change> changes)
            {
                if (ReferenceEquals(older, newer))
                    return;
                if (older is Branch oldBranch && newer is Branch newBranch)
                {
                    for (var i = 0; i < 32; i++)
                    {
                        var bit = 1u << i;
                        var inOld = (oldBranch.Bitmap & bit) != 0;
                        var inNew = (newBranch.Bitmap & bit) != 0;
                        if (!inOld && !inNew)
                            continue;
                        var oldChild = inOld ? oldBranch.Children[oldBranch.Slot(bit)] : null;
                        var newChild = inNew ? newBranch.Children[newBranch.Slot(bit)] : null;
                        DiffNodes(oldChild, newChild, comparer, changes);
                    }

                    return;
                }

                var oldItems = new List<KeyValuePair<TKey, TValue>>();
                var newItems = new List<KeyValuePair<TKey, TValue>>();
                older?.Collect(oldItems);
                newer?.Collect(newItems);
                var newerHas = new Dictionary<TKey, TValue>(comparer);
                var olderHas = new Dictionary<TKey, TValue>(comparer);
                for (var i = 0; i < newItems.Count; i++)
                    newerHas[newItems[i].Key] = newItems[i].Value;
                for (var i = 0; i < oldItems.Count; i++)
                {
                    var key = oldItems[i].Key;
                    var value = oldItems[i].Value;
                    olderHas[key] = value;
                    if (!newerHas.TryGetValue(key, out var newValue))
                        changes.Add(new Change(ChangeKind.Removed, key, value, default(TValue)));
                    else if (!ValuesEqual(value, newValue))
                        changes.Add(new Change(ChangeKind.Updated, key, newValue, value));
                }

                for (var i = 0; i < newItems.Count; i++)
                {
                    if (!olderHas.ContainsKey(newItems[i].Key))
                        changes.Add(new Change(ChangeKind.Added, newItems[i].Key, newItems[i].Value, default(TValue)));
                }
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
            public Change(ChangeKind kind, TKey key, TValue value, TValue previousValue)
            {
                Kind = kind;
                Key = key;
                Value = value;
                PreviousValue = previousValue;
            }

            public ChangeKind Kind { get; }
            public TKey Key { get; }
            public TValue Value { get; }
            public TValue PreviousValue { get; }
        }

        abstract class Node
        {
            public abstract bool Contains(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer);
            public abstract bool TryGet(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer, out TValue value);
            public abstract Node Add(int hash, TKey key, TValue value, int shift, IEqualityComparer<TKey> comparer, out bool added);
            public abstract Node Set(int hash, TKey key, TValue value, int shift, IEqualityComparer<TKey> comparer, out TValue previous, out bool changed, out bool inserted);
            public abstract Node Remove(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer, out bool removed, out TValue removedValue);
            public abstract void Collect(List<KeyValuePair<TKey, TValue>> into);

            protected static int Index(int hash, int shift)
            {
                return (int)(((uint)hash >> shift) & 31);
            }

            protected static uint Bit(int hash, int shift)
            {
                return 1u << Index(hash, shift);
            }

            protected static int PopCount(uint value)
            {
                var count = 0;
                while (value != 0)
                {
                    value &= value - 1;
                    count++;
                }

                return count;
            }

            protected static Node Join(int hashA, Node a, int hashB, Node b, int shift, IEqualityComparer<TKey> comparer)
            {
                if (hashA == hashB)
                    return Collision.Merge(hashA, a, b, comparer);
                if (shift > 30)
                    throw new ReactiveException("Hash trie could not place two different hashes.");
                var ia = Index(hashA, shift);
                var ib = Index(hashB, shift);
                if (ia != ib)
                {
                    var bitmap = (1u << ia) | (1u << ib);
                    var children = ia < ib ? new[] { a, b } : new[] { b, a };
                    return new Branch(bitmap, children);
                }

                var child = Join(hashA, a, hashB, b, shift + 5, comparer);
                return new Branch(1u << ia, new[] { child });
            }
        }

        sealed class Leaf : Node
        {
            public Leaf(int hash, TKey key, TValue value)
            {
                Hash = hash;
                Key = key;
                Value = value;
            }

            public int Hash { get; }
            public TKey Key { get; }
            public TValue Value { get; }

            public override bool Contains(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer)
            {
                return Hash == hash && comparer.Equals(Key, key);
            }

            public override bool TryGet(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer, out TValue value)
            {
                if (Hash == hash && comparer.Equals(Key, key))
                {
                    value = Value;
                    return true;
                }

                value = default(TValue);
                return false;
            }

            public override Node Add(int hash, TKey key, TValue value, int shift, IEqualityComparer<TKey> comparer, out bool added)
            {
                if (Hash == hash && comparer.Equals(Key, key))
                {
                    added = false;
                    return this;
                }

                added = true;
                if (Hash == hash)
                    return new Collision(hash, new[] { Pair(Key, Value), Pair(key, value) });
                return Join(Hash, this, hash, new Leaf(hash, key, value), shift, comparer);
            }

            public override Node Set(int hash, TKey key, TValue value, int shift, IEqualityComparer<TKey> comparer, out TValue previous, out bool changed, out bool inserted)
            {
                if (Hash == hash && comparer.Equals(Key, key))
                {
                    previous = Value;
                    inserted = false;
                    if (ValuesEqual(Value, value))
                    {
                        changed = false;
                        return this;
                    }

                    changed = true;
                    return new Leaf(hash, Key, value);
                }

                previous = default(TValue);
                changed = true;
                inserted = true;
                if (Hash == hash)
                    return new Collision(hash, new[] { Pair(Key, Value), Pair(key, value) });
                return Join(Hash, this, hash, new Leaf(hash, key, value), shift, comparer);
            }

            public override Node Remove(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer, out bool removed, out TValue removedValue)
            {
                if (Hash == hash && comparer.Equals(Key, key))
                {
                    removed = true;
                    removedValue = Value;
                    return null;
                }

                removed = false;
                removedValue = default(TValue);
                return this;
            }

            public override void Collect(List<KeyValuePair<TKey, TValue>> into)
            {
                into.Add(Pair(Key, Value));
            }
        }

        sealed class Collision : Node
        {
            public Collision(int hash, KeyValuePair<TKey, TValue>[] items)
            {
                Hash = hash;
                Items = items;
            }

            public int Hash { get; }
            public KeyValuePair<TKey, TValue>[] Items { get; }

            public static Collision Merge(int hash, Node a, Node b, IEqualityComparer<TKey> comparer)
            {
                var items = new List<KeyValuePair<TKey, TValue>>();
                a.Collect(items);
                var more = new List<KeyValuePair<TKey, TValue>>();
                b.Collect(more);
                for (var i = 0; i < more.Count; i++)
                {
                    var found = false;
                    for (var j = 0; j < items.Count; j++)
                    {
                        if (comparer.Equals(items[j].Key, more[i].Key))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                        items.Add(more[i]);
                }

                return new Collision(hash, items.ToArray());
            }

            public override bool Contains(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer)
            {
                return IndexOf(hash, key, comparer) >= 0;
            }

            public override bool TryGet(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer, out TValue value)
            {
                var index = IndexOf(hash, key, comparer);
                if (index >= 0)
                {
                    value = Items[index].Value;
                    return true;
                }

                value = default(TValue);
                return false;
            }

            public override Node Add(int hash, TKey key, TValue value, int shift, IEqualityComparer<TKey> comparer, out bool added)
            {
                if (Hash != hash)
                {
                    added = true;
                    return Join(Hash, this, hash, new Leaf(hash, key, value), shift, comparer);
                }

                if (IndexOf(hash, key, comparer) >= 0)
                {
                    added = false;
                    return this;
                }

                added = true;
                var next = new KeyValuePair<TKey, TValue>[Items.Length + 1];
                Array.Copy(Items, next, Items.Length);
                next[Items.Length] = Pair(key, value);
                return new Collision(Hash, next);
            }

            public override Node Set(int hash, TKey key, TValue value, int shift, IEqualityComparer<TKey> comparer, out TValue previous, out bool changed, out bool inserted)
            {
                if (Hash != hash)
                {
                    previous = default(TValue);
                    changed = true;
                    inserted = true;
                    return Join(Hash, this, hash, new Leaf(hash, key, value), shift, comparer);
                }

                var index = IndexOf(hash, key, comparer);
                if (index >= 0)
                {
                    previous = Items[index].Value;
                    inserted = false;
                    if (ValuesEqual(previous, value))
                    {
                        changed = false;
                        return this;
                    }

                    changed = true;
                    var replaced = (KeyValuePair<TKey, TValue>[])Items.Clone();
                    replaced[index] = Pair(Items[index].Key, value);
                    return new Collision(Hash, replaced);
                }

                previous = default(TValue);
                changed = true;
                inserted = true;
                var next = new KeyValuePair<TKey, TValue>[Items.Length + 1];
                Array.Copy(Items, next, Items.Length);
                next[Items.Length] = Pair(key, value);
                return new Collision(Hash, next);
            }

            public override Node Remove(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer, out bool removed, out TValue removedValue)
            {
                var index = IndexOf(hash, key, comparer);
                if (index < 0)
                {
                    removed = false;
                    removedValue = default(TValue);
                    return this;
                }

                removed = true;
                removedValue = Items[index].Value;
                if (Items.Length == 1)
                    return null;
                if (Items.Length == 2)
                {
                    var kept = Items[index == 0 ? 1 : 0];
                    return new Leaf(Hash, kept.Key, kept.Value);
                }

                var next = new KeyValuePair<TKey, TValue>[Items.Length - 1];
                Array.Copy(Items, 0, next, 0, index);
                Array.Copy(Items, index + 1, next, index, Items.Length - index - 1);
                return new Collision(Hash, next);
            }

            public override void Collect(List<KeyValuePair<TKey, TValue>> into)
            {
                for (var i = 0; i < Items.Length; i++)
                    into.Add(Items[i]);
            }

            int IndexOf(int hash, TKey key, IEqualityComparer<TKey> comparer)
            {
                if (Hash != hash)
                    return -1;
                for (var i = 0; i < Items.Length; i++)
                {
                    if (comparer.Equals(Items[i].Key, key))
                        return i;
                }

                return -1;
            }
        }

        sealed class Branch : Node
        {
            public Branch(uint bitmap, Node[] children)
            {
                Bitmap = bitmap;
                Children = children;
            }

            public uint Bitmap { get; }
            public Node[] Children { get; }

            public int Slot(uint bit)
            {
                return PopCount(Bitmap & (bit - 1));
            }

            public override bool Contains(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer)
            {
                var bit = Bit(hash, shift);
                if ((Bitmap & bit) == 0)
                    return false;
                return Children[Slot(bit)].Contains(hash, key, shift + 5, comparer);
            }

            public override bool TryGet(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer, out TValue value)
            {
                var bit = Bit(hash, shift);
                if ((Bitmap & bit) == 0)
                {
                    value = default(TValue);
                    return false;
                }

                return Children[Slot(bit)].TryGet(hash, key, shift + 5, comparer, out value);
            }

            public override Node Add(int hash, TKey key, TValue value, int shift, IEqualityComparer<TKey> comparer, out bool added)
            {
                var bit = Bit(hash, shift);
                if ((Bitmap & bit) == 0)
                {
                    added = true;
                    var index = Slot(bit);
                    var next = new Node[Children.Length + 1];
                    Array.Copy(Children, 0, next, 0, index);
                    next[index] = new Leaf(hash, key, value);
                    Array.Copy(Children, index, next, index + 1, Children.Length - index);
                    return new Branch(Bitmap | bit, next);
                }

                var slot = Slot(bit);
                var updated = Children[slot].Add(hash, key, value, shift + 5, comparer, out added);
                if (ReferenceEquals(updated, Children[slot]))
                    return this;
                var replaced = (Node[])Children.Clone();
                replaced[slot] = updated;
                return new Branch(Bitmap, replaced);
            }

            public override Node Set(int hash, TKey key, TValue value, int shift, IEqualityComparer<TKey> comparer, out TValue previous, out bool changed, out bool inserted)
            {
                var bit = Bit(hash, shift);
                if ((Bitmap & bit) == 0)
                {
                    previous = default(TValue);
                    changed = true;
                    inserted = true;
                    var index = Slot(bit);
                    var next = new Node[Children.Length + 1];
                    Array.Copy(Children, 0, next, 0, index);
                    next[index] = new Leaf(hash, key, value);
                    Array.Copy(Children, index, next, index + 1, Children.Length - index);
                    return new Branch(Bitmap | bit, next);
                }

                var slot = Slot(bit);
                var updated = Children[slot].Set(hash, key, value, shift + 5, comparer, out previous, out changed, out inserted);
                if (!changed || ReferenceEquals(updated, Children[slot]))
                    return this;
                var replaced = (Node[])Children.Clone();
                replaced[slot] = updated;
                return new Branch(Bitmap, replaced);
            }

            public override Node Remove(int hash, TKey key, int shift, IEqualityComparer<TKey> comparer, out bool removed, out TValue removedValue)
            {
                var bit = Bit(hash, shift);
                if ((Bitmap & bit) == 0)
                {
                    removed = false;
                    removedValue = default(TValue);
                    return this;
                }

                var slot = Slot(bit);
                var updated = Children[slot].Remove(hash, key, shift + 5, comparer, out removed, out removedValue);
                if (!removed || ReferenceEquals(updated, Children[slot]))
                    return this;
                if (updated == null)
                {
                    if (Children.Length == 1)
                        return null;
                    var next = new Node[Children.Length - 1];
                    Array.Copy(Children, 0, next, 0, slot);
                    Array.Copy(Children, slot + 1, next, slot, Children.Length - slot - 1);
                    return Collapse(new Branch(Bitmap ^ bit, next));
                }

                if (Children.Length == 1 && !(updated is Branch))
                    return updated;
                var replaced = (Node[])Children.Clone();
                replaced[slot] = updated;
                return new Branch(Bitmap, replaced);
            }

            public override void Collect(List<KeyValuePair<TKey, TValue>> into)
            {
                for (var i = 0; i < Children.Length; i++)
                    Children[i].Collect(into);
            }

            static Node Collapse(Branch branch)
            {
                if (branch.Children.Length == 1 && !(branch.Children[0] is Branch))
                    return branch.Children[0];
                return branch;
            }
        }

        static KeyValuePair<TKey, TValue> Pair(TKey key, TValue value)
        {
            return new KeyValuePair<TKey, TValue>(key, value);
        }
    }
}

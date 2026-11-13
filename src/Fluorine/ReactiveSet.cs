using System;
using System.Collections;
using System.Collections.Generic;

namespace Fluorine
{
    /// <summary>
    /// A hash set published as immutable versions.
    /// <see cref="Value"/> subscribes to every membership change.
    /// <see cref="Contains"/> subscribes only to that element's presence.
    /// A version shares unchanged structure with the version it was built from
    /// and does not point back at it, so a version nobody holds can be collected.
    /// Membership uses <see cref="EqualityComparer{T}"/> (Equals and GetHashCode).
    /// Add leaves an existing member in place. Diff reports <see cref="ChangeKind.Replaced"/>
    /// when two pictures hold different instances of the same member.
    /// Hash codes must stay stable while an element is in the set.
    /// </summary>
    public sealed class ReactiveSet<T> : Register, IEnumerable<T>
    {
        readonly IEqualityComparer<T> _comparer;
        readonly Dictionary<T, Membership> _memberships;
        readonly Version _empty;
        readonly bool _allowsNull;
        Version _version;
        Membership _nullMembership;

        public ReactiveSet(): this(null)
        {
        }

        public ReactiveSet(IEqualityComparer<T> comparer)
        {
            _comparer = comparer ?? EqualityComparer<T>.Default;
            _empty = Version.CreateEmpty(_comparer);
            _version = _empty;
            _allowsNull = default(T) == null;
            _memberships = new Dictionary<T, Membership>(_comparer);
        }

        public ReactiveSet(IEnumerable<T> items, IEqualityComparer<T> comparer = null): this(comparer)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            var version = _empty;
            foreach (var item in items)
                version = version.Added(item);
            _version = version.Count == 0 ? _empty : version;
        }

        public IEqualityComparer<T> Comparer => _comparer;

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

        public bool Contains(T item)
        {
            Version version;
            if (!Reaction.IsRecording)
            {
                lock (this)
                    version = _version;
                return version.Contains(item);
            }

            // Subscribe before reading, and keep that pair atomic with publication.
            lock (this)
            {
                if (!TryGetMembership(item, out var membership))
                {
                    membership = new Membership(this, item);
                    SetMembership(item, membership);
                }

                membership.RecordRead();
                version = _version;
            }

            return version.Contains(item);
        }

        public bool Add(T item)
        {
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                var next = current.Added(item);
                if (ReferenceEquals(next, current))
                    return false;

                Membership membership;
                lock (this)
                {
                    if (!ReferenceEquals(_version, current))
                        continue;
                    TryGetMembership(item, out membership);
                    _version = next;
                }

                TriggerReactions();
                if (membership != null)
                    membership.TriggerReactions();
                return true;
            }
        }

        public bool Remove(T item)
        {
            while (true)
            {
                Version current;
                lock (this)
                    current = _version;
                var next = current.Removed(item);
                if (ReferenceEquals(next, current))
                    return false;
                if (next.Count == 0)
                    next = _empty;

                Membership membership;
                lock (this)
                {
                    if (!ReferenceEquals(_version, current))
                        continue;
                    TryGetMembership(item, out membership);
                    _version = next;
                }

                TriggerReactions();
                if (membership != null)
                    membership.TriggerReactions();
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
                Membership membership;
                lock (this)
                    TryGetMembership(item, out membership);

                if (membership != null)
                    membership.TriggerReactions();
            }
        }

        public IEnumerator<T> GetEnumerator() => Value.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal int WatchedMemberships
        {
            get
            {
                lock (this)
                    return _memberships.Count + (_nullMembership == null ? 0 : 1);
            }
        }

        bool IsNullItem(T item) => _allowsNull && item == null;

        bool TryGetMembership(T item, out Membership membership)
        {
            if (IsNullItem(item))
            {
                membership = _nullMembership;
                return membership != null;
            }

            return _memberships.TryGetValue(item, out membership);
        }

        void SetMembership(T item, Membership membership)
        {
            if (IsNullItem(item))
                _nullMembership = membership;
            else
                _memberships.Add(item, membership);
        }

        void RemoveMembershipLocked(Membership membership)
        {
            if (IsNullItem(membership.Item))
            {
                if (ReferenceEquals(_nullMembership, membership))
                    _nullMembership = null;
                return;
            }

            if (_memberships.TryGetValue(membership.Item, out var current) &&
                ReferenceEquals(current, membership))
                _memberships.Remove(membership.Item);
        }

        sealed class Membership(ReactiveSet<T> owner, T item) : Register
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
                        owner.RemoveMembershipLocked(this);
                    return true;
                }
            }
        }

        /// <summary>
        /// One published picture of the set. Diff compares two pictures.
        /// It does not mark either of them as seen.
        /// </summary>
        public sealed class Version : IEnumerable<T>
        {
            readonly IEqualityComparer<T> _comparer;
            readonly Node _root;

            Version(Node root, int count, IEqualityComparer<T> comparer)
            {
                _root = root;
                Count = count;
                _comparer = comparer ?? EqualityComparer<T>.Default;
            }

            internal static Version CreateEmpty(IEqualityComparer<T> comparer)
            {
                return new Version(null, 0, comparer);
            }

            public int Count { get; }
            
            public bool Contains(T item)
            {
                if (_root == null)
                    return false;
                return _root.Contains(_comparer.GetHashCode(item), item, 0, _comparer);
            }

            public Change[] Diff(Version previous)
            {
                var older = previous?._root;
                if (ReferenceEquals(_root, older))
                    return [];
                var changes = new List<Change>();
                DiffNodes(older, _root, _comparer, changes);
                return changes.Count == 0 ? [] : [.. changes];
            }
            
            public IEnumerator<T> GetEnumerator() => _root.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            internal Version Added(T item)
            {
                var hash = _comparer.GetHashCode(item);
                if (_root == null)
                    return new Version(new Leaf(hash, item), 1, _comparer);
                var root = _root.Add(hash, item, 0, _comparer, out var added);
                if (!added)
                    return this;
                return new Version(root, Count + 1, _comparer);
            }

            internal Version Removed(T item)
            {
                if (_root == null)
                    return this;
                var hash = _comparer.GetHashCode(item);
                var root = _root.Remove(hash, item, 0, _comparer, out var removed);
                if (!removed)
                    return this;
                if (root == null)
                    return new Version(null, 0, _comparer);
                return new Version(root, Count - 1, _comparer);
            }

            static void DiffNodes(Node older, Node newer, IEqualityComparer<T> comparer, List<Change> changes)
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

                var oldItems = new List<T>();
                if (older!=null) oldItems.AddRange(older);
                var newItems = new List<T>();
                if (newer!=null) newItems.AddRange(newer);
                
                var newIndex = new Dictionary<T, int>(newItems.Count, comparer);
                var nullIndex = -1;
                for (var j = 0; j < newItems.Count; j++)
                {
                    if (newItems[j] == null) nullIndex = j;
                    else newIndex[newItems[j]] = j;
                }
                var matched = new bool[newItems.Count];
                foreach (var old in oldItems)
                {
                    int j;
                    var found = old == null ? (j = nullIndex) >= 0 : newIndex.TryGetValue(old, out j);
                    if (!found)
                    {
                        changes.Add(new Change(ChangeKind.Removed, old));
                        continue;
                    }
                    matched[j] = true;
                    if (!SameInstance(old, newItems[j]))
                        changes.Add(new Change(ChangeKind.Replaced, newItems[j], old));
                }
                for (var j = 0; j < newItems.Count; j++)
                    if (!matched[j])
                        changes.Add(new Change(ChangeKind.Added, newItems[j]));
            }
        }

        public enum ChangeKind
        {
            Added,
            Removed,
            Replaced
        }

        public readonly struct Change(ChangeKind kind, T item, T previous)
        {
            public Change(ChangeKind kind, T item) : this(kind, item, default)
            {
            }

            public ChangeKind Kind { get; } = kind;

            /// <summary>The added instance, the removed instance, or the instance now stored.</summary>
            public T Item { get; } = item;

            /// <summary>The instance that was stored, when <see cref="Kind"/> is <see cref="ChangeKind.Replaced"/>.</summary>
            public T Previous { get; } = previous;
        }

        // Equals already matched. Value types have no instance to tell apart.
        static bool SameInstance(T stored, T incoming)
        {
            if (typeof(T).IsValueType)
                return true;
            return ReferenceEquals(stored, incoming);
        }

        abstract class Node: IEnumerable<T>
        {
            public abstract bool Contains(int hash, T item, int shift, IEqualityComparer<T> comparer);
            public abstract Node Add(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool added);
            public abstract Node Remove(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool removed);

            private static int Index(int hash, int shift) => (int)(((uint)hash >> shift) & 31);
            protected static uint Bit(int hash, int shift) => 1u << Index(hash, shift);

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

            protected static Node Join(int hashA, Node a, int hashB, Node b, int shift)
            {
                if (hashA == hashB)
                    throw new InvalidOperationException("Cannot join the same hashes");
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

                var child = Join(hashA, a, hashB, b, shift + 5);
                return new Branch(1u << ia, [child]);
            }
            
            sealed class Enumerator : IEnumerator<T>
            {
                // 32-bit hash, 5 bits per level: at most 7 branch levels, plus the root slot.
                const int MaxDepth = 8;
                readonly Node[][] _nodes = new Node[MaxDepth][];
                readonly int[] _next = new int[MaxDepth];
                int _depth;
                T[] _collision;
                int _collisionNext;
                public Enumerator(Node root)
                {
                    _nodes[0] = [root];
                }
                public T Current { get; private set; }
                object IEnumerator.Current => Current;
                public bool MoveNext()
                {
                    if (_collision != null)
                    {
                        if (_collisionNext < _collision.Length)
                        {
                            Current = _collision[_collisionNext++];
                            return true;
                        }
                        _collision = null;
                    }
                    while (_depth >= 0)
                    {
                        var nodes = _nodes[_depth];
                        if (_next[_depth] == nodes.Length)
                        {
                            _depth--;
                            continue;
                        }
                        switch (nodes[_next[_depth]++])
                        {
                            case Leaf leaf:
                                Current = leaf.Item;
                                return true;
                            case Collision collision:
                                _collision = collision.Items;
                                _collisionNext = 1;
                                Current = collision.Items[0];
                                return true;
                            case Branch branch:
                                _depth++;
                                _nodes[_depth] = branch.Children;
                                _next[_depth] = 0;
                                break;
                        }
                    }
                    return false;
                }
                public void Reset()
                {
                    _depth = 0;
                    _next[0] = 0;
                    _collision = null;
                }
                public void Dispose()
                {
                }
            }

            public IEnumerator<T> GetEnumerator() => new Enumerator(this);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class Leaf(int hash, T item) : Node
        {
            private int Hash { get; } = hash;
            public T Item { get; } = item;

            public override bool Contains(int hash, T item, int shift, IEqualityComparer<T> comparer) 
                => Hash == hash && comparer.Equals(Item, item);

            public override Node Add(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool added)
            {
                if (Hash == hash && comparer.Equals(Item, item))
                {
                    added = false;
                    return this;
                }

                added = true;
                if (Hash == hash)
                    return new Collision(hash, [Item, item]);
                return Join(Hash, this, hash, new Leaf(hash, item), shift);
            }

            public override Node Remove(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool removed)
            {
                if (Hash == hash && comparer.Equals(Item, item))
                {
                    removed = true;
                    return null;
                }

                removed = false;
                return this;
            }
        }

        private sealed class Collision(int hash, T[] items) : Node
        {
            private int Hash { get; } = hash;
            public T[] Items { get; } = items;

            public override bool Contains(int hash, T item, int shift, IEqualityComparer<T> comparer)
            {
                if (Hash != hash)
                    return false;
                for (var i = 0; i < Items.Length; i++)
                {
                    if (comparer.Equals(Items[i], item))
                        return true;
                }

                return false;
            }

            public override Node Add(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool added)
            {
                if (Hash != hash)
                {
                    added = true;
                    return Join(Hash, this, hash, new Leaf(hash, item), shift);
                }

                for (var i = 0; i < Items.Length; i++)
                {
                    if (comparer.Equals(Items[i], item))
                    {
                        added = false;
                        return this;
                    }
                }

                added = true;
                var next = new T[Items.Length + 1];
                Array.Copy(Items, next, Items.Length);
                next[Items.Length] = item;
                return new Collision(Hash, next);
            }

            public override Node Remove(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool removed)
            {
                if (Hash != hash)
                {
                    removed = false;
                    return this;
                }

                var index = -1;
                for (var i = 0; i < Items.Length; i++)
                {
                    if (comparer.Equals(Items[i], item))
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    removed = false;
                    return this;
                }

                removed = true;
                if (Items.Length == 1)
                    return null;
                if (Items.Length == 2)
                    return new Leaf(Hash, Items[index == 0 ? 1 : 0]);
                var next = new T[Items.Length - 1];
                Array.Copy(Items, 0, next, 0, index);
                Array.Copy(Items, index + 1, next, index, Items.Length - index - 1);
                return new Collision(Hash, next);
            }
        }

        sealed class Branch(uint bitmap, Node[] children) : Node
        {
            public uint Bitmap { get; } = bitmap;
            public Node[] Children { get; } = children;

            public int Slot(uint bit)
            {
                return PopCount(Bitmap & (bit - 1));
            }

            public override bool Contains(int hash, T item, int shift, IEqualityComparer<T> comparer)
            {
                var bit = Bit(hash, shift);
                if ((Bitmap & bit) == 0)
                    return false;
                return Children[Slot(bit)].Contains(hash, item, shift + 5, comparer);
            }

            public override Node Add(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool added)
            {
                var bit = Bit(hash, shift);
                if ((Bitmap & bit) == 0)
                {
                    added = true;
                    var index = Slot(bit);
                    var next = new Node[Children.Length + 1];
                    Array.Copy(Children, 0, next, 0, index);
                    next[index] = new Leaf(hash, item);
                    Array.Copy(Children, index, next, index + 1, Children.Length - index);
                    return new Branch(Bitmap | bit, next);
                }

                var slot = Slot(bit);
                var updated = Children[slot].Add(hash, item, shift + 5, comparer, out added);
                if (ReferenceEquals(updated, Children[slot]))
                    return this;
                var copied = (Node[])Children.Clone();
                copied[slot] = updated;
                return new Branch(Bitmap, copied);
            }

            public override Node Remove(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool removed)
            {
                var bit = Bit(hash, shift);
                if ((Bitmap & bit) == 0)
                {
                    removed = false;
                    return this;
                }

                var slot = Slot(bit);
                var updated = Children[slot].Remove(hash, item, shift + 5, comparer, out removed);
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

            static Node Collapse(Branch branch)
            {
                if (branch.Children.Length == 1 && !(branch.Children[0] is Branch))
                    return branch.Children[0];
                return branch;
            }
        }
    }
}

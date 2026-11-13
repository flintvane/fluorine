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
    /// Add leaves an existing member in place. Diff reports <see cref="ChangeKind.Replace"/>
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

        public ReactiveSet()
            : this((IEqualityComparer<T>)null)
        {
        }

        public ReactiveSet(IEqualityComparer<T> comparer)
        {
            _comparer = comparer ?? EqualityComparer<T>.Default;
            _empty = Version.CreateEmpty(_comparer);
            _version = _empty;
            _allowsNull = (object)default(T) == null;
            _memberships = new Dictionary<T, Membership>(_comparer);
        }

        public ReactiveSet(IEnumerable<T> items)
            : this(items, null)
        {
        }

        public ReactiveSet(IEnumerable<T> items, IEqualityComparer<T> comparer)
            : this(comparer)
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

        bool IsNullItem(T item)
        {
            return _allowsNull && (object)item == null;
        }

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

        sealed class Membership : Register
        {
            readonly ReactiveSet<T> _owner;
            int _listeners;

            public Membership(ReactiveSet<T> owner, T item)
            {
                _owner = owner;
                Item = item;
            }

            public T Item { get; }

            // I am a bit surprised on why this is done like thiat
            // So the syncroot would effectively 
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
                        _owner.RemoveMembershipLocked(this);
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

            public bool SameAs(Version other)
            {
                if (ReferenceEquals(this, other))
                    return true;
                return ReferenceEquals(_root, other == null ? null : other._root);
            }

            public bool Contains(T item)
            {
                if (_root == null)
                    return false;
                return _root.Contains(_comparer.GetHashCode(item), item, 0, _comparer);
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

            public IEnumerator<T> GetEnumerator() 
                => _root == null ? Empty() : _root.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            static IEnumerator<T> Empty()
            {
                yield break;
            }

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
                var matched = new bool[newItems.Count];
                for (var i = 0; i < oldItems.Count; i++)
                {
                    var found = -1;
                    for (var j = 0; j < newItems.Count; j++)
                    {
                        if (matched[j] || !comparer.Equals(oldItems[i], newItems[j]))
                            continue;
                        found = j;
                        break;
                    }

                    if (found < 0)
                    {
                        changes.Add(new Change(ChangeKind.Removed, oldItems[i]));
                        continue;
                    }

                    matched[found] = true;
                    if (!SameInstance(oldItems[i], newItems[found]))
                        changes.Add(new Change(ChangeKind.Replace, newItems[found], oldItems[i]));
                }

                for (var j = 0; j < newItems.Count; j++)
                {
                    if (!matched[j])
                        changes.Add(new Change(ChangeKind.Added, newItems[j]));
                }
            }
        }

        public enum ChangeKind
        {
            Added,
            Removed,
            Replace
        }

        public readonly struct Change
        {
            public Change(ChangeKind kind, T item) : this(kind, item, default)
            {
            }

            public Change(ChangeKind kind, T item, T previous)
            {
                Kind = kind;
                Item = item;
                Previous = previous;
            }

            public ChangeKind Kind { get; }

            /// <summary>The added instance, the removed instance, or the instance now stored.</summary>
            public T Item { get; }

            /// <summary>The instance that was stored, when <see cref="Kind"/> is <see cref="ChangeKind.Replace"/>.</summary>
            public T Previous { get; }
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

            protected static Node Join(int hashA, Node a, int hashB, Node b, int shift, IEqualityComparer<T> comparer)
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

            public abstract IEnumerator<T> GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        sealed class Leaf : Node
        {
            public Leaf(int hash, T item)
            {
                Hash = hash;
                Item = item;
            }

            public int Hash { get; }
            public T Item { get; }

            public override bool Contains(int hash, T item, int shift, IEqualityComparer<T> comparer)
            {
                return Hash == hash && comparer.Equals(Item, item);
            }

            public override Node Add(int hash, T item, int shift, IEqualityComparer<T> comparer, out bool added)
            {
                if (Hash == hash && comparer.Equals(Item, item))
                {
                    added = false;
                    return this;
                }

                added = true;
                if (Hash == hash)
                    return new Collision(hash, new[] { Item, item });
                return Join(Hash, this, hash, new Leaf(hash, item), shift, comparer);
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
            
            public override IEnumerator<T> GetEnumerator()
            {
                yield return Item;
            }
        }

        sealed class Collision : Node
        {
            public Collision(int hash, T[] items)
            {
                Hash = hash;
                Items = items;
            }

            public int Hash { get; }
            public T[] Items { get; }

            public static Collision Merge(int hash, Node a, Node b, IEqualityComparer<T> comparer)
            {
                var items = new List<T>(a);
                var more = new List<T>(b);
                for (var i = 0; i < more.Count; i++)
                {
                    var found = false;
                    for (var j = 0; j < items.Count; j++)
                    {
                        if (comparer.Equals(items[j], more[i]))
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
                    return Join(Hash, this, hash, new Leaf(hash, item), shift, comparer);
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

            public override IEnumerator<T> GetEnumerator() 
                => ((IEnumerable<T>)Items).GetEnumerator();
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

            public override IEnumerator<T> GetEnumerator()
            {
                foreach (var a in Children)
                     foreach(var b in a)
                         yield return b;
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

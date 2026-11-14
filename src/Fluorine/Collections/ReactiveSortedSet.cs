using System;
using System.Collections;
using System.Collections.Generic;

namespace Fluorine
{
    /// <summary>
    /// A sorted set published as immutable versions.
    /// <see cref="Value"/> subscribes to every membership change.
    /// <see cref="Contains"/> subscribes only to that element's presence.
    /// A version shares unchanged blocks with the version it was built from
    /// and does not point back at it, so a version nobody holds can be collected.
    /// Membership uses <see cref="IComparer{T}"/>. Compare returning zero means
    /// the same element. Add leaves an existing member in place. Diff reports
    /// <see cref="ChangeKind.Replaced"/> when two pictures hold different instances
    /// of the same member. Enumeration and <see cref="Version.Diff"/> run in
    /// comparer order. Comparison results must stay stable while an element is in the set.
    /// </summary>
    public sealed class ReactiveSortedSet<T> : Register, IEnumerable<T>
    {
        readonly IComparer<T> _comparer;
        readonly SortedDictionary<T, Membership> _memberships;
        readonly Version _empty;
        readonly bool _allowsNull;
        Version _version;
        Membership _nullMembership;

        public ReactiveSortedSet()
            : this((IComparer<T>)null)
        {
        }

        public ReactiveSortedSet(IComparer<T> comparer)
        {
            _comparer = comparer ?? Comparer<T>.Default;
            _empty = Version.CreateEmpty(_comparer);
            _version = _empty;
            _allowsNull = default(T) == null;
            _memberships = new SortedDictionary<T, Membership>(_comparer);
        }

        public ReactiveSortedSet(IEnumerable<T> items, IComparer<T> comparer = null)
            : this(comparer)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            var version = _empty;
            foreach (var item in items)
                version = version.Added(item);
            _version = version.Count == 0 ? _empty : version;
        }

        public IComparer<T> Comparer => _comparer;

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
                {
                    if (Still(current))
                        return false;
                    continue;
                }

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
                {
                    if (Still(current))
                        return false;
                    continue;
                }

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

        internal void CheckInvariants()
        {
            Version version;
            lock (this)
                version = _version;
            version.Check();
        }

        bool Still(Version current)
        {
            lock (this)
                return ReferenceEquals(_version, current);
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

        sealed class Membership(ReactiveSortedSet<T> owner, T item) : Register
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
        /// One published picture of the set. Enumeration and <see cref="Diff"/>
        /// run in comparer order. Diff compares two pictures. It does not mark
        /// either of them as seen.
        /// </summary>
        public sealed class Version : IEnumerable<T>
        {
            readonly IComparer<T> _comparer;
            readonly BlockTree<T>.Node _root;

            Version(BlockTree<T>.Node root, IComparer<T> comparer)
            {
                _root = root;
                _comparer = comparer ?? Comparer<T>.Default;
            }

            internal static Version CreateEmpty(IComparer<T> comparer)
            {
                return new Version(null, comparer);
            }

            public int Count => _root == null ? 0 : _root.Count;

            public bool Contains(T item)
            {
                return BlockTree<T>.TryGet(_root, item, _comparer, out _);
            }

            public Change[] Diff(Version previous)
            {
                var older = previous == null ? null : previous._root;
                if (ReferenceEquals(_root, older))
                    return [];
                var changes = new List<Change>();
                BlockTree<T>.Diff(older, _root, _comparer, new Sink(changes));
                return changes.Count == 0 ? [] : changes.ToArray();
            }

            public IEnumerator<T> GetEnumerator() => new BlockTree<T>.Enumerator(_root);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            internal int NodeCount => BlockTree<T>.CountNodes(_root);

            internal int SharedWith(Version other)
            {
                return BlockTree<T>.CountShared(_root, other == null ? null : other._root);
            }

            internal void Check()
            {
                BlockTree<T>.Check(_root, _comparer);
            }

            internal Version Added(T item)
            {
                var root = BlockTree<T>.Add(_root, item, _comparer, out var added);
                if (!added)
                    return this;
                return new Version(root, _comparer);
            }

            internal Version Removed(T item)
            {
                var root = BlockTree<T>.Remove(_root, item, _comparer, out var removed);
                if (!removed)
                    return this;
                if (root == null)
                    return new Version(null, _comparer);
                return new Version(root, _comparer);
            }

            readonly struct Sink(List<Change> changes) : BlockTree<T>.IDiff
            {
                public void Added(T item)
                {
                    changes.Add(new Change(ChangeKind.Added, item));
                }

                public void Removed(T item)
                {
                    changes.Add(new Change(ChangeKind.Removed, item));
                }

                public void Matched(T older, T newer)
                {
                    if (!SameInstance(older, newer))
                        changes.Add(new Change(ChangeKind.Replaced, newer, older));
                }
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
            public Change(ChangeKind kind, T item)
                : this(kind, item, default)
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
    }
}

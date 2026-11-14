using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Fluorine
{
    /// <summary>
    /// Persistent B+ tree ordered by an <see cref="IComparer{T}"/>.
    /// A leaf holds at most five elements and a branch at most five children,
    /// the same fanout the hash trie uses per level. Path copies share every
    /// block a write did not touch, and a diff skips a block both pictures
    /// still point at. A hash trie cannot do this: its shape follows hash
    /// bits, not comparer order.
    /// </summary>
    internal static class BlockTree<T>
    {
        internal const int MaxItems = 5;

        // A non-root branch keeps at least two children, so the height stays
        // logarithmic. A leaf may shrink to one element; it borrows or merges
        // when a sibling is there to take part.
        const int MinItems = 2;
        const int MaxDepth = 48;

        internal abstract class Node
        {
            public abstract int Count { get; }
            public abstract T First { get; }
            public abstract T Last { get; }
        }

        internal sealed class Leaf : Node
        {
            public readonly T[] Items;

            public Leaf(T[] items)
            {
                if (items == null || items.Length == 0 || items.Length > MaxItems)
                    throw new InvalidOperationException("A block must hold 1 to " + MaxItems + " elements.");
                Items = items;
            }

            public override int Count => Items.Length;
            public override T First => Items[0];
            public override T Last => Items[Items.Length - 1];
        }

        internal sealed class Branch : Node
        {
            public readonly Node[] Children;

            public Branch(Node[] children)
            {
                if (children == null || children.Length == 0 || children.Length > MaxItems)
                    throw new InvalidOperationException("A block branch must hold 1 to " + MaxItems + " children.");
                Children = children;
                var count = 0;
                for (var i = 0; i < children.Length; i++)
                {
                    if (children[i] == null)
                        throw new InvalidOperationException("A block branch child is missing.");
                    count += children[i].Count;
                }

                Count = count;
                First = children[0].First;
                Last = children[children.Length - 1].Last;
            }

            public override int Count { get; }
            public override T First { get; }
            public override T Last { get; }
        }

        internal interface IReplace
        {
            /// <summary>
            /// The stored item compares equal to <paramref name="incoming"/>.
            /// Return false to leave it. The replacement must compare equal to it.
            /// </summary>
            bool Replace(T existing, T incoming, out T stored);
        }

        internal interface IDiff
        {
            void Added(T item);
            void Removed(T item);
            void Matched(T older, T newer);
        }

        internal static Node Add(Node root, T item, IComparer<T> comparer, out bool added)
        {
            var next = Update(root, item, comparer, default(KeepExisting), out _, out var changed, out added);
            return changed ? next : root;
        }

        internal static Node Update<TReplace>(Node root, T incoming, IComparer<T> comparer, TReplace replace,
            out T previous, out bool changed, out bool inserted)
            where TReplace : IReplace
        {
            if (root == null)
            {
                previous = default;
                changed = true;
                inserted = true;
                return new Leaf(new[] { incoming });
            }

            var edit = UpdateNode(root, incoming, comparer, replace);
            previous = edit.Previous;
            changed = edit.Changed;
            inserted = edit.Inserted;
            if (!changed)
                return root;
            if (edit.SplitRight == null)
                return edit.Node;
            return new Branch(new[] { edit.Node, edit.SplitRight });
        }

        internal static Node Remove(Node root, T item, IComparer<T> comparer, out bool removed)
        {
            if (root == null)
            {
                removed = false;
                return null;
            }

            var updated = Delete(root, item, comparer, out removed);
            if (!removed)
                return root;
            return Collapse(updated);
        }

        internal static bool TryGet(Node root, T probe, IComparer<T> comparer, out T stored)
        {
            var node = root;
            while (node is Branch branch)
                node = branch.Children[FindChild(branch, probe, comparer)];
            if (node is Leaf leaf)
            {
                var items = leaf.Items;
                for (var i = 0; i < items.Length; i++)
                {
                    var cmp = comparer.Compare(probe, items[i]);
                    if (cmp == 0)
                    {
                        stored = items[i];
                        return true;
                    }

                    if (cmp < 0)
                        break;
                }
            }

            stored = default;
            return false;
        }

        // Position of the element in comparer order, or -1 when absent. Each
        // branch knows the size of its children, so skipping a child adds its
        // count instead of walking it: O(log n).
        internal static int IndexOf(Node root, T probe, IComparer<T> comparer)
        {
            var index = 0;
            var node = root;
            while (node is Branch branch)
            {
                var child = FindChild(branch, probe, comparer);
                for (var i = 0; i < child; i++)
                    index += branch.Children[i].Count;
                node = branch.Children[child];
            }

            if (node is Leaf leaf)
            {
                var items = leaf.Items;
                for (var i = 0; i < items.Length; i++)
                {
                    var cmp = comparer.Compare(probe, items[i]);
                    if (cmp == 0)
                        return index + i;
                    if (cmp < 0)
                        break;
                }
            }

            return -1;
        }

        internal static void Diff<TSink>(Node older, Node newer, IComparer<T> comparer, TSink sink)
            where TSink : IDiff
        {
            if (ReferenceEquals(older, newer))
                return;
            var left = new Cursor(older);
            var right = new Cursor(newer);
            var guard = (older == null ? 0 : older.Count) + (newer == null ? 0 : newer.Count) + 1;
            var steps = 0;
            while (left.Has && right.Has)
            {
                if (++steps > guard)
                    throw new InvalidOperationException("Sorted diff did not advance.");
                var shared = LargestSharedStart(left, right);
                if (shared != null)
                {
                    left.SkipThrough(shared);
                    right.SkipThrough(shared);
                    continue;
                }

                var cmp = comparer.Compare(left.Current, right.Current);
                if (cmp == 0)
                {
                    sink.Matched(left.Current, right.Current);
                    left.MoveNext();
                    right.MoveNext();
                }
                else if (cmp < 0)
                {
                    sink.Removed(left.Current);
                    left.MoveNext();
                }
                else
                {
                    sink.Added(right.Current);
                    right.MoveNext();
                }
            }

            while (left.Has)
            {
                if (++steps > guard)
                    throw new InvalidOperationException("Sorted diff did not advance.");
                sink.Removed(left.Current);
                left.MoveNext();
            }

            while (right.Has)
            {
                if (++steps > guard)
                    throw new InvalidOperationException("Sorted diff did not advance.");
                sink.Added(right.Current);
                right.MoveNext();
            }
        }

        internal static void Check(Node root, IComparer<T> comparer)
        {
            if (comparer == null)
                throw new ArgumentNullException(nameof(comparer));
            if (root == null)
                return;
            var leafDepth = -1;
            CheckNode(root, comparer, false, default, false, default, 0, ref leafDepth);
        }

        internal static int CountNodes(Node node)
        {
            if (node == null)
                return 0;
            if (!(node is Branch branch))
                return 1;
            var count = 1;
            var children = branch.Children;
            for (var i = 0; i < children.Length; i++)
                count += CountNodes(children[i]);
            return count;
        }

        internal static int CountShared(Node left, Node right)
        {
            if (left == null || right == null)
                return 0;
            var seen = new HashSet<Node>(CountNodes(left), ReferenceEqual<Node>.Instance);
            Collect(left, seen);
            return Hits(right, seen);
        }

        internal sealed class Enumerator : IEnumerator<T>
        {
            readonly Node _root;
            Cursor _cursor;
            bool _started;

            public Enumerator(Node root)
            {
                _root = root;
            }

            public T Current => _cursor == null ? default : _cursor.Current;

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (!_started)
                {
                    _started = true;
                    _cursor = new Cursor(_root);
                    return _cursor.Has;
                }

                return _cursor != null && _cursor.MoveNext();
            }

            public void Reset()
            {
                _started = false;
                _cursor = null;
            }

            public void Dispose()
            {
            }
        }

        struct Edit
        {
            public Node Node;
            public Node SplitRight;
            public bool Changed;
            public bool Inserted;
            public T Previous;
        }

        struct KeepExisting : IReplace
        {
            public bool Replace(T existing, T incoming, out T stored)
            {
                stored = existing;
                return false;
            }
        }

        static Edit UpdateNode<TReplace>(Node node, T incoming, IComparer<T> comparer, TReplace replace)
            where TReplace : IReplace
        {
            if (node is Leaf leaf)
                return UpdateLeaf(leaf, incoming, comparer, replace);

            var branch = (Branch)node;
            var index = FindChild(branch, incoming, comparer);
            var child = UpdateNode(branch.Children[index], incoming, comparer, replace);
            if (!child.Changed)
            {
                return new Edit
                {
                    Node = branch,
                    Previous = child.Previous,
                    Changed = false,
                    Inserted = false
                };
            }

            Node replacement;
            Node splitRight = null;
            if (child.SplitRight == null)
            {
                var children = (Node[])branch.Children.Clone();
                children[index] = child.Node;
                replacement = new Branch(children);
            }
            else
            {
                var count = branch.Children.Length + 1;
                var expanded = new Node[count];
                Array.Copy(branch.Children, 0, expanded, 0, index);
                expanded[index] = child.Node;
                expanded[index + 1] = child.SplitRight;
                var tail = branch.Children.Length - index - 1;
                if (tail > 0)
                    Array.Copy(branch.Children, index + 1, expanded, index + 2, tail);
                if (count <= MaxItems)
                {
                    replacement = new Branch(expanded);
                }
                else
                {
                    SplitChildren(expanded, out var left, out var right);
                    replacement = left;
                    splitRight = right;
                }
            }

            return new Edit
            {
                Node = replacement,
                SplitRight = splitRight,
                Changed = true,
                Inserted = child.Inserted,
                Previous = child.Previous
            };
        }

        static Edit UpdateLeaf<TReplace>(Leaf leaf, T incoming, IComparer<T> comparer, TReplace replace)
            where TReplace : IReplace
        {
            var items = leaf.Items;
            var index = 0;
            while (index < items.Length)
            {
                var cmp = comparer.Compare(incoming, items[index]);
                if (cmp == 0)
                {
                    if (!replace.Replace(items[index], incoming, out var stored))
                    {
                        return new Edit
                        {
                            Node = leaf,
                            Previous = items[index],
                            Changed = false,
                            Inserted = false
                        };
                    }

                    var replacedItems = (T[])items.Clone();
                    replacedItems[index] = stored;
                    return new Edit
                    {
                        Node = new Leaf(replacedItems),
                        Previous = items[index],
                        Changed = true,
                        Inserted = false
                    };
                }

                if (cmp < 0)
                    break;
                index++;
            }

            var next = new T[items.Length + 1];
            Array.Copy(items, 0, next, 0, index);
            next[index] = incoming;
            if (index < items.Length)
                Array.Copy(items, index, next, index + 1, items.Length - index);
            if (next.Length <= MaxItems)
            {
                return new Edit
                {
                    Node = new Leaf(next),
                    Changed = true,
                    Inserted = true
                };
            }

            SplitItems(next, out var left, out var right);
            return new Edit
            {
                Node = left,
                SplitRight = right,
                Changed = true,
                Inserted = true
            };
        }

        static Node Delete(Node node, T item, IComparer<T> comparer, out bool removed)
        {
            if (node is Leaf leaf)
                return DeleteLeaf(leaf, item, comparer, out removed);
            return DeleteBranch((Branch)node, item, comparer, out removed);
        }

        static Node DeleteLeaf(Leaf leaf, T item, IComparer<T> comparer, out bool removed)
        {
            var items = leaf.Items;
            var index = -1;
            for (var i = 0; i < items.Length; i++)
            {
                var cmp = comparer.Compare(item, items[i]);
                if (cmp == 0)
                {
                    index = i;
                    break;
                }

                if (cmp < 0)
                    break;
            }

            if (index < 0)
            {
                removed = false;
                return leaf;
            }

            removed = true;
            if (items.Length == 1)
                return null;
            var next = new T[items.Length - 1];
            Array.Copy(items, 0, next, 0, index);
            Array.Copy(items, index + 1, next, index, items.Length - index - 1);
            return new Leaf(next);
        }

        static Node DeleteBranch(Branch branch, T item, IComparer<T> comparer, out bool removed)
        {
            var index = FindChild(branch, item, comparer);
            var updated = Delete(branch.Children[index], item, comparer, out removed);
            if (!removed)
                return branch;
            return Install(branch, index, updated);
        }

        static Node Install(Branch branch, int index, Node updated)
        {
            var list = new List<Node>(branch.Children.Length);
            for (var i = 0; i < branch.Children.Length; i++)
                list.Add(branch.Children[i]);
            if (updated == null)
            {
                list.RemoveAt(index);
            }
            else
            {
                list[index] = updated;
                if (Size(updated) < MinItems)
                    FixUnderfull(list, index);
            }

            if (list.Count == 0)
                return null;
            return new Branch(list.ToArray());
        }

        static void FixUnderfull(List<Node> siblings, int index)
        {
            var need = MinItems - Size(siblings[index]);
            if (need <= 0)
                return;
            if (index > 0 && CanGive(siblings[index - 1]) >= need)
            {
                GiveFromLeft(siblings, index - 1, need);
                return;
            }

            if (index + 1 < siblings.Count && CanGive(siblings[index + 1]) >= need)
            {
                GiveFromRight(siblings, index, need);
                return;
            }

            if (index > 0)
            {
                Merge(siblings, index - 1);
                return;
            }

            if (index + 1 < siblings.Count)
                Merge(siblings, index);
        }

        static void GiveFromLeft(List<Node> siblings, int leftIndex, int count)
        {
            var rightIndex = leftIndex + 1;
            if (siblings[leftIndex] is Leaf leftLeaf)
            {
                var rightLeaf = (Leaf)siblings[rightIndex];
                var leftItems = leftLeaf.Items;
                var rightItems = rightLeaf.Items;
                var newLeft = new T[leftItems.Length - count];
                var newRight = new T[rightItems.Length + count];
                Array.Copy(leftItems, 0, newLeft, 0, newLeft.Length);
                Array.Copy(leftItems, newLeft.Length, newRight, 0, count);
                Array.Copy(rightItems, 0, newRight, count, rightItems.Length);
                siblings[leftIndex] = new Leaf(newLeft);
                siblings[rightIndex] = new Leaf(newRight);
                return;
            }

            var left = (Branch)siblings[leftIndex];
            var right = (Branch)siblings[rightIndex];
            var newLeftChildren = new Node[left.Children.Length - count];
            var newRightChildren = new Node[right.Children.Length + count];
            Array.Copy(left.Children, 0, newLeftChildren, 0, newLeftChildren.Length);
            Array.Copy(left.Children, newLeftChildren.Length, newRightChildren, 0, count);
            Array.Copy(right.Children, 0, newRightChildren, count, right.Children.Length);
            siblings[leftIndex] = new Branch(newLeftChildren);
            siblings[rightIndex] = new Branch(newRightChildren);
        }

        static void GiveFromRight(List<Node> siblings, int leftIndex, int count)
        {
            var rightIndex = leftIndex + 1;
            if (siblings[leftIndex] is Leaf leftLeaf)
            {
                var rightLeaf = (Leaf)siblings[rightIndex];
                var leftItems = leftLeaf.Items;
                var rightItems = rightLeaf.Items;
                var newLeft = new T[leftItems.Length + count];
                var newRight = new T[rightItems.Length - count];
                Array.Copy(leftItems, 0, newLeft, 0, leftItems.Length);
                Array.Copy(rightItems, 0, newLeft, leftItems.Length, count);
                Array.Copy(rightItems, count, newRight, 0, newRight.Length);
                siblings[leftIndex] = new Leaf(newLeft);
                siblings[rightIndex] = new Leaf(newRight);
                return;
            }

            var left = (Branch)siblings[leftIndex];
            var right = (Branch)siblings[rightIndex];
            var newLeftChildren = new Node[left.Children.Length + count];
            var newRightChildren = new Node[right.Children.Length - count];
            Array.Copy(left.Children, 0, newLeftChildren, 0, left.Children.Length);
            Array.Copy(right.Children, 0, newLeftChildren, left.Children.Length, count);
            Array.Copy(right.Children, count, newRightChildren, 0, newRightChildren.Length);
            siblings[leftIndex] = new Branch(newLeftChildren);
            siblings[rightIndex] = new Branch(newRightChildren);
        }

        static void Merge(List<Node> siblings, int leftIndex)
        {
            var rightIndex = leftIndex + 1;
            if (siblings[leftIndex] is Leaf leftLeaf)
            {
                var rightLeaf = (Leaf)siblings[rightIndex];
                if (leftLeaf.Items.Length + rightLeaf.Items.Length > MaxItems)
                    throw new InvalidOperationException("Leaf merge exceeded block capacity.");
                var items = new T[leftLeaf.Items.Length + rightLeaf.Items.Length];
                Array.Copy(leftLeaf.Items, items, leftLeaf.Items.Length);
                Array.Copy(rightLeaf.Items, 0, items, leftLeaf.Items.Length, rightLeaf.Items.Length);
                siblings[leftIndex] = new Leaf(items);
            }
            else
            {
                var left = (Branch)siblings[leftIndex];
                var right = (Branch)siblings[rightIndex];
                if (left.Children.Length + right.Children.Length > MaxItems)
                    throw new InvalidOperationException("Branch merge exceeded block capacity.");
                var children = new Node[left.Children.Length + right.Children.Length];
                Array.Copy(left.Children, children, left.Children.Length);
                Array.Copy(right.Children, 0, children, left.Children.Length, right.Children.Length);
                siblings[leftIndex] = new Branch(children);
            }

            siblings.RemoveAt(rightIndex);
        }

        static Node Collapse(Node node)
        {
            while (node is Branch branch && branch.Children.Length == 1)
                node = branch.Children[0];
            return node;
        }

        static int FindChild(Branch branch, T key, IComparer<T> comparer)
        {
            var children = branch.Children;
            var index = 0;
            while (index + 1 < children.Length && comparer.Compare(key, children[index + 1].First) >= 0)
                index++;
            return index;
        }

        static int Size(Node node)
        {
            if (node is Leaf leaf)
                return leaf.Items.Length;
            return ((Branch)node).Children.Length;
        }

        static int CanGive(Node node)
        {
            return Size(node) - MinItems;
        }

        static void SplitItems(T[] items, out Leaf left, out Leaf right)
        {
            var mid = items.Length / 2;
            var leftItems = new T[mid];
            var rightItems = new T[items.Length - mid];
            Array.Copy(items, 0, leftItems, 0, mid);
            Array.Copy(items, mid, rightItems, 0, rightItems.Length);
            left = new Leaf(leftItems);
            right = new Leaf(rightItems);
        }

        static void SplitChildren(Node[] children, out Node left, out Node right)
        {
            var mid = children.Length / 2;
            var leftChildren = new Node[mid];
            var rightChildren = new Node[children.Length - mid];
            Array.Copy(children, 0, leftChildren, 0, mid);
            Array.Copy(children, mid, rightChildren, 0, rightChildren.Length);
            left = new Branch(leftChildren);
            right = new Branch(rightChildren);
        }

        static int CheckNode(Node node, IComparer<T> comparer, bool hasLower, T lower, bool hasUpper, T upper,
            int depth, ref int leafDepth)
        {
            if (node is Leaf leaf)
            {
                var items = leaf.Items;
                if (items.Length < 1 || items.Length > MaxItems)
                    throw new InvalidOperationException("Leaf length is " + items.Length + ".");
                if (leafDepth == -1)
                    leafDepth = depth;
                else if (leafDepth != depth)
                    throw new InvalidOperationException("Leaves sit at different depths (" + leafDepth + " and " + depth + ").");
                for (var i = 0; i < items.Length; i++)
                {
                    if (i > 0 && comparer.Compare(items[i - 1], items[i]) >= 0)
                        throw new InvalidOperationException("Leaf items are not strictly sorted.");
                    if (hasLower && comparer.Compare(items[i], lower) < 0)
                        throw new InvalidOperationException("An item falls below its block.");
                    if (hasUpper && comparer.Compare(items[i], upper) >= 0)
                        throw new InvalidOperationException("An item falls outside its block.");
                }

                return items.Length;
            }

            var branch = (Branch)node;
            var children = branch.Children;
            if (children.Length < 2 || children.Length > MaxItems)
                throw new InvalidOperationException("Branch has " + children.Length + " children.");
            if (comparer.Compare(branch.First, children[0].First) != 0)
                throw new InvalidOperationException("Branch first key does not match its first child.");
            if (comparer.Compare(branch.Last, children[children.Length - 1].Last) != 0)
                throw new InvalidOperationException("Branch last key does not match its last child.");

            var count = 0;
            var last = children.Length - 1;
            for (var i = 0; i < children.Length; i++)
            {
                var childHasLower = i > 0 || hasLower;
                var childLower = i == 0 ? lower : children[i].First;
                var childHasUpper = i < last || hasUpper;
                var childUpper = i < last ? children[i + 1].First : upper;
                count += CheckNode(children[i], comparer, childHasLower, childLower, childHasUpper, childUpper,
                    depth + 1, ref leafDepth);
            }

            if (count != branch.Count)
                throw new InvalidOperationException("Branch count is " + branch.Count + " but children sum to " + count + ".");
            return count;
        }

        static void Collect(Node node, HashSet<Node> seen)
        {
            if (node == null || !seen.Add(node))
                return;
            if (!(node is Branch branch))
                return;
            var children = branch.Children;
            for (var i = 0; i < children.Length; i++)
                Collect(children[i], seen);
        }

        static int Hits(Node node, HashSet<Node> seen)
        {
            if (node == null)
                return 0;
            var count = seen.Contains(node) ? 1 : 0;
            if (!(node is Branch branch))
                return count;
            var children = branch.Children;
            for (var i = 0; i < children.Length; i++)
                count += Hits(children[i], seen);
            return count;
        }

        static Node LargestSharedStart(Cursor left, Cursor right)
        {
            if (!left.Has || !right.Has || left.ItemIndex != 0 || right.ItemIndex != 0)
                return null;
            var a = new Node[MaxDepth];
            var b = new Node[MaxDepth];
            var aCount = left.FillStartNodes(a);
            var bCount = right.FillStartNodes(b);
            Node best = null;
            var bestCount = -1;
            for (var i = 0; i < aCount; i++)
            {
                for (var j = 0; j < bCount; j++)
                {
                    if (!ReferenceEquals(a[i], b[j]) || a[i].Count <= bestCount)
                        continue;
                    best = a[i];
                    bestCount = a[i].Count;
                }
            }

            return best;
        }

        sealed class ReferenceEqual<TNode> : IEqualityComparer<TNode> where TNode : class
        {
            public static readonly ReferenceEqual<TNode> Instance = new();

            public bool Equals(TNode x, TNode y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(TNode obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        sealed class Cursor
        {
            struct Frame
            {
                public Branch Branch;
                public int Child;
            }

            readonly Frame[] _frames = new Frame[MaxDepth];
            int _top = -1;
            Leaf _leaf;
            int _index;

            public Cursor(Node root)
            {
                if (root == null)
                    return;
                Push(root);
                Has = true;
                Current = _leaf.Items[_index];
            }

            public bool Has { get; private set; }

            public T Current { get; private set; }

            public int ItemIndex => _index;

            public bool MoveNext()
            {
                if (!Has)
                    return false;
                _index++;
                if (_leaf != null && _index < _leaf.Items.Length)
                {
                    Current = _leaf.Items[_index];
                    return true;
                }

                return Rise();
            }

            public int FillStartNodes(Node[] into)
            {
                if (!Has || _index != 0 || _leaf == null)
                    return 0;
                var count = 0;
                into[count++] = _leaf;
                for (var i = _top; i >= 0; i--)
                {
                    if (_frames[i].Child != 0)
                        break;
                    into[count++] = _frames[i].Branch;
                }

                return count;
            }

            public bool SkipThrough(Node node)
            {
                if (!Has)
                    return false;
                if (ReferenceEquals(node, _leaf))
                    return Rise();
                for (var i = _top; i >= 0; i--)
                {
                    if (!ReferenceEquals(_frames[i].Branch, node))
                        continue;
                    _top = i - 1;
                    if (_top < 0)
                    {
                        Has = false;
                        _leaf = null;
                        return false;
                    }

                    return Rise();
                }

                throw new InvalidOperationException("Diff cursor lost a shared block.");
            }

            void Push(Node node)
            {
                while (node is Branch branch)
                {
                    _top++;
                    if (_top >= _frames.Length)
                        throw new InvalidOperationException("Sorted block tree is deeper than the cursor allows.");
                    _frames[_top].Branch = branch;
                    _frames[_top].Child = 0;
                    node = branch.Children[0];
                }

                _leaf = (Leaf)node;
                _index = 0;
            }

            bool Rise()
            {
                while (_top >= 0)
                {
                    var next = _frames[_top].Child + 1;
                    var branch = _frames[_top].Branch;
                    if (next < branch.Children.Length)
                    {
                        _frames[_top].Child = next;
                        Push(branch.Children[next]);
                        Current = _leaf.Items[_index];
                        return true;
                    }

                    _top--;
                }

                Has = false;
                _leaf = null;
                return false;
            }
        }
    }
}

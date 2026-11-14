using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Fluorine
{
    /// <summary>
    /// Persistent sequence. A leaf holds at most five elements and a branch at
    /// most five children, the same fanout the other pictures use. Path copies
    /// share every block a write did not touch. Insert and remove shift only
    /// the blocks on the edited path; later siblings stay shared. A diff skips
    /// a block both pictures still point at when that block starts at the same
    /// index in both.
    /// </summary>
    internal static class IndexTree<T>
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
            }

            public override int Count { get; }
        }

        internal static Node FromItems(T[] items)
        {
            if (items == null || items.Length == 0)
                return null;
            var level = new List<Node>();
            var index = 0;
            while (index < items.Length)
            {
                var take = Math.Min(MaxItems, items.Length - index);
                if (items.Length - index - take == 1)
                    take--;
                var slice = new T[take];
                Array.Copy(items, index, slice, 0, take);
                level.Add(new Leaf(slice));
                index += take;
            }

            return Pack(level);
        }

        internal static T Get(Node root, int index)
        {
            var node = root;
            while (node is Branch branch)
            {
                var children = branch.Children;
                var offset = 0;
                for (var i = 0; i < children.Length; i++)
                {
                    var childCount = children[i].Count;
                    if (index < offset + childCount)
                    {
                        node = children[i];
                        index -= offset;
                        break;
                    }

                    offset += childCount;
                }
            }

            return ((Leaf)node).Items[index];
        }

        internal static Node Set(Node root, int index, T value, out T previous, out bool changed)
        {
            // Read the stored value before equality, so a throwing Equals still
            // reports it and leaves the tree untouched.
            previous = Get(root, index);
            if (Equals(previous, value))
            {
                changed = false;
                return root;
            }

            changed = true;
            return Write(root, index, value);
        }

        internal static Node Insert(Node root, int index, T item)
        {
            if (root == null)
            {
                if (index != 0)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return new Leaf([item]);
            }

            var edit = InsertNode(root, index, item);
            if (edit.Split == null)
                return edit.Node;
            return new Branch([edit.Node, edit.Split]);
        }

        internal static Node RemoveAt(Node root, int index)
        {
            return Collapse(RemoveNode(root, index));
        }

        internal static int LargestShared(Node older, Node newer, int index)
        {
            var left = new Node[MaxDepth];
            var right = new Node[MaxDepth];
            var leftCount = Aligned(older, index, left);
            var rightCount = Aligned(newer, index, right);
            var best = 0;
            for (var i = 0; i < leftCount; i++)
            {
                for (var j = 0; j < rightCount; j++)
                {
                    if (ReferenceEquals(left[i], right[j]) && left[i].Count > best)
                        best = left[i].Count;
                }
            }

            return best;
        }

        internal static void Check(Node root)
        {
            if (root == null)
                return;
            var leafDepth = -1;
            var count = CheckNode(root, 0, ref leafDepth);
            if (count != root.Count)
                throw new InvalidOperationException("Root count does not match its blocks.");
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
            readonly Branch[] _stack = new Branch[MaxDepth];
            readonly int[] _nextChild = new int[MaxDepth];
            int _top = -1;
            Leaf _leaf;
            int _index = -1;
            bool _started;

            public Enumerator(Node root)
            {
                _root = root;
            }

            public T Current => _leaf == null ? default(T) : _leaf.Items[_index];

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (!_started)
                {
                    _started = true;
                    if (_root == null)
                        return false;
                    return Descend(_root);
                }

                _index++;
                if (_leaf != null && _index < _leaf.Items.Length)
                    return true;
                return Advance();
            }

            public void Reset()
            {
                _started = false;
                _top = -1;
                _leaf = null;
                _index = -1;
            }

            public void Dispose()
            {
            }

            bool Descend(Node node)
            {
                while (node is Branch branch)
                {
                    if (_top + 1 >= MaxDepth)
                        throw new InvalidOperationException("The sequence is deeper than " + MaxDepth + " blocks.");
                    _top++;
                    _stack[_top] = branch;
                    _nextChild[_top] = 1;
                    node = branch.Children[0];
                }

                _leaf = (Leaf)node;
                _index = 0;
                return true;
            }

            bool Advance()
            {
                while (_top >= 0)
                {
                    var branch = _stack[_top];
                    var child = _nextChild[_top];
                    if (child < branch.Children.Length)
                    {
                        _nextChild[_top] = child + 1;
                        return Descend(branch.Children[child]);
                    }

                    _top--;
                }

                _leaf = null;
                return false;
            }
        }

        struct Edit
        {
            public Node Node;
            public Node Split;
        }

        static Node Write(Node node, int index, T value)
        {
            if (node is Leaf leaf)
            {
                var copy = (T[])leaf.Items.Clone();
                copy[index] = value;
                return new Leaf(copy);
            }

            var branch = (Branch)node;
            var childIndex = FindChild(branch, index, out var offset);
            var children = (Node[])branch.Children.Clone();
            children[childIndex] = Write(children[childIndex], index - offset, value);
            return new Branch(children);
        }

        static Edit InsertNode(Node node, int index, T item)
        {
            if (node is Leaf leaf)
                return InsertLeaf(leaf, index, item);

            var branch = (Branch)node;
            var children = branch.Children;
            var offset = 0;
            var childIndex = 0;
            for (; childIndex < children.Length; childIndex++)
            {
                var childCount = children[childIndex].Count;
                if (childIndex == children.Length - 1 || index < offset + childCount)
                    break;
                offset += childCount;
            }

            var edited = InsertNode(children[childIndex], index - offset, item);
            Node replacement;
            Node split = null;
            if (edited.Split == null)
            {
                var copy = (Node[])children.Clone();
                copy[childIndex] = edited.Node;
                replacement = new Branch(copy);
            }
            else
            {
                var count = children.Length + 1;
                var expanded = new Node[count];
                Array.Copy(children, 0, expanded, 0, childIndex);
                expanded[childIndex] = edited.Node;
                expanded[childIndex + 1] = edited.Split;
                var tail = children.Length - childIndex - 1;
                if (tail > 0)
                    Array.Copy(children, childIndex + 1, expanded, childIndex + 2, tail);
                if (count <= MaxItems)
                    replacement = new Branch(expanded);
                else
                    SplitChildren(expanded, out replacement, out split);
            }

            return new Edit
            {
                Node = replacement,
                Split = split
            };
        }

        static Edit InsertLeaf(Leaf leaf, int index, T item)
        {
            var items = leaf.Items;
            var next = new T[items.Length + 1];
            Array.Copy(items, 0, next, 0, index);
            next[index] = item;
            if (index < items.Length)
                Array.Copy(items, index, next, index + 1, items.Length - index);
            if (next.Length <= MaxItems)
            {
                return new Edit
                {
                    Node = new Leaf(next)
                };
            }

            SplitItems(next, out var left, out var right);
            return new Edit
            {
                Node = left,
                Split = right
            };
        }

        static Node RemoveNode(Node node, int index)
        {
            if (node is Leaf leaf)
            {
                var items = leaf.Items;
                if (items.Length == 1)
                    return null;
                var next = new T[items.Length - 1];
                Array.Copy(items, 0, next, 0, index);
                Array.Copy(items, index + 1, next, index, items.Length - index - 1);
                return new Leaf(next);
            }

            var branch = (Branch)node;
            var childIndex = FindChild(branch, index, out var offset);
            var updated = RemoveNode(branch.Children[childIndex], index - offset);
            return Install(branch, childIndex, updated);
        }

        static int FindChild(Branch branch, int index, out int offset)
        {
            var children = branch.Children;
            offset = 0;
            for (var i = 0; i < children.Length; i++)
            {
                var childCount = children[i].Count;
                if (index < offset + childCount)
                    return i;
                offset += childCount;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }

        static Node Install(Branch branch, int index, Node updated)
        {
            var list = new List<Node>(branch.Children.Length);
            for (var i = 0; i < branch.Children.Length; i++)
                list.Add(branch.Children[i]);
            if (updated == null)
                list.RemoveAt(index);
            else
            {
                list[index] = updated;
                if (Size(updated) < MinItems)
                    FixUnderfull(list, index);
            }

            if (list.Count == 0)
                return null;
            return new Branch([.. list]);
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

        static Node Pack(List<Node> nodes)
        {
            if (nodes.Count == 1)
                return nodes[0];
            var up = new List<Node>();
            var index = 0;
            while (index < nodes.Count)
            {
                var take = Math.Min(MaxItems, nodes.Count - index);
                if (nodes.Count - index - take == 1)
                    take--;
                var slice = new Node[take];
                for (var i = 0; i < take; i++)
                    slice[i] = nodes[index + i];
                up.Add(new Branch(slice));
                index += take;
            }

            return Pack(up);
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

        static void SplitItems(T[] items, out Node left, out Node right)
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

        static int Aligned(Node root, int index, Node[] dest)
        {
            if (root == null || index < 0 || index >= root.Count)
                return 0;
            var count = 0;
            var node = root;
            var origin = 0;
            while (true)
            {
                if (origin == index)
                {
                    if (count >= dest.Length)
                        break;
                    dest[count++] = node;
                }

                if (!(node is Branch branch))
                    break;
                var offset = origin;
                var found = false;
                var children = branch.Children;
                for (var i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    var next = offset + child.Count;
                    if (index < next)
                    {
                        node = child;
                        origin = offset;
                        found = true;
                        break;
                    }

                    offset = next;
                }

                if (!found)
                    break;
            }

            return count;
        }

        static int CheckNode(Node node, int depth, ref int leafDepth)
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
                return items.Length;
            }

            var branch = (Branch)node;
            var children = branch.Children;
            if (children.Length < 2 || children.Length > MaxItems)
                throw new InvalidOperationException("Branch has " + children.Length + " children.");
            var count = 0;
            for (var i = 0; i < children.Length; i++)
                count += CheckNode(children[i], depth + 1, ref leafDepth);
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

        sealed class ReferenceEqual<TNode> : IEqualityComparer<TNode> where TNode : class
        {
            public static readonly ReferenceEqual<TNode> Instance = new ReferenceEqual<TNode>();

            public bool Equals(TNode x, TNode y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(TNode obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}

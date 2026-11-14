using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class ReactiveSortedSetTests
    {
        [Fact]
        public void AddRemoveAndContainsFollowTheComparerInOrder()
        {
            var set = new ReactiveSortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.True(set.Add("b"));
            Assert.True(set.Add("Ada"));
            Assert.False(set.Add("ada"));
            Assert.True(set.Contains("ADA"));
            Assert.Equal(2, set.Count);
            Assert.Equal(new[] { "Ada", "b" }, set.ToArray());
            Assert.Same("Ada", set.Single(item => item.Length == 3));

            var picture = set.Value;
            Assert.False(set.Add("ADA"));
            Assert.Same(picture, set.Value);
            Assert.Equal(new[] { "Ada", "b" }, picture.ToArray());

            Assert.True(set.Remove("aDa"));
            Assert.False(set.Contains("Ada"));
            Assert.False(set.Remove("Ada"));
            Assert.Equal(new[] { "b" }, set.ToArray());
            set.CheckInvariants();
        }

        [Fact]
        public void NullElementSortsFirst()
        {
            var set = new ReactiveSortedSet<string>();
            Assert.True(set.Add("b"));
            Assert.True(set.Add(null));
            Assert.True(set.Add("a"));
            Assert.False(set.Add(null));
            Assert.Equal(new string[] { null, "a", "b" }, set.ToArray());
            Assert.True(set.Contains(null));
            Assert.True(set.Remove(null));
            Assert.False(set.Contains(null));
            Assert.Equal(new[] { "a", "b" }, set.ToArray());
        }

        [Fact]
        public void ConstructorKeepsTheFirstInstanceAndDoesNotRequireAScheduler()
        {
            var set = new ReactiveSortedSet<int>(new[] { 3, 1, 2, 2 }, Comparer<int>.Default);
            Assert.Equal(3, set.Count);
            Assert.Equal(new[] { 1, 2, 3 }, set.ToArray());
            Assert.Throws<ArgumentNullException>(() => new ReactiveSortedSet<int>((IEnumerable<int>)null));
            set.CheckInvariants();
        }

        [Fact]
        public void DescendingComparerEnumeratesAndDiffsHighToLow()
        {
            var set = new ReactiveSortedSet<int>(Descending);
            set.Add(1);
            set.Add(3);
            set.Add(2);
            Assert.Equal(new[] { 3, 2, 1 }, set.ToArray());
            Assert.Equal(new[] { "Added:3", "Added:2", "Added:1" }, Names(set.Value.Diff(null)).ToArray());
            set.CheckInvariants();
        }

        [Fact]
        public void ClearDropsEveryMemberInOrder()
        {
            var set = new ReactiveSortedSet<int>(new[] { 2, 3, 1 });
            var before = set.Value;
            set.Clear();
            Assert.Equal(0, set.Count);
            Assert.Same(set.Value, set.Value);
            Assert.Equal(new[] { "Removed:1", "Removed:2", "Removed:3" }, Names(set.Value.Diff(before)).ToArray());
            var empty = set.Value;
            set.Clear();
            Assert.Same(empty, set.Value);
        }

        [Fact]
        public void DiffIsTheNetChangeInComparerOrder()
        {
            var set = new ReactiveSortedSet<int>();
            var empty = set.Value;
            set.Add(2);
            set.Add(1);
            var both = set.Value;
            Assert.Equal(new[] { "Added:1", "Added:2" }, Names(both.Diff(empty)).ToArray());
            Assert.Empty(both.Diff(both));

            set.Remove(1);
            set.Add(3);
            Assert.Equal(new[] { "Removed:1", "Added:3" }, Names(set.Value.Diff(both)).ToArray());
            Assert.Equal(new[] { "Added:2", "Added:3" }, Names(set.Value.Diff(null)).ToArray());
        }

        [Fact]
        public void SeparatelyBuiltEqualSetsDiffEmptyDespiteDifferentShapes()
        {
            var ascending = new ReactiveSortedSet<int>();
            var descending = new ReactiveSortedSet<int>();
            for (var i = 0; i < 30; i++)
            {
                ascending.Add(i);
                descending.Add(29 - i);
            }

            Assert.Equal(ascending.ToArray(), descending.ToArray());
            Assert.Empty(ascending.Value.Diff(descending.Value));
            Assert.Empty(descending.Value.Diff(ascending.Value));
            Assert.False(ascending.Value == descending.Value);
        }

        [Fact]
        public void HeldPictureStaysPutAfterLaterWrites()
        {
            var set = new ReactiveSortedSet<int>(new[] { 1, 2, 3 });
            var before = set.Value;
            set.Add(4);
            set.Remove(1);
            Assert.Equal(new[] { 1, 2, 3 }, before.ToArray());
            Assert.True(before.Contains(1));
            Assert.False(before.Contains(4));
            Assert.Equal(new[] { 2, 3, 4 }, set.ToArray());
        }

        [Fact]
        public void EqualReferenceReplacesAcrossPicturesAndAddKeepsTheStoredInstance()
        {
            var set = new ReactiveSortedSet<Entity>(new IdComparer());
            var first = new Entity(1, "Ada");
            var other = new Entity(2, "Grace");
            var second = new Entity(1, "Augusta");
            Assert.True(set.Add(first));
            Assert.True(set.Add(other));
            var before = set.Value;
            Assert.False(set.Add(second));
            Assert.Same(before, set.Value);
            Assert.Same(first, set.Single(item => item.Id == 1));
            Assert.Empty(set.Value.Diff(before));

            Assert.True(set.Remove(first));
            Assert.True(set.Add(second));
            var net = Assert.Single(set.Value.Diff(before));
            Assert.Equal(ReactiveSortedSet<Entity>.ChangeKind.Replaced, net.Kind);
            Assert.Same(second, net.Item);
            Assert.Same(first, net.Previous);
            Assert.Equal(new[] { second, other }, set.ToArray());
        }

        [Fact]
        public void OneEditSharesTheUntouchedBlocks()
        {
            var set = new ReactiveSortedSet<int>();
            for (var i = 0; i < 80; i++)
                set.Add(i);
            var before = set.Value;
            set.CheckInvariants();
            Assert.True(set.Add(1000));
            var after = set.Value;
            set.CheckInvariants();
            var added = Assert.Single(after.Diff(before));
            Assert.Equal(ReactiveSortedSet<int>.ChangeKind.Added, added.Kind);
            Assert.Equal(1000, added.Item);
            Assert.True(after.SharedWith(before) >= after.NodeCount - 12,
                "shared " + after.SharedWith(before) + " of " + after.NodeCount);

            before = after;
            Assert.True(set.Remove(40));
            after = set.Value;
            set.CheckInvariants();
            var removed = Assert.Single(after.Diff(before));
            Assert.Equal(ReactiveSortedSet<int>.ChangeKind.Removed, removed.Kind);
            Assert.Equal(40, removed.Item);
            Assert.True(after.SharedWith(before) >= after.NodeCount - 12,
                "shared " + after.SharedWith(before) + " of " + after.NodeCount);
        }

        [Fact]
        public void EverySizeBuildsSortsAndEmpties()
        {
            for (var n = 0; n <= 48; n++)
            {
                var set = new ReactiveSortedSet<int>();
                for (var i = 0; i < n; i++)
                    Assert.True(set.Add(i));
                set.CheckInvariants();
                Assert.Equal(Enumerable.Range(0, n), set.ToArray());
                for (var i = 0; i < n; i++)
                    Assert.True(set.Contains(i));
                Assert.False(set.Contains(-1));

                for (var i = n - 1; i >= 0; i--)
                    Assert.True(set.Remove(i));
                Assert.Equal(0, set.Count);
                set.CheckInvariants();

                for (var i = 0; i < n; i++)
                    set.Add(n - 1 - i);
                for (var i = 0; i < n; i += 2)
                    Assert.True(set.Remove(i));
                set.CheckInvariants();
                Assert.Equal(Enumerable.Range(0, n).Where(i => i % 2 == 1), set.ToArray());
            }
        }

        [Fact]
        public void RandomEditsMatchASortedSet()
        {
            Exercise(Comparer<int>.Default, -40, 40);
            Exercise(Descending, -20, 20);
            Exercise(Comparer<int>.Default, 0, 8);
        }

        [Fact]
        public void ConcurrentAddsKeepEveryDistinctElement()
        {
            var set = new ReactiveSortedSet<int>();
            const int count = 1000;
            var first = new Thread(() =>
            {
                for (var i = 0; i < count; i++)
                    set.Add(i);
            });
            var second = new Thread(() =>
            {
                for (var i = 0; i < count; i++)
                    set.Add(count + i);
            });
            first.Start();
            second.Start();
            first.Join();
            second.Join();
            Assert.Equal(count * 2, set.Count);
            Assert.Equal(Enumerable.Range(0, count * 2), set.ToArray());
            set.CheckInvariants();
        }

        [Fact]
        public void ValueReadRerunsOnAnyMembershipChange()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var set = new ReactiveSortedSet<int>();
                var runs = 0;
                var count = 0;
                new Reactor(scheduler).Add(() =>
                {
                    count = set.Value.Count;
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, count);

                set.Add(1);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(1, count);

                set.Add(1);
                scheduler.Pump();
                Assert.Equal(2, runs);

                set.Add(2);
                scheduler.Pump();
                Assert.Equal(3, runs);
                set.Clear();
                scheduler.Pump();
                Assert.Equal(4, runs);
                Assert.Equal(0, count);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void ContainsRerunsOnlyForThatElement()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var set = new ReactiveSortedSet<int>();
                var runs = 0;
                var present = false;
                new Reactor(scheduler).Add(() =>
                {
                    present = set.Contains(1);
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.False(present);
                Assert.Equal(1, set.WatchedMemberships);

                set.Add(2);
                scheduler.Pump();
                Assert.Equal(1, runs);

                set.Add(1);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.True(present);

                set.Remove(1);
                scheduler.Pump();
                Assert.Equal(3, runs);
                Assert.False(present);

                set.Clear();
                scheduler.Pump();
                Assert.Equal(3, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void PausedContainsDoesNotSubscribe()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var set = new ReactiveSortedSet<int>();
                var runs = 0;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    Reaction.Pause(() => set.Contains(1));
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, set.WatchedMemberships);
                set.Add(1);
                scheduler.Pump();
                Assert.Equal(1, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void TwoContainsReactionsShareOneMembershipAndDisposeReleasesIt()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var set = new ReactiveSortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var first = 0;
                var second = 0;
                var one = new Reactor(scheduler).Add(() =>
                {
                    set.Contains("Ada");
                    first++;
                });
                var two = new Reactor(scheduler).Add(() =>
                {
                    set.Contains("ada");
                    second++;
                });
                scheduler.Pump();
                Assert.Equal(1, first);
                Assert.Equal(1, second);
                Assert.Equal(1, set.WatchedMemberships);

                one.Dispose();
                scheduler.Pump();
                Assert.Equal(1, set.WatchedMemberships);
                two.Dispose();
                scheduler.Pump();
                Assert.Equal(0, set.WatchedMemberships);

                set.Add("ADA");
                scheduler.Pump();
                Assert.Equal(1, first);
                Assert.Equal(1, second);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void EarlyReturnKeepsThePreviousPicture()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var set = new ReactiveSortedSet<int>();
                var ready = new Register<bool>(false);
                ReactiveSortedSet<int>.Version seen = null;
                var applied = new List<string>();
                new Reactor(scheduler).Add(() =>
                {
                    var current = set.Value;
                    if (!ready.Value)
                        return;
                    foreach (var change in current.Diff(seen))
                        applied.Add(change.Kind + ":" + change.Item);
                    seen = current;
                });
                scheduler.Pump();

                set.Add(1);
                set.Add(2);
                set.Remove(1);
                scheduler.Pump();
                Assert.Empty(applied);

                ready.Value = true;
                scheduler.Pump();
                Assert.Equal(new[] { "Added:2" }, applied.ToArray());

                set.Add(3);
                set.Remove(2);
                scheduler.Pump();
                Assert.Equal(new[] { "Added:2", "Removed:2", "Added:3" }, applied.ToArray());
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void AddingAnExistingMemberDoesNotRerun()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var set = new ReactiveSortedSet<Entity>(new IdComparer());
                var first = new Entity(1, "Ada");
                var second = new Entity(1, "Augusta");
                set.Add(first);
                var runs = 0;
                new Reactor(scheduler).Add(() =>
                {
                    set.Value.Count.ToString();
                    set.Contains(first);
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);

                Assert.False(set.Add(second));
                scheduler.Pump();
                Assert.Equal(1, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        static void Exercise(IComparer<int> comparer, int keyMin, int keyMax)
        {
            var set = new ReactiveSortedSet<int>(comparer);
            var mirror = new SortedSet<int>(comparer);
            var random = new Random(1);
            ReactiveSortedSet<int>.Version marked = null;
            SortedSet<int> markedMirror = null;
            for (var i = 0; i < 2000; i++)
            {
                var value = random.Next(keyMin, keyMax);
                if (random.Next(5) == 0 && mirror.Count > 0 && i % 37 == 0)
                {
                    set.Clear();
                    mirror.Clear();
                }
                else if (random.Next(3) == 0)
                {
                    Assert.Equal(mirror.Remove(value), set.Remove(value));
                }
                else
                {
                    Assert.Equal(mirror.Add(value), set.Add(value));
                }

                Assert.Equal(mirror.ToArray(), set.ToArray());
                foreach (var item in mirror)
                    Assert.True(set.Value.Contains(item));
                set.CheckInvariants();
                if (i == 1000)
                {
                    marked = set.Value;
                    markedMirror = new SortedSet<int>(mirror, comparer);
                }
            }

            var changes = set.Value.Diff(marked);
            var added = changes.Where(change => change.Kind == ReactiveSortedSet<int>.ChangeKind.Added).Select(change => change.Item);
            var removed = changes.Where(change => change.Kind == ReactiveSortedSet<int>.ChangeKind.Removed).Select(change => change.Item);
            Assert.Equal(mirror.Where(item => !markedMirror.Contains(item)), added);
            Assert.Equal(markedMirror.Where(item => !mirror.Contains(item)), removed);
            Assert.Equal(changes, changes.OrderBy(change => change.Item, comparer));
        }

        static readonly IComparer<int> Descending = Comparer<int>.Create((x, y) => y.CompareTo(x));

        static IEnumerable<string> Names(ReactiveSortedSet<int>.Change[] changes)
        {
            return changes.Select(change => change.Kind + ":" + change.Item);
        }

        sealed class Entity(int id, string name)
        {
            public int Id { get; } = id;
            public string Name { get; } = name;
        }

        sealed class IdComparer : IComparer<Entity>
        {
            public int Compare(Entity x, Entity y)
            {
                if (ReferenceEquals(x, y))
                    return 0;
                if (x == null)
                    return -1;
                if (y == null)
                    return 1;
                return x.Id.CompareTo(y.Id);
            }
        }
    }
}

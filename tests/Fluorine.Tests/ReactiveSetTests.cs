using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using Xunit;

namespace Fluorine.Tests
{
    public class ReactiveSetTests
    {
        [Fact]
        public void AddRemoveAndContainsFollowEqualsAndHashCode()
        {
            var set = new ReactiveSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.True(set.Add("Ada"));
            Assert.False(set.Add("ada"));
            Assert.True(set.Contains("ADA"));
            Assert.Equal(1, set.Count);
            Assert.Same("Ada", set.Single());

            var picture = set.Value;
            Assert.False(set.Add("ADA"));
            Assert.Same(picture, set.Value);

            Assert.True(set.Remove("aDa"));
            Assert.False(set.Contains("Ada"));
            Assert.False(set.Remove("Ada"));
            Assert.Equal(0, set.Count);
        }

        [Fact]
        public void NullElementIsAMember()
        {
            var set = new ReactiveSet<string>();
            Assert.True(set.Add(null));
            Assert.True(set.Contains(null));
            Assert.False(set.Add(null));
            Assert.True(set.Remove(null));
            Assert.False(set.Contains(null));
        }

        [Fact]
        public void ConstructorKeepsDistinctItemsAndDoesNotRequireAScheduler()
        {
            var set = new ReactiveSet<int>(new[] { 1, 2, 2, 3 });
            Assert.Equal(3, set.Count);
            Assert.Equal(new[] { 1, 2, 3 }, set.OrderBy(x => x).ToArray());
            Assert.Throws<ArgumentNullException>(() => new ReactiveSet<int>((IEnumerable<int>)null));
        }

        [Fact]
        public void ClearDropsEveryMember()
        {
            var set = new ReactiveSet<int>(new[] { 1, 2, 3 });
            var before = set.Value;
            set.Clear();
            Assert.Equal(0, set.Count);
            Assert.Equal(new[] { Change(false, 1), Change(false, 2), Change(false, 3) },
                Names(set.Value.Diff(before)).OrderBy(x => x).ToArray());
            set.Clear();
            Assert.Empty(set.Value.Diff(before).Where(c => c.Kind == ReactiveSet<int>.ChangeKind.Added));
        }

        [Fact]
        public void DiffIsTheNetChangeBetweenTwoPictures()
        {
            var set = new ReactiveSet<int>();
            var empty = set.Value;
            set.Add(1);
            set.Add(2);
            var both = set.Value;
            Assert.Equal(new[] { "Added:1", "Added:2" }, Names(both.Diff(empty)).OrderBy(x => x).ToArray());
            Assert.Empty(both.Diff(both));

            set.Remove(1);
            set.Add(3);
            Assert.Equal(new[] { "Added:3", "Removed:1" }, Names(set.Value.Diff(both)).OrderBy(x => x).ToArray());
            Assert.Equal(new[] { "Added:2", "Added:3" }, Names(set.Value.Diff(null)).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void SeparatelyBuiltEqualSetsDiffEmpty()
        {
            var first = new ReactiveSet<int>(new[] { 3, 1, 2 });
            var second = new ReactiveSet<int>();
            second.Add(2);
            second.Add(1);
            second.Add(3);
            Assert.Empty(first.Value.Diff(second.Value));
            Assert.False(first.Value==second.Value);
        }

        [Fact]
        public void ConstantHashAndNegativeHashesMatchAHashSet()
        {
            var set = new ReactiveSet<int>(new ConstantHash());
            var mirror = new HashSet<int>(new ConstantHash());
            var values = new[] { int.MinValue, -1, 0, 1, 31, 32, 1024, -1024 };
            foreach (var value in values)
            {
                Assert.Equal(mirror.Add(value), set.Add(value));
                Assert.Equal(mirror.Add(value), set.Add(value));
            }

            Assert.Equal(mirror.Remove(31), set.Remove(31));
            Assert.Equal(mirror.Remove(31), set.Remove(31));
            AssertSameMembers(mirror, set);

            var random = new Random(1);
            var plain = new ReactiveSet<int>();
            var plainMirror = new HashSet<int>();
            ReactiveSet<int>.Version marked = null;
            for (var i = 0; i < 2000; i++)
            {
                var value = random.Next(int.MinValue, int.MaxValue);
                if (random.Next(3) == 0)
                    Assert.Equal(plainMirror.Remove(value), plain.Remove(value));
                else
                    Assert.Equal(plainMirror.Add(value), plain.Add(value));
                if (i == 1000)
                    marked = plain.Value;
            }

            Assert.Equal(plainMirror.Count, plain.Count);
            AssertSameMembers(plainMirror, plain);
            var sinceMark = plain.Value.Diff(marked);
            var added = new HashSet<int>(sinceMark.Where(c => c.Kind == ReactiveSet<int>.ChangeKind.Added).Select(c => c.Item));
            var removed = new HashSet<int>(sinceMark.Where(c => c.Kind == ReactiveSet<int>.ChangeKind.Removed).Select(c => c.Item));
            var markedItems = new HashSet<int>(marked);
            Assert.Equal(plainMirror.Where(item => !markedItems.Contains(item)).OrderBy(x => x), added.OrderBy(x => x));
            Assert.Equal(markedItems.Where(item => !plainMirror.Contains(item)).OrderBy(x => x), removed.OrderBy(x => x));
        }

        [Fact]
        public void ConcurrentAddsKeepEveryDistinctElement()
        {
            var set = new ReactiveSet<int>();
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
            for (var i = 0; i < count * 2; i++)
                Assert.True(set.Contains(i));
        }

        [Fact]
        public void ValueReadRerunsOnAnyMembershipChange()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var set = new ReactiveSet<int>();
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
                var set = new ReactiveSet<int>();
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
                var set = new ReactiveSet<int>();
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
                var set = new ReactiveSet<int>();
                var first = 0;
                var second = 0;
                var one = new Reactor(scheduler).Add(() =>
                {
                    set.Contains(7);
                    first++;
                });
                var two = new Reactor(scheduler).Add(() =>
                {
                    set.Contains(7);
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

                set.Add(7);
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
                var set = new ReactiveSet<int>();
                var ready = new Register<bool>(false);
                ReactiveSet<int>.Version seen = null;
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
                Assert.Equal(new[] { "Added:2" }, applied.OrderBy(x => x).ToArray());

                set.Add(3);
                set.Remove(2);
                scheduler.Pump();
                Assert.Equal(new[] { "Added:2", "Added:3", "Removed:2" }, applied.OrderBy(x => x).ToArray());
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void EqualReferenceReplacesTheStoredInstance()
        {
            var set = new ReactiveSet<Entity>();
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
            Assert.Equal(ReactiveSet<Entity>.ChangeKind.Replace, net.Kind);
            Assert.Same(second, net.Item);
            Assert.Same(first, net.Previous);
        }

        [Fact]
        public void AddingAnExistingMemberDoesNotRerun()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var set = new ReactiveSet<Entity>();
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

        static string Change(bool added, int item)
        {
            return (added ? "Added:" : "Removed:") + item;
        }

        static IEnumerable<string> Names<T>(ReactiveSet<T>.Change[] changes)
        {
            return changes.Select(change => change.Kind + ":" + change.Item);
        }

        sealed class Entity
        {
            public Entity(int id, string name)
            {
                Id = id;
                Name = name;
            }

            public int Id { get; }
            public string Name { get; }

            public override bool Equals(object obj)
            {
                return obj is Entity other && other.Id == Id;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        static void AssertSameMembers(HashSet<int> expected, ReactiveSet<int> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            foreach (var item in expected)
                Assert.True(actual.Contains(item));
            Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
        }

        sealed class ConstantHash : IEqualityComparer<int>
        {
            public bool Equals(int x, int y)
            {
                return x == y;
            }

            public int GetHashCode(int obj)
            {
                return 0;
            }
        }
    }
}

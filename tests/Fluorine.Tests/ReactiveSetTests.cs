using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Xunit;

namespace Fluorine
{
    public class ReactiveSetTests : CollectionAcceptanceTest
    {
        [Fact(DisplayName = "cs12.4-01 Set Add/Remove return values")]
        public void AddRemoveAndContainsFollowEqualsAndHashCode()
        {
            var set = new ReactiveSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.True(set.Add("Ada"));
            Assert.False(set.Add("ada"));
            Assert.True(set.Contains("ADA"));
            Assert.Equal(1, set.Count);
            Assert.Same("Ada", set.Single());

            var picture = set.Values;
            Assert.False(set.Add("ADA"));
            Assert.Same(picture, set.Values);

            Assert.True(set.Remove("aDa"));
            Assert.False(set.Contains("Ada"));
            Assert.False(set.Remove("Ada"));
            Assert.Equal(0, set.Count);
        }

        [Fact(DisplayName = "cs12.3-01 Set accepts null as an element")]
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
            var set = new ReactiveSet<int>([1, 2, 2, 3]);
            Assert.Equal(3, set.Count);
            Assert.Equal([1, 2, 3], set.OrderBy(x => x).ToArray());
            Assert.Throws<ArgumentNullException>(() => new ReactiveSet<int>((IEnumerable<int>)null));
        }

        [Fact(DisplayName = "cs12.4-02 Set Clear empties the set")]
        public void ClearDropsEveryMember()
        {
            var set = new ReactiveSet<int>([1, 2, 3]);
            var before = set.Values;
            set.Clear();
            Assert.Equal(0, set.Count);
            Assert.Equal([Change(false, 1), Change(false, 2), Change(false, 3)],
                Names(set.Values.Diff(before)).OrderBy(x => x).ToArray());
            set.Clear();
            Assert.Empty(set.Values.Diff(before).Where(c => c.Kind == ChangeKind.Added));
        }

        [Fact]
        public void SeparatelyBuiltEqualSetsDiffEmpty()
        {
            var first = new ReactiveSet<int>([3, 1, 2]);
            var second = new ReactiveSet<int>
            {
                2,
                1,
                3
            };
            Assert.Empty(first.Values.Diff(second.Values));
            Assert.False(first.Values == second.Values);
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
                    marked = plain.Values;
            }

            Assert.Equal(plainMirror.Count, plain.Count);
            AssertSameMembers(plainMirror, plain);
            var sinceMark = plain.Values.Diff(marked);
            var added = new HashSet<int>(sinceMark.Where(c => c.Kind == ChangeKind.Added).Select(c => c.Item));
            var removed = new HashSet<int>(sinceMark.Where(c => c.Kind == ChangeKind.Removed).Select(c => c.Item));
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
                    count = set.Values.Count;
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
                    var current = set.Values;
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
                Assert.Equal(["Added:2"], applied.OrderBy(x => x).ToArray());

                set.Add(3);
                set.Remove(2);
                scheduler.Pump();
                Assert.Equal(["Added:2", "Added:3", "Removed:2"], applied.OrderBy(x => x).ToArray());
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
            var before = set.Values;
            Assert.False(set.Add(second));
            Assert.Same(before, set.Values);
            Assert.Same(first, set.Single(item => item.Id == 1));
            Assert.Empty(set.Values.Diff(before));

            Assert.True(set.Remove(first));
            Assert.True(set.Add(second));
            var net = Assert.Single(set.Values.Diff(before));
            Assert.Equal(ChangeKind.Updated, net.Kind);
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
                    set.Values.Count.ToString();
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

        static string Change(bool added, int item) => (added ? "Added:" : "Removed:") + item;

        static IEnumerable<string> Names<T>(SetChange<T>[] changes) => changes.Select(change => change.Kind + ":" + change.Item);

        sealed class Entity(int id, string name)
        {
            public int Id { get; } = id;
            public string Name { get; } = name;

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

        [Fact]
        void ObservableSet()
        {
            object result = null;
            var set = new ReactiveSet<Entity>();
            ObservableCollectionConverter converter = new ObservableCollectionConverter();
            var scheduler = DispatcherScheduler.CreateNew("observable-set");
            scheduler.Queue(() => result = converter.Convert(set, typeof(ObservableCollection<Entity>), null, null));
            while (result == null) Thread.Yield(); Assert.True(result is ObservableCollection<Entity>);
            var collection = (ObservableCollection<Entity>)result;
            collection.CollectionChanged += Dummy; 
            // Do changes arrive
            set.Add(new Entity(1, "Ploppy"));
            while (collection.Count == 0) Thread.Yield();
            scheduler.Shutdown();
        }

        [Fact(DisplayName = "cs12.13-01 Set Diff reports net Added/Removed")]
        public void Rule_12_13_01_SetDiffReportsNetAddedAndRemoved()
        {
            var sc = new ReactiveSet<K>();
            var v0 = sc.Values;
            sc.Add(k1);
            sc.Add(k2);
            var v1 = sc.Values;
            sc.Remove(k1);
            sc.Add(k3);
            var v2 = sc.Values;
            AssertSetTags(v1.Diff(v0), "Added:1:null", "Added:2:null");
            AssertSetTags(v2.Diff(v1), "Removed:1:null", "Added:3:null");
            AssertSetTags(v2.Diff(v0), "Added:2:null", "Added:3:null");
            foreach (var change in v1.Diff(v0))
                Assert.Null(change.Previous);
            foreach (var change in v2.Diff(v1))
                Assert.Null(change.Previous);
            foreach (var change in v2.Diff(v0))
                Assert.Null(change.Previous);
        }

        [Fact(DisplayName = "cs12.13-06 Set Diff: other equal instance=Updated")]
        public void Rule_12_13_06_SetDiffReportsReplacedForOtherInstance()
        {
            var sc = new ReactiveSet<K> { k1 };
            var v1 = sc.Values;
            sc.Remove(k1);
            sc.Add(k1p);
            var v2 = sc.Values;
            var forward = v2.Diff(v1);
            Assert.Single(forward);
            Assert.Equal(ChangeKind.Updated, forward[0].Kind);
            Assert.True(ReferenceEquals(forward[0].Item, k1p));
            Assert.True(ReferenceEquals(forward[0].Previous, k1));
            var back = v1.Diff(v2);
            Assert.Single(back);
            Assert.Equal(ChangeKind.Updated, back[0].Kind);
            Assert.True(ReferenceEquals(back[0].Item, k1));
            Assert.True(ReferenceEquals(back[0].Previous, k1p));

            var ints = new ReactiveSet<int> { 1 };
            var w1 = ints.Values;
            ints.Remove(1);
            ints.Add(1);
            var w2 = ints.Values;
            Assert.Empty(w2.Diff(w1));
        }

        private void Dummy(object sender, NotifyCollectionChangedEventArgs e)
        {
        }
    }
}
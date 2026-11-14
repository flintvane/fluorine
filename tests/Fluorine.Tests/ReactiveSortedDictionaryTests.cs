using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class ReactiveSortedDictionaryTests
    {
        [Fact]
        public void AddSetRemoveAndLookupFollowTheKeyComparerInOrder()
        {
            var map = new ReactiveSortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Assert.True(map.Add("b", 2));
            Assert.True(map.Add("Ada", 1));
            Assert.False(map.Add("ada", 9));
            Assert.Equal(1, map["ADA"]);
            Assert.True(map.ContainsKey("Ada"));
            Assert.Equal(2, map.Count);
            Assert.Equal(new[] { "Ada", "b" }, map.Keys.ToArray());
            Assert.Equal(new[] { 1, 2 }, map.Values.ToArray());

            var picture = map.Value;
            Assert.False(map.SetValue("ADA", 1));
            Assert.Same(picture, map.Value);
            Assert.Equal("Ada", map.Single(pair => pair.Value == 1).Key);

            Assert.True(map.SetValue("ada", 4, out var old));
            Assert.Equal(1, old);
            Assert.Equal("Ada", map.Single(pair => pair.Key.Equals("Ada")).Key);
            Assert.Equal(4, map["aDa"]);

            Assert.True(map.Remove("ADA"));
            Assert.False(map.ContainsKey("Ada"));
            Assert.False(map.Remove("Ada"));
            Assert.Equal(new[] { "b" }, map.Keys.ToArray());
            map.CheckInvariants();
        }

        [Fact]
        public void SetValueUsesObjectEqualsAndKeepsTheStoredKey()
        {
            var map = new ReactiveSortedDictionary<string, ByValue>();
            var first = new ByValue(1);
            var equal = new ByValue(1);
            Assert.True(map.SetValue("a", first, out var missing));
            Assert.Null(missing);
            Assert.False(map.SetValue("a", equal, out var same));
            Assert.Same(first, same);
            Assert.Same(first, map["a"]);
            Assert.Same(map.Value, map.Value);

            var other = new ByValue(2);
            Assert.True(map.SetValue("a", other, out var previous));
            Assert.Same(first, previous);
            Assert.Same(other, map["a"]);

            var equatable = new ReactiveSortedDictionary<string, EquatableOnly>();
            equatable.SetValue("a", new EquatableOnly(1));
            var picture = equatable.Value;
            Assert.True(equatable.SetValue("a", new EquatableOnly(1)));
            Assert.NotSame(picture, equatable.Value);

            var ada = new string(new[] { 'A', 'd', 'a' });
            var again = new string(new[] { 'a', 'd', 'a' });
            var names = new ReactiveSortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Assert.True(names.Add(ada, 1));
            Assert.True(names.SetValue(again, 4, out var was));
            Assert.Equal(1, was);
            Assert.Same(ada, names.Single().Key);
            Assert.Equal(4, names[ada]);
        }

        [Fact]
        public void EqualsThrowLeavesTheStoredValue()
        {
            var map = new ReactiveSortedDictionary<string, Boom>();
            var stored = new Boom();
            Assert.True(map.SetValue("a", stored));
            var thrown = Assert.Throws<InvalidOperationException>(() => map.SetValue("a", new Boom()));
            Assert.Equal("boom", thrown.Message);
            Assert.Same(stored, map["a"]);
            Assert.Equal(1, map.Count);
        }

        [Fact]
        public void NullValueIsAnEntryAndNullKeyIsRejected()
        {
            var map = new ReactiveSortedDictionary<string, string>();
            Assert.True(map.SetValue("a", null, out var missing));
            Assert.Null(missing);
            Assert.True(map.TryGetValue("a", out var value));
            Assert.Null(value);
            Assert.False(map.SetValue("a", null, out var again));
            Assert.Null(again);
            Assert.True(map.SetValue("a", "b", out again));
            Assert.Null(again);

            Assert.Throws<ArgumentNullException>(() => map.Add(null, "x"));
            Assert.Throws<ArgumentNullException>(() => map.SetValue(null, "x"));
            Assert.Throws<ArgumentNullException>(() => map.SetValue(null, "x", out _));
            Assert.Throws<ArgumentNullException>(() => map.Remove(null));
            Assert.Throws<ArgumentNullException>(() => map.ContainsKey(null));
            Assert.Throws<ArgumentNullException>(() => map.TryGetValue(null, out _));
            Assert.Throws<ArgumentNullException>(() => map[null] = "x");
            Assert.Throws<ArgumentNullException>(() => map[null]);
            Assert.Throws<ArgumentNullException>(() => map.Value.ContainsKey(null));
            Assert.Throws<ArgumentNullException>(() => map.Value.TryGetValue(null, out _));
            Assert.Equal(1, map.Count);
        }

        [Fact]
        public void ConstructorKeepsTheFirstValueAndEnumeratesInOrder()
        {
            var map = new ReactiveSortedDictionary<string, int>(new[]
            {
                Pair("b", 2),
                Pair("a", 1),
                Pair("a", 3),
                Pair("c", 4)
            });
            Assert.Equal(3, map.Count);
            Assert.Equal(1, map["a"]);
            Assert.Equal(new[] { "a", "b", "c" }, map.Select(pair => pair.Key).ToArray());
            Assert.Equal(new[] { 1, 2, 4 }, map.Values.ToArray());
            Assert.Throws<ArgumentNullException>(() => new ReactiveSortedDictionary<string, int>((IEnumerable<KeyValuePair<string, int>>)null));
            Assert.Throws<ArgumentNullException>(() => new ReactiveSortedDictionary<string, int>(new[] { Pair<string, int>(null, 1) }));
            map.CheckInvariants();
        }

        [Fact]
        public void MissingIndexerThrowsAfterTheValueWasRead()
        {
            var map = new ReactiveSortedDictionary<string, int>();
            Assert.Throws<KeyNotFoundException>(() => map["missing"].ToString());
            Assert.Throws<KeyNotFoundException>(() => map.Value["missing"].ToString());
            map["missing"] = 3;
            Assert.Equal(3, map["missing"]);
        }

        [Fact]
        public void DiffIsTheNetChangeInComparerOrder()
        {
            var map = new ReactiveSortedDictionary<string, int>();
            var empty = map.Value;
            map["b"] = 2;
            map.Add("a", 1);
            var both = map.Value;
            Assert.Equal(new[] { "Added:a=1", "Added:b=2" }, Names(both.Diff(empty)).ToArray());
            Assert.Empty(both.Diff(both));

            map.SetValue("a", 4);
            map.Remove("b");
            map.Add("c", 3);
            Assert.Equal(new[] { "Updated:a=1->4", "Removed:b=2", "Added:c=3" }, Names(map.Value.Diff(both)).ToArray());
            Assert.Equal(new[] { "Added:a=4", "Added:c=3" }, Names(map.Value.Diff(null)).ToArray());

            var updated = map.Value.Diff(both).Single(change => change.Key == "a");
            Assert.Equal(ReactiveSortedDictionary<string, int>.ChangeKind.Updated, updated.Kind);
            Assert.Equal(4, updated.Value);
            Assert.Equal(1, updated.PreviousValue);
            Assert.Equal("a", updated.Key);
            var added = map.Value.Diff(both).Single(change => change.Key == "c");
            Assert.Equal(0, added.PreviousValue);
        }

        [Fact]
        public void SeparatelyBuiltEqualDictionariesDiffEmpty()
        {
            var first = new ReactiveSortedDictionary<string, int>();
            var second = new ReactiveSortedDictionary<string, int>();
            for (var i = 0; i < 30; i++)
            {
                first.Add(i.ToString("00"), i);
                second[ (29 - i).ToString("00") ] = 29 - i;
            }

            Assert.Empty(first.Value.Diff(second.Value));
            Assert.Equal(first.Select(pair => pair.Key + "=" + pair.Value), second.Select(pair => pair.Key + "=" + pair.Value));
        }

        [Fact]
        public void OneChangedKeyDiffsAsThatKeyAloneAndSharesBlocks()
        {
            var map = new ReactiveSortedDictionary<int, int>();
            for (var i = 0; i < 200; i++)
                map.Add(i, i);
            var before = map.Value;
            map.CheckInvariants();
            map.SetValue(50, -1);
            var updated = Assert.Single(map.Value.Diff(before));
            Assert.Equal(ReactiveSortedDictionary<int, int>.ChangeKind.Updated, updated.Kind);
            Assert.Equal(50, updated.Key);
            Assert.Equal(50, updated.PreviousValue);
            Assert.Equal(-1, updated.Value);
            Assert.True(map.Value.SharedWith(before) >= map.Value.NodeCount - 12);

            before = map.Value;
            Assert.True(map.Remove(32));
            var removed = Assert.Single(map.Value.Diff(before));
            Assert.Equal(ReactiveSortedDictionary<int, int>.ChangeKind.Removed, removed.Kind);
            Assert.Equal(32, removed.Key);
            Assert.Equal(32, removed.Value);

            before = map.Value;
            map[1000] = 7;
            var added = Assert.Single(map.Value.Diff(before));
            Assert.Equal(ReactiveSortedDictionary<int, int>.ChangeKind.Added, added.Kind);
            Assert.Equal(1000, added.Key);
            Assert.Equal(7, added.Value);
            map.CheckInvariants();
            Assert.Equal(200, map.Count);
        }

        [Fact]
        public void ClearDropsEveryEntryInOrder()
        {
            var map = new ReactiveSortedDictionary<string, int>(new[] { Pair("b", 2), Pair("a", 1) });
            var before = map.Value;
            map.Clear();
            Assert.Equal(0, map.Count);
            Assert.Equal(new[] { "Removed:a=1", "Removed:b=2" }, Names(map.Value.Diff(before)).ToArray());
            var empty = map.Value;
            map.Clear();
            Assert.Same(empty, map.Value);
        }

        [Fact]
        public void EverySizeBuildsSortsAndEmpties()
        {
            for (var n = 0; n <= 48; n++)
            {
                var map = new ReactiveSortedDictionary<int, int>(Descending);
                for (var i = 0; i < n; i++)
                    Assert.True(map.Add(i, i * 10));
                map.CheckInvariants();
                Assert.Equal(Enumerable.Range(0, n).Reverse(), map.Keys.ToArray());
                for (var i = 0; i < n; i++)
                {
                    Assert.True(map.TryGetValue(i, out var value));
                    Assert.Equal(i * 10, value);
                }

                for (var i = 0; i < n; i++)
                    Assert.True(map.Remove(i));
                Assert.Equal(0, map.Count);
                map.CheckInvariants();
            }
        }

        [Fact]
        public void RandomEditsMatchASortedDictionary()
        {
            Exercise(Comparer<int>.Default, -30, 30);
            Exercise(Descending, 0, 12);
        }

        [Fact]
        public void ConcurrentAddsAndSetsKeepAConsistentMap()
        {
            var map = new ReactiveSortedDictionary<int, int>();
            const int count = 400;
            var first = new Thread(() =>
            {
                for (var i = 0; i < count; i++)
                    map.SetValue(i, 1);
            });
            var second = new Thread(() =>
            {
                for (var i = 0; i < count; i++)
                    map.SetValue(i, 2);
            });
            first.Start();
            second.Start();
            first.Join();
            second.Join();
            Assert.Equal(count, map.Count);
            Assert.Equal(Enumerable.Range(0, count), map.Keys.ToArray());
            foreach (var value in map.Values)
                Assert.True(value == 1 || value == 2);
            map.CheckInvariants();
        }

        [Fact]
        public void ContainsKeyIgnoresValueChangesAndTryGetValueFollowsThem()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var map = new ReactiveSortedDictionary<int, int>();
                var presenceRuns = 0;
                var valueRuns = 0;
                var present = false;
                var seen = 0;
                new Reactor(scheduler).Add(() =>
                {
                    present = map.ContainsKey(1);
                    presenceRuns++;
                });
                new Reactor(scheduler).Add(() =>
                {
                    map.TryGetValue(1, out seen);
                    valueRuns++;
                });
                scheduler.Pump();
                Assert.Equal(1, presenceRuns);
                Assert.Equal(1, valueRuns);
                Assert.False(present);

                map.SetValue(2, 5);
                scheduler.Pump();
                Assert.Equal(1, presenceRuns);
                Assert.Equal(1, valueRuns);

                map.SetValue(1, 4);
                scheduler.Pump();
                Assert.Equal(2, presenceRuns);
                Assert.Equal(2, valueRuns);
                Assert.True(present);
                Assert.Equal(4, seen);

                map.SetValue(1, 8);
                scheduler.Pump();
                Assert.Equal(2, presenceRuns);
                Assert.Equal(3, valueRuns);
                Assert.Equal(8, seen);

                map.Remove(1);
                scheduler.Pump();
                Assert.Equal(3, presenceRuns);
                Assert.Equal(4, valueRuns);
                Assert.False(present);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void IndexerGetterSubscribesThenThrowsWhenTheKeyIsAbsent()
        {
            var scheduler = new ManualScheduler();
            var seen = new List<Exception>();
            Action<Exception> handler = seen.Add;
            Reactive.UnhandledException += handler;
            try
            {
                var map = new ReactiveSortedDictionary<int, string>();
                var runs = 0;
                string text = null;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    text = map[1];
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Null(text);
                var error = Assert.Single(seen);
                Assert.IsType<KeyNotFoundException>(error);
                Assert.Equal(1, map.WatchedValues);
                Assert.Equal(0, map.WatchedPresences);

                map.SetValue(1, "one");
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal("one", text);
            }
            finally
            {
                Reactive.UnhandledException -= handler;
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void PausedReadsDoNotSubscribe()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var map = new ReactiveSortedDictionary<int, int>();
                var runs = 0;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    Reaction.Pause(() =>
                    {
                        map.ContainsKey(1);
                        map.TryGetValue(1, out _);
                    });
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, map.WatchedPresences);
                Assert.Equal(0, map.WatchedValues);
                map.SetValue(1, 1);
                scheduler.Pump();
                Assert.Equal(1, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void VersionReadsDoNotSubscribe()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var map = new ReactiveSortedDictionary<int, int>();
                map.SetValue(1, 1);
                var picture = map.Value;
                var runs = 0;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    Assert.True(picture.ContainsKey(1));
                    Assert.True(picture.TryGetValue(1, out var value));
                    Assert.Equal(1, value);
                    Assert.Equal(1, picture[1]);
                    Assert.Equal(new[] { 1 }, picture.Select(pair => pair.Key).ToArray());
                    Assert.Equal(1, picture.Count);
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, map.WatchedPresences);
                Assert.Equal(0, map.WatchedValues);
                map.SetValue(1, 2);
                scheduler.Pump();
                Assert.Equal(1, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void PresenceAndValueWatchesDisposeOnTheirOwn()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var map = new ReactiveSortedDictionary<int, int>();
                var presence = new Reactor(scheduler).Add(() => map.ContainsKey(7));
                var value = new Reactor(scheduler).Add(() => map.TryGetValue(7, out _));
                scheduler.Pump();
                Assert.Equal(1, map.WatchedPresences);
                Assert.Equal(1, map.WatchedValues);

                presence.Dispose();
                scheduler.Pump();
                Assert.Equal(0, map.WatchedPresences);
                Assert.Equal(1, map.WatchedValues);
                value.Dispose();
                scheduler.Pump();
                Assert.Equal(0, map.WatchedValues);
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
                var map = new ReactiveSortedDictionary<string, int>();
                var ready = new Register<bool>(false);
                ReactiveSortedDictionary<string, int>.Version seen = null;
                var applied = new List<string>();
                new Reactor(scheduler).Add(() =>
                {
                    var current = map.Value;
                    if (!ready.Value)
                        return;
                    foreach (var change in current.Diff(seen))
                        applied.Add(change.Kind + ":" + change.Key + "=" + change.Value);
                    seen = current;
                });
                scheduler.Pump();

                map.SetValue("a", 1);
                map.SetValue("a", 2);
                map.SetValue("b", 3);
                map.Remove("b");
                scheduler.Pump();
                Assert.Empty(applied);

                ready.Value = true;
                scheduler.Pump();
                Assert.Equal(new[] { "Added:a=2" }, applied.ToArray());

                map.SetValue("a", 4);
                map.Remove("a");
                scheduler.Pump();
                Assert.Equal(new[] { "Added:a=2", "Removed:a=2" }, applied.ToArray());
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        static void Exercise(IComparer<int> comparer, int keyMin, int keyMax)
        {
            var map = new ReactiveSortedDictionary<int, int>(comparer);
            var mirror = new SortedDictionary<int, int>(comparer);
            var random = new Random(1);
            ReactiveSortedDictionary<int, int>.Version marked = null;
            Dictionary<int, int> markedMirror = null;
            for (var i = 0; i < 2000; i++)
            {
                var key = random.Next(keyMin, keyMax);
                var value = random.Next(-5, 6);
                var op = random.Next(5);
                if (op == 0)
                {
                    Assert.Equal(MirrorAdd(mirror, key, value), map.Add(key, value));
                }
                else if (op == 1)
                {
                    var expected = MirrorSet(mirror, key, value, out var old);
                    var actual = map.SetValue(key, value, out var actualOld);
                    Assert.Equal(expected, actual);
                    Assert.Equal(old, actualOld);
                }
                else if (op == 2)
                {
                    Assert.Equal(mirror.Remove(key), map.Remove(key));
                }
                else if (op == 3 && mirror.Count > 0 && i % 29 == 0)
                {
                    map.Clear();
                    mirror.Clear();
                }
                else
                {
                    map[key] = value;
                    mirror[key] = value;
                }

                Assert.Equal(mirror.Keys, map.Keys.ToArray());
                Assert.Equal(mirror.Values, map.Values.ToArray());
                foreach (var pair in mirror)
                {
                    Assert.True(map.Value.TryGetValue(pair.Key, out var stored));
                    Assert.Equal(pair.Value, stored);
                }

                map.CheckInvariants();
                if (i == 1000)
                {
                    marked = map.Value;
                    markedMirror = new Dictionary<int, int>(mirror);
                }
            }

            AssertDiff(markedMirror, mirror, map.Value.Diff(marked), comparer);
        }

        static bool MirrorAdd(SortedDictionary<int, int> mirror, int key, int value)
        {
            if (mirror.ContainsKey(key))
                return false;
            mirror.Add(key, value);
            return true;
        }

        static bool MirrorSet(SortedDictionary<int, int> mirror, int key, int value, out int old)
        {
            if (mirror.TryGetValue(key, out old))
            {
                if (Equals(old, value))
                    return false;
                mirror[key] = value;
                return true;
            }

            old = 0;
            mirror.Add(key, value);
            return true;
        }

        static void AssertDiff(
            Dictionary<int, int> older,
            SortedDictionary<int, int> newer,
            ReactiveSortedDictionary<int, int>.Change[] changes,
            IComparer<int> comparer)
        {
            var expected = new List<string>();
            foreach (var key in older.Keys.OrderBy(key => key, comparer))
            {
                if (!newer.TryGetValue(key, out var value))
                    expected.Add("Removed:" + key + "=" + older[key]);
                else if (!Equals(older[key], value))
                    expected.Add("Updated:" + key + "=" + older[key] + "->" + value);
            }

            foreach (var pair in newer)
            {
                if (!older.ContainsKey(pair.Key))
                    expected.Add("Added:" + pair.Key + "=" + pair.Value);
            }

            Assert.Equal(expected.OrderBy(line => LineKey(line), comparer).ToArray(), Names(changes).ToArray());
        }

        static int LineKey(string line)
        {
            var start = line.IndexOf(':') + 1;
            var end = line.IndexOf('=', start);
            return int.Parse(line.Substring(start, end - start));
        }

        static KeyValuePair<TKey, TValue> Pair<TKey, TValue>(TKey key, TValue value)
        {
            return new KeyValuePair<TKey, TValue>(key, value);
        }

        static IEnumerable<string> Names(ReactiveSortedDictionary<string, int>.Change[] changes)
        {
            return changes.Select(change =>
            {
                if (change.Kind == ReactiveSortedDictionary<string, int>.ChangeKind.Updated)
                    return change.Kind + ":" + change.Key + "=" + change.PreviousValue + "->" + change.Value;
                return change.Kind + ":" + change.Key + "=" + change.Value;
            });
        }

        static IEnumerable<string> Names(ReactiveSortedDictionary<int, int>.Change[] changes)
        {
            return changes.Select(change =>
            {
                if (change.Kind == ReactiveSortedDictionary<int, int>.ChangeKind.Updated)
                    return change.Kind + ":" + change.Key + "=" + change.PreviousValue + "->" + change.Value;
                return change.Kind + ":" + change.Key + "=" + change.Value;
            });
        }

        static readonly IComparer<int> Descending = Comparer<int>.Create((x, y) => y.CompareTo(x));

        sealed class ByValue(int number)
        {
            public int Number { get; } = number;

            public override bool Equals(object obj) => obj is ByValue other && other.Number == Number;

            public override int GetHashCode() => Number;
        }

        sealed class EquatableOnly(int number) : IEquatable<EquatableOnly>
        {
            public int Number { get; } = number;

            public bool Equals(EquatableOnly other)
            {
                return other != null && other.Number == Number;
            }
        }

        sealed class Boom
        {
            public override bool Equals(object obj)
            {
                throw new InvalidOperationException("boom");
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}

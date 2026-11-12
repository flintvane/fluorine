using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class ReactiveDictionaryTests
    {
        [Fact]
        public void AddSetRemoveAndLookupFollowTheKeyComparer()
        {
            var map = new ReactiveDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Assert.True(map.Add("Ada", 1));
            Assert.False(map.Add("ada", 2));
            Assert.Equal(1, map["ADA"]);
            Assert.True(map.ContainsKey("Ada"));
            Assert.Equal(1, map.Count);
            Assert.Same(map.Value, map.Value);

            var picture = map.Value;
            Assert.False(map.SetValue("ADA", 1));
            Assert.Same(picture, map.Value);
            Assert.Equal("Ada", map.Single().Key);

            int old;
            Assert.True(map.SetValue("ada", 4, out old));
            Assert.Equal(1, old);
            Assert.Equal("Ada", map.Single().Key);
            Assert.Equal(4, map["aDa"]);

            Assert.True(map.Remove("ADA"));
            Assert.False(map.ContainsKey("Ada"));
            Assert.False(map.Remove("Ada"));
            Assert.Equal(0, map.Count);
            Assert.True(map.Value.SameAs(null));
        }

        [Fact]
        public void SetValueUsesObjectEqualsAndKeepsTheStoredInstance()
        {
            var map = new ReactiveDictionary<string, ByValue>();
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

            var equatable = new ReactiveDictionary<string, EquatableOnly>();
            equatable.SetValue("a", new EquatableOnly(1));
            var picture = equatable.Value;
            Assert.True(equatable.SetValue("a", new EquatableOnly(1)));
            Assert.NotSame(picture, equatable.Value);
        }

        [Fact]
        public void EqualsThrowLeavesTheStoredValue()
        {
            var map = new ReactiveDictionary<string, Boom>();
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
            var map = new ReactiveDictionary<string, string>();
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
            Assert.Throws<ArgumentNullException>(() => map[null].ToString());
            Assert.Throws<ArgumentNullException>(() => map.Value.ContainsKey(null));
            Assert.Throws<ArgumentNullException>(() => map.Value.TryGetValue(null, out _));
            Assert.Equal(1, map.Count);
        }

        [Fact]
        public void MissingIndexerThrowsAfterTheValueWasRead()
        {
            var map = new ReactiveDictionary<string, int>();
            Assert.Throws<KeyNotFoundException>(() => map["missing"].ToString());
            Assert.Throws<KeyNotFoundException>(() => map.Value["missing"].ToString());
            map["missing"] = 3;
            Assert.Equal(3, map["missing"]);
        }

        [Fact]
        public void ConstructorKeepsTheFirstValueAndDoesNotRequireAScheduler()
        {
            var map = new ReactiveDictionary<string, int>(new[]
            {
                Pair("a", 1),
                Pair("b", 2),
                Pair("a", 3)
            });
            Assert.Equal(2, map.Count);
            Assert.Equal(1, map["a"]);
            Assert.Equal(2, map["b"]);
            Assert.Throws<ArgumentNullException>(() => new ReactiveDictionary<string, int>((IEnumerable<KeyValuePair<string, int>>)null));
            Assert.Throws<ArgumentNullException>(() => new ReactiveDictionary<string, int>(new[] { Pair<string, int>(null, 1) }));
        }

        [Fact]
        public void ClearDropsEveryEntry()
        {
            var map = new ReactiveDictionary<string, int>(new[] { Pair("a", 1), Pair("b", 2) });
            var before = map.Value;
            map.Clear();
            Assert.Equal(0, map.Count);
            Assert.Equal(new[] { "Removed:a=1", "Removed:b=2" }, Names(map.Value.Diff(before)).OrderBy(x => x).ToArray());
            var empty = map.Value;
            map.Clear();
            Assert.Same(empty, map.Value);
            Assert.True(empty.SameAs(null));
            Assert.True(empty.SameAs(new ReactiveDictionary<string, int>().Value));
        }

        [Fact]
        public void DiffIsTheNetChangeBetweenTwoPictures()
        {
            var map = new ReactiveDictionary<string, int>();
            var empty = map.Value;
            map.Add("a", 1);
            map["b"] = 2;
            var both = map.Value;
            Assert.Equal(new[] { "Added:a=1", "Added:b=2" }, Names(both.Diff(empty)).OrderBy(x => x).ToArray());
            Assert.Empty(both.Diff(both));
            Assert.True(both.SameAs(both));

            map.SetValue("a", 4);
            map.Remove("b");
            map.Add("c", 3);
            Assert.Equal(new[] { "Added:c=3", "Removed:b=2", "Updated:a=1->4" },
                Names(map.Value.Diff(both)).OrderBy(x => x).ToArray());
            Assert.Equal(new[] { "Added:a=4", "Added:c=3" }, Names(map.Value.Diff(null)).OrderBy(x => x).ToArray());

            var updated = map.Value.Diff(both).Single(change => change.Key == "a");
            Assert.Equal(ReactiveDictionary<string, int>.ChangeKind.Updated, updated.Kind);
            Assert.Equal(4, updated.Value);
            Assert.Equal(1, updated.PreviousValue);
            var added = map.Value.Diff(both).Single(change => change.Key == "c");
            Assert.Equal(0, added.PreviousValue);
        }

        [Fact]
        public void SeparatelyBuiltEqualDictionariesDiffEmpty()
        {
            var first = new ReactiveDictionary<string, int>(new[] { Pair("b", 2), Pair("a", 1) });
            var second = new ReactiveDictionary<string, int>();
            second.Add("a", 1);
            second["b"] = 2;
            Assert.Empty(first.Value.Diff(second.Value));
            Assert.False(first.Value.SameAs(second.Value));
        }

        [Fact]
        public void OneChangedKeyDiffsAsThatKeyAlone()
        {
            var map = new ReactiveDictionary<int, int>();
            for (var i = 0; i < 1000; i++)
                map.Add(i, i);
            var before = map.Value;
            map.SetValue(500, -1);
            var updated = Assert.Single(map.Value.Diff(before));
            Assert.Equal(ReactiveDictionary<int, int>.ChangeKind.Updated, updated.Kind);
            Assert.Equal(500, updated.Key);
            Assert.Equal(500, updated.PreviousValue);
            Assert.Equal(-1, updated.Value);

            before = map.Value;
            Assert.True(map.Remove(32));
            var removed = Assert.Single(map.Value.Diff(before));
            Assert.Equal(ReactiveDictionary<int, int>.ChangeKind.Removed, removed.Kind);
            Assert.Equal(32, removed.Value);

            before = map.Value;
            map[1000] = 7;
            var added = Assert.Single(map.Value.Diff(before));
            Assert.Equal(ReactiveDictionary<int, int>.ChangeKind.Added, added.Kind);
            Assert.Equal(1000, added.Key);
            Assert.Equal(7, added.Value);
        }

        [Fact]
        public void ConstantHashAndRandomEditsMatchADictionary()
        {
            Exercise(new ConstantHash(), -8, 8);
            Exercise(EqualityComparer<int>.Default, -40, 40);
            Exercise(EqualityComparer<int>.Default, int.MinValue / 2, int.MaxValue / 2);
        }

        [Fact]
        public void ConcurrentAddsAndSetsKeepAConsistentMap()
        {
            var map = new ReactiveDictionary<int, int>();
            const int count = 1000;
            var first = new Thread(() =>
            {
                for (var i = 0; i < count; i++)
                    map.Add(i, 1);
            });
            var second = new Thread(() =>
            {
                for (var i = 0; i < count; i++)
                    map.Add(count + i, 1);
            });
            first.Start();
            second.Start();
            first.Join();
            second.Join();
            Assert.Equal(count * 2, map.Count);
            for (var i = 0; i < count * 2; i++)
                Assert.Equal(1, map[i]);

            var shared = new ReactiveDictionary<int, int>();
            var left = new Thread(() =>
            {
                for (var i = 0; i < count; i++)
                    shared.SetValue(i, 1);
            });
            var right = new Thread(() =>
            {
                for (var i = 0; i < count; i++)
                    shared.SetValue(i, 2);
            });
            left.Start();
            right.Start();
            left.Join();
            right.Join();
            Assert.Equal(count, shared.Count);
            for (var i = 0; i < count; i++)
            {
                var value = shared[i];
                Assert.True(value == 1 || value == 2);
            }
        }

        [Fact]
        public void ValueReadRerunsOnAnyChange()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var map = new ReactiveDictionary<int, int>();
                var runs = 0;
                var count = 0;
                new Reactor(scheduler).Add(() =>
                {
                    count = map.Value.Count + map.Count;
                    foreach (var _ in map)
                        count++;
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, count);

                map.Add(1, 10);
                scheduler.Pump();
                Assert.Equal(2, runs);

                map.SetValue(1, 10);
                scheduler.Pump();
                Assert.Equal(2, runs);

                map[1] = 11;
                scheduler.Pump();
                Assert.Equal(3, runs);
                map.Clear();
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
        public void ContainsKeyRerunsOnlyForThatKeysPresence()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var map = new ReactiveDictionary<int, int>();
                var runs = 0;
                var present = false;
                new Reactor(scheduler).Add(() =>
                {
                    present = map.ContainsKey(1);
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.False(present);
                Assert.Equal(1, map.WatchedPresences);
                Assert.Equal(0, map.WatchedValues);

                map.SetValue(2, 1);
                scheduler.Pump();
                Assert.Equal(1, runs);

                map.SetValue(1, 5);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.True(present);

                map.SetValue(1, 6);
                scheduler.Pump();
                Assert.Equal(2, runs);

                map.Remove(1);
                scheduler.Pump();
                Assert.Equal(3, runs);
                Assert.False(present);

                map.Clear();
                scheduler.Pump();
                Assert.Equal(3, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void ValueReadRerunsWhenThatKeyChanges()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var map = new ReactiveDictionary<int, int>();
                var runs = 0;
                var seen = 0;
                new Reactor(scheduler).Add(() =>
                {
                    map.TryGetValue(1, out seen);
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, seen);
                Assert.Equal(1, map.WatchedValues);
                Assert.Equal(0, map.WatchedPresences);

                map.SetValue(2, 1);
                scheduler.Pump();
                Assert.Equal(1, runs);

                map.SetValue(1, 5);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(5, seen);

                map.SetValue(1, 5);
                scheduler.Pump();
                Assert.Equal(2, runs);

                map[1] = 8;
                scheduler.Pump();
                Assert.Equal(3, runs);
                Assert.Equal(8, seen);

                map.Remove(1);
                scheduler.Pump();
                Assert.Equal(4, runs);
                Assert.Equal(0, seen);
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
                var map = new ReactiveDictionary<int, string>();
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
                var map = new ReactiveDictionary<int, int>();
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
                var map = new ReactiveDictionary<int, int>();
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
                var map = new ReactiveDictionary<int, int>();
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
        public void TwoValueReactionsShareOneWatch()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var map = new ReactiveDictionary<int, int>();
                var first = new Reactor(scheduler).Add(() => map.TryGetValue(7, out _));
                var second = new Reactor(scheduler).Add(() => map.TryGetValue(7, out _));
                scheduler.Pump();
                Assert.Equal(1, map.WatchedValues);

                first.Dispose();
                scheduler.Pump();
                Assert.Equal(1, map.WatchedValues);
                second.Dispose();
                scheduler.Pump();
                Assert.Equal(0, map.WatchedValues);

                var runs = 0;
                new Reactor(scheduler).Add(() =>
                {
                    map.TryGetValue(7, out _);
                    runs++;
                });
                scheduler.Pump();
                map.SetValue(7, 1);
                scheduler.Pump();
                Assert.Equal(2, runs);
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
                var map = new ReactiveDictionary<string, int>();
                var ready = new Register<bool>(false);
                ReactiveDictionary<string, int>.Version seen = null;
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
                Assert.Equal(new[] { "Added:a=2" }, applied.OrderBy(x => x).ToArray());

                map.SetValue("a", 4);
                map.Remove("a");
                scheduler.Pump();
                Assert.Equal(new[] { "Added:a=2", "Removed:a=2" }, applied.OrderBy(x => x).ToArray());
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        static void Exercise(IEqualityComparer<int> comparer, int keyMin, int keyMax)
        {
            var map = new ReactiveDictionary<int, int>(comparer);
            var mirror = new Dictionary<int, int>(comparer);
            var random = new Random(1);
            ReactiveDictionary<int, int>.Version marked = null;
            Dictionary<int, int> markedMirror = null;
            var steps = keyMax - keyMin > 1000 ? 1500 : 2000;
            for (var i = 0; i < steps; i++)
            {
                var key = random.Next(keyMin, keyMax);
                var value = random.Next(-5, 6);
                var op = random.Next(5);
                if (op == 0)
                    Assert.Equal(MirrorAdd(mirror, key, value), map.Add(key, value));
                else if (op == 1)
                {
                    var expected = MirrorSet(mirror, key, value, out var old);
                    var actual = map.SetValue(key, value, out var actualOld);
                    Assert.Equal(expected, actual);
                    Assert.Equal(old, actualOld);
                }
                else if (op == 2)
                    Assert.Equal(mirror.Remove(key), map.Remove(key));
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

                AssertSame(mirror, map);
                if (i == steps / 2)
                {
                    marked = map.Value;
                    markedMirror = new Dictionary<int, int>(mirror, comparer);
                }
            }

            AssertDiff(markedMirror, mirror, map.Value.Diff(marked));
        }

        static bool MirrorAdd(Dictionary<int, int> mirror, int key, int value)
        {
            if (mirror.ContainsKey(key))
                return false;
            mirror.Add(key, value);
            return true;
        }

        static bool MirrorSet(Dictionary<int, int> mirror, int key, int value, out int old)
        {
            if (mirror.TryGetValue(key, out old))
            {
                if (object.Equals(old, value))
                    return false;
                mirror[key] = value;
                return true;
            }

            old = default(int);
            mirror.Add(key, value);
            return true;
        }

        static void AssertDiff(
            Dictionary<int, int> older,
            Dictionary<int, int> newer,
            ReactiveDictionary<int, int>.Change[] changes)
        {
            var expected = new List<string>();
            foreach (var pair in older)
            {
                if (!newer.TryGetValue(pair.Key, out var value))
                    expected.Add("Removed:" + pair.Key + "=" + pair.Value);
                else if (!object.Equals(pair.Value, value))
                    expected.Add("Updated:" + pair.Key + "=" + pair.Value + "->" + value);
            }

            foreach (var pair in newer)
            {
                if (!older.ContainsKey(pair.Key))
                    expected.Add("Added:" + pair.Key + "=" + pair.Value);
            }

            Assert.Equal(expected.OrderBy(x => x).ToArray(), Names(changes).OrderBy(x => x).ToArray());
        }

        static void AssertSame(Dictionary<int, int> expected, ReactiveDictionary<int, int> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            foreach (var pair in expected)
            {
                Assert.True(actual.TryGetValue(pair.Key, out var value));
                Assert.Equal(pair.Value, value);
            }

            Assert.Equal(expected.Count, actual.Count(pair => expected.ContainsKey(pair.Key)));
        }

        static KeyValuePair<TKey, TValue> Pair<TKey, TValue>(TKey key, TValue value)
        {
            return new KeyValuePair<TKey, TValue>(key, value);
        }

        static IEnumerable<string> Names(ReactiveDictionary<string, int>.Change[] changes)
        {
            return changes.Select(change =>
            {
                if (change.Kind == ReactiveDictionary<string, int>.ChangeKind.Updated)
                    return change.Kind + ":" + change.Key + "=" + change.PreviousValue + "->" + change.Value;
                return change.Kind + ":" + change.Key + "=" + change.Value;
            });
        }

        static IEnumerable<string> Names(ReactiveDictionary<int, int>.Change[] changes)
        {
            return changes.Select(change =>
            {
                if (change.Kind == ReactiveDictionary<int, int>.ChangeKind.Updated)
                    return change.Kind + ":" + change.Key + "=" + change.PreviousValue + "->" + change.Value;
                return change.Kind + ":" + change.Key + "=" + change.Value;
            });
        }

        sealed class ByValue
        {
            public ByValue(int number)
            {
                Number = number;
            }

            public int Number { get; }

            public override bool Equals(object obj)
            {
                return obj is ByValue other && other.Number == Number;
            }

            public override int GetHashCode()
            {
                return Number;
            }
        }

        sealed class EquatableOnly : IEquatable<EquatableOnly>
        {
            public EquatableOnly(int number)
            {
                Number = number;
            }

            public int Number { get; }

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

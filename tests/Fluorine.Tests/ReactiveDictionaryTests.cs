using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System;
using Xunit;

namespace Fluorine
{
    public class ReactiveDictionaryTests : CollectionAcceptanceTest
    {
        [Fact(DisplayName = "cs12.5-01 Dict Add/SetValue/Remove return values")]
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

            Assert.True(map.SetValue("ada", 4, out var old));
            Assert.Equal(1, old);
            Assert.Equal("Ada", map.Single().Key);
            Assert.Equal(4, map["aDa"]);

            Assert.True(map.Remove("ADA"));
            Assert.False(map.ContainsKey("Ada"));
            Assert.False(map.Remove("Ada"));
            Assert.Equal(0, map.Count);
        }

        [Fact(DisplayName = "cs12.2-02 Dict values compare with object.Equals")]
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

        [Fact(DisplayName = "cs12.5-03 SetValue(out old) with null values")]
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
            Assert.Throws<ArgumentNullException>(() => map[null]);
            Assert.Throws<ArgumentNullException>(() => map.Value.ContainsKey(null));
            Assert.Throws<ArgumentNullException>(() => map.Value.TryGetValue(null, out _));
            Assert.Equal(1, map.Count);
        }

        [Fact(DisplayName = "cs12.9-05 Absent key outside reaction throws")]
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
            var map = new ReactiveDictionary<string, int>([
                Pair("a", 1),
                Pair("b", 2),
                Pair("a", 3)
            ]);
            Assert.Equal(2, map.Count);
            Assert.Equal(1, map["a"]);
            Assert.Equal(2, map["b"]);
            Assert.Throws<ArgumentNullException>(() => new ReactiveDictionary<string, int>((IEnumerable<KeyValuePair<string, int>>)null));
            Assert.Throws<ArgumentNullException>(() => new ReactiveDictionary<string, int>([Pair<string, int>(null, 1)]));
        }

        [Fact(DisplayName = "cs12.5-06 Dict Clear removes every entry")]
        public void ClearDropsEveryEntry()
        {
            var map = new ReactiveDictionary<string, int>([Pair("a", 1), Pair("b", 2)]);
            var before = map.Value;
            map.Clear();
            Assert.Equal(0, map.Count);
            Assert.Equal(["Removed:a=1", "Removed:b=2"], Names(map.Value.Diff(before)).OrderBy(x => x).ToArray());
            var empty = map.Value;
            map.Clear();
            Assert.Same(empty, map.Value);
        }

        [Fact]
        public void SeparatelyBuiltEqualDictionariesDiffEmpty()
        {
            var first = new ReactiveDictionary<string, int>([Pair("b", 2), Pair("a", 1)]);
            var second = new ReactiveDictionary<string, int> { { "a", 1 } };
            second["b"] = 2;
            Assert.Empty(first.Value.Diff(second.Value));
            Assert.False(first.Value==second.Value);
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
            Assert.Equal(ChangeKind.Updated, updated.Kind);
            Assert.Equal(500, updated.Key);
            Assert.Equal(500, updated.PreviousValue);
            Assert.Equal(-1, updated.Value);

            before = map.Value;
            Assert.True(map.Remove(32));
            var removed = Assert.Single(map.Value.Diff(before));
            Assert.Equal(ChangeKind.Removed, removed.Kind);
            Assert.Equal(32, removed.Value);

            before = map.Value;
            map[1000] = 7;
            var added = Assert.Single(map.Value.Diff(before));
            Assert.Equal(ChangeKind.Added, added.Kind);
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

        [Fact(DisplayName = "cs12.9-04 Absent-key indexer records, then throws")]
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
                    Assert.Equal([1], picture.Select(pair => pair.Key).ToArray());
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
                Assert.Equal(["Added:a=2"], applied.OrderBy(x => x).ToArray());

                map.SetValue("a", 4);
                map.Remove("a");
                scheduler.Pump();
                Assert.Equal(["Added:a=2", "Removed:a=2"], applied.OrderBy(x => x).ToArray());
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact(DisplayName = "cs12.1-02 Reads/writes from many threads")]
        public void Rule_12_1_02_ReadsAndWritesFromAnyThread()
        {
            using var log = new Log();
            RunAnyThreadStress(true);
            RunAnyThreadStress(false);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs12.1-03 Foreign-thread write triggers reaction")]
        public void Rule_12_1_03_ForeignThreadWriteTriggersReaction()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int sn = 0;
            new Reactor(s).Add(() => { var unused = sc.Values; sn++; });
            Harness.Barrier(s);
            var setThread = new Thread(() => sc.Add(k1))
            {
                IsBackground = true
            };
            setThread.Start();
            Assert.True(setThread.Join(Harness.TimeoutMs));
            Harness.Barrier(s);
            Assert.Equal(2, sn);

            var dc = new ReactiveDictionary<K, string>();
            int dn = 0;
            new Reactor(s).Add(() => { var unused = dc.Value; dn++; });
            Harness.Barrier(s);
            var dictThread = new Thread(() => dc.Add(k1, "a"))
            {
                IsBackground = true
            };
            dictThread.Start();
            Assert.True(dictThread.Join(Harness.TimeoutMs));
            Harness.Barrier(s);
            Assert.Equal(2, dn);
        }

        [Fact(DisplayName = "cs12.1-04 Version is an immutable picture")]
        public void Rule_12_1_04_VersionIsImmutablePicture()
        {
            var sc = new ReactiveSet<K> { k1 };
            var sv = sc.Values;
            sc.Add(k2);
            sc.Remove(k1);
            Assert.Equal(1, sv.Count);
            Assert.True(sv.Contains(k1));
            Assert.False(sv.Contains(k2));

            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            var dv = dc.Value;
            dc[k1] = "b";
            dc.Add(k2, "c");
            dc.Remove(k1);
            Assert.Equal(1, dv.Count);
            Assert.True(dv.TryGetValue(k1, out var v));
            Assert.Equal("a", v);
            Assert.Equal("a", dv[k1]);
            Assert.False(dv.ContainsKey(k2));
        }

        [Fact(DisplayName = "cs12.2-01 Matching uses the collection Comparer")]
        public void Rule_12_2_01_MatchingUsesCollectionComparer()
        {
            var sc = new ReactiveSet<K>();
            Assert.True(sc.Add(k1));
            Assert.False(sc.Add(k1p));
            Assert.True(sc.Contains(k1p));
            Assert.Equal(1, sc.Count);
            Assert.NotNull(sc.Comparer);
            Assert.True(sc.Comparer.Equals(k1, k1p));

            var dc = new ReactiveDictionary<K, string>();
            Assert.True(dc.Add(k1, "a"));
            Assert.False(dc.Add(k1p, "b"));
            Assert.True(dc.ContainsKey(k1p));
            Assert.Equal("a", dc[k1p]);
            Assert.Equal(1, dc.Count);
            Assert.NotNull(dc.Comparer);
            Assert.True(dc.Comparer.Equals(k1, k1p));
        }

        [Fact(DisplayName = "cs12.2-04 Null values compare as equal")]
        public void Rule_12_2_04_NullValuesCompareEqual()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>
            {
                [k1] = null
            };
            int n = 0;
            new Reactor(s).Add(() => { var unused = dc.Value; n++; });
            Harness.Barrier(s);
            Assert.False(dc.SetValue(k1, null));
            Harness.Barrier(s);
            Assert.True(dc.SetValue(k1, "a"));
            Harness.Barrier(s);
            Assert.True(dc.SetValue(k1, null));
            Harness.Barrier(s);
            Assert.Equal(3, n);
        }

        [Fact(DisplayName = "cs12.2-05 Throwing value Equals reaches writer")]
        public void Rule_12_2_05_ThrowingValueEqualityReachesWriter()
        {
            using var log = new Log();
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, AccBoom>();
            var x = new InvalidOperationException("eq");
            var b1 = new AccBoom(x);
            var b2 = new AccBoom(x);
            dc[k1] = b1;
            int nValue = 0, nCount = 0, nContains = 0, nTry = 0;
            new Reactor(s).Add(() => { var unused = dc.Value; nValue++; });
            new Reactor(s).Add(() => { var unused = dc.Count; nCount++; });
            new Reactor(s).Add(() => { var unused = dc.ContainsKey(k1); nContains++; });
            new Reactor(s).Add(() => { AccBoom v; dc.TryGetValue(k1, out v); nTry++; });
            Harness.Barrier(s);
            AssertThrowsUnwrapped(x, () => dc.SetValue(k1, b2));
            AccBoom old;
            AssertThrowsUnwrapped(x, () => dc.SetValue(k1, b2, out old));
            AssertThrowsUnwrapped(x, () => dc[k1] = b2);
            Harness.Barrier(s);
            Assert.Same(b1, dc[k1]);
            Assert.Equal(1, dc.Count);
            Assert.Equal(1, nValue);
            Assert.Equal(1, nCount);
            Assert.Equal(1, nContains);
            Assert.Equal(1, nTry);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs12.3-02 Dictionary accepts null values")]
        public void Rule_12_3_02_DictionaryAcceptsNullValue()
        {
            var dc = new ReactiveDictionary<K, string>();
            Assert.True(dc.Add(k1, null));
            Assert.True(dc.TryGetValue(k1, out var v));
            Assert.Null(v);
            Assert.True(dc.ContainsKey(k1));
            Assert.Null(dc[k1]);
            Assert.Equal(1, dc.Count);
        }

        [Fact(DisplayName = "cs12.3-03 Null key throws ArgumentNullException")]
        public void Rule_12_3_03_NullKeyThrowsArgumentNullException()
        {
            using var log = new Log();
            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            AssertArgNull(() => dc.Add(null, "x"));
            AssertArgNull(() => dc.SetValue(null, "x"));
            AssertArgNull(() => dc.SetValue(null, "x", out _));
            AssertArgNull(() => dc[null] = "x");
            AssertArgNull(() => { var unused = dc[null]; });
            AssertArgNull(() => dc.Remove(null));
            AssertArgNull(() => dc.ContainsKey(null));
            AssertArgNull(() => dc.TryGetValue(null, out _));
            var version = dc.Value;
            AssertArgNull(() => version.ContainsKey(null));
            AssertArgNull(() => version.TryGetValue(null, out _));
            AssertArgNull(() => { var unused = version[null]; });
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs12.5-04 SetValue(out old) records no read")]
        public void Rule_12_5_04_OldValueReadWithoutRecording()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            int n = 0;
            new Reactor(s).Add(() =>
            {
                string unused;
                dc.SetValue(k1, "a", out unused);
                n++;
            });
            Harness.Barrier(s);
            var writer = new Thread(() => dc[k1] = "b")
            {
                IsBackground = true
            };
            writer.Start();
            Assert.True(writer.Join(Harness.TimeoutMs));
            Harness.Barrier(s);
            Assert.Equal(1, n);
        }

        [Fact(DisplayName = "cs12.6-01 No-op write publishes/triggers nothing")]
        public void Rule_12_6_01_NoOpWritePublishesAndTriggersNothing()
        {
            using var log = new Log();
            var s = Harness.Create();
            var sc = new ReactiveSet<K> { k1 };
            int sv = 0, scount = 0, scontains = 0;
            new Reactor(s).Add(() => { var unused = sc.Values; sv++; });
            new Reactor(s).Add(() => { var unused = sc.Count; scount++; });
            new Reactor(s).Add(() => { var unused = sc.Contains(k1); scontains++; });
            Harness.Barrier(s);
            var sv0 = sc.Values;
            Assert.False(sc.Add(k1p));
            Assert.False(sc.Remove(k2));
            Harness.Barrier(s);
            Assert.Equal(1, sv);
            Assert.Equal(1, scount);
            Assert.Equal(1, scontains);
            Assert.Equal(0, sc.Values.Diff(sv0).Length);

            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            int dv = 0, dcount = 0, dcontains = 0, dtry = 0, dindex = 0;
            new Reactor(s).Add(() => { var unused = dc.Value; dv++; });
            new Reactor(s).Add(() => { var unused = dc.Count; dcount++; });
            new Reactor(s).Add(() => { var unused = dc.ContainsKey(k1); dcontains++; });
            new Reactor(s).Add(() => { string unused; dc.TryGetValue(k1, out unused); dtry++; });
            new Reactor(s).Add(() => { var unused = dc[k1]; dindex++; });
            Harness.Barrier(s);
            var dv0 = dc.Value;
            Assert.False(dc.Add(k1p, "z"));
            Assert.False(dc.SetValue(k1, "a"));
            string old;
            Assert.False(dc.SetValue(k1, "a", out old));
            dc[k1] = "a";
            Assert.False(dc.Remove(k2));
            Harness.Barrier(s);
            Assert.Equal(1, dv);
            Assert.Equal(1, dcount);
            Assert.Equal(1, dcontains);
            Assert.Equal(1, dtry);
            Assert.Equal(1, dindex);
            Assert.Equal(0, dc.Value.Diff(dv0).Length);
            Assert.Equal("a", dc[k1]);

            var emptySet = new ReactiveSet<K>();
            int emptySetN = 0;
            new Reactor(s).Add(() => { var unused = emptySet.Values; emptySetN++; });
            Harness.Barrier(s);
            emptySet.Clear();
            Harness.Barrier(s);
            Assert.Equal(1, emptySetN);

            var emptyDict = new ReactiveDictionary<K, string>();
            int emptyDictN = 0;
            new Reactor(s).Add(() => { var unused = emptyDict.Value; emptyDictN++; });
            Harness.Barrier(s);
            emptyDict.Clear();
            Harness.Barrier(s);
            Assert.Equal(1, emptyDictN);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs12.6-02 Value replace keeps stored key instance")]
        public void Rule_12_6_02_ReplacingValueKeepsStoredKeyInstance()
        {
            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            dc.SetValue(k1p, "b");
            dc[k1p] = "c";
            var ch = dc.Value.Diff(null);
            Assert.Equal(1, ch.Length);
            Assert.Equal(ChangeKind.Added, ch[0].Kind);
            Assert.Same(k1, ch[0].Key);
            Assert.Equal("c", ch[0].Value);
        }

        [Fact(DisplayName = "cs12.7-01 Clear and writes publish atomically")]
        public void Rule_12_7_01_ClearAndWritesPublishAtomically()
        {
            AssertAtomicClear(true);
            AssertAtomicClear(false);
        }

        [Fact(DisplayName = "cs12.7-02 Replaced struct value never tears")]
        public void Rule_12_7_02_ReplacedValueNeverTears()
        {
            var dc = new ReactiveDictionary<K, Big>
            {
                [k1] = new Big(0)
            };
            int stop = 0;
            int mismatch = 0;
            var writer = new Thread(() =>
            {
                for (int i = 1; i <= Harness.Stress; i++)
                    dc[k1] = new Big(i);
                Volatile.Write(ref stop, 1);
            });
            var readers = new Thread[4];
            for (int r = 0; r < readers.Length; r++)
            {
                readers[r] = new Thread(() =>
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        AssertFoundUntorn(dc.Value.TryGetValue(k1, out var fromVersion), fromVersion, ref mismatch);
                        AssertFoundUntorn(dc.TryGetValue(k1, out var fromDict), fromDict, ref mismatch);
                        if (dc.Count != 1)
                            Interlocked.Increment(ref mismatch);
                    }
                })
                {
                    IsBackground = true
                };
                readers[r].Start();
            }
            writer.IsBackground = true;
            writer.Start();
            Assert.True(writer.Join(Harness.TimeoutMs * 4));
            foreach (var reader in readers)
                Assert.True(reader.Join(Harness.TimeoutMs));
            Assert.Equal(0, mismatch);
        }

        [Fact(DisplayName = "cs12.8-01 Contains records presence, even absent")]
        public void Rule_12_8_01_ContainsRecordsPresenceAlsoWhenAbsent()
        {
            PresenceAddRemove(c => c.Add(k1));
            PresenceAddRemoveDict(d => d.Add(k1, "a"));
            PresenceAddRemoveDict(d => d.SetValue(k1, "a"));
            PresenceAddRemoveDict(d => d[k1] = "a");
        }

        [Fact(DisplayName = "cs12.8-02 Clear triggers presence reader")]
        public void Rule_12_8_02_ClearTriggersPresenceReader()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>
            {
                k1,
                k2
            };
            int sn = 0;
            bool slast = true;
            new Reactor(s).Add(() => { slast = sc.Contains(k1); sn++; });
            Harness.Barrier(s);
            sc.Clear();
            Harness.Barrier(s);
            Assert.Equal(2, sn);
            Assert.False(slast);

            var dc = new ReactiveDictionary<K, string>
            {
                { k1, "a" },
                { k2, "b" }
            };
            int dn = 0;
            bool dlast = true;
            new Reactor(s).Add(() => { dlast = dc.ContainsKey(k1); dn++; });
            Harness.Barrier(s);
            dc.Clear();
            Harness.Barrier(s);
            Assert.Equal(2, dn);
            Assert.False(dlast);
        }

        [Fact(DisplayName = "cs12.8-03 Other keys don't trigger presence read")]
        public void Rule_12_8_03_OtherKeysDoNotTriggerPresenceReader()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int sn = 0;
            new Reactor(s).Add(() => { var unused = sc.Contains(k1); sn++; });
            Harness.Barrier(s);
            sc.Add(k2);
            Harness.Barrier(s);
            sc.Remove(k2);
            Harness.Barrier(s);
            sc.Add(k2);
            sc.Clear();
            Harness.Barrier(s);
            Assert.Equal(1, sn);

            var dc = new ReactiveDictionary<K, string>();
            int dn = 0;
            new Reactor(s).Add(() => { var unused = dc.ContainsKey(k1); dn++; });
            Harness.Barrier(s);
            dc[k2] = "x";
            Harness.Barrier(s);
            dc.Remove(k2);
            Harness.Barrier(s);
            dc[k2] = "x";
            dc.Clear();
            Harness.Barrier(s);
            Assert.Equal(1, dn);
        }

        [Fact(DisplayName = "cs12.8-04 Value replace: presence reader quiet")]
        public void Rule_12_8_04_ReplacingValueDoesNotTriggerPresenceReader()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            int n = 0;
            new Reactor(s).Add(() => { var unused = dc.ContainsKey(k1); n++; });
            Harness.Barrier(s);
            dc.SetValue(k1, "b");
            dc[k1] = "c";
            string old;
            dc.SetValue(k1, "d", out old);
            Harness.Barrier(s);
            Assert.Equal(1, n);
        }

        [Fact(DisplayName = "cs12.9-01 TryGetValue reader sees add/set/remove")]
        public void Rule_12_9_01_TryGetValueRecordsValueOfKey()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>();
            int n = 0;
            var seen = new List<string>();
            var found = new List<bool>();
            new Reactor(s).Add(() =>
            {
                bool ok = dc.TryGetValue(k1, out var v);
                found.Add(ok);
                seen.Add(v);
                n++;
            });
            Harness.Barrier(s);
            dc.Add(k1, "a");
            Harness.Barrier(s);
            dc.SetValue(k1, "b");
            Harness.Barrier(s);
            dc[k1] = "c";
            Harness.Barrier(s);
            dc.Remove(k1);
            Harness.Barrier(s);
            dc.Add(k1, "d");
            Harness.Barrier(s);
            dc.Clear();
            Harness.Barrier(s);
            Assert.Equal(7, n);
            Assert.Equal([false, true, true, true, false, true, false], found.ToArray());
            Assert.Equal("a", seen[1]);
            Assert.Equal("b", seen[2]);
            Assert.Equal("c", seen[3]);
            Assert.Equal("d", seen[5]);
        }

        [Fact(DisplayName = "cs12.9-02 Indexer reader reruns, throws if gone")]
        public void Rule_12_9_02_IndexerGetterRecordsValueOfKey()
        {
            using var log = new Log();
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            int n = 0;
            var seen = new List<string>();
            new Reactor(s).Add(() =>
            {
                n++;
                seen.Add(dc[k1]);
            });
            Harness.Barrier(s);
            dc.SetValue(k1, "b");
            Harness.Barrier(s);
            log.Clear();
            dc.Remove(k1);
            Harness.Barrier(s);
            Assert.Equal(1, log.Count);
            Assert.IsType<KeyNotFoundException>(log.Snapshot()[0]);
            Assert.IsNotType<ReactiveException>(log.Snapshot()[0]);
            dc.Add(k1, "c");
            Harness.Barrier(s);
            Assert.Equal(4, n);
            Assert.Equal(["a", "b", "c"], seen.ToArray());
        }

        [Fact(DisplayName = "cs12.9-03 Other keys don't trigger value reader")]
        public void Rule_12_9_03_OtherKeysDoNotTriggerValueReader()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            int nTry = 0, nIndex = 0;
            new Reactor(s).Add(() => { string unused; dc.TryGetValue(k1, out unused); nTry++; });
            new Reactor(s).Add(() => { var unused = dc[k1]; nIndex++; });
            Harness.Barrier(s);
            dc.Add(k2, "x");
            dc.SetValue(k2, "y");
            dc[k2] = "z";
            dc.Remove(k2);
            Harness.Barrier(s);
            Assert.Equal(1, nTry);
            Assert.Equal(1, nIndex);
        }

        [Fact(DisplayName = "cs12.10-01 Values/Value reader reruns every write")]
        public void Rule_12_10_01_ValueRecordsWholeCollection()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int sn = 0;
            new Reactor(s).Add(() => { var unused = sc.Values; sn++; });
            Harness.Barrier(s);
            AssertRose(ref sn, () => sc.Add(k1), s);
            AssertRose(ref sn, () => sc.Add(k2), s);
            AssertRose(ref sn, () => sc.Remove(k1), s);
            AssertRose(ref sn, () => sc.Clear(), s);

            var dc = new ReactiveDictionary<K, string>();
            int dn = 0;
            new Reactor(s).Add(() => { var unused = dc.Value; dn++; });
            Harness.Barrier(s);
            AssertRose(ref dn, () => dc.Add(k1, "a"), s);
            AssertRose(ref dn, () => dc.SetValue(k1, "b"), s);
            string old;
            AssertRose(ref dn, () => dc.SetValue(k1, "c", out old), s);
            AssertRose(ref dn, () => dc[k1] = "d", s);
            AssertRose(ref dn, () => dc[k2] = "e", s);
            AssertRose(ref dn, () => dc.Remove(k1), s);
            AssertRose(ref dn, () => dc.Clear(), s);
        }

        [Fact(DisplayName = "cs12.10-02 Count reader reruns on every write")]
        public void Rule_12_10_02_CountRecordsWholeCollection()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int sn = 0;
            new Reactor(s).Add(() => { var unused = sc.Count; sn++; });
            Harness.Barrier(s);
            AssertRose(ref sn, () => sc.Add(k1), s);
            AssertRose(ref sn, () => sc.Add(k2), s);
            AssertRose(ref sn, () => sc.Remove(k1), s);
            AssertRose(ref sn, sc.Clear, s);

            var dc = new ReactiveDictionary<K, string>();
            int dn = 0;
            new Reactor(s).Add(() => { var unused = dc.Count; dn++; });
            Harness.Barrier(s);
            AssertRose(ref dn, () => dc.Add(k1, "a"), s);
            AssertRose(ref dn, () => dc.SetValue(k1, "b"), s);
            string old;
            AssertRose(ref dn, () => dc.SetValue(k1, "c", out old), s);
            AssertRose(ref dn, () => dc[k1] = "d", s);
            AssertRose(ref dn, () => dc[k2] = "e", s);
            AssertRose(ref dn, () => dc.Remove(k1), s);
            AssertRose(ref dn, dc.Clear, s);
        }

        [Fact(DisplayName = "cs12.11-01 Reads on a Version record nothing")]
        public void Rule_12_11_01_VersionReadsRecordNothing()
        {
            using var log = new Log();
            var s = Harness.Create();
            var sc = new ReactiveSet<K> { k1 };
            var sv = sc.Values;
            var sw = sc.Values;
            int sn = 0;
            new Reactor(s).Add(() =>
            {
                var unusedCount = sv.Count;
                var unusedDiff = sv.Diff(sw);
                var unusedNull = sv.Diff(null);
                var unusedContains = sv.Contains(k1);
                sn++;
            });
            Harness.Barrier(s);
            sc.Add(k2);
            Harness.Barrier(s);
            sc.Remove(k1);
            Harness.Barrier(s);
            sc.Clear();
            Harness.Barrier(s);
            Assert.Equal(1, sn);

            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            var dv = dc.Value;
            var dw = dc.Value;
            int dn = 0;
            new Reactor(s).Add(() =>
            {
                var unusedCount = dv.Count;
                var unusedDiff = dv.Diff(dw);
                var unusedNull = dv.Diff(null);
                var unusedContains = dv.ContainsKey(k1);
                string unused;
                dv.TryGetValue(k1, out unused);
                var unusedIndex = dv[k1];
                dn++;
            });
            Harness.Barrier(s);
            dc.Add(k2, "x");
            Harness.Barrier(s);
            dc[k1] = "b";
            Harness.Barrier(s);
            dc.Remove(k1);
            Harness.Barrier(s);
            dc.Clear();
            Harness.Barrier(s);
            Assert.Equal(1, dn);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs12.12-02 Version indexer throws for absent key")]
        public void Rule_12_12_02_VersionIndexerThrowsForAbsentKey()
        {
            using var log = new Log();
            var dc = new ReactiveDictionary<K, string> { { k2, "b" } };
            var v = dc.Value;
            var thrown = Record.Exception(() => { var unused = v[k1]; });
            Assert.IsType<KeyNotFoundException>(thrown);
            Assert.IsNotType<ReactiveException>(thrown);

            var s = Harness.Create();
            int n = 0;
            int caught = 0;
            new Reactor(s).Add(() =>
            {
                n++;
                try
                {
                    var unused = v[k1];
                }
                catch (KeyNotFoundException)
                {
                    caught++;
                }
            });
            Harness.Barrier(s);
            Assert.Equal(1, caught);
            dc.Add(k1, "a");
            Harness.Barrier(s);
            Assert.Equal(1, n);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs12.13-02 Dictionary Diff kinds and fields")]
        public void Rule_12_13_02_DictionaryDiffKindsAndFields()
        {
            var dc = new ReactiveDictionary<K, string>
            {
                { k1, "a" },
                { k2, "b" }
            };
            var v1 = dc.Value;
            dc.SetValue(k1, "c");
            dc.Remove(k2);
            dc.Add(k3, "d");
            var v2 = dc.Value;
            AssertDictTags(v2.Diff(v1), "Updated:1:c:a", "Removed:2:b:", "Added:3:d:");

            var di = new ReactiveDictionary<K, int>
            {
                { k1, 1 },
                { k2, 2 }
            };
            var w1 = di.Value;
            di.SetValue(k1, 3);
            di.Remove(k2);
            di.Add(k3, 4);
            var w2 = di.Value;
            var changes = w2.Diff(w1);
            Assert.Equal(3, changes.Length);
            foreach (var change in changes)
            {
                if (change.Kind == ChangeKind.Added)
                {
                    Assert.Same(k3, change.Key);
                    Assert.Equal(4, change.Value);
                    Assert.Equal(0, change.PreviousValue);
                }
                else if (change.Kind == ChangeKind.Removed)
                {
                    Assert.Same(k2, change.Key);
                    Assert.Equal(2, change.Value);
                    Assert.Equal(0, change.PreviousValue);
                }
                else
                {
                    Assert.Equal(ChangeKind.Updated, change.Kind);
                    Assert.Same(k1, change.Key);
                    Assert.Equal(3, change.Value);
                    Assert.Equal(1, change.PreviousValue);
                }
            }
        }

        [Fact(DisplayName = "cs12.13-03 Diff is the net change")]
        public void Rule_12_13_03_DiffIsNetChange()
        {
            var sc = new ReactiveSet<K> { k1 };
            var sv0 = sc.Values;
            sc.Add(k2);
            sc.Remove(k2);
            sc.Remove(k1);
            sc.Add(k1);
            var sv1 = sc.Values;
            Assert.Equal(0, sv1.Diff(sv0).Length);
            Assert.Equal(0, sv1.Diff(sv1).Length);

            var dc = new ReactiveDictionary<K, string> { { k1, "a" } };
            var dv0 = dc.Value;
            dc.SetValue(k1, "b");
            dc.SetValue(k1, "a");
            dc.Add(k2, "x");
            dc.Remove(k2);
            var dv1 = dc.Value;
            Assert.Empty(dv1.Diff(dv0));
            Assert.Equal(0, dv1.Diff(dv1).Length);
        }

        [Fact(DisplayName = "cs12.13-04 Diff(null) treats previous as empty")]
        public void Rule_12_13_04_NullPreviousCountsAsEmpty()
        {
            var sc = new ReactiveSet<K>
            {
                k1,
                k2
            };
            var setFromNull = sc.Values.Diff(null);
            foreach (var change in setFromNull)
                Assert.Equal(ChangeKind.Added, change.Kind);
            AssertSetTags(setFromNull, "Added:1:null", "Added:2:null");
            Assert.Equal(0, new ReactiveSet<K>().Values.Diff(null).Length);

            var dc = new ReactiveDictionary<K, string>
            {
                { k1, "a" },
                { k2, "b" }
            };
            var dictFromNull = dc.Value.Diff(null);
            foreach (var change in dictFromNull)
                Assert.Equal(ChangeKind.Added, change.Kind);
            AssertDictTags(dictFromNull, "Added:1:a:", "Added:2:b:");
            Assert.Equal(0, new ReactiveDictionary<K, string>().Value.Diff(null).Length);
        }

        [Fact(DisplayName = "cs12.13-05 Diff changes neither version")]
        public void Rule_12_13_05_DiffChangesNeitherVersion()
        {
            var dc = new ReactiveDictionary<K, string>
            {
                { k1, "a" },
                { k2, "b" }
            };
            var dv1 = dc.Value;
            dc.SetValue(k1, "c");
            dc.Remove(k2);
            dc.Add(k3, "d");
            var dv2 = dc.Value;
            var first = Tags(dv2.Diff(dv1), DictTag);
            var second = Tags(dv2.Diff(dv1), DictTag);
            AssertMultiset(first, second);
            AssertDictTags(dv1.Diff(dv2), "Updated:1:a:c", "Added:2:b:", "Removed:3:d:");
            Assert.Equal(2, dv1.Count);
            Assert.Equal("a", dv1[k1]);
            Assert.Equal("b", dv1[k2]);
            Assert.Equal(2, dv2.Count);
            Assert.Equal("c", dv2[k1]);
            Assert.Equal("d", dv2[k3]);

            var sc = new ReactiveSet<K>();
            var sv0 = sc.Values;
            sc.Add(k1);
            sc.Add(k2);
            var sv1 = sc.Values;
            sc.Remove(k1);
            sc.Add(k3);
            var sv2 = sc.Values;
            var setFirst = Tags(sv2.Diff(sv1), SetTag);
            var setSecond = Tags(sv2.Diff(sv1), SetTag);
            AssertMultiset(setFirst, setSecond);
            AssertSetTags(sv1.Diff(sv2), "Added:1:null", "Removed:3:null");
            Assert.Equal(2, sv1.Count);
            Assert.True(sv1.Contains(k1));
            Assert.True(sv1.Contains(k2));
            Assert.Equal(2, sv2.Count);
            Assert.True(sv2.Contains(k2));
            Assert.True(sv2.Contains(k3));
        }

        [Fact(DisplayName = "cs12.15-01 Version keeps no older version alive")]
        public void Rule_12_15_01_VersionKeepsNoOlderVersionAlive()
        {
            var setWr = MakeSetVersion(out var setV1);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.KeepAlive(setV1);
            Assert.False(setWr.IsAlive);
            Assert.True(setV1.Count >= 99);
            Assert.True(setV1.Contains(new K(50)));

            var dictWr = MakeDictVersion(out var dictV1);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.KeepAlive(dictV1);
            Assert.False(dictWr.IsAlive);
            Assert.True(dictV1.Count >= 99);
            Assert.True(dictV1.ContainsKey(new K(50)));
        }

        [Fact(DisplayName = "cs12.23-01 All four constructors work")]
        public void Rule_12_23_01_AllFourConstructorsWork()
        {
            var cmp = StringComparer.OrdinalIgnoreCase;
            var setEmpty = new ReactiveSet<string>();
            var setCmp = new ReactiveSet<string>(cmp);
            var setItems = new ReactiveSet<string>(["Ada", "Grace"]);
            var setBoth = new ReactiveSet<string>(["Ada", "Grace"], cmp);
            Assert.NotNull(setEmpty);
            Assert.NotNull(setCmp);
            Assert.Equal(2, setItems.Values.Count);
            Assert.True(setItems.Values.Contains("Ada"));
            Assert.True(setItems.Values.Contains("Grace"));
            Assert.Equal(2, setBoth.Values.Count);
            Assert.True(setBoth.Contains("ADA"));
            Assert.True(setBoth.Contains("grace"));
            setCmp.Add("Ada");
            Assert.True(setCmp.Contains("ADA"));

            var dictEmpty = new ReactiveDictionary<string, string>();
            var dictCmp = new ReactiveDictionary<string, string>(cmp);
            var dictItems = new ReactiveDictionary<string, string>([
                new KeyValuePair<string, string>("Ada", "1"),
                new KeyValuePair<string, string>("Grace", "2")
            ]);
            var dictBoth = new ReactiveDictionary<string, string>([
                new KeyValuePair<string, string>("Ada", "1"),
                new KeyValuePair<string, string>("Grace", "2")
            ], cmp);
            Assert.NotNull(dictEmpty);
            Assert.NotNull(dictCmp);
            Assert.Equal(2, dictItems.Value.Count);
            Assert.Equal("1", dictItems["Ada"]);
            Assert.Equal("2", dictItems["Grace"]);
            Assert.Equal(2, dictBoth.Value.Count);
            Assert.True(dictBoth.ContainsKey("ADA"));
            Assert.Equal("1", dictBoth["ada"]);
            dictCmp.Add("Ada", "1");
            Assert.True(dictCmp.ContainsKey("ADA"));
        }

        [Fact(DisplayName = "cs12.23-02 Null comparer means default comparer")]
        public void Rule_12_23_02_NullComparerMeansDefault()
        {
            var setCmp = new ReactiveSet<string>((IEqualityComparer<string>)null);
            var setItems = new ReactiveSet<string>(["a"], null);
            Assert.True(setCmp.Add("a"));
            Assert.True(setCmp.Add("A"));
            Assert.Equal(2, setCmp.Count);
            Assert.True(setItems.Contains("a"));
            Assert.False(setItems.Contains("A"));

            var dictCmp = new ReactiveDictionary<string, string>((IEqualityComparer<string>)null);
            var dictItems = new ReactiveDictionary<string, string>(
                [new KeyValuePair<string, string>("a", "1")],
                null);
            Assert.True(dictCmp.Add("a", "1"));
            Assert.True(dictCmp.Add("A", "2"));
            Assert.Equal(2, dictCmp.Count);
            Assert.True(dictItems.ContainsKey("a"));
            Assert.False(dictItems.ContainsKey("A"));
        }

        [Fact(DisplayName = "cs12.23-03 Null initial contents throw")]
        public void Rule_12_23_03_NullInitialContentsThrow()
        {
            var cmp = StringComparer.OrdinalIgnoreCase;
            Assert.Throws<ArgumentNullException>(() => new ReactiveSet<string>((IEnumerable<string>)null));
            Assert.Throws<ArgumentNullException>(() => new ReactiveSet<string>((IEnumerable<string>)null, cmp));
            Assert.Throws<ArgumentNullException>(() => new ReactiveDictionary<string, string>((IEnumerable<KeyValuePair<string, string>>)null));
            Assert.Throws<ArgumentNullException>(() => new ReactiveDictionary<string, string>((IEnumerable<KeyValuePair<string, string>>)null, cmp));
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
            Dictionary<int, int> newer,
            DictChange<int, int>[] changes)
        {
            var expected = new List<string>();
            foreach (var pair in older)
            {
                if (!newer.TryGetValue(pair.Key, out var value))
                    expected.Add("Removed:" + pair.Key + "=" + pair.Value);
                else if (!Equals(pair.Value, value))
                    expected.Add("Updated:" + pair.Key + "=" + pair.Value + "->" + value);
            }

            foreach (var pair in newer)
            {
                if (!older.ContainsKey(pair.Key))
                    expected.Add("Added:" + pair.Key + "=" + pair.Value);
            }

            Assert.Equal([.. expected.OrderBy(x => x)], Names(changes).OrderBy(x => x).ToArray());
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

        static IEnumerable<string> Names(DictChange<string,int>[] changes)
        {
            return changes.Select(change =>
            {
                if (change.Kind == ChangeKind.Updated)
                    return change.Kind + ":" + change.Key + "=" + change.PreviousValue + "->" + change.Value;
                return change.Kind + ":" + change.Key + "=" + change.Value;
            });
        }

        static IEnumerable<string> Names(DictChange<int,int>[] changes)
        {
            return changes.Select(change =>
            {
                if (change.Kind == ChangeKind.Updated)
                    return change.Kind + ":" + change.Key + "=" + change.PreviousValue + "->" + change.Value;
                return change.Kind + ":" + change.Key + "=" + change.Value;
            });
        }

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

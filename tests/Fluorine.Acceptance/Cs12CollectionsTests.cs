using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs12CollectionsTests : AcceptanceTest
    {
        static readonly K k1 = new(1);
        static readonly K k1p = new(1);
        static readonly K k2 = new(2);
        static readonly K k3 = new(3);

        [Fact(DisplayName = "cs12.1-01")]
        public void cs12_1_01()
        {
            Assert.True(typeof(Register).IsAssignableFrom(typeof(ReactiveSet<K>)));
            Assert.True(typeof(Register).IsAssignableFrom(typeof(ReactiveDictionary<K, string>)));
            Assert.Equal("Version", typeof(ReactiveSet<K>).GetProperty("Value").PropertyType.Name);
            Assert.Equal("Version", typeof(ReactiveDictionary<K, string>).GetProperty("Value").PropertyType.Name);
        }

        [Fact(DisplayName = "cs12.1-02")]
        public void cs12_1_02()
        {
            using (var log = new Log())
            {
                RunAnyThreadStress(true);
                RunAnyThreadStress(false);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs12.1-03")]
        public void cs12_1_03()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int sn = 0;
            new Reactor(s).Add(() => { var unused = sc.Value; sn++; });
            Harness.Barrier(s);
            var setThread = new Thread(() => sc.Add(k1));
            setThread.IsBackground = true;
            setThread.Start();
            Assert.True(setThread.Join(Harness.TimeoutMs));
            Harness.Barrier(s);
            Assert.Equal(2, sn);

            var dc = new ReactiveDictionary<K, string>();
            int dn = 0;
            new Reactor(s).Add(() => { var unused = dc.Value; dn++; });
            Harness.Barrier(s);
            var dictThread = new Thread(() => dc.Add(k1, "a"));
            dictThread.IsBackground = true;
            dictThread.Start();
            Assert.True(dictThread.Join(Harness.TimeoutMs));
            Harness.Barrier(s);
            Assert.Equal(2, dn);
        }

        [Fact(DisplayName = "cs12.1-04")]
        public void cs12_1_04()
        {
            var sc = new ReactiveSet<K>();
            sc.Add(k1);
            var sv = sc.Value;
            sc.Add(k2);
            sc.Remove(k1);
            Assert.Equal(1, sv.Count);
            Assert.True(sv.Contains(k1));
            Assert.False(sv.Contains(k2));

            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            var dv = dc.Value;
            dc[k1] = "b";
            dc.Add(k2, "c");
            dc.Remove(k1);
            Assert.Equal(1, dv.Count);
            string v;
            Assert.True(dv.TryGetValue(k1, out v));
            Assert.Equal("a", v);
            Assert.Equal("a", dv[k1]);
            Assert.False(dv.ContainsKey(k2));
        }

        [Fact(DisplayName = "cs12.2-01")]
        public void cs12_2_01()
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

        [Fact(DisplayName = "cs12.2-02")]
        public void cs12_2_02()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, Val>();
            var v1 = new Val(1);
            var v1p = new Val(1);
            dc[k1] = v1;
            int n = 0;
            new Reactor(s).Add(() => { var unused = dc.Value; n++; });
            Harness.Barrier(s);
            Assert.False(dc.SetValue(k1, v1p));
            Harness.Barrier(s);
            Assert.Equal(1, n);
            Assert.Same(v1, dc[k1]);
        }

        [Fact(DisplayName = "cs12.2-03")]
        public void cs12_2_03()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, EquatableVal>();
            var e1 = new EquatableVal(1);
            var e1p = new EquatableVal(1);
            dc[k1] = e1;
            int n = 0;
            new Reactor(s).Add(() => { var unused = dc.Value; n++; });
            Harness.Barrier(s);
            Assert.True(dc.SetValue(k1, e1p));
            Harness.Barrier(s);
            Assert.Equal(2, n);
        }

        [Fact(DisplayName = "cs12.2-04")]
        public void cs12_2_04()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>();
            dc[k1] = null;
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

        [Fact(DisplayName = "cs12.2-05")]
        public void cs12_2_05()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var dc = new ReactiveDictionary<K, Boom>();
                var x = new InvalidOperationException("eq");
                var b1 = new Boom(x);
                var b2 = new Boom(x);
                dc[k1] = b1;
                int nValue = 0, nCount = 0, nContains = 0, nTry = 0;
                new Reactor(s).Add(() => { var unused = dc.Value; nValue++; });
                new Reactor(s).Add(() => { var unused = dc.Count; nCount++; });
                new Reactor(s).Add(() => { var unused = dc.ContainsKey(k1); nContains++; });
                new Reactor(s).Add(() => { Boom v; dc.TryGetValue(k1, out v); nTry++; });
                Harness.Barrier(s);
                AssertThrowsUnwrapped(x, () => dc.SetValue(k1, b2));
                Boom old;
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
        }

        [Fact(DisplayName = "cs12.3-01")]
        public void cs12_3_01()
        {
            var sc = new ReactiveSet<K>();
            Assert.True(sc.Add(null));
            Assert.True(sc.Contains(null));
            Assert.False(sc.Add(null));
            Assert.True(sc.Remove(null));
            Assert.False(sc.Contains(null));
            Assert.False(sc.Remove(null));
        }

        [Fact(DisplayName = "cs12.3-02")]
        public void cs12_3_02()
        {
            var dc = new ReactiveDictionary<K, string>();
            Assert.True(dc.Add(k1, null));
            string v;
            Assert.True(dc.TryGetValue(k1, out v));
            Assert.Null(v);
            Assert.True(dc.ContainsKey(k1));
            Assert.Null(dc[k1]);
            Assert.Equal(1, dc.Count);
        }

        [Fact(DisplayName = "cs12.3-03")]
        public void cs12_3_03()
        {
            using (var log = new Log())
            {
                var dc = new ReactiveDictionary<K, string>();
                dc.Add(k1, "a");
                AssertArgNull(() => dc.Add(null, "x"));
                AssertArgNull(() => dc.SetValue(null, "x"));
                string old;
                AssertArgNull(() => dc.SetValue(null, "x", out old));
                AssertArgNull(() => dc[null] = "x");
                AssertArgNull(() => { var unused = dc[null]; });
                AssertArgNull(() => dc.Remove(null));
                AssertArgNull(() => dc.ContainsKey(null));
                string v;
                AssertArgNull(() => dc.TryGetValue(null, out v));
                var version = dc.Value;
                AssertArgNull(() => version.ContainsKey(null));
                AssertArgNull(() => version.TryGetValue(null, out v));
                AssertArgNull(() => { var unused = version[null]; });
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs12.4-01")]
        public void cs12_4_01()
        {
            var sc = new ReactiveSet<K>();
            Assert.True(sc.Add(k1));
            Assert.False(sc.Add(k1p));
            Assert.False(sc.Remove(k2));
            Assert.True(sc.Remove(k1p));
            Assert.False(sc.Remove(k1));
        }

        [Fact(DisplayName = "cs12.4-02")]
        public void cs12_4_02()
        {
            var sc = new ReactiveSet<K>();
            sc.Add(k1);
            sc.Add(k2);
            sc.Clear();
            Assert.Equal(0, sc.Count);
            Assert.False(sc.Contains(k1));
            Assert.False(sc.Contains(k2));
        }

        [Fact(DisplayName = "cs12.5-01")]
        public void cs12_5_01()
        {
            var dc = new ReactiveDictionary<K, string>();
            Assert.True(dc.Add(k1, "a"));
            Assert.False(dc.Add(k1p, "b"));
            Assert.Equal("a", dc[k1]);
            Assert.Equal(1, dc.Count);
        }

        [Fact(DisplayName = "cs12.5-02")]
        public void cs12_5_02()
        {
            var dc = new ReactiveDictionary<K, string>();
            Assert.True(dc.SetValue(k1, "a"));
            Assert.True(dc.SetValue(k1p, "b"));
            Assert.False(dc.SetValue(k1, "b"));
            Assert.Equal("b", dc[k1]);
        }

        [Fact(DisplayName = "cs12.5-03")]
        public void cs12_5_03()
        {
            var dc = new ReactiveDictionary<K, string>();
            string o;
            Assert.True(dc.SetValue(k1, "a", out o));
            Assert.Null(o);
            Assert.True(dc.SetValue(k1, "b", out o));
            Assert.Equal("a", o);
            Assert.False(dc.SetValue(k1, "b", out o));
            Assert.Equal("b", o);

            var di = new ReactiveDictionary<K, int>();
            int oi;
            Assert.True(di.SetValue(k1, 5, out oi));
            Assert.Equal(0, oi);
        }

        [Fact(DisplayName = "cs12.5-04")]
        public void cs12_5_04()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            int n = 0;
            new Reactor(s).Add(() =>
            {
                string unused;
                dc.SetValue(k1, "a", out unused);
                n++;
            });
            Harness.Barrier(s);
            var writer = new Thread(() => dc[k1] = "b");
            writer.IsBackground = true;
            writer.Start();
            Assert.True(writer.Join(Harness.TimeoutMs));
            Harness.Barrier(s);
            Assert.Equal(1, n);
        }

        [Fact(DisplayName = "cs12.5-05")]
        public void cs12_5_05()
        {
            var dc = new ReactiveDictionary<K, string>();
            dc[k1] = "a";
            dc[k1p] = "b";
            Assert.Equal(1, dc.Count);
            Assert.Equal("b", dc[k1]);
        }

        [Fact(DisplayName = "cs12.5-06")]
        public void cs12_5_06()
        {
            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            dc.Add(k2, "b");
            Assert.False(dc.Remove(k3));
            Assert.True(dc.Remove(k1p));
            Assert.False(dc.Remove(k1));
            dc.Clear();
            Assert.Equal(0, dc.Count);
            Assert.False(dc.ContainsKey(k2));
        }

        [Fact(DisplayName = "cs12.6-01")]
        public void cs12_6_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var sc = new ReactiveSet<K>();
                sc.Add(k1);
                int sv = 0, scount = 0, scontains = 0;
                new Reactor(s).Add(() => { var unused = sc.Value; sv++; });
                new Reactor(s).Add(() => { var unused = sc.Count; scount++; });
                new Reactor(s).Add(() => { var unused = sc.Contains(k1); scontains++; });
                Harness.Barrier(s);
                var sv0 = sc.Value;
                Assert.False(sc.Add(k1p));
                Assert.False(sc.Remove(k2));
                Harness.Barrier(s);
                Assert.Equal(1, sv);
                Assert.Equal(1, scount);
                Assert.Equal(1, scontains);
                Assert.Equal(0, sc.Value.Diff(sv0).Length);

                var dc = new ReactiveDictionary<K, string>();
                dc.Add(k1, "a");
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
                new Reactor(s).Add(() => { var unused = emptySet.Value; emptySetN++; });
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
        }

        [Fact(DisplayName = "cs12.6-02")]
        public void cs12_6_02()
        {
            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            dc.SetValue(k1p, "b");
            dc[k1p] = "c";
            var ch = dc.Value.Diff(null);
            Assert.Equal(1, ch.Length);
            Assert.Equal(ReactiveDictionary<K, string>.ChangeKind.Added, ch[0].Kind);
            Assert.Same(k1, ch[0].Key);
            Assert.Equal("c", ch[0].Value);
        }

        [Fact(DisplayName = "cs12.7-01")]
        public void cs12_7_01()
        {
            AssertAtomicClear(true);
            AssertAtomicClear(false);
        }

        [Fact(DisplayName = "cs12.7-02")]
        public void cs12_7_02()
        {
            var dc = new ReactiveDictionary<K, Big>();
            dc[k1] = new Big(0);
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
                        Big fromVersion;
                        AssertFoundUntorn(dc.Value.TryGetValue(k1, out fromVersion), fromVersion, ref mismatch);
                        Big fromDict;
                        AssertFoundUntorn(dc.TryGetValue(k1, out fromDict), fromDict, ref mismatch);
                        if (dc.Count != 1)
                            Interlocked.Increment(ref mismatch);
                    }
                });
                readers[r].IsBackground = true;
                readers[r].Start();
            }
            writer.IsBackground = true;
            writer.Start();
            Assert.True(writer.Join(Harness.TimeoutMs * 4));
            foreach (var reader in readers)
                Assert.True(reader.Join(Harness.TimeoutMs));
            Assert.Equal(0, mismatch);
        }

        [Fact(DisplayName = "cs12.8-01")]
        public void cs12_8_01()
        {
            PresenceAddRemove(c => c.Add(k1));
            PresenceAddRemoveDict(d => d.Add(k1, "a"));
            PresenceAddRemoveDict(d => d.SetValue(k1, "a"));
            PresenceAddRemoveDict(d => d[k1] = "a");
        }

        [Fact(DisplayName = "cs12.8-02")]
        public void cs12_8_02()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            sc.Add(k1);
            sc.Add(k2);
            int sn = 0;
            bool slast = true;
            new Reactor(s).Add(() => { slast = sc.Contains(k1); sn++; });
            Harness.Barrier(s);
            sc.Clear();
            Harness.Barrier(s);
            Assert.Equal(2, sn);
            Assert.False(slast);

            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            dc.Add(k2, "b");
            int dn = 0;
            bool dlast = true;
            new Reactor(s).Add(() => { dlast = dc.ContainsKey(k1); dn++; });
            Harness.Barrier(s);
            dc.Clear();
            Harness.Barrier(s);
            Assert.Equal(2, dn);
            Assert.False(dlast);
        }

        [Fact(DisplayName = "cs12.8-03")]
        public void cs12_8_03()
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

        [Fact(DisplayName = "cs12.8-04")]
        public void cs12_8_04()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
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

        [Fact(DisplayName = "cs12.9-01")]
        public void cs12_9_01()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>();
            int n = 0;
            var seen = new List<string>();
            var found = new List<bool>();
            new Reactor(s).Add(() =>
            {
                string v;
                bool ok = dc.TryGetValue(k1, out v);
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
            Assert.Equal(new[] { false, true, true, true, false, true, false }, found.ToArray());
            Assert.Equal("a", seen[1]);
            Assert.Equal("b", seen[2]);
            Assert.Equal("c", seen[3]);
            Assert.Equal("d", seen[5]);
        }

        [Fact(DisplayName = "cs12.9-02")]
        public void cs12_9_02()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var dc = new ReactiveDictionary<K, string>();
                dc.Add(k1, "a");
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
                Assert.Equal(new[] { "a", "b", "c" }, seen.ToArray());
            }
        }

        [Fact(DisplayName = "cs12.9-03")]
        public void cs12_9_03()
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
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

        [Fact(DisplayName = "cs12.9-04")]
        public void cs12_9_04()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var dc = new ReactiveDictionary<K, string>();
                int n = 0;
                int caught = 0;
                new Reactor(s).Add(() =>
                {
                    n++;
                    try
                    {
                        var unused = dc[k1];
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
                Assert.Equal(2, n);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs12.9-05")]
        public void cs12_9_05()
        {
            using (var log = new Log())
            {
                var dc = new ReactiveDictionary<K, string>();
                var thrown = Record.Exception(() => { var unused = dc[k1]; });
                Assert.IsType<KeyNotFoundException>(thrown);
                Assert.IsNotType<ReactiveException>(thrown);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs12.10-01")]
        public void cs12_10_01()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int sn = 0;
            new Reactor(s).Add(() => { var unused = sc.Value; sn++; });
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

        [Fact(DisplayName = "cs12.10-02")]
        public void cs12_10_02()
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int sn = 0;
            new Reactor(s).Add(() => { var unused = sc.Count; sn++; });
            Harness.Barrier(s);
            AssertRose(ref sn, () => sc.Add(k1), s);
            AssertRose(ref sn, () => sc.Add(k2), s);
            AssertRose(ref sn, () => sc.Remove(k1), s);
            AssertRose(ref sn, () => sc.Clear(), s);

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
            AssertRose(ref dn, () => dc.Clear(), s);
        }

        [Fact(DisplayName = "cs12.11-01")]
        public void cs12_11_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var sc = new ReactiveSet<K>();
                sc.Add(k1);
                var sv = sc.Value;
                var sw = sc.Value;
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

                var dc = new ReactiveDictionary<K, string>();
                dc.Add(k1, "a");
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
        }

        [Fact(DisplayName = "cs12.12-01")]
        public void cs12_12_01()
        {
            AssertVersionSurface(typeof(ReactiveSet<K>), true);
            AssertVersionSurface(typeof(ReactiveDictionary<K, string>), false);
        }

        [Fact(DisplayName = "cs12.12-02")]
        public void cs12_12_02()
        {
            using (var log = new Log())
            {
                var dc = new ReactiveDictionary<K, string>();
                dc.Add(k2, "b");
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
        }

        [Fact(DisplayName = "cs12.13-01")]
        public void cs12_13_01()
        {
            var sc = new ReactiveSet<K>();
            var v0 = sc.Value;
            sc.Add(k1);
            sc.Add(k2);
            var v1 = sc.Value;
            sc.Remove(k1);
            sc.Add(k3);
            var v2 = sc.Value;
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

        [Fact(DisplayName = "cs12.13-02")]
        public void cs12_13_02()
        {
            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            dc.Add(k2, "b");
            var v1 = dc.Value;
            dc.SetValue(k1, "c");
            dc.Remove(k2);
            dc.Add(k3, "d");
            var v2 = dc.Value;
            AssertDictTags(v2.Diff(v1), "Updated:1:c:a", "Removed:2:b:", "Added:3:d:");

            var di = new ReactiveDictionary<K, int>();
            di.Add(k1, 1);
            di.Add(k2, 2);
            var w1 = di.Value;
            di.SetValue(k1, 3);
            di.Remove(k2);
            di.Add(k3, 4);
            var w2 = di.Value;
            var changes = w2.Diff(w1);
            Assert.Equal(3, changes.Length);
            foreach (var change in changes)
            {
                if (change.Kind == ReactiveDictionary<K, int>.ChangeKind.Added)
                {
                    Assert.Same(k3, change.Key);
                    Assert.Equal(4, change.Value);
                    Assert.Equal(0, change.PreviousValue);
                }
                else if (change.Kind == ReactiveDictionary<K, int>.ChangeKind.Removed)
                {
                    Assert.Same(k2, change.Key);
                    Assert.Equal(2, change.Value);
                    Assert.Equal(0, change.PreviousValue);
                }
                else
                {
                    Assert.Equal(ReactiveDictionary<K, int>.ChangeKind.Updated, change.Kind);
                    Assert.Same(k1, change.Key);
                    Assert.Equal(3, change.Value);
                    Assert.Equal(1, change.PreviousValue);
                }
            }
        }

        [Fact(DisplayName = "cs12.13-03")]
        public void cs12_13_03()
        {
            var sc = new ReactiveSet<K>();
            sc.Add(k1);
            var sv0 = sc.Value;
            sc.Add(k2);
            sc.Remove(k2);
            sc.Remove(k1);
            sc.Add(k1);
            var sv1 = sc.Value;
            Assert.Equal(0, sv1.Diff(sv0).Length);
            Assert.Equal(0, sv1.Diff(sv1).Length);

            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            var dv0 = dc.Value;
            dc.SetValue(k1, "b");
            dc.SetValue(k1, "a");
            dc.Add(k2, "x");
            dc.Remove(k2);
            var dv1 = dc.Value;
            Assert.Equal(0, dv1.Diff(dv0).Length);
            Assert.Equal(0, dv1.Diff(dv1).Length);
        }

        [Fact(DisplayName = "cs12.13-04")]
        public void cs12_13_04()
        {
            var sc = new ReactiveSet<K>();
            sc.Add(k1);
            sc.Add(k2);
            var setFromNull = sc.Value.Diff(null);
            foreach (var change in setFromNull)
                Assert.Equal(ReactiveSet<K>.ChangeKind.Added, change.Kind);
            AssertSetTags(setFromNull, "Added:1:null", "Added:2:null");
            Assert.Equal(0, new ReactiveSet<K>().Value.Diff(null).Length);

            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            dc.Add(k2, "b");
            var dictFromNull = dc.Value.Diff(null);
            foreach (var change in dictFromNull)
                Assert.Equal(ReactiveDictionary<K, string>.ChangeKind.Added, change.Kind);
            AssertDictTags(dictFromNull, "Added:1:a:", "Added:2:b:");
            Assert.Equal(0, new ReactiveDictionary<K, string>().Value.Diff(null).Length);
        }

        [Fact(DisplayName = "cs12.13-05")]
        public void cs12_13_05()
        {
            var dc = new ReactiveDictionary<K, string>();
            dc.Add(k1, "a");
            dc.Add(k2, "b");
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
            var sv0 = sc.Value;
            sc.Add(k1);
            sc.Add(k2);
            var sv1 = sc.Value;
            sc.Remove(k1);
            sc.Add(k3);
            var sv2 = sc.Value;
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

        [Fact(DisplayName = "cs12.13-06")]
        public void cs12_13_06()
        {
            var sc = new ReactiveSet<K>();
            sc.Add(k1);
            var v1 = sc.Value;
            sc.Remove(k1);
            sc.Add(k1p);
            var v2 = sc.Value;
            var forward = v2.Diff(v1);
            Assert.Equal(1, forward.Length);
            Assert.Equal(ReactiveSet<K>.ChangeKind.Replaced, forward[0].Kind);
            Assert.True(ReferenceEquals(forward[0].Item, k1p));
            Assert.True(ReferenceEquals(forward[0].Previous, k1));
            var back = v1.Diff(v2);
            Assert.Equal(1, back.Length);
            Assert.Equal(ReactiveSet<K>.ChangeKind.Replaced, back[0].Kind);
            Assert.True(ReferenceEquals(back[0].Item, k1));
            Assert.True(ReferenceEquals(back[0].Previous, k1p));

            var ints = new ReactiveSet<int>();
            ints.Add(1);
            var w1 = ints.Value;
            ints.Remove(1);
            ints.Add(1);
            var w2 = ints.Value;
            Assert.Equal(0, w2.Diff(w1).Length);
        }

        [Fact(DisplayName = "cs12.15-01")]
        public void cs12_15_01()
        {
            ReactiveSet<K>.Version setV1;
            var setWr = MakeSetVersion(out setV1);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.KeepAlive(setV1);
            Assert.False(setWr.IsAlive);
            Assert.True(setV1.Count >= 99);
            Assert.True(setV1.Contains(new K(50)));

            ReactiveDictionary<K, string>.Version dictV1;
            var dictWr = MakeDictVersion(out dictV1);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.KeepAlive(dictV1);
            Assert.False(dictWr.IsAlive);
            Assert.True(dictV1.Count >= 99);
            Assert.True(dictV1.ContainsKey(new K(50)));
        }

        [Fact(DisplayName = "cs12.23-01")]
        public void cs12_23_01()
        {
            var cmp = StringComparer.OrdinalIgnoreCase;
            var setEmpty = new ReactiveSet<string>();
            var setCmp = new ReactiveSet<string>(cmp);
            var setItems = new ReactiveSet<string>(new[] { "Ada", "Grace" });
            var setBoth = new ReactiveSet<string>(new[] { "Ada", "Grace" }, cmp);
            Assert.NotNull(setEmpty);
            Assert.NotNull(setCmp);
            Assert.Equal(2, setItems.Value.Count);
            Assert.True(setItems.Value.Contains("Ada"));
            Assert.True(setItems.Value.Contains("Grace"));
            Assert.Equal(2, setBoth.Value.Count);
            Assert.True(setBoth.Contains("ADA"));
            Assert.True(setBoth.Contains("grace"));
            setCmp.Add("Ada");
            Assert.True(setCmp.Contains("ADA"));

            var dictEmpty = new ReactiveDictionary<string, string>();
            var dictCmp = new ReactiveDictionary<string, string>(cmp);
            var dictItems = new ReactiveDictionary<string, string>(new[]
            {
                new KeyValuePair<string, string>("Ada", "1"),
                new KeyValuePair<string, string>("Grace", "2")
            });
            var dictBoth = new ReactiveDictionary<string, string>(new[]
            {
                new KeyValuePair<string, string>("Ada", "1"),
                new KeyValuePair<string, string>("Grace", "2")
            }, cmp);
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

        [Fact(DisplayName = "cs12.23-02")]
        public void cs12_23_02()
        {
            var setCmp = new ReactiveSet<string>((IEqualityComparer<string>)null);
            var setItems = new ReactiveSet<string>(new[] { "a" }, null);
            Assert.True(setCmp.Add("a"));
            Assert.True(setCmp.Add("A"));
            Assert.Equal(2, setCmp.Count);
            Assert.True(setItems.Contains("a"));
            Assert.False(setItems.Contains("A"));

            var dictCmp = new ReactiveDictionary<string, string>((IEqualityComparer<string>)null);
            var dictItems = new ReactiveDictionary<string, string>(
                new[] { new KeyValuePair<string, string>("a", "1") },
                null);
            Assert.True(dictCmp.Add("a", "1"));
            Assert.True(dictCmp.Add("A", "2"));
            Assert.Equal(2, dictCmp.Count);
            Assert.True(dictItems.ContainsKey("a"));
            Assert.False(dictItems.ContainsKey("A"));
        }

        [Fact(DisplayName = "cs12.23-03")]
        public void cs12_23_03()
        {
            var cmp = StringComparer.OrdinalIgnoreCase;
            Assert.Throws<ArgumentNullException>(() => new ReactiveSet<string>((IEnumerable<string>)null));
            Assert.Throws<ArgumentNullException>(() => new ReactiveSet<string>((IEnumerable<string>)null, cmp));
            Assert.Throws<ArgumentNullException>(() => new ReactiveDictionary<string, string>((IEnumerable<KeyValuePair<string, string>>)null));
            Assert.Throws<ArgumentNullException>(() => new ReactiveDictionary<string, string>((IEnumerable<KeyValuePair<string, string>>)null, cmp));
        }

        [Fact(DisplayName = "cs12.E-01", Skip = "pending spec rule (enumeration not yet specified)")]
        public void cs12_E_01()
        {
        }

        [Fact(DisplayName = "cs12.E-02", Skip = "pending spec rule")]
        public void cs12_E_02()
        {
        }

        [Fact(DisplayName = "cs12.E-03", Skip = "pending spec rule")]
        public void cs12_E_03()
        {
        }

        [Fact(DisplayName = "cs12.E-04", Skip = "pending spec rule")]
        public void cs12_E_04()
        {
        }

        [Fact(DisplayName = "cs12.E-05", Skip = "pending spec rule")]
        public void cs12_E_05()
        {
        }

        [Fact(DisplayName = "cs12.E-06", Skip = "pending spec rule")]
        public void cs12_E_06()
        {
        }

        [Fact(DisplayName = "cs12.E-07", Skip = "pending spec rule")]
        public void cs12_E_07()
        {
        }

        [Fact(DisplayName = "cs12.E-08", Skip = "pending spec rule (whether enumerating C records a whole-collection read)")]
        public void cs12_E_08()
        {
        }

        static void RunAnyThreadStress(bool set)
        {
            var sc = new ReactiveSet<K>();
            var dc = new ReactiveDictionary<K, string>();
            var errors = new Exception[8];
            var mirrors = new HashSet<int>[8];
            var values = new Dictionary<int, string>[8];
            var threads = new Thread[8];
            for (int i = 0; i < 8; i++)
            {
                int index = i;
                mirrors[index] = new HashSet<int>();
                values[index] = new Dictionary<int, string>();
                threads[index] = new Thread(() =>
                {
                    try
                    {
                        int start = 1000 * index;
                        var rng = new Random(index + 1);
                        for (int n = 0; n < Harness.Stress; n++)
                        {
                            int id = start + rng.Next(100);
                            var key = new K(id);
                            if (set)
                            {
                                if (rng.Next(2) == 0)
                                {
                                    if (sc.Add(key))
                                        mirrors[index].Add(id);
                                }
                                else
                                {
                                    if (sc.Remove(key))
                                        mirrors[index].Remove(id);
                                }
                                var unusedCount = sc.Count;
                                var unusedValue = sc.Value;
                                var unusedContains = sc.Contains(key);
                            }
                            else
                            {
                                int op = rng.Next(3);
                                if (op == 0)
                                {
                                    if (dc.Add(key, "v" + id))
                                    {
                                        mirrors[index].Add(id);
                                        values[index][id] = "v" + id;
                                    }
                                }
                                else if (op == 1)
                                {
                                    string next = "s" + n;
                                    if (dc.SetValue(key, next))
                                    {
                                        mirrors[index].Add(id);
                                        values[index][id] = next;
                                    }
                                }
                                else
                                {
                                    if (dc.Remove(key))
                                    {
                                        mirrors[index].Remove(id);
                                        values[index].Remove(id);
                                    }
                                }
                                var unusedCount = dc.Count;
                                var unusedValue = dc.Value;
                                var unusedContains = dc.ContainsKey(key);
                                string unused;
                                dc.TryGetValue(key, out unused);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors[index] = ex;
                    }
                });
                threads[index].IsBackground = true;
                threads[index].Start();
            }
            foreach (var thread in threads)
                Assert.True(thread.Join(Harness.TimeoutMs * 4), "stress thread did not finish");
            foreach (var error in errors)
                Assert.Null(error);

            int total = 0;
            for (int i = 0; i < 8; i++)
            {
                int start = 1000 * i;
                for (int id = start; id < start + 100; id++)
                {
                    var key = new K(id);
                    bool expected = mirrors[i].Contains(id);
                    if (set)
                    {
                        Assert.Equal(expected, sc.Contains(key));
                    }
                    else
                    {
                        Assert.Equal(expected, dc.ContainsKey(key));
                        string v;
                        Assert.Equal(expected, dc.TryGetValue(key, out v));
                        if (expected)
                            Assert.Equal(values[i][id], v);
                    }
                    if (expected)
                        total++;
                }
            }
            if (set)
                Assert.Equal(total, sc.Count);
            else
                Assert.Equal(total, dc.Count);
        }

        static void AssertAtomicClear(bool set)
        {
            var sc = new ReactiveSet<K>();
            var dc = new ReactiveDictionary<K, string>();
            int stop = 0;
            int mismatch = 0;
            var writer = new Thread(() =>
            {
                for (int n = 0; n < Harness.Stress; n++)
                {
                    if (set)
                    {
                        for (int i = 0; i < 10; i++)
                            sc.Add(new K(i));
                        sc.Clear();
                    }
                    else
                    {
                        for (int i = 0; i < 10; i++)
                            dc[new K(i)] = "v" + i;
                        dc.Clear();
                    }
                }
                Volatile.Write(ref stop, 1);
            });
            var reader = new Thread(() =>
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    if (set)
                    {
                        var v = sc.Value;
                        int count = v.Count;
                        for (int i = 0; i < 10; i++)
                        {
                            bool has = v.Contains(new K(i));
                            if (has != (i < count))
                                Interlocked.Increment(ref mismatch);
                        }
                    }
                    else
                    {
                        var v = dc.Value;
                        int count = v.Count;
                        for (int i = 0; i < 10; i++)
                        {
                            bool has = v.ContainsKey(new K(i));
                            if (has != (i < count))
                                Interlocked.Increment(ref mismatch);
                        }
                    }
                }
            });
            writer.IsBackground = true;
            reader.IsBackground = true;
            reader.Start();
            writer.Start();
            Assert.True(writer.Join(Harness.TimeoutMs * 4));
            Assert.True(reader.Join(Harness.TimeoutMs));
            Assert.Equal(0, mismatch);
        }

        static void PresenceAddRemove(Action<ReactiveSet<K>> add)
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int n = 0;
            var seen = new List<bool>();
            new Reactor(s).Add(() => { seen.Add(sc.Contains(k1)); n++; });
            Harness.Barrier(s);
            add(sc);
            Harness.Barrier(s);
            sc.Remove(k1);
            Harness.Barrier(s);
            Assert.Equal(3, n);
            Assert.Equal(new[] { false, true, false }, seen.ToArray());
        }

        static void PresenceAddRemoveDict(Action<ReactiveDictionary<K, string>> add)
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>();
            int n = 0;
            var seen = new List<bool>();
            new Reactor(s).Add(() => { seen.Add(dc.ContainsKey(k1)); n++; });
            Harness.Barrier(s);
            add(dc);
            Harness.Barrier(s);
            dc.Remove(k1);
            Harness.Barrier(s);
            Assert.Equal(3, n);
            Assert.Equal(new[] { false, true, false }, seen.ToArray());
        }

        static void AssertRose(ref int n, Action write, Scheduler s)
        {
            int before = n;
            write();
            Harness.Barrier(s);
            Assert.Equal(before + 1, n);
        }

        static void AssertThrowsUnwrapped(Exception expected, Action action)
        {
            var thrown = Record.Exception(action);
            Assert.Same(expected, thrown);
            Assert.IsNotType<ReactiveException>(thrown);
        }

        static void AssertArgNull(Action action)
        {
            var thrown = Record.Exception(action);
            Assert.IsType<ArgumentNullException>(thrown);
            Assert.IsNotType<ReactiveException>(thrown);
        }

        static void AssertFoundUntorn(bool found, Big value, ref int mismatch)
        {
            if (!found)
            {
                Interlocked.Increment(ref mismatch);
                return;
            }
            if (value.A != value.B || value.B != value.C || value.C != value.D)
                Interlocked.Increment(ref mismatch);
        }

        static void AssertVersionSurface(Type collection, bool set)
        {
            var version = collection.GetNestedType("Version");
            Assert.NotNull(version);
            Assert.NotNull(version.GetProperty("Count"));
            var diff = version.GetMethod("Diff");
            Assert.NotNull(diff);
            Assert.True(diff.ReturnType.IsArray);
            Assert.Equal("Change", diff.ReturnType.GetElementType().Name);
            Assert.Equal(0, version.GetMember("SameAs").Length);
            if (set)
            {
                Assert.NotNull(version.GetMethod("Contains"));
            }
            else
            {
                Assert.NotNull(version.GetMethod("ContainsKey"));
                Assert.NotNull(version.GetMethod("TryGetValue"));
                var indexer = version.GetProperty("Item");
                Assert.NotNull(indexer);
                Assert.NotNull(indexer.GetGetMethod());
                Assert.Null(indexer.GetSetMethod());
            }

            var change = collection.GetNestedType("Change");
            Assert.NotNull(change);
            Assert.NotNull(change.GetProperty("Kind"));
            Assert.Equal("ChangeKind", change.GetProperty("Kind").PropertyType.Name);
            if (set)
            {
                Assert.NotNull(change.GetProperty("Item"));
                Assert.NotNull(change.GetProperty("Previous"));
            }
            else
            {
                Assert.NotNull(change.GetProperty("Key"));
                Assert.NotNull(change.GetProperty("Value"));
                Assert.NotNull(change.GetProperty("PreviousValue"));
            }

            var kind = collection.GetNestedType("ChangeKind");
            Assert.NotNull(kind);
            Assert.True(kind.IsEnum);
            Assert.True(Enum.IsDefined(kind, Enum.Parse(kind, "Added")));
            Assert.True(Enum.IsDefined(kind, Enum.Parse(kind, "Removed")));
            if (set)
                Assert.True(Enum.IsDefined(kind, Enum.Parse(kind, "Replaced")));
            else
                Assert.True(Enum.IsDefined(kind, Enum.Parse(kind, "Updated")));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference MakeSetVersion(out ReactiveSet<K>.Version v1)
        {
            var c = new ReactiveSet<K>();
            for (int i = 0; i < 100; i++)
                c.Add(new K(i));
            var v0 = c.Value;
            var wr = new WeakReference(v0);
            c.Add(new K(100));
            c.Remove(new K(0));
            v1 = c.Value;
            return wr;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference MakeDictVersion(out ReactiveDictionary<K, string>.Version v1)
        {
            var c = new ReactiveDictionary<K, string>();
            for (int i = 0; i < 100; i++)
                c.Add(new K(i), "v" + i);
            var v0 = c.Value;
            var wr = new WeakReference(v0);
            c.Add(new K(100), "x");
            c.Remove(new K(0));
            c[new K(1)] = "z";
            v1 = c.Value;
            return wr;
        }

        static string SetTag(ReactiveSet<K>.Change change)
        {
            string item = change.Item == null ? "null" : change.Item.Id.ToString();
            string previous = change.Previous == null ? "null" : change.Previous.Id.ToString();
            return change.Kind + ":" + item + ":" + previous;
        }

        static string DictTag(ReactiveDictionary<K, string>.Change change)
        {
            string key = change.Key == null ? "null" : change.Key.Id.ToString();
            return change.Kind + ":" + key + ":" + change.Value + ":" + change.PreviousValue;
        }

        static string[] Tags<T>(T[] changes, Func<T, string> tag)
        {
            var tags = new string[changes.Length];
            for (int i = 0; i < changes.Length; i++)
                tags[i] = tag(changes[i]);
            return tags;
        }

        static void AssertSetTags(ReactiveSet<K>.Change[] actual, params string[] expected)
        {
            AssertMultiset(Tags(actual, SetTag), expected);
        }

        static void AssertDictTags(ReactiveDictionary<K, string>.Change[] actual, params string[] expected)
        {
            AssertMultiset(Tags(actual, DictTag), expected);
        }

        static void AssertMultiset(string[] actual, params string[] expected)
        {
            var left = (string[])actual.Clone();
            var right = (string[])expected.Clone();
            Array.Sort(left);
            Array.Sort(right);
            Assert.Equal(right, left);
        }

        public sealed class K
        {
            public readonly int Id;

            public K(int id)
            {
                Id = id;
            }

            public override bool Equals(object obj)
            {
                var other = obj as K;
                return other != null && other.Id == Id;
            }

            public override int GetHashCode()
            {
                return Id;
            }
        }

        sealed class Val
        {
            public readonly int Id;

            public Val(int id)
            {
                Id = id;
            }

            public override bool Equals(object obj)
            {
                var other = obj as Val;
                return other != null && other.Id == Id;
            }

            public override int GetHashCode()
            {
                return Id;
            }
        }

        struct EquatableVal : IEquatable<EquatableVal>
        {
            public readonly int Id;

            public EquatableVal(int id)
            {
                Id = id;
            }

            public bool Equals(EquatableVal other)
            {
                return true;
            }

            public override bool Equals(object obj)
            {
                return false;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        sealed class Boom
        {
            readonly Exception _thrown;

            public Boom(Exception thrown)
            {
                _thrown = thrown;
            }

            public override bool Equals(object obj)
            {
                throw _thrown;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        struct Big
        {
            public long A;
            public long B;
            public long C;
            public long D;

            public Big(long value)
            {
                A = value;
                B = value;
                C = value;
                D = value;
            }
        }
    }
}

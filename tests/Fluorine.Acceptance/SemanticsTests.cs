using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Fluorine;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class SemanticsTests : AcceptanceTest
    {
        [Fact(DisplayName = "s1.2-01")]
        public void s1_2_01()
        {
            // The row's pause is a blocked run. A gate stands in for it so the harness does not sleep.
            var s = Harness.Create();
            int inflight = 0;
            int peak = 0;
            int runs = 0;
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            Reaction reaction = null;
            try
            {
                reaction = new Reactor(s).Add(() =>
                {
                    int now = Interlocked.Increment(ref inflight);
                    NotePeak(ref peak, now);
                    int n = Interlocked.Increment(ref runs);
                    if (n == 2)
                    {
                        entered.Set();
                        release.WaitOne();
                    }
                    Interlocked.Decrement(ref inflight);
                });
                Harness.Barrier(s);
                reaction.Trigger();
                Harness.Wait(entered, "held run");
                var threads = StartThreads(8, () =>
                {
                    for (int i = 0; i < Harness.Stress / 8; i++)
                        reaction.Trigger();
                });
                JoinAll(threads);
                Assert.True(Volatile.Read(ref peak) <= 1, "in-flight " + Volatile.Read(ref peak));
            }
            finally
            {
                release.Set();
            }
            Harness.Barrier2(s);
            Assert.True(Volatile.Read(ref peak) <= 1, "in-flight " + Volatile.Read(ref peak));
        }

        [Fact(DisplayName = "s1.4-01")]
        public void s1_4_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                Interlocked.Increment(ref runs);
            });
            Harness.Barrier(s);
            Assert.Equal(1, Volatile.Read(ref runs));
            using (var hold = Harness.Gate(s))
            {
                for (int i = 1; i <= 100; i++)
                    reg.Value = i;
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(2, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s1.5-01")]
        public void s1_5_01()
        {
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            var r1Go = new ManualResetEvent(false);
            var r2Go = new ManualResetEvent(false);
            var r1Done = new ManualResetEvent(false);
            var r2Done = new ManualResetEvent(false);
            try
            {
                new Reactor(s1).Add(() =>
                {
                    r1Go.Set();
                    r2Go.WaitOne();
                    r1Done.Set();
                });
                new Reactor(s2).Add(() =>
                {
                    r1Go.WaitOne();
                    r2Go.Set();
                    r2Done.Set();
                });
                Harness.Wait(r1Done, "r1");
                Harness.Wait(r2Done, "r2");
            }
            finally
            {
                r1Go.Set();
                r2Go.Set();
            }
        }

        [Fact(DisplayName = "s1.8-01")]
        public void s1_8_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(1);
            var reactor = new Reactor(s);
            var reaction = reactor.Add(() =>
            {
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            reaction.Dispose();
            reactor.Dispose();
            s.Shutdown();

            Assert.Null(Record.Exception(() => reaction.Trigger()));
            Assert.Null(Record.Exception(() => reg.TriggerReactions()));
            Assert.Null(Record.Exception(() => { reg.Value = 2; }));
            Assert.Null(Record.Exception(() => reactor.Queue(null)));
            Assert.Null(Record.Exception(() => reactor.Queue(() => { })));
            Assert.Null(Record.Exception(() => s.Queue((Action)null)));
            Assert.Null(Record.Exception(() => s.Queue(() => { })));
            var late = new Reactor(s);
            Assert.Null(Record.Exception(() => late.Queue(null)));
            Assert.Null(late.Add(() => { }));
        }

        [Fact(DisplayName = "s1.8-02")]
        public void s1_8_02()
        {
            using (var log = new Log())
            {
                var ts = new BombScheduler();
                try
                {
                    var reg = new Register<int>(0);
                    var reactor = new Reactor(ts);
                    var triggered = reactor.Add(() => { });
                    reactor.Add(() =>
                    {
                        var unused = reg.Value;
                    });
                    ts.Pump();
                    var platform = new InvalidOperationException("schedule");
                    ts.Bomb = platform;
                    Assert.Same(platform, Assert.Throws<InvalidOperationException>(() => triggered.Trigger()));
                    Assert.Same(platform, Assert.Throws<InvalidOperationException>(() => { reg.Value = 1; }));
                    Assert.Same(platform, Assert.Throws<InvalidOperationException>(() => ts.Queue(() => { })));
                    Assert.Equal(0, log.Count);
                }
                finally
                {
                    ts.Bomb = null;
                }
            }
        }

        [Fact(DisplayName = "s1.8-03")]
        public void s1_8_03()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reactor = new Reactor(s);
                int runs = 0;
                var reaction = reactor.Add(() => runs++);
                Harness.Barrier(s);
                s.Shutdown();
                log.Clear();
                int actions = 0;
                Assert.Null(Record.Exception(() => reaction.Trigger()));
                Assert.Null(Record.Exception(() => s.Queue(() => actions++)));
                Assert.Null(Record.Exception(() => reactor.Queue(() => actions++)));
                Assert.Equal(0, log.Count);
                Assert.Equal(1, runs);
                Assert.Equal(0, actions);
            }
        }

        [Fact(DisplayName = "s1.9-01")]
        public void s1_9_01()
        {
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            Exception error = null;
            new Reactor(s1).Add(() =>
            {
                try
                {
                    Interlocked.Increment(ref runs);
                    var onS2 = new ManualResetEvent(false);
                    s2.Queue(() =>
                    {
                        var unused = reg.Value;
                        onS2.Set();
                    });
                    Harness.Wait(onS2, "s2 read");
                    var onPlain = new ManualResetEvent(false);
                    var plain = new Thread(() =>
                    {
                        var unused = reg.Value;
                        onPlain.Set();
                    });
                    plain.IsBackground = true;
                    plain.Start();
                    Harness.Wait(onPlain, "plain read");
                    plain.Join();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            Harness.Barrier(s1);
            Assert.Null(error);
            int after = Volatile.Read(ref runs);
            Assert.Equal(1, after);
            reg.Value = 1;
            Harness.Barrier(s1);
            Assert.Equal(after, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s2-01")]
        public void s2_01()
        {
            var s = Harness.Create();
            var first = new Register<string>("");
            var last = new Register<string>("");
            var labels = new List<string>();
            int runs = 0;
            Reaction reaction = null;
            var ready = new ManualResetEvent(false);
            s.Queue(() =>
            {
                first.Value = "Ada";
                last.Value = "Lovelace";
                reaction = new Reactor().Add(() =>
                {
                    Interlocked.Increment(ref runs);
                    string label = first.Value == ""
                        ? "Must have a first name"
                        : first.Value + " " + last.Value;
                    lock (labels)
                        labels.Add(label);
                });
                ready.Set();
            });
            Harness.Wait(ready, "setup");
            Harness.Barrier(s);
            Assert.Equal(new[] { "Ada Lovelace" }, Snapshot(labels));

            using (var hold = Harness.Gate(s))
            {
                last.Value = "Byron";
                first.Value = "Augusta";
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(new[] { "Ada Lovelace", "Augusta Byron" }, Snapshot(labels));
            Assert.Equal(2, Volatile.Read(ref runs));

            first.Value = "Augusta";
            Harness.Barrier(s);
            Assert.Equal(2, Volatile.Read(ref runs));
            Assert.Equal("Augusta Byron", Last(labels));

            first.Value = "";
            Harness.Barrier(s);
            Assert.Equal("Must have a first name", Last(labels));
            int afterEmpty = Volatile.Read(ref runs);
            last.Value = "X";
            Harness.Barrier(s);
            Assert.Equal(afterEmpty, Volatile.Read(ref runs));
            Assert.Equal("Must have a first name", Last(labels));

            first.Value = "Ada";
            Harness.Barrier(s);
            Assert.Equal("Ada X", Last(labels));

            int beforeDispose = Volatile.Read(ref runs);
            reaction.Dispose();
            first.Value = "ZZ";
            last.Value = "YY";
            Harness.Barrier(s);
            Assert.Equal(beforeDispose, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s3.1b-01")]
        public void s3_1b_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int seen = 0;
            new Reactor(s).Add(() => { seen = reg.Value; });
            Harness.Barrier(s);
            int next = 0;
            var threads = StartThreads(4, () =>
            {
                for (int i = 0; i < Harness.Stress / 4; i++)
                {
                    int value = Interlocked.Increment(ref next);
                    reg.Value = value;
                }
            });
            JoinAll(threads);
            int final = reg.Value;
            Harness.Barrier2(s);
            Assert.Equal(final, seen);
            Assert.InRange(final, 1, Harness.Stress);
        }

        [Fact(DisplayName = "s3.1b-02")]
        public void s3_1b_02()
        {
            var reg = new Register<ReenterEqual>();
            var first = new ReenterEqual { Id = 1, Reg = reg };
            var second = new ReenterEqual { Id = 2, Reg = reg };
            reg.Value = first;
            reg.Value = second;
            Assert.Equal(2, reg.Value.Id);
        }

        [Fact(DisplayName = "s4.1-01")]
        public void s4_1_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int seen = 0;
            new Reactor(s).Add(() => { seen = reg.Value; });
            Harness.Barrier(s);
            var threads = new Thread[4];
            for (int t = 0; t < threads.Length; t++)
            {
                int seed = t + 1;
                threads[t] = new Thread(() =>
                {
                    var random = new Random(seed);
                    for (int i = 0; i < Harness.Stress / 4; i++)
                        reg.Value = random.Next();
                });
                threads[t].IsBackground = true;
                threads[t].Start();
            }
            JoinAll(threads);
            int final = reg.Value;
            Harness.Barrier2(s);
            Assert.Equal(final, seen);
        }

        [Fact(DisplayName = "s4.4-01")]
        public void s4_4_01()
        {
            var s = Harness.Create();
            int runs = 0;
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            Reaction reaction = null;
            try
            {
                reaction = new Reactor(s).Add(() =>
                {
                    int n = Interlocked.Increment(ref runs);
                    if (n == 2)
                    {
                        entered.Set();
                        release.WaitOne();
                    }
                });
                Harness.Barrier(s);
                reaction.Trigger();
                Harness.Wait(entered, "run 2");
                for (int i = 0; i < 5; i++)
                    reaction.Trigger();
            }
            finally
            {
                release.Set();
            }
            Harness.Barrier2(s);
            Assert.Equal(3, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s4.6-01")]
        public void s4_6_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                Interlocked.Increment(ref runs);
            });
            Harness.Barrier(s);
            using (var hold = Harness.Gate(s))
            {
                reg.Value = 1;
                reaction.Trigger();
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(2, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s5.4-01")]
        public void s5_4_01()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            var sum = new Register<int>(-1);
            new Reactor(s).Add(() => { sum.Value = a.Value + b.Value; });
            Harness.Barrier(s);
            s.Queue(() =>
            {
                a.Value = 2;
                b.Value = 3;
            });
            Harness.Barrier2(s);
            Assert.Equal(5, sum.Value);
        }

        [Fact(DisplayName = "s6.1-01")]
        public void s6_1_01()
        {
            var s = Harness.Create();
            var c = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                Interlocked.Increment(ref runs);
                if (c.Value < 10)
                    c.Value++;
            });
            for (int i = 0; i < 12; i++)
                Harness.Barrier(s);
            Assert.Equal(10, c.Value);
            Assert.Equal(11, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s6.2-01")]
        public void s6_2_01()
        {
            var s = Harness.Create();
            var c = new Register<int>(0);
            new Reactor(s).Add(() => { c.Value++; });
            var ran = new ManualResetEvent(false);
            s.Queue(() => ran.Set());
            Assert.True(ran.WaitOne(1000), "queued action did not run within 1 s");
            var finished = new ManualResetEvent(false);
            var thread = new Thread(() =>
            {
                s.Shutdown();
                finished.Set();
            });
            thread.IsBackground = true;
            thread.Start();
            Assert.True(finished.WaitOne(Harness.TimeoutMs), "Shutdown did not complete");
            Assert.True(thread.Join(Harness.TimeoutMs));
        }

        [Fact(DisplayName = "s6.3-01")]
        public void s6_3_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                Interlocked.Increment(ref runs);
            });
            Harness.Barrier(s);
            int before = Volatile.Read(ref runs);
            new Reactor(s).Add(() =>
            {
                using (Reaction.Pause())
                    reg.Value = 1;
            });
            Harness.Barrier2(s);
            Assert.True(Volatile.Read(ref runs) > before);
        }

        [Fact(DisplayName = "s6.4-01")]
        public void s6_4_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var reactor = new Reactor(s);
            int r1Runs = 0;
            int r2Runs = 0;
            Reaction created = null;
            var r1 = reactor.Add(() =>
            {
                Interlocked.Increment(ref r1Runs);
                if (created == null)
                {
                    created = reactor.Add(() =>
                    {
                        var unused = reg.Value;
                        Interlocked.Increment(ref r2Runs);
                    });
                }
            });
            Harness.Barrier2(s);
            Assert.True(Volatile.Read(ref r2Runs) >= 1);
            int r1Before = Volatile.Read(ref r1Runs);
            int r2Before = Volatile.Read(ref r2Runs);
            r1.Dispose();
            reg.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(r1Before, Volatile.Read(ref r1Runs));
            Assert.True(Volatile.Read(ref r2Runs) > r2Before);

            var other = Harness.Create();
            var reg2 = new Register<int>(0);
            var owner = new Reactor(other);
            int a = 0;
            int b = 0;
            Reaction child = null;
            owner.Add(() =>
            {
                var unused = reg2.Value;
                Interlocked.Increment(ref a);
                if (child == null)
                {
                    child = owner.Add(() =>
                    {
                        var ignored = reg2.Value;
                        Interlocked.Increment(ref b);
                    });
                }
            });
            Harness.Barrier2(other);
            Assert.True(Volatile.Read(ref a) >= 1);
            Assert.True(Volatile.Read(ref b) >= 1);
            int a0 = Volatile.Read(ref a);
            int b0 = Volatile.Read(ref b);
            owner.Dispose();
            reg2.Value = 2;
            child.Trigger();
            Harness.Barrier(other);
            Assert.Equal(a0, Volatile.Read(ref a));
            Assert.Equal(b0, Volatile.Read(ref b));
        }

        [Fact(DisplayName = "s7.2-01")]
        public void s7_2_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int lastSeen = 0;
            int older = 0;
            new Reactor(s).Add(() =>
            {
                int value = reg.Value;
                if (value < lastSeen)
                    Interlocked.Exchange(ref older, value);
                lastSeen = value;
            });
            Harness.Barrier(s);
            var writer = new Thread(() =>
            {
                for (int i = 1; i <= Harness.Stress; i++)
                    reg.Value = i;
            });
            writer.IsBackground = true;
            writer.Start();
            Assert.True(writer.Join(Harness.TimeoutMs));
            Harness.Barrier2(s);
            Assert.Equal(0, Volatile.Read(ref older));
            Assert.Equal(reg.Value, lastSeen);
        }

        [Fact(DisplayName = "s7.3a-01")]
        public void s7_3a_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                Interlocked.Increment(ref runs);
                var normal = reg.Value;
                using (Reaction.Pause())
                {
                    var paused = reg.Value;
                    GC.KeepAlive(paused);
                }
                GC.KeepAlive(normal);
            });
            Harness.Barrier(s);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s7.3a-02")]
        public void s7_3a_02()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                int n = Interlocked.Increment(ref runs);
                if (n == 1)
                {
                    var normal = reg.Value;
                    GC.KeepAlive(normal);
                }
                else
                {
                    using (Reaction.Pause())
                    {
                        var paused = reg.Value;
                        GC.KeepAlive(paused);
                    }
                }
            });
            Harness.Barrier(s);
            reaction.Trigger();
            Harness.Barrier(s);
            int after = Volatile.Read(ref runs);
            Assert.Equal(2, after);
            reg.Value = 5;
            Harness.Barrier(s);
            Assert.Equal(after, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s7.4-01")]
        public void s7_4_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int depth = 0;
            int maxDepth = 0;
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                int now = Interlocked.Increment(ref depth);
                NotePeak(ref maxDepth, now);
                int value = reg.Value;
                if (value < 4)
                {
                    reaction.Trigger();
                    reg.Value = value + 1;
                }
                Interlocked.Decrement(ref depth);
            });
            for (int i = 0; i < 8; i++)
                Harness.Barrier(s);
            Assert.True(Volatile.Read(ref maxDepth) <= 1, "depth " + Volatile.Read(ref maxDepth));
        }

        [Fact(DisplayName = "s7.5-01")]
        public void s7_5_01()
        {
            var s = Harness.Create();
            var sig = new Register();
            int r1Runs = 0;
            int r2Runs = 0;
            Reaction r2 = null;
            r2 = new Reactor(s).Add(() => { Interlocked.Increment(ref r2Runs); });
            new Reactor(s).Add(() =>
            {
                Interlocked.Increment(ref r1Runs);
                sig.TriggerReactions();
                r2.Trigger();
            });
            Harness.Barrier2(s);
            int before = Volatile.Read(ref r1Runs);
            sig.TriggerReactions();
            Harness.Barrier(s);
            Assert.Equal(before, Volatile.Read(ref r1Runs));
        }

        [Fact(DisplayName = "s8.2-01")]
        public void s8_2_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reaction = new Reactor(s).Add(() => { throw new InvalidOperationException("again"); });
                Harness.Barrier(s);
                log.Clear();
                for (int i = 0; i < 10; i++)
                {
                    reaction.Trigger();
                    Harness.Barrier(s);
                }
                var items = log.Snapshot();
                Assert.Equal(10, items.Length);
                bool ran = false;
                s.Queue(() => ran = true);
                Harness.Barrier(s);
                Assert.True(ran);
            }
        }

        [Fact(DisplayName = "s11.1-01")]
        public void s11_1_01()
        {
            var s = Harness.Create();
            var owner = Harness.ThreadOf(s);
            int runs = 0;
            int wrong = 0;
            var reaction = new Reactor(s).Add(() =>
            {
                Interlocked.Increment(ref runs);
                if (!object.ReferenceEquals(Thread.CurrentThread, owner))
                    Interlocked.Exchange(ref wrong, 1);
            });
            Harness.Barrier(s);
            for (int n = 0; n < 100; n++)
            {
                var done = new ManualResetEvent(false);
                var worker = new Thread(() =>
                {
                    reaction.Trigger();
                    done.Set();
                });
                worker.IsBackground = true;
                worker.Start();
                Harness.Wait(done, "trigger");
                worker.Join();
                Harness.Barrier(s);
            }
            Assert.Equal(0, Volatile.Read(ref wrong));
            Assert.Equal(101, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s11.5-01")]
        public void s11_5_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            int seen = -1;
            Exception error = null;
            s.Queue(() =>
            {
                try
                {
                    new Reactor().Add(() =>
                    {
                        seen = reg.Value;
                        Interlocked.Increment(ref runs);
                    });
                    reg.Value = 5;
                    if (Volatile.Read(ref runs) != 0)
                        throw new InvalidOperationException("run before the action ended: " + runs);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            Harness.Barrier2(s);
            Assert.Null(error);
            Assert.True(Volatile.Read(ref runs) >= 1);
            Assert.Equal(5, seen);
        }

        [Fact(DisplayName = "s11.7-01")]
        public void s11_7_01()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            int inflight = 0;
            int peak = 0;
            int arm = 0;
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            var reactions = new Reaction[10];
            try
            {
                for (int i = 0; i < reactions.Length; i++)
                {
                    int index = i;
                    reactions[i] = reactor.Add(() =>
                    {
                        int now = Interlocked.Increment(ref inflight);
                        NotePeak(ref peak, now);
                        if (index == 0 && Interlocked.Increment(ref arm) == 2)
                        {
                            entered.Set();
                            release.WaitOne();
                        }
                        Interlocked.Decrement(ref inflight);
                    });
                }
                Harness.Barrier(s);
                reactions[0].Trigger();
                Harness.Wait(entered, "held run");
                var threads = StartThreads(8, () =>
                {
                    for (int i = 0; i < Harness.Stress / 8; i++)
                        reactions[i % reactions.Length].Trigger();
                });
                JoinAll(threads);
                Assert.True(Volatile.Read(ref peak) <= 1, "in-flight " + Volatile.Read(ref peak));
            }
            finally
            {
                release.Set();
            }
            Harness.Barrier2(s);
            Assert.True(Volatile.Read(ref peak) <= 1, "in-flight " + Volatile.Read(ref peak));
        }

        [Fact(DisplayName = "s11.9-01")]
        public void s11_9_01()
        {
            var s = Harness.Create();
            var order = new List<int>();
            int phase = 0;
            var r1 = new Reactor(s).Add(() => { if (Volatile.Read(ref phase) == 1) lock (order) order.Add(1); });
            var r2 = new Reactor(s).Add(() => { if (Volatile.Read(ref phase) == 1) lock (order) order.Add(2); });
            var r3 = new Reactor(s).Add(() => { if (Volatile.Read(ref phase) == 1) lock (order) order.Add(3); });
            Harness.Barrier(s);
            using (var hold = Harness.Gate(s))
            {
                Volatile.Write(ref phase, 1);
                r1.Trigger();
                r2.Trigger();
                r3.Trigger();
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(new[] { 1, 2, 3 }, order.ToArray());
        }

        [Fact(DisplayName = "s11.9-02")]
        public void s11_9_02()
        {
            var s = Harness.Create();
            var order = new List<int>();
            int phase = 0;
            var r1 = new Reactor(s).Add(() => { if (Volatile.Read(ref phase) == 1) lock (order) order.Add(1); });
            var r2 = new Reactor(s).Add(() => { if (Volatile.Read(ref phase) == 1) lock (order) order.Add(2); });
            Harness.Barrier(s);
            using (var hold = Harness.Gate(s))
            {
                Volatile.Write(ref phase, 1);
                r1.Trigger();
                r2.Trigger();
                r1.Trigger();
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(new[] { 1, 2 }, order.ToArray());
        }

        [Fact(DisplayName = "s11.10-01")]
        public void s11_10_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int seen = -1;
            new Reactor(s).Add(() =>
            {
                reg.Value = 7;
                seen = reg.Value;
            });
            Harness.Barrier2(s);
            Assert.Equal(7, seen);
        }

        [Fact(DisplayName = "s11.11-01")]
        public void s11_11_01()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            var pairs = new List<string>();
            new Reactor(s).Add(() =>
            {
                int av = a.Value;
                int bv = b.Value;
                lock (pairs)
                    pairs.Add(av + "," + bv);
            });
            Harness.Barrier(s);
            s.Queue(() =>
            {
                a.Value = 1;
                b.Value = 1;
            });
            Harness.Barrier2(s);
            var seen = Snapshot(pairs);
            Assert.Contains("0,0", seen);
            Assert.Contains("1,1", seen);
            for (int i = 0; i < seen.Length; i++)
                Assert.True(seen[i] == "0,0" || seen[i] == "1,1", "observed " + seen[i]);
        }

        [Fact(DisplayName = "s11.15-01")]
        public void s11_15_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                Interlocked.Increment(ref runs);
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            int before = Volatile.Read(ref runs);
            s.Shutdown();
            reg.Value = 1;
            Assert.Equal(before, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s12.6b-01")]
        public void s12_6b_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            var reaction = new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                Interlocked.Increment(ref runs);
            });
            Harness.Barrier(s);
            reaction.Dispose();
            int before = Volatile.Read(ref runs);
            reg.Value = 1;
            reaction.Trigger();
            Harness.Barrier(s);
            Assert.Equal(before, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s12.9-01")]
        public void s12_9_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reactor = new Reactor(s);
                Assert.Null(Record.Exception(() => Reaction.Pause((Action)null)));
                Assert.Null(Record.Exception(() => reactor.Queue(null)));
                Assert.Null(Record.Exception(() => s.Queue((Action)null)));
                Harness.Barrier(s);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "s13.3-01")]
        public void s13_3_01()
        {
            var s = Harness.Create();
            int runs = 0;
            var reaction = new Reactor(s).Add(() => { Interlocked.Increment(ref runs); });
            Harness.Barrier(s);
            using (var hold = Harness.Gate(s))
            {
                var threads = StartThreads(16, () =>
                {
                    for (int i = 0; i < 1000; i++)
                        reaction.Trigger();
                });
                JoinAll(threads);
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(2, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "s13.5-01")]
        public void s13_5_01()
        {
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            var c = new Register<int>(0);
            int budget = 200;
            var r1 = new Reactor(s1).Add(() =>
            {
                int av = a.Value;
                var unused = b.Value;
                if (Interlocked.Decrement(ref budget) > 0)
                    c.Value = av;
            });
            var r2 = new Reactor(s2).Add(() =>
            {
                int cv = c.Value;
                var unused = a.Value;
                if (Interlocked.Decrement(ref budget) > 0 && b.Value != cv)
                    b.Value = cv;
            });
            var sw = Stopwatch.StartNew();
            var threads = StartThreads(4, () =>
            {
                for (int i = 0; i < Harness.Stress / 4; i++)
                {
                    int value = i % 64;
                    a.Value = value;
                    b.Value = value;
                    c.Value = value;
                    r1.Trigger();
                    r2.Trigger();
                    a.TriggerReactions();
                }
            });
            for (int i = 0; i < threads.Length; i++)
            {
                int remaining = 30000 - (int)sw.ElapsedMilliseconds;
                if (remaining < 1)
                    remaining = 1;
                Assert.True(threads[i].Join(remaining), "deadlock: writer did not finish within 30 s");
            }
            int left = 30000 - (int)sw.ElapsedMilliseconds;
            if (left < 1)
                left = 1;
            var done1 = new ManualResetEvent(false);
            var done2 = new ManualResetEvent(false);
            s1.Queue(() => done1.Set());
            s2.Queue(() => done2.Set());
            Assert.True(done1.WaitOne(left), "deadlock: S1 did not drain within 30 s");
            left = 30000 - (int)sw.ElapsedMilliseconds;
            if (left < 1)
                left = 1;
            Assert.True(done2.WaitOne(left), "deadlock: S2 did not drain within 30 s");
        }

        [Fact(DisplayName = "s13.7-01")]
        public void s13_7_01()
        {
            var s1 = Harness.Create();
            var reg = new Register<int>(0);
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            Thread worker = null;
            try
            {
                new Reactor(s1).Add(() =>
                {
                    var unused = reg.Value;
                    entered.Set();
                    release.WaitOne();
                });
                Harness.Wait(entered, "reaction blocked");
                var done = new ManualResetEvent(false);
                Exception error = null;
                worker = new Thread(() =>
                {
                    try
                    {
                        reg.Value = 4;
                        reg.RecordRead();
                        reg.TriggerReactions();
                        s1.Queue(() => { });
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                worker.IsBackground = true;
                worker.Start();
                Assert.True(done.WaitOne(Harness.TimeoutMs), "worker blocked while a reaction held the register");
                Assert.Null(error);
            }
            finally
            {
                release.Set();
                if (worker != null)
                    worker.Join(Harness.TimeoutMs);
            }
        }

        [Fact(DisplayName = "s13.8-01")]
        public void s13_8_01()
        {
            // The row's 2 s block is a blocked run. A gate stands in for it so the harness does not sleep.
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            Thread writer = null;
            try
            {
                new Reactor(s).Add(() =>
                {
                    var unused = reg.Value;
                    entered.Set();
                    release.WaitOne();
                });
                Harness.Wait(entered, "reaction blocked");
                var wrote = new ManualResetEvent(false);
                writer = new Thread(() =>
                {
                    reg.Value = 9;
                    wrote.Set();
                });
                writer.IsBackground = true;
                writer.Start();
                Assert.True(wrote.WaitOne(100), "write did not return within 100 ms");
            }
            finally
            {
                release.Set();
                if (writer != null)
                    writer.Join(Harness.TimeoutMs);
            }
        }

        [Fact(DisplayName = "s14.1-01")]
        public void s14_1_01()
        {
            var s = Harness.Create();
            var sig = new Register();
            int field = 0;
            int seen = -1;
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                Interlocked.Increment(ref runs);
                sig.RecordRead();
                seen = field;
            });
            Harness.Barrier(s);
            int before = Volatile.Read(ref runs);
            field = 4;
            sig.TriggerReactions();
            Harness.Barrier(s);
            Assert.True(Volatile.Read(ref runs) > before);
            Assert.Equal(4, seen);
        }

        [Fact(DisplayName = "s14.7-01")]
        public void s14_7_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var order = new Order();
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            int recorded = 0;
            int selfTriggers = 0;
            bool record = false;
            Reaction r1 = null;
            try
            {
                var reactor = new Reactor(s);
                r1 = reactor.Add(() =>
                {
                    int value = reg.Value;
                    if (!record)
                        return;
                    order.Add("r1");
                    if (Interlocked.Increment(ref recorded) == 1)
                    {
                        entered.Set();
                        release.WaitOne();
                    }
                    if (Volatile.Read(ref selfTriggers) < 3)
                    {
                        Interlocked.Increment(ref selfTriggers);
                        reg.Value = value + 1;
                    }
                });
                var r2 = reactor.Add(() =>
                {
                    if (record)
                        order.Add("r2");
                });
                var r3 = reactor.Add(() =>
                {
                    if (record)
                        order.Add("r3");
                });
                Harness.Barrier(s);
                record = true;
                r1.Trigger();
                Harness.Wait(entered, "r1 waiting");
                r2.Trigger();
                r3.Trigger();
                release.Set();
                Harness.Barrier2(s);
                Assert.Equal(new[] { "r1", "r2", "r3", "r1", "r1", "r1" }, order.Snapshot());
            }
            finally
            {
                release.Set();
            }
        }

        [Fact(DisplayName = "s15.1-01")]
        public void s15_1_01()
        {
            var x = new InvalidOperationException("loop-x");
            using (var log = new Log())
            {
                var s = Harness.Create();
                new Reactor(s).Add(() => { throw x; });
                Harness.Barrier(s);
                Assert.Equal(1, log.Count);
                Assert.Same(x, log.Snapshot()[0]);
                bool continued = false;
                s.Queue(() => continued = true);
                Harness.Barrier(s);
                Assert.True(continued);
                s.Shutdown();
            }
            var s2 = Harness.Create();
            using (var console = new ConsoleCapture())
            {
                new Reactor(s2).Add(() => { throw new InvalidOperationException("console-x"); });
                Harness.Barrier(s2);
                Assert.False(string.IsNullOrWhiteSpace(console.Text));
                bool continued = false;
                s2.Queue(() => continued = true);
                Harness.Barrier(s2);
                Assert.True(continued);
            }
        }

        [Fact(DisplayName = "s15.2-01")]
        public void s15_2_01()
        {
            using (var log = new Log())
            {
                Exception caught = null;
                var ready = new ManualResetEvent(false);
                var thread = new Thread(() =>
                {
                    try
                    {
                        var reactor = new Reactor();
                        GC.KeepAlive(reactor);
                    }
                    catch (Exception ex)
                    {
                        caught = ex;
                    }
                    ready.Set();
                });
                thread.IsBackground = true;
                thread.Start();
                Harness.Wait(ready, "no-scheduler thread");
                Assert.IsType<ReactiveException>(caught);

                var s = Harness.Create();
                bool caughtInReaction = false;
                new Reactor(s).Add(() =>
                {
                    try
                    {
                        Reaction.ContinueRecording();
                    }
                    catch (ReactiveException)
                    {
                        caughtInReaction = true;
                    }
                });
                Harness.Barrier(s);
                Assert.True(caughtInReaction);
                Assert.Equal(0, log.Count);
            }
        }

        static void NotePeak(ref int peak, int now)
        {
            int prev;
            do
            {
                prev = Volatile.Read(ref peak);
                if (now <= prev)
                    return;
            }
            while (Interlocked.CompareExchange(ref peak, now, prev) != prev);
        }

        static Thread[] StartThreads(int count, Action body)
        {
            var threads = new Thread[count];
            for (int i = 0; i < count; i++)
            {
                threads[i] = new Thread(() => body());
                threads[i].IsBackground = true;
                threads[i].Start();
            }
            return threads;
        }

        static void JoinAll(Thread[] threads)
        {
            for (int i = 0; i < threads.Length; i++)
                Assert.True(threads[i].Join(Harness.TimeoutMs), "worker timed out");
        }

        static string[] Snapshot(List<string> items)
        {
            lock (items)
                return items.ToArray();
        }

        static string Last(List<string> items)
        {
            lock (items)
                return items[items.Count - 1];
        }

        sealed class ReenterEqual
        {
            public Register<ReenterEqual> Reg;
            public int Id;

            public override bool Equals(object obj)
            {
                if (object.ReferenceEquals(this, obj))
                    return true;
                var done = new ManualResetEvent(false);
                Exception error = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        if (Reg != null)
                        {
                            Reg.RecordRead();
                            Reg.TriggerReactions();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                thread.IsBackground = true;
                thread.Start();
                if (!done.WaitOne(Harness.TimeoutMs))
                    throw new TimeoutException("equality re-entered the register and deadlocked");
                thread.Join();
                if (error != null)
                    throw new InvalidOperationException("re-entrant register call failed", error);
                var other = obj as ReenterEqual;
                return other != null && other.Id == Id;
            }

            public override int GetHashCode()
            {
                return Id;
            }
        }

        sealed class BombScheduler : Scheduler
        {
            readonly Queue<Action> _queue = new Queue<Action>();

            public Exception Bomb;

            protected override void Schedule(Action action)
            {
                var bomb = Bomb;
                if (bomb != null)
                    throw bomb;
                if (action == null || !AcceptWork())
                    return;
                lock (_queue)
                    _queue.Enqueue(action);
            }

            protected override void DrainOnOwnerThread()
            {
                Pump();
            }

            public void Pump()
            {
                while (true)
                {
                    Action action;
                    lock (_queue)
                    {
                        if (_queue.Count == 0)
                            return;
                        action = _queue.Dequeue();
                    }
                    action();
                }
            }
        }
    }
}

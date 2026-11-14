using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System;
using Xunit;

namespace Fluorine
{
    public class Md1_ThreadingTests : SemanticsAcceptanceTest
    {
        [Fact]
        public void Rule_1_1_EventLoopProcessesOneAtATime()
        {
            using (var host = new ThreadHost())
            {
                var concurrent = 0;
                var max = 0;
                var n = 0;
                Action work = () =>
                {
                    var now = Interlocked.Increment(ref concurrent);
                    if (now > max) max = now;
                    Thread.Sleep(20);
                    Interlocked.Decrement(ref concurrent);
                    Interlocked.Increment(ref n);
                };
                host.NewReactor().Add(work);
                host.NewReactor().Add(work);
                WaitUntil.True(() => n == 2, "both");
                Assert.Equal(1, max);
            }
        }

        [Fact]
        public void Rule_1_2_ReactionPinnedToCreatingScheduler()
        {
            using (var a = new ThreadHost())
            using (var b = new ThreadHost())
            {
                Thread ranOn = null;
                Thread expected = null;
                a.OnScheduler(() => expected = Thread.CurrentThread);
                new Reactor(a.Scheduler).Add(() => ranOn = Thread.CurrentThread);
                WaitUntil.True(() => ranOn != null, "ran");
                Assert.Equal(expected.ManagedThreadId, ranOn.ManagedThreadId);
            }
        }

        [Fact(DisplayName = "s1.2-01 Reaction never runs parallel to itself")]
        public void Sem_1_2_01_RunsNeverParallelWithThemselves()
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

        [Fact]
        public void Rule_1_3_RegistersAreNotPinned()
        {
            var r = new Register<int>(0);
            var t = new Thread(() => r.Value = 4);
            t.Start();
            t.Join();
            Assert.Equal(4, r.Value);
        }

        [Fact]
        public void Rule_1_4_TriggerQueuesAndCoalesces()
        {
            using (var host = new ThreadHost())
            {
                var inRun = new ManualResetEventSlim(false);
                var go = new ManualResetEventSlim(false);
                var n = 0;
                var r = host.NewReactor().Add(() =>
                {
                    Interlocked.Increment(ref n);
                    if (n == 1)
                    {
                        inRun.Set();
                        go.Wait();
                    }
                });
                WaitUntil.Event(inRun, "in run");
                r.Trigger();
                r.Trigger();
                r.Trigger();
                go.Set();
                WaitUntil.True(() => n == 2, "coalesced to one extra");
            }
        }

        [Fact(DisplayName = "s1.4-01 Many writes coalesce into one run")]
        public void Sem_1_4_01_Coalescing()
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

        [Fact(DisplayName = "s1.5-01 Different schedulers run in parallel")]
        public void Rule_1_5_DifferentSchedulersRunInParallel()
        {
            using (var a = new ThreadHost())
            using (var b = new ThreadHost())
            {
                var aReady = new ManualResetEventSlim(false);
                var bReady = new ManualResetEventSlim(false);
                var n = 0;
                a.NewReactor().Add(() =>
                {
                    aReady.Set();
                    bReady.Wait();
                    Interlocked.Increment(ref n);
                });
                b.NewReactor().Add(() =>
                {
                    bReady.Set();
                    aReady.Wait();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 2, "parallel");
            }
        }

        [Fact]
        public void Rule_1_6_RunIsSynchronous()
        {
            using (var host = new ThreadHost())
            {
                var order = "";
                host.NewReactor().Add(() =>
                {
                    order += "1";
                    order += "2";
                });
                WaitUntil.True(() => order == "12", "sync");
            }
        }

        [Fact]
        public void Rule_1_8_TriggeringNeverThrows()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add(() => { });
                host.Scheduler.Shutdown();
                r.Trigger();
                host.Scheduler.Queue(() => { });
            }
        }

        [Fact(DisplayName = "s1.8-02 Schedule exception passes unwrapped")]
        public void Rule_1_8_ScheduleExceptionPassesThrough()
        {
            var scheduler = new ThrowingScheduler();
            Exception logged = null;
            Action<Exception> handler = e => logged = e;
            Reactive.UnhandledException += handler;
            try
            {
                var reg = new Register<int>(0);
                var reactor = new Reactor(scheduler);
                var triggered = reactor.Add(() => { });
                reactor.Add(() =>
                {
                    var unused = reg.Value;
                });
                scheduler.Pump();
                var platform = new InvalidOperationException("schedule");
                scheduler.Bomb = platform;
                Assert.Same(platform, Assert.Throws<InvalidOperationException>(() => triggered.Trigger()));
                Assert.Same(platform, Assert.Throws<InvalidOperationException>(() => { reg.Value = 1; }));
                Assert.Same(platform, Assert.Throws<InvalidOperationException>(() => scheduler.Queue(() => { })));
                Assert.Null(logged);
            }
            finally
            {
                scheduler.Bomb = null;
                Reactive.UnhandledException -= handler;
                scheduler.Shutdown();
            }
        }

        [Fact(DisplayName = "s1.8-01 Trigger/Queue/write never throw")]
        public void Sem_1_8_01_TriggerAndQueueNeverThrow()
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
            Assert.Null(Record.Exception(() => reactor.Queue((Action)null)));
            Assert.Null(Record.Exception(() => reactor.Queue(() => { })));
            Assert.Null(Record.Exception(() => s.Queue((Action)null)));
            Assert.Null(Record.Exception(() => s.Queue(() => { })));
            var late = new Reactor(s);
            Assert.Null(Record.Exception(() => late.Queue((Action)null)));
            Assert.Null(late.Add(() => { }));
        }

        [Fact(DisplayName = "s1.8-03 After shutdown: no throw, nothing runs")]
        public void Sem_1_8_03_RefusedRunIsSilent()
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

        [Fact]
        public void Rule_1_9_TrackingIsPerThread()
        {
            using (var a = new ThreadHost())
            using (var b = new ThreadHost())
            {
                var reg = new Register<int>(0);
                var an = 0;
                var bn = 0;
                a.NewReactor().Add(() =>
                {
                    var _ = reg.Value;
                    Interlocked.Increment(ref an);
                });
                WaitUntil.True(() => an == 1, "a");
                b.NewReactor().Add(() => Interlocked.Increment(ref bn));
                WaitUntil.True(() => bn == 1, "b");
                reg.Value = 1;
                WaitUntil.True(() => an == 2, "a retriggered");
                Thread.Sleep(40);
                Assert.Equal(1, bn);
            }
        }

        [Fact(DisplayName = "s1.9-01 Reads on another thread not tracked")]
        public void Sem_1_9_01_TrackingPerThread()
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
                    })
                    {
                        IsBackground = true
                    };
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

        sealed class ThrowingScheduler : Scheduler
        {
            readonly System.Collections.Generic.Queue<Action> _queue = new();

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

    public class Md2_ControlFlowTests : SemanticsAcceptanceTest
    {
        [Fact(DisplayName = "s2-01 Spec section 2 full-name example")]
        public void Sem_2_01_EndToEndFullNameExample()
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
            Assert.Equal(["Ada Lovelace"], Snapshot(labels));

            using (var hold = Harness.Gate(s))
            {
                last.Value = "Byron";
                first.Value = "Augusta";
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(["Ada Lovelace", "Augusta Byron"], Snapshot(labels));
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
    }

    public class Md3_EqualityTests : SemanticsAcceptanceTest
    {
        [Fact]
        public void Rule_3_1_ObjectEquals()
        {
            var r = new Register<string>("x");
            Assert.False(r.SetValue("x"));
            Assert.True(r.SetValue("y"));
        }

        [Fact]
        public void Rule_3_1a_DefaultValue()
        {
            Assert.Equal(0, new Register<int>().Value);
        }

        [Fact]
        public void Rule_3_1b_CompareThenStoreThenTrigger()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(1);
                var seen = 0;
                host.NewReactor().Add(() => seen = r.Value);
                WaitUntil.True(() => seen == 1, "first");
                r.Value = 2;
                WaitUntil.True(() => seen == 2, "new value");
            }
        }

        [Fact(DisplayName = "s3.1b-01 Racing writers: last run sees final")]
        public void Sem_3_1b_01_RacingWritersLoseNothing()
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

        [Fact(DisplayName = "s3.1b-02 Equals not run under register lock")]
        public void Sem_3_1b_02_EqualityNotUnderRegisterLock()
        {
            var reg = new Register<ReenterEqual>();
            var first = new ReenterEqual { Id = 1, Reg = reg };
            var second = new ReenterEqual { Id = 2, Reg = reg };
            reg.Value = first;
            reg.Value = second;
            Assert.Equal(2, reg.Value.Id);
        }

        [Fact]
        public void Rule_3_1c_EqualityThrowStoresNothing()
        {
            var v = new EqThrower();
            var r = new Register<EqThrower>(v);
            Assert.Throws<InvalidOperationException>(() => r.SetValue(new EqThrower()));
            Assert.Same(v, r.Value);
        }

        [Fact(DisplayName = "cs1.8-03 Writing an equal value does not trigger")]
        public void Rule_3_2_NoChangeNoTrigger()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(3);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    var _ = r.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                r.Value = 3;
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }
    }

    public class Md4_RunGuaranteeTests : SemanticsAcceptanceTest
    {
        [Fact]
        public void Rule_4_1_RunsAtLeastOnceAfterLastModification()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var last = -1;
                host.NewReactor().Add(() => last = r.Value);
                WaitUntil.True(() => last == 0, "first");
                r.Value = 9;
                WaitUntil.True(() => last == 9, "last");
            }
        }

        [Fact(DisplayName = "s4.1-01 Last run sees the final value")]
        public void Sem_4_1_01_RunsAfterLastModification()
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
                })
                {
                    IsBackground = true
                };
                threads[t].Start();
            }
            JoinAll(threads);
            int final = reg.Value;
            Harness.Barrier2(s);
            Assert.Equal(final, seen);
        }

        [Fact]
        public void Rule_4_2_IntermediateValuesMayBeSkipped()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var inRun = new ManualResetEventSlim(false);
                var go = new ManualResetEventSlim(false);
                var last = 0;
                host.NewReactor().Add(() =>
                {
                    last = r.Value;
                    if (last == 0)
                    {
                        inRun.Set();
                        go.Wait();
                    }
                });
                WaitUntil.Event(inRun, "in");
                r.Value = 1;
                r.Value = 2;
                r.Value = 3;
                go.Set();
                WaitUntil.True(() => last == 3, "skipped");
            }
        }

        [Fact]
        public void Rule_4_4_QueuedAtMostOnceAndTriggerDuringRunQueuesOneMore()
        {
            using (var host = new ThreadHost())
            {
                var inRun = new ManualResetEventSlim(false);
                var go = new ManualResetEventSlim(false);
                var n = 0;
                var reaction = host.NewReactor().Add(() =>
                {
                    Interlocked.Increment(ref n);
                    if (n == 1)
                    {
                        inRun.Set();
                        go.Wait();
                    }
                });
                WaitUntil.Event(inRun, "in");
                reaction.Trigger();
                reaction.Trigger();
                go.Set();
                WaitUntil.True(() => n == 2, "one more");
            }
        }

        [Fact(DisplayName = "s4.4-01 Triggers during a run queue one more")]
        public void Sem_4_4_01_TriggerDuringRunQueuesOneMore()
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

        [Fact]
        public void Rule_4_5_ManualRegisterTriggerIgnoresEquality()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(1);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    var _ = r.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                r.TriggerReactions();
                WaitUntil.True(() => n == 2, "manual");
            }
        }

        [Fact]
        public void Rule_4_6_ManualReactionTrigger()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                var reaction = host.NewReactor().Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 1, "first");
                reaction.Trigger();
                WaitUntil.True(() => n == 2, "manual");
            }
        }

        [Fact(DisplayName = "s4.6-01 Manual trigger coalesces with write")]
        public void Sem_4_6_01_ManualTriggerQueuedLikeOthers()
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
    }

    public class Md6_CyclesTests : SemanticsAcceptanceTest
    {
        [Fact]
        public void Rule_6_1_SelfWriteRetriggers()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    var v = r.Value;
                    Interlocked.Increment(ref n);
                    if (v < 3)
                        r.Value = v + 1;
                });
                WaitUntil.True(() => n >= 4 && r.Value == 3, "self writes");
            }
        }

        [Fact(DisplayName = "s6.1-01 Self-write retriggers until stable")]
        public void Sem_6_1_01_SelfWriteRetriggers()
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

        [Fact(DisplayName = "s6.2-01 Endless self-trigger doesn't starve")]
        public void Sem_6_2_01_EndlessSelfTriggerDoesNotStarve()
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
            })
            {
                IsBackground = true
            };
            thread.Start();
            Assert.True(finished.WaitOne(Harness.TimeoutMs), "Shutdown did not complete");
            Assert.True(thread.Join(Harness.TimeoutMs));
        }

        [Fact]
        public void Rule_6_3_PausingAppliesToReadsOnly()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    Interlocked.Increment(ref n);
                    if (n == 1)
                    {
                        Reaction.Pause(() => { var _ = r.Value; });
                        r.Value = 1;
                    }
                    else
                    {
                        var _ = r.Value;
                    }
                });
                WaitUntil.True(() => n >= 1, "started");
                Thread.Sleep(80);
                Assert.Equal(1, n);
            }
        }

        [Fact(DisplayName = "s6.3-01 Writes inside Pause still trigger")]
        public void Sem_6_3_01_WritesInsidePauseStillTrigger()
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

        [Fact(DisplayName = "s6.4-01 Reaction-added reaction owned by reactor")]
        public void Sem_6_4_01_ReactionCreatingReactions()
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
    }

    public class Md7_ProtocolTests : SemanticsAcceptanceTest
    {
        [Fact]
        public void Rule_7_1_SubscribeBeforeValueReturned()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    var _ = r.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                r.Value = 1;
                WaitUntil.True(() => n == 2, "subscribed");
            }
        }

        [Fact]
        public void Rule_7_2_StoreThenTrigger()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var seen = 0;
                host.NewReactor().Add(() => seen = r.Value);
                WaitUntil.True(() => seen == 0, "first");
                r.Value = 5;
                WaitUntil.True(() => seen == 5, "stored");
            }
        }

        [Fact(DisplayName = "s7.2-01 Triggered run never sees older value")]
        public void Sem_7_2_01_TriggeredReactionSeesNewValue()
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
            })
            {
                IsBackground = true
            };
            writer.Start();
            Assert.True(writer.Join(Harness.TimeoutMs));
            Harness.Barrier2(s);
            Assert.Equal(0, Volatile.Read(ref older));
            Assert.Equal(reg.Value, lastSeen);
        }

        [Fact]
        public void Rule_7_3_DependencyMaintenance()
        {
            using (var host = new ThreadHost())
            {
                var a = new Register<int>(0);
                var b = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    Interlocked.Increment(ref n);
                    if (a.Value == 0)
                    {
                        var _ = b.Value;
                    }
                });
                WaitUntil.True(() => n == 1, "first");
                a.Value = 1;
                WaitUntil.True(() => n == 2, "a");
                b.Value = 1;
                Thread.Sleep(50);
                Assert.Equal(2, n);
            }
        }

        [Fact]
        public void Rule_7_3a_PausedReadDoesNotKeepSubscription()
        {
            using (var host = new ThreadHost())
            {
                var a = new Register<int>(0);
                var b = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    var _ = a.Value;
                    Reaction.Pause(() => { var unused = b.Value; });
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                b.Value = 1;
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }

        [Fact(DisplayName = "s7.3a-01 Normal + paused read keeps subscription")]
        public void Sem_7_3a_01_NormalAndPausedReadKeepsSubscription()
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

        [Fact(DisplayName = "s7.3a-02 Only-paused read drops subscription")]
        public void Sem_7_3a_02_OnlyPausedReadDropsSubscription()
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

        [Fact(DisplayName = "s7.4-01 Reaction never runs re-entrantly")]
        public void Rule_7_4_ReactionNeverRunsReentrantly()
        {
            using (var host = new ThreadHost())
            {
                var depth = 0;
                var max = 0;
                var n = 0;
                var r = new Register<int>(0);
                host.NewReactor().Add(() =>
                {
                    depth++;
                    if (depth > max) max = depth;
                    var v = r.Value;
                    if (v == 0)
                        r.Value = 1;
                    depth--;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 2, "two runs");
                Assert.Equal(1, max);
            }
        }

        [Fact]
        public void Rule_7_5_TriggeringDoesNotSubscribe()
        {
            using (var host = new ThreadHost())
            {
                var a = new Register();
                var b = new Register();
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    a.RecordRead();
                    b.TriggerReactions();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                b.TriggerReactions();
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }

        [Fact(DisplayName = "s7.5-01 Triggering does not subscribe")]
        public void Sem_7_5_01_TriggerDoesNotSubscribe()
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
    }

    public class Md8_ExceptionTests : SemanticsAcceptanceTest
    {
        [Fact(DisplayName = "cs3.5-02 Throw logged; reads before throw kept")]
        public void Rule_8_1_ExceptionEndsRunKeepsReadsUpToThrow()
        {
            using (var host = new ThreadHost())
            {
                var a = new Register<int>(0);
                var b = new Register<int>(0);
                var n = 0;
                Exception seen = null;
                Action<Exception> h = e => seen = e;
                Reactive.UnhandledException += h;
                try
                {
                    host.NewReactor().Add(() =>
                    {
                        Interlocked.Increment(ref n);
                        var _ = a.Value;
                        throw new InvalidOperationException("rx");
#pragma warning disable 162
                        var unused = b.Value;
#pragma warning restore 162
                    });
                    WaitUntil.True(() => n == 1 && seen != null, "logged");
                    b.Value = 1;
                    Thread.Sleep(40);
                    Assert.Equal(1, n);
                    a.Value = 1;
                    WaitUntil.True(() => n == 2, "a still connected");
                }
                finally
                {
                    Reactive.UnhandledException -= h;
                }
            }
        }

        [Fact]
        public void Rule_8_2_ReactionDoesNotStopTheLoop()
        {
            using (var host = new ThreadHost())
            {
                Reactive.UnhandledException += Ignore;
                try
                {
                    host.NewReactor().Add(() => { throw new InvalidOperationException(); });
                    var n = 0;
                    host.NewReactor().Add(() => Interlocked.Increment(ref n));
                    WaitUntil.True(() => n == 1, "loop lives");
                }
                finally
                {
                    Reactive.UnhandledException -= Ignore;
                }
            }
        }

        [Fact(DisplayName = "s8.2-01 Throwing reaction doesn't stop loop")]
        public void Sem_8_2_01_ThrowingReactionDoesNotStopLoop()
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

        static void Ignore(Exception e)
        {
        }
    }

    public class Md9_PauseTests
    {
        [Fact]
        public void Rule_9_1_PausedReadsDoNotSubscribe()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    Reaction.Pause(() => { var _ = r.Value; });
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                r.Value = 1;
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_9_2_PauseIsReentrant()
        {
            using (var host = new ThreadHost())
            {
                var rec = true;
                host.NewReactor().Add(() =>
                {
                    Reaction.Pause(() =>
                    {
                        Reaction.Pause(() => rec = Reaction.IsRecording);
                    });
                });
                Thread.Sleep(50);
                Assert.False(rec);
            }
        }

        [Fact]
        public void Rule_9_3_UnbalancedPauseEndsWithReaction()
        {
            using (var host = new ThreadHost())
            {
                host.NewReactor().Add(() => Reaction.PauseRecording());
                Thread.Sleep(40);
                Assert.Null(Reaction.Current);
                Assert.False(Reaction.IsRecording);
            }
        }
    }

    public class Md10_ExecutionTests
    {
        [Fact]
        public void Rule_10_1_PlainSynchronousRun()
        {
            using (var host = new ThreadHost())
            {
                var depth = 0;
                host.NewReactor().Add(() => depth = new System.Diagnostics.StackTrace().FrameCount);
                WaitUntil.True(() => depth > 0, "ran");
            }
        }

    }

    public class Md11_SchedulingTests : SemanticsAcceptanceTest
    {
        [Fact(DisplayName = "s11.1-01 Runs always on the scheduler thread")]
        public void Sem_11_1_01_ReactionStaysOnItsThread()
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
                })
                {
                    IsBackground = true
                };
                worker.Start();
                Harness.Wait(done, "trigger");
                worker.Join();
                Harness.Barrier(s);
            }
            Assert.Equal(0, Volatile.Read(ref wrong));
            Assert.Equal(101, Volatile.Read(ref runs));
        }

        [Fact]
        public void Rule_11_2_ReactorCapturesCreatingThreadScheduler()
        {
            using (var host = new ThreadHost())
            {
                Reactor reactor = null;
                host.OnScheduler(() => reactor = new Reactor());
                var n = 0;
                reactor.Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 1, "ran on captured");
            }
        }

        [Fact]
        public void Rule_11_5_SameLoopFirstRunAfterCreatorFinishes()
        {
            using (var host = new ThreadHost())
            {
                var order = "";
                host.OnScheduler(() =>
                {
                    new Reactor(host.Scheduler).Add(() => order += "R");
                    order += "C";
                });
                WaitUntil.True(() => order == "CR", "creator first");
            }
        }

        [Fact(DisplayName = "s11.5-01 Same-loop add+write: first run sees it")]
        public void Sem_11_5_01_SameLoopStartupOrdering()
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

        [Fact]
        public void Rule_11_7_TwoReactionsOnSameLoopNeverConcurrent()
        {
            using (var host = new ThreadHost())
            {
                var concurrent = 0;
                var max = 0;
                var n = 0;
                Action body = () =>
                {
                    var c = Interlocked.Increment(ref concurrent);
                    if (c > max) max = c;
                    Thread.Sleep(15);
                    Interlocked.Decrement(ref concurrent);
                    Interlocked.Increment(ref n);
                };
                host.NewReactor().Add(body);
                host.NewReactor().Add(body);
                WaitUntil.True(() => n == 2, "both");
                Assert.Equal(1, max);
            }
        }

        [Fact(DisplayName = "s11.7-01 No concurrent runs on one scheduler")]
        public void Sem_11_7_01_NoConcurrentRunsOnOneLoop()
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

        [Fact(DisplayName = "s11.9-01 Queued work runs first come first served")]
        public void Rule_11_9_FirstComeFirstServed()
        {
            using (var host = new ThreadHost())
            {
                var order = "";
                var go = new ManualResetEventSlim(false);
                host.Scheduler.Queue(() => go.Wait());
                host.Scheduler.Queue(() => order += "a");
                host.Scheduler.Queue(() => order += "b");
                go.Set();
                WaitUntil.True(() => order == "ab", "fifo");
            }
        }

        [Fact(DisplayName = "s11.9-02 Coalesced run keeps first queue spot")]
        public void Sem_11_9_02_CoalescedPositionStaysAtFirstTrigger()
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
            Assert.Equal([1, 2], order.ToArray());
        }

        [Fact(DisplayName = "s14.7-01 Self-requeue goes to end of the queue")]
        public void Rule_14_7_SelfRequeueGoesToEndOfQueue()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(0);
                var order = "";
                var entered = new ManualResetEvent(false);
                var release = new ManualResetEvent(false);
                var done = new ManualResetEvent(false);
                int recorded = 0;
                int selfTriggers = 0;
                bool record = false;
                try
                {
                    var reactor = host.NewReactor();
                    Reaction r1 = null;
                    r1 = reactor.Add(() =>
                    {
                        int value = reg.Value;
                        if (!record)
                            return;
                        order += "r1";
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
                        else
                            done.Set();
                    });
                    var r2 = reactor.Add(() =>
                    {
                        if (record)
                            order += "r2";
                    });
                    var r3 = reactor.Add(() =>
                    {
                        if (record)
                            order += "r3";
                    });
                    host.OnScheduler(() => { });
                    record = true;
                    r1.Trigger();
                    if (!entered.WaitOne(TimeSpan.FromSeconds(15)))
                        throw new TimeoutException("r1 waiting");
                    r2.Trigger();
                    r3.Trigger();
                    release.Set();
                    if (!done.WaitOne(TimeSpan.FromSeconds(15)))
                        throw new TimeoutException("self requeue");
                    Assert.Equal("r1r2r3r1r1r1", order);
                }
                finally
                {
                    release.Set();
                }
            }
        }

        [Fact(DisplayName = "s11.10-01 Own write is visible immediately")]
        public void Rule_11_10_WritesTakeEffectImmediately()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var seen = -1;
                host.NewReactor().Add(() =>
                {
                    r.Value = 4;
                    seen = r.Value;
                });
                WaitUntil.True(() => seen == 4, "immediate");
            }
        }

        [Fact]
        public void Rule_11_11_WritesOnOneLoopSeenTogether()
        {
            using (var host = new ThreadHost())
            {
                var a = new Register<int>(0);
                var b = new Register<int>(0);
                var pair = "";
                var writerDone = false;
                host.Scheduler.Queue(() =>
                {
                    a.Value = 1;
                    b.Value = 2;
                    writerDone = true;
                });
                WaitUntil.True(() => writerDone, "written");
                host.NewReactor().Add(() => pair = a.Value + "," + b.Value);
                WaitUntil.True(() => pair == "1,2", "together");
            }
        }

        [Fact(DisplayName = "s11.11-01 Same-action writes seen together")]
        public void Sem_11_11_01_SameLoopMultiWritesSeenTogether()
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

        [Fact(DisplayName = "s11.15-01 After shutdown no reaction runs again")]
        public void Rule_11_15_ShutdownDrainsThenDisposesAndRefusesNew()
        {
            var host = new ThreadHost();
            var n = 0;
            var reactor = host.NewReactor();
            reactor.Add(() => Interlocked.Increment(ref n));
            WaitUntil.True(() => n == 1, "first");
            host.Scheduler.Queue(() => Interlocked.Increment(ref n));
            host.Scheduler.Shutdown();
            Assert.True(n >= 2);
            reactor.Add(() => Interlocked.Increment(ref n));
            var after = n;
            Thread.Sleep(40);
            Assert.Equal(after, n);
        }

        [Fact]
        public void Rule_11_16_ShutdownFromAnyThreadAndNotFromReaction()
        {
            var host = new ThreadHost();
            var worker = new Thread(() => host.Scheduler.Shutdown());
            worker.Start();
            worker.Join();
            Assert.True(host.Scheduler.IsFinished);
        }

        [Fact(DisplayName = "cs3.9-01 Schedulers known to Reactive.Shutdown")]
        public void Rule_11_17_ReactiveShutdownAll()
        {
            var a = ThreadScheduler.CreateNew("a");
            var b = ThreadScheduler.CreateNew("b");
            Reactive.Shutdown();
            Assert.True(a.IsFinished);
            Assert.True(b.IsFinished);
        }
    }

    public class Md12_LifetimeTests : SemanticsAcceptanceTest
    {
        [Fact]
        public void Rule_12_5_DisposeRemovesSubscriptions()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var n = 0;
                var reaction = host.NewReactor().Add(() =>
                {
                    var _ = r.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                reaction.Dispose();
                Thread.Sleep(40);
                r.Value = 1;
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_12_6_QueuedRunDoesNotExecuteOriginalLambda()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                Reaction reaction = null;
                host.OnScheduler(() =>
                {
                    var reactor = new Reactor(host.Scheduler);
                    reaction = reactor.Add(() => Interlocked.Increment(ref n));
                    reaction.Dispose();
                    Assert.Equal(0, n);
                });
                Thread.Sleep(50);
                Assert.Equal(0, n);
            }
        }

        [Fact(DisplayName = "s12.6b-01 Trigger after dispose has no effect")]
        public void Sem_12_6b_01_LaterTriggerAfterDisposeNoEffect()
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

        [Fact]
        public void Rule_12_8_ValuelessRegisterIsPureSignal()
        {
            using (var host = new ThreadHost())
            {
                var s = new Register();
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    s.RecordRead();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                s.TriggerReactions();
                WaitUntil.True(() => n == 2, "signal");
            }
        }

        [Fact]
        public void Rule_12_9_NullActionIsNoAction()
        {
            using (var host = new ThreadHost())
            {
                host.Scheduler.Queue((Action)null);
                Assert.NotNull(host.NewReactor().Add(null));
            }
        }

        [Fact(DisplayName = "s12.9-01 Null actions are not errors")]
        public void Sem_12_9_01_NullActionsNotErrors()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reactor = new Reactor(s);
                Assert.Null(Record.Exception(() => Reaction.Pause((Action)null)));
                Assert.Null(Record.Exception(() => reactor.Queue((Action)null)));
                Assert.Null(Record.Exception(() => s.Queue((Action)null)));
                Harness.Barrier(s);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact]
        public void Rule_12_12_AddDuringDisposeCreatesNothing()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                reactor.Dispose();
                Assert.Null(reactor.Add(() => { }));
            }
        }
    }

    public class Md13_SyncTests : SemanticsAcceptanceTest
    {
        [Fact]
        public void Rule_13_1_NoTearing()
        {
            var r = new Register<Big>(new Big(1, 1));
            var errors = 0;
            var stop = false;
            var t = new Thread(() =>
            {
                while (!stop)
                {
                    var v = r.Value;
                    if (v.X != v.Y)
                        Interlocked.Increment(ref errors);
                }
            });
            t.Start();
            for (var i = 0; i < 20000; i++)
                r.Value = new Big(i, i);
            stop = true;
            t.Join();
            Assert.Equal(0, errors);
        }

        [Fact]
        public void Rule_13_3_QueuedAtMostOnceFromManyThreads()
        {
            using (var host = new ThreadHost())
            {
                var inRun = new ManualResetEventSlim(false);
                var go = new ManualResetEventSlim(false);
                var n = 0;
                var reaction = host.NewReactor().Add(() =>
                {
                    Interlocked.Increment(ref n);
                    if (n == 1)
                    {
                        inRun.Set();
                        go.Wait();
                    }
                });
                WaitUntil.Event(inRun, "in");
                var threads = new Thread[8];
                for (var i = 0; i < threads.Length; i++)
                {
                    threads[i] = new Thread(() => reaction.Trigger());
                    threads[i].Start();
                }
                foreach (var t in threads)
                    t.Join();
                go.Set();
                WaitUntil.True(() => n == 2, "one extra from many triggers");
            }
        }

        [Fact(DisplayName = "s13.3-01 Concurrent triggers queue only once")]
        public void Sem_13_3_01_QueuedAtMostOnceUnderConcurrency()
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

        [Fact(DisplayName = "s13.5-01 Register/queue locks never deadlock")]
        public void Sem_13_5_01_NoDeadlockBetweenRegisterAndQueue()
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

        [Fact(DisplayName = "s13.7-01 Lambda never runs under a lock")]
        public void Sem_13_7_01_NoLambdaUnderLock()
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
                })
                {
                    IsBackground = true
                };
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

        [Fact(DisplayName = "s13.8-01 Writer doesn't wait for a running run")]
        public void Rule_13_8_WritersDoNotWaitForReaction()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var inRun = new ManualResetEventSlim(false);
                var go = new ManualResetEventSlim(false);
                host.NewReactor().Add(() =>
                {
                    var _ = r.Value;
                    inRun.Set();
                    go.Wait();
                });
                WaitUntil.Event(inRun, "in");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                r.Value = 1;
                Assert.True(sw.ElapsedMilliseconds < 1000);
                go.Set();
            }
        }

        struct Big(long x, long y)
        {
            public readonly long X = x, Y = y;
        }
    }

    public class Md14_DiscussionTests : SemanticsAcceptanceTest
    {
        [Fact(DisplayName = "s14.1-01 Shadow register: field change reruns")]
        public void Sem_14_1_01_ShadowRegisterPattern()
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
    }

    public class Md15_LoggingTests : SemanticsAcceptanceTest
    {
        [Fact(DisplayName = "s15.1-01 Reaction exception goes to the handler")]
        public void Rule_15_1_UnhandledFromReactionIsLogged()
        {
            Exception seen = null;
            Action<Exception> h = e => seen = e;
            Reactive.UnhandledException += h;
            try
            {
                using (var host = new ThreadHost())
                {
                    host.NewReactor().Add(() => { throw new InvalidOperationException(); });
                    WaitUntil.True(() => seen != null, "logged");
                }
            }
            finally
            {
                Reactive.UnhandledException -= h;
            }
        }

        [Fact(DisplayName = "s15.2-01 Usage errors thrown, not logged")]
        public void Sem_15_2_01_UsageErrorsThrownNotLogged()
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
                })
                {
                    IsBackground = true
                };
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
    }
}

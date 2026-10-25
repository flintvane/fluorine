using System;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class Md1_ThreadingTests
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

        [Fact]
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

        [Fact]
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

        sealed class ThrowingScheduler : Scheduler
        {
            readonly System.Collections.Generic.Queue<Action> _queue = new System.Collections.Generic.Queue<Action>();

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

    public class Md3_EqualityTests
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

        [Fact]
        public void Rule_3_1c_EqualityThrowStoresNothing()
        {
            var v = new EqThrower();
            var r = new Register<EqThrower>(v);
            Assert.Throws<InvalidOperationException>(() => r.SetValue(new EqThrower()));
            Assert.Same(v, r.Value);
        }

        [Fact]
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

    public class Md4_RunGuaranteeTests
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
    }

    public class Md6_CyclesTests
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
    }

    public class Md7_ProtocolTests
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

        [Fact]
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
    }

    public class Md8_ExceptionTests
    {
        [Fact]
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

    public class Md11_SchedulingTests
    {
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

        [Fact]
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

        [Fact]
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

        [Fact]
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

        [Fact]
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

        [Fact]
        public void Rule_11_17_ReactiveShutdownAll()
        {
            var a = ThreadScheduler.CreateNew("a");
            var b = ThreadScheduler.CreateNew("b");
            Reactive.Shutdown();
            Assert.True(a.IsFinished);
            Assert.True(b.IsFinished);
        }
    }

    public class Md12_LifetimeTests
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

    public class Md13_SyncTests
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

        [Fact]
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

        struct Big
        {
            public readonly long X, Y;
            public Big(long x, long y) { X = x; Y = y; }
        }
    }

    public class Md15_LoggingTests
    {
        [Fact]
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

    }
}

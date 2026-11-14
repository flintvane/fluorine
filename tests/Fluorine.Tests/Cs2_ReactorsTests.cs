using System.Reflection;
using System.Threading;
using System;
using Xunit;

namespace Fluorine
{
    public class Cs2_ReactorsTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs2.1-02 Queue on disposed reactor never runs")]
        public void Rule_2_1_ReactorQueueUsesSchedulerAndDisposedDrops()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                var n = 0;
                reactor.Queue(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 1, "queued");
                reactor.Dispose();
                reactor.Queue(() => Interlocked.Increment(ref n));
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }

        [Fact(DisplayName = "cs2.1-01 Reactor.Queue runs on scheduler thread")]
        public void Rule_2_1_01_QueueActionRunsOnScheduler()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            Thread tid = null;
            reactor.Queue(() => tid = Thread.CurrentThread);
            Harness.Barrier(s);
            Assert.Equal(Harness.ThreadOf(s), tid);
        }

        [Fact(DisplayName = "cs2.1-03 Reactor.Queue shares FIFO with runs")]
        public void Rule_2_1_03_QueueActionSharesFifoWithReactions()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            var order = new Order();
            var hold = Harness.Gate(s);
            try
            {
                reactor.Add(() => order.Add("r1"));
                reactor.Queue(() => order.Add("a"));
                reactor.Add(() => order.Add("r2"));
            }
            finally
            {
                hold.Release();
            }
            Harness.Barrier2(s);
            Assert.Equal(["r1", "a", "r2"], order.Snapshot());
        }

        [Fact(DisplayName = "cs2.2-01 Reactor() captures Scheduler.Current")]
        public void Rule_2_2_01_ReactorCapturesSchedulerCurrent()
        {
            var s = Harness.Create();
            Thread tid = null;
            var owner = Harness.ThreadOf(s);
            s.Queue(() =>
            {
                var reactor = new Reactor();
                reactor.Add(() => tid = Thread.CurrentThread);
            });
            Harness.Barrier2(s);
            Assert.Equal(owner, tid);
        }

        [Fact(DisplayName = "cs2.2-02 Reactor() without scheduler throws")]
        public void Rule_2_2_02_ReactorWithoutSchedulerThrowsNotLogged()
        {
            using (var log = new Log())
            {
                Exception thrown = null;
                int seen = 0;
                var done = new ManualResetEvent(false);
                var thread = new Thread(() =>
                {
                    try
                    {
                        var reactor = new Reactor();
                        GC.KeepAlive(reactor);
                    }
                    catch (Exception ex)
                    {
                        thrown = ex;
                        seen = log.Count;
                    }
                    finally
                    {
                        done.Set();
                    }
                })
                {
                    IsBackground = true
                };
                thread.Start();
                Harness.Wait(done, "reactor ctor");
                Assert.IsType<ReactiveException>(thrown);
                Assert.Equal(0, seen);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.2-03 Scheduler captured at Reactor creation")]
        public void Rule_2_2_03_SchedulerCapturedAtCreation()
        {
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            Reactor reactor = null;
            s1.Queue(() => reactor = new Reactor());
            Harness.Barrier(s1);
            var owner = Harness.ThreadOf(s1);
            Thread ranOn = null;
            s2.Queue(() => reactor.Add(() => ranOn = Thread.CurrentThread));
            Harness.Barrier(s2);
            Harness.Barrier2(s1);
            Assert.Equal(owner, ranOn);
        }

        [Fact(DisplayName = "cs2.3-02 Reactor(null) throws, not logged")]
        public void Rule_2_3_02_ReactorNullThrowsNotLogged()
        {
            using (var log = new Log())
            {
                Assert.Throws<ReactiveException>(() => new Reactor(null));
                Assert.Equal(0, log.Count);
            }
        }

        [Fact]
        public void Rule_2_4_ReactorOnShuttingDownSchedulerIsDisposed()
        {
            var host = new ThreadHost();
            host.Scheduler.Shutdown();
            var reactor = new Reactor(host.Scheduler);
            Assert.Null(reactor.Add(() => { }));
        }

        [Fact(DisplayName = "cs2.4-01 Reactor on shut-down sched is disposed")]
        public void Rule_2_4_01_ReactorOnShutDownSchedulerActsDisposed()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                s.Shutdown();
                Reactor reactor = null;
                var ex = Record.Exception(() => reactor = new Reactor(s));
                Assert.Null(ex);
                bool ran = false;
                Assert.Null(reactor.Add(() => ran = true));
                reactor.Queue(() => ran = true);
                Assert.False(ran);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.4-02 Reactor made during shutdown: disposed")]
        public void Rule_2_4_02_ReactorDuringShutdownActsDisposed()
        {
            var s = Harness.Create();
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            s.Queue(() =>
            {
                entered.Set();
                release.WaitOne();
            });
            Harness.Wait(entered, "drain gate");
            Reaction created = null;
            int runs = 0;
            s.Queue(() =>
            {
                created = new Reactor(s).Add(() => runs++);
            });
            var started = new ManualResetEvent(false);
            var caller = Harness.CallShutdown(s, started, null);
            try
            {
                Harness.WaitUntilBlocked(caller, started);
                release.Set();
                Assert.True(caller.Join(Harness.TimeoutMs), "shutdown did not return");
            }
            finally
            {
                release.Set();
            }
            Assert.Null(created);
            Assert.Equal(0, runs);
        }

        [Fact]
        public void Rule_2_5_NameIsSettableNoNameConstructor()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                reactor.Name = "r";
                Assert.Equal("r", reactor.Name);
                Assert.Null(typeof(Reactor).GetConstructor([typeof(string)]));
            }
        }

        [Fact(DisplayName = "cs2.6-01 Reaction has no public constructor")]
        public void Rule_2_6_ReactionHasNoPublicConstructor()
        {
            Assert.Empty(typeof(Reaction).GetConstructors());
        }

        [Fact(DisplayName = "cs2.7-01 Add returns reaction, queues first run")]
        public void Rule_2_7_AddQueuesFirstRunAndReturnsReaction()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                var r = host.NewReactor().Add("n", () => Interlocked.Increment(ref n));
                Assert.NotNull(r);
                Assert.Equal("n", r.Name);
                WaitUntil.True(() => n == 1, "first run");
            }
        }
        
        [Fact(DisplayName = "cs2.8-01 Add never runs inline, even on S")]
        public void Rule_2_8_ReactionNeverRunsInline()
        {
            using (var host = new ThreadHost())
            {
                var ran = false;
                host.OnScheduler(() =>
                {
                    var r = new Reactor(host.Scheduler);
                    r.Add(() => ran = true);
                    Assert.False(ran);
                });
                WaitUntil.True(() => ran, "later");
            }
        }

        [Fact]
        public void Rule_2_9_AddOnDisposedReturnsNull()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                reactor.Dispose();
                Assert.Null(reactor.Add(() => { }));
            }
        }

        [Fact(DisplayName = "cs2.9-01 Add on disposed reactor returns null")]
        public void Rule_2_9_01_AddOnDisposedReturnsNullSilently()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reactor = new Reactor(s);
                reactor.Dispose();
                bool ran = false;
                var reaction = reactor.Add(() => ran = true);
                Harness.Barrier(s);
                Assert.Null(reaction);
                Assert.False(ran);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact]
        public void Rule_2_10_ReactionAlwaysHasAReactor()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add(() => { });
                Assert.NotNull(r);
            }
        }

        [Fact]
        public void Rule_2_11_DisposingReactorDisposesReactions()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(0);
                var n = 0;
                var reactor = host.NewReactor();
                reactor.Add(() =>
                {
                    var _ = reg.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                reactor.Dispose();
                reg.Value = 1;
                Thread.Sleep(50);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_2_12_DisposingReactionRemovesItFromReactor()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(0);
                var n = 0;
                var reactor = host.NewReactor();
                var reaction = reactor.Add(() =>
                {
                    var _ = reg.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                reaction.Dispose();
                WaitUntil.True(() => true, "dispose queued");
                Thread.Sleep(50);
                reg.Value = 2;
                Thread.Sleep(50);
                Assert.Equal(1, n);
            }
        }

        [Fact(DisplayName = "cs2.12-01 Reaction dispose leaves others running")]
        public void Rule_2_12_01_ReactionDisposeRemovesFromReactor()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var reactor = new Reactor(s);
            int n1 = 0, n2 = 0;
            var r1 = reactor.Add(() => { n1++; var unused = reg.Value; });
            reactor.Add(() => { n2++; var unused = reg.Value; });
            Harness.Barrier(s);
            r1.Dispose();
            int n1Before = n1;
            int n2Before = n2;
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(n1Before, n1);
            Assert.True(n2 > n2Before, "r2 did not run");
            var disposeEx = Record.Exception(() => reactor.Dispose());
            Assert.Null(disposeEx);
            int n2AfterDispose = n2;
            reg.Value = 2;
            Harness.Barrier(s);
            Assert.Equal(n2AfterDispose, n2);
        }

        [Fact]
        public void Rule_2_13_NullLambdaIsEmptyAndFixedAtCreation()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add(null);
                Assert.NotNull(r);
                WaitUntil.True(() => r.ExecutionCount >= 1, "null lambda ran");
            }
        }

        [Fact(DisplayName = "cs2.13-01 Null lambda acts as empty block")]
        public void Rule_2_13_01_NullLambdaIsEmptyBlock()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reaction = new Reactor(s).Add(null);
                Harness.Barrier(s);
                Assert.NotNull(reaction);
                var ex = Record.Exception(() => reaction.Trigger());
                Harness.Barrier(s);
                Assert.Null(ex);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.13-02 Reaction lambda cannot be changed")]
        public void Rule_2_13_02_LambdaNotChangeable()
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            foreach (var property in typeof(Reaction).GetProperties(flags))
            {
                if (!typeof(Delegate).IsAssignableFrom(property.PropertyType))
                    continue;
                Assert.Null(property.GetSetMethod());
            }
            foreach (var field in typeof(Reaction).GetFields(flags))
                Assert.False(typeof(Delegate).IsAssignableFrom(field.FieldType));
        }

        [Fact]
        public void Rule_2_14_LambdaCanBeCalledOutsideReaction()
        {
            var n = 0;
            Action lambda = () => n++;
            lambda();
            Assert.Equal(1, n);
            Assert.Null(Reaction.Current);
        }

        [Fact(DisplayName = "cs2.15-01 Trigger queues one more run")]
        public void Rule_2_15_TriggerQueuesAndNeverThrowsOnDisposed()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                var r = host.NewReactor().Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 1, "first");
                r.Trigger();
                WaitUntil.True(() => n == 2, "trigger");
                r.Dispose();
                r.Trigger();
            }
        }

        [Fact(DisplayName = "cs2.15-02 Trigger never runs inline")]
        public void Rule_2_15_02_TriggerNeverInline()
        {
            var s = Harness.Create();
            int runs = 0;
            var reaction = new Reactor(s).Add(() => runs++);
            Harness.Barrier(s);
            s.Queue(() =>
            {
                int before = runs;
                reaction.Trigger();
                Assert.Equal(before, runs);
            });
            Harness.Barrier2(s);
            Assert.Equal(2, runs);
        }

        [Fact(DisplayName = "cs2.15-03 Trigger on disposed reaction: silent")]
        public void Rule_2_15_03_TriggerOnDisposedReaction()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                int runs = 0;
                var reaction = new Reactor(s).Add(() => runs++);
                Harness.Barrier(s);
                reaction.Dispose();
                Harness.Barrier(s);
                int before = runs;
                var ex = Record.Exception(() => reaction.Trigger());
                Harness.Barrier(s);
                Assert.Null(ex);
                Assert.Equal(before, runs);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact]
        public void Rule_2_16_NameIsTheNameGivenToAdd()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add("named", () => { });
                Assert.Equal("named", r.Name);
            }
        }
        
        [Fact(DisplayName = "cs2.17-02 Dispose twice from test thread detaches")]
        public void Rule_2_17_DisposeNullsLambdaAndDetaches()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(1);
                var n = 0;
                var r = host.NewReactor().Add(() =>
                {
                    var _ = reg.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                r.Dispose();
                r.Dispose();
                Thread.Sleep(50);
                reg.Value = 2;
                Thread.Sleep(50);
                Assert.Equal(1, n);
            }
        }

        [Fact(DisplayName = "cs2.17-01 Dispose detaches from all registers")]
        public void Rule_2_17_01_DisposeDetachesFromAllRegisters()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            int runs = 0;
            var reaction = new Reactor(s).Add(() =>
            {
                runs++;
                var unusedA = a.Value;
                var unusedB = b.Value;
            });
            Harness.Barrier(s);
            reaction.Dispose();
            Harness.Barrier(s);
            int before = runs;
            a.Value = 1;
            b.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
        }

        [Fact(DisplayName = "cs2.17-04 Queued run after dispose skips lambda")]
        public void Rule_2_17_04_QueuedRunAfterDisposeSkipsLambda()
        {
            var s = Harness.Create();
            int runs = 0;
            var reaction = new Reactor(s).Add(() => runs++);
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            var hold = Harness.Gate(s);
            try
            {
                reaction.Trigger();
                reaction.Dispose();
            }
            finally
            {
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(1, runs);
        }

        [Fact(DisplayName = "cs2.17-05 Self-dispose mid-run finishes the run")]
        public void Rule_2_17_05_SelfDisposeMidRun()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            int n = 0;
            Reaction self = null;
            self = new Reactor(s).Add(() =>
            {
                var unusedA = a.Value;
                self.Dispose();
                var unusedB = b.Value;
                n++;
            });
            Harness.Barrier(s);
            Assert.Equal(1, n);
            a.Value = 1;
            b.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(1, n);
        }

        [Fact(DisplayName = "cs2.17-06 Dispose from other thread during run")]
        public void Rule_2_17_06_DisposeDuringRunFromOtherThread()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            int runs = 0;
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                runs++;
                var unused = a.Value;
                entered.Set();
                release.WaitOne();
                var unusedB = b.Value;
            });
            try
            {
                Harness.Wait(entered, "run entered");
                var ex = Record.Exception(() => reaction.Dispose());
                Assert.Null(ex);
                release.Set();
                Harness.Barrier2(s);
                int before = runs;
                a.Value = 1;
                b.Value = 2;
                Harness.Barrier2(s);
                Assert.Equal(1, before);
                Assert.Equal(before, runs);
            }
            finally
            {
                release.Set();
            }
        }

        [Fact(DisplayName = "cs2.18-01 Reaction.Current is the running reaction")]
        public void Rule_2_18_CurrentIsRunningReaction()
        {
            using (var host = new ThreadHost())
            {
                Reaction seen = null;
                Reaction created = null;
                var reactor = host.NewReactor();
                created = reactor.Add(() => seen = Reaction.Current);
                WaitUntil.True(() => seen != null, "ran");
                Assert.Same(created, seen);
                Assert.Null(Reaction.Current);
            }
        }

        [Fact(DisplayName = "cs2.18-02 Reaction.Current null outside a run")]
        public void Rule_2_18_02_CurrentNullOutside()
        {
            var s = Harness.Create();
            Reaction inAction = null;
            bool actionRan = false;
            s.Queue(() =>
            {
                actionRan = true;
                inAction = Reaction.Current;
            });
            Harness.Barrier(s);
            Assert.True(actionRan);
            Assert.Null(inAction);
            Assert.Null(Reaction.Current);
        }

        [Fact(DisplayName = "cs2.18-03 Reaction.Current set while paused")]
        public void Rule_2_18_03_CurrentWhilePaused()
        {
            var s = Harness.Create();
            Reaction seen = null;
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                using (Reaction.Pause())
                    seen = Reaction.Current;
            });
            Harness.Barrier(s);
            Assert.Same(reaction, seen);
        }

        [Fact(DisplayName = "cs2.19-01 PauseRecording stops recording reads")]
        public void Rule_2_19_PauseRecordingIncrementsPauseCount()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    Interlocked.Increment(ref n);
                    Reaction.PauseRecording();
                    var _ = reg.Value;
                    Reaction.ContinueRecording();
                });
                WaitUntil.True(() => n == 1, "first");
                reg.Value = 1;
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }

        [Fact(DisplayName = "cs2.20-02 Extra ContinueRecording throws, recovers")]
        public void Rule_2_20_ContinueRecordingAtZeroThrows()
        {
            using (var host = new ThreadHost())
            {
                Exception logged = null;
                Action<Exception> handler = e => logged = e;
                Reactive.UnhandledException += handler;
                try
                {
                    var reg = new Register<int>(0);
                    var n = 0;
                    var caught = false;
                    var recordingAfter = false;
                    host.NewReactor().Add(() =>
                    {
                        Interlocked.Increment(ref n);
                        try
                        {
                            Reaction.ContinueRecording();
                        }
                        catch (ReactiveException)
                        {
                            caught = true;
                        }
                        recordingAfter = Reaction.IsRecording;
                        var unused = reg.Value;
                    });
                    host.OnScheduler(() => { });
                    Assert.Equal(1, n);
                    Assert.True(caught);
                    Assert.True(recordingAfter);
                    Assert.Null(logged);
                    reg.Value = 1;
                    host.OnScheduler(() => { });
                    Assert.Equal(2, n);
                }
                finally
                {
                    Reactive.UnhandledException -= handler;
                }
            }
        }

        [Fact(DisplayName = "cs2.20-01 ContinueRecording resumes recording")]
        public void Rule_2_20_01_ContinueRecordingResumes()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                Reaction.PauseRecording();
                Reaction.ContinueRecording();
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, runs);
        }

        [Fact]
        public void Rule_2_21_PauseOutsideReactionHasNoEffect()
        {
            Reaction.PauseRecording();
            Reaction.ContinueRecording();
            Assert.False(Reaction.IsRecording);
            Assert.Null(Reaction.Current);
        }

        [Fact(DisplayName = "cs2.21-01 Pause/Continue outside reaction: no-op")]
        public void Rule_2_21_01_PauseContinueOutsideReactionNoEffect()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var ex = Record.Exception(() =>
                {
                    Reaction.PauseRecording();
                    Reaction.ContinueRecording();
                    Reaction.ContinueRecording();
                });
                Assert.Null(ex);
                Assert.False(Reaction.IsRecording);
                Exception actionError = null;
                bool recording = true;
                s.Queue(() =>
                {
                    try
                    {
                        Reaction.PauseRecording();
                        Reaction.ContinueRecording();
                        Reaction.ContinueRecording();
                        recording = Reaction.IsRecording;
                    }
                    catch (Exception ex2)
                    {
                        actionError = ex2;
                    }
                });
                Harness.Barrier(s);
                Assert.Null(actionError);
                Assert.False(recording);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.22-01 IsRecording true only in unpaused run")]
        public void Rule_2_22_RecordingTrueOnlyWhenUnpausedReaction()
        {
            using (var host = new ThreadHost())
            {
                var rec = false;
                var paused = true;
                host.NewReactor().Add(() =>
                {
                    rec = Reaction.IsRecording;
                    Reaction.Pause(() => paused = Reaction.IsRecording);
                });
                WaitUntil.True(() => rec, "recorded");
                Assert.False(paused);
                Assert.False(Reaction.IsRecording);
            }
        }

        [Fact]
        public void Rule_2_23_PauseCountEndsWithTheRunningReaction()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    Reaction.PauseRecording();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "ran");
                Assert.False(Reaction.IsRecording);
                Assert.Null(Reaction.Current);
            }
        }

        [Fact(DisplayName = "cs2.23-01 Unbalanced pause resets at next run")]
        public void Rule_2_23_01_UnbalancedPauseEndsWithRun()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                runs++;
                if (runs == 1)
                    Reaction.PauseRecording();
                else
                {
                    var unused = reg.Value;
                }
            });
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            reaction.Trigger();
            Harness.Barrier(s);
            Assert.Equal(2, runs);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(3, runs);
        }

        [Fact]
        public void Rule_2_24_PauseWrappersAreReentrant()
        {
            using (var host = new ThreadHost())
            {
                var inner = true;
                var ran = 0;
                host.NewReactor().Add(() =>
                {
                    using (Reaction.Pause())
                    using (Reaction.Pause())
                    {
                        inner = Reaction.IsRecording;
                    }
                    Assert.True(Reaction.IsRecording);
                    var v = Reaction.Pause(() => 7);
                    Assert.Equal(7, v);
                    Interlocked.Increment(ref ran);
                });
                WaitUntil.True(() => ran == 1, "ran");
                Assert.False(inner);
            }
        }

        [Fact(DisplayName = "cs2.24-01 using Pause() stops recording")]
        public void Rule_2_24_01_PauseIsDisposable()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            bool recordingAfter = false;
            new Reactor(s).Add(() =>
            {
                runs++;
                using (Reaction.Pause())
                {
                    var unused = reg.Value;
                }
                recordingAfter = Reaction.IsRecording;
            });
            Harness.Barrier(s);
            Assert.True(recordingAfter);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(1, runs);
        }

        [Fact(DisplayName = "cs2.24-02 Pause(Action)/Pause(Func) don't record")]
        public void Rule_2_24_02_PauseActionAndFunc()
        {
            var s = Harness.Create();
            var a = new Register<int>(1);
            var b = new Register<int>(2);
            int runs = 0;
            int seenA = 0;
            int seenB = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                Reaction.Pause(() => seenA = a.Value);
                seenB = Reaction.Pause(() => b.Value);
            });
            Harness.Barrier(s);
            Assert.Equal(1, seenA);
            Assert.Equal(2, seenB);
            int before = runs;
            a.Value = 5;
            b.Value = 6;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
        }

        [Fact(DisplayName = "cs2.24-03 Pause(Action) restores on throw")]
        public void Rule_2_24_03_PauseActionRestoresOnThrow()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                try
                {
                    Reaction.Pause(() => { throw new InvalidOperationException("x"); });
                }
                catch (InvalidOperationException)
                {
                }
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, runs);
        }

        [Fact(DisplayName = "cs2.24-04 Nested pauses unwind correctly")]
        public void Rule_2_24_04_ReentrantPauses()
        {
            var s = Harness.Create();
            var c = new Register<int>(0);
            var d = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                Reaction.Pause(() =>
                {
                    Reaction.Pause(() => { });
                    var unused = c.Value;
                });
                var unusedD = d.Value;
            });
            Harness.Barrier(s);
            int before = runs;
            c.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
            d.Value = 1;
            Harness.Barrier(s);
            Assert.True(runs > before, "d did not retrigger");
        }
    }
}

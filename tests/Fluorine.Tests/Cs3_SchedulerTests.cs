using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Forms;
using Xunit;

namespace Fluorine
{
    public class Cs3_SchedulerTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs3.1-02 Custom Scheduler subclass works")]
        public void Rule_3_1_02_UserDerivedSchedulerPossible()
        {
            var scheduler = new OnlyScheduleScheduler();
            Thread pumping = null;
            int runs = 0;
            new Reactor(scheduler).Add(() =>
            {
                pumping = Thread.CurrentThread;
                runs++;
            });
            Assert.Equal(0, runs);
            scheduler.Pump();
            Assert.Equal(1, runs);
            Assert.Equal(Thread.CurrentThread, pumping);
        }

        [Fact(DisplayName = "cs3.2-01 Queue(Reaction) never inline or throws")]
        public void Rule_3_2_01_QueueReactionNeverInlineNeverThrows()
        {
            var s = Harness.Create();
            bool ran = false;
            Exception error = null;
            Reaction reaction = null;
            s.Queue(() =>
            {
                reaction = new Reactor().Add(() => ran = true);
                if (ran)
                    error = new InvalidOperationException("Add ran inline");
                try
                {
                    reaction.Trigger();
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                if (ran)
                    error = new InvalidOperationException("Trigger ran inline");
                try
                {
                    s.Queue(reaction);
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                if (ran)
                    error = new InvalidOperationException("Queue(Reaction) ran inline");
            });
            Harness.Barrier2(s);
            Assert.Null(error);
            Assert.True(ran);

            var probe = new ProbeScheduler();
            bool probeRan = false;
            var probeReaction = new Reactor(probe).Add(() => probeRan = true);
            Assert.False(probeRan);
            var queueEx = Record.Exception(() => probe.Queue(probeReaction));
            Assert.Null(queueEx);
            Assert.False(probeRan);
            probe.Pump();
            Assert.True(probeRan);
        }

        [Fact(DisplayName = "cs3.2-02 Queue(Reaction) after shutdown: silent")]
        public void Rule_3_2_02_QueueReactionAfterShutdownSilent()
        {
            using var log = new Log();
            var s = Harness.Create();
            int runs = 0;
            var reaction = new Reactor(s).Add(() => runs++);
            Harness.Barrier(s);
            s.Shutdown();
            log.Clear();
            var ex = Record.Exception(() => s.Queue(reaction));
            Assert.Null(ex);
            Assert.Equal(0, log.Count);
            Assert.Equal(1, runs);
        }

        [Fact(DisplayName = "cs3.3-01 ThreadScheduler: work on its own thread")]
        public void Rule_3_3_01_ThreadSchedulerWorkOnItsOwnThread()
        {
            var s = ThreadScheduler.CreateNew("cs3.3-01");
            try
            {
                AssertWorkRunsOnSchedulerThread(s, Harness.ThreadOf(s));
            }
            finally
            {
                s.Shutdown();
            }
        }

        [Fact(DisplayName = "cs3.3-02 Actions and runs share one FIFO")]
        public void Rule_3_3_02_ActionsAndRunsShareFifo()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            var order = new Order();
            int phase = 0;
            var r2 = reactor.Add(() =>
            {
                if (Volatile.Read(ref phase) == 1)
                    order.Add("r2");
            });
            Harness.Barrier(s);
            var hold = Harness.Gate(s);
            try
            {
                Volatile.Write(ref phase, 1);
                reactor.Add(() => order.Add("r1"));
                s.Queue(() => order.Add("a1"));
                r2.Trigger();
                s.Queue(() => order.Add("a2"));
            }
            finally
            {
                hold.Release();
            }

            Harness.Barrier(s);
            Assert.Equal(["r1", "a1", "r2", "a2"], order.Snapshot());
        }

        [Fact(DisplayName = "cs3.3-03 Queued action records no reads")]
        public void Rule_3_3_03_ActionRecordsNoReads()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int reads = 0;
            bool ran = false;
            Reaction current = null;
            s.Queue(() =>
            {
                ran = true;
                current = Reaction.Current;
                reads++;
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            Assert.True(ran);
            Assert.Null(current);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(1, reads);
        }

        [Fact(DisplayName = "cs3.3-04 Action exception logged, loop survives")]
        public void Rule_3_3_04_ActionExceptionLoggedUnwrappedLoopSurvives()
        {
            using var log = new Log();
            var s = Harness.Create();
            var x = new InvalidOperationException("action-x");
            s.Queue(() => { throw x; });
            bool b = false;
            s.Queue(() => b = true);
            Harness.Barrier(s);
            Assert.True(b);
            var items = log.Snapshot();
            Assert.Single(items);
            Assert.Same(x, items[0]);
            Assert.IsNotType<ReactiveException>(items[0]);
        }

        [Fact(DisplayName = "cs3.3-05 Queue(Action) never runs inline")]
        public void Rule_3_3_05_QueueActionNeverInline()
        {
            var s = Harness.Create();
            bool ran = false;
            s.Queue(() =>
            {
                s.Queue(() => ran = true);
                Assert.False(ran);
            });
            Harness.Barrier2(s);
            Assert.True(ran);
        }

        [Fact(DisplayName = "cs3.3-06 Queue(null action) is a silent no-op")]
        public void Rule_3_3_06_QueueNullAction()
        {
            using var log = new Log();
            var s = Harness.Create();
            var ex = Record.Exception(() => s.Queue((Action)null));
            Harness.Barrier(s);
            Assert.Null(ex);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs3.3-07 Actions queued before shutdown run")]
        public void Rule_3_3_07_ActionsQueuedBeforeShutdownRun()
        {
            var s = Harness.Create();
            var hold = Harness.Gate(s);
            int n = 0;
            try
            {
                s.Queue(() => n++);
                s.Queue(() => n++);
                s.Queue(() => n++);
                FinishShutdown(s, hold);
            }
            finally
            {
                hold.Release();
            }

            Assert.Equal(3, n);
        }

        [Fact(DisplayName = "cs3.3-08 Action queued during drain never runs")]
        public void Rule_3_3_08_ActionQueuedAfterShutdownBeganIgnored()
        {
            using var log = new Log();
            var s = Harness.Create();
            var hold = Harness.Gate(s);
            bool a1 = false;
            int a2 = 0;
            try
            {
                s.Queue(() =>
                {
                    a1 = true;
                    s.Queue(() => a2++);
                });
                FinishShutdown(s, hold);
            }
            finally
            {
                hold.Release();
            }

            Assert.True(a1);
            Assert.Equal(0, a2);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs3.3-09 Dispatcher: work on its own thread")]
        public void Rule_3_3_09_DispatcherWorkOnItsOwnThread()
        {
            try
            {
                var created = DispatcherScheduler.CreateNew("cs3.3-09");
                try
                {
                    AssertWorkRunsOnSchedulerThread(created, Harness.ThreadOf(created));
                }
                finally
                {
                    created.Shutdown();
                }

                using var loop = DispatcherLoop.Start();
                AssertWorkRunsOnSchedulerThread(loop.Scheduler, loop.Thread);
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_3_3_09", ex.Message);
                throw;
            }
        }

        [Fact(DisplayName = "cs3.3-10 WinForms: work on its own thread")]
        public void Rule_3_3_10_WinFormsWorkOnItsOwnThread()
        {
            try
            {
                using var loop = WinFormsLoop.Start();
                AssertWorkRunsOnSchedulerThread(loop.Scheduler, loop.Thread);
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_3_3_10", ex.Message);
                throw;
            }
        }

        [Fact(DisplayName = "cs3.4-02 Queue hands work to Schedule")]
        public void Rule_3_4_02_QueueHandsWorkToSchedule()
        {
            var ts = new ProbeScheduler();
            var order = new Order();
            int runs = 0;
            var reaction = new Reactor(ts).Add(() =>
            {
                order.Add("r");
                runs++;
            });
            ts.Pump();
            Assert.Equal(1, runs);
            int before = ts.ScheduleCalls;
            ts.Queue(() => order.Add("a"));
            Assert.Equal(before + 1, ts.ScheduleCalls);
            ts.Queue(reaction);
            Assert.Equal(before + 2, ts.ScheduleCalls);
            Assert.Equal(["r"], order.Snapshot());
            Assert.Equal(1, runs);
            ts.Pump();
            Assert.Equal(["r", "a", "r"], order.Snapshot());
            Assert.Equal(2, runs);
        }

        [Fact(DisplayName = "cs3.4-03 Run(Reaction) is synchronous, records")]
        public void Rule_3_4_03_RunReactionIsSynchronousAndRecords()
        {
            var ts = new ProbeScheduler();
            var reg = new Register<int>(0);
            int runs = 0;
            Reaction seen = null;
            var reaction = new Reactor(ts).Add(() =>
            {
                runs++;
                seen = Reaction.Current;
                var unused = reg.Value;
            });
            int beforeRun = ts.ScheduleCalls;
            ts.RunDirect(reaction);
            Assert.Equal(1, runs);
            Assert.Same(reaction, seen);
            Assert.Equal(beforeRun, ts.ScheduleCalls);
            reg.Value = 1;
            Assert.Equal(beforeRun + 1, ts.ScheduleCalls);
        }

        [Fact(DisplayName = "cs3.5-01 Dependencies rediscovered each run")]
        public void Rule_3_5_01_DependenciesRediscoveredEachRun()
        {
            var s = Harness.Create();
            var a = new Register<bool>(true);
            var b = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                if (a.Value)
                {
                    var unused = b.Value;
                }
            });
            Harness.Barrier(s);
            a.Value = false;
            Harness.Barrier(s);
            int afterDrop = runs;
            b.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(afterDrop, runs);
            a.Value = true;
            Harness.Barrier(s);
            int afterReattach = runs;
            Assert.True(afterReattach > afterDrop);
            b.Value = 2;
            Harness.Barrier(s);
            Assert.True(runs > afterReattach, "b was not reattached");
        }

        [Fact(DisplayName = "cs3.5-03 Throw before reads: only Trigger reruns")]
        public void Rule_3_5_03_ThrowBeforeAnyReadNoRerun()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            var reaction = new Reactor(s).Add(() =>
            {
                runs++;
                throw new InvalidOperationException("x");
            });
            Harness.Barrier(s);
            int before = runs;
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
            reaction.Trigger();
            Harness.Barrier(s);
            Assert.Equal(before + 1, runs);
        }

        [Fact(DisplayName = "cs3.6-01 cs3.6-02 Scheduler.Current is S on its own thread")]
        public void Rule_3_6_CurrentIsThreadStaticAndClearedAfterShutdown()
        {
            Thread owner = null;
            var host = new ThreadHost();
            host.OnScheduler(() =>
            {
                Assert.Same(host.Scheduler, Scheduler.Current);
                owner = Thread.CurrentThread;
            });
            Assert.Same(host.Scheduler, Scheduler.ForThread(owner));
            host.Dispose();
            Assert.Null(Scheduler.ForThread(owner));
            Assert.Null(Scheduler.Current);
        }

        [Fact(DisplayName = "cs3.6-03 Scheduler.Current still set in drain")]
        public void Rule_3_6_03_StillInstalledDuringDrain()
        {
            var s = Harness.Create();
            var hold = Harness.Gate(s);
            Scheduler seen = null;
            try
            {
                s.Queue(() => seen = Scheduler.Current);
                FinishShutdown(s, hold);
            }
            finally
            {
                hold.Release();
            }

            Assert.Same(s, seen);
        }

        [Fact(DisplayName = "cs3.8-01 Shutdown drains queued runs")]
        public void Rule_3_8_01_ShutdownDrainsQueuedRuns()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            var reactions = new Reaction[3];
            bool count = false;
            int extra = 0;
            for (int i = 0; i < reactions.Length; i++)
            {
                reactions[i] = reactor.Add(() =>
                {
                    if (count)
                        Interlocked.Increment(ref extra);
                });
            }

            Harness.Barrier(s);
            count = true;
            var hold = Harness.Gate(s);
            try
            {
                for (int i = 0; i < reactions.Length; i++)
                    reactions[i].Trigger();
                FinishShutdown(s, hold);
            }
            finally
            {
                hold.Release();
            }

            Assert.Equal(3, extra);
        }

        [Fact(DisplayName = "cs3.8-02 Shutdown disposes the reactors")]
        public void Rule_3_8_02_ShutdownDisposesReactors()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var reactor = new Reactor(s);
            int runs = 0;
            reactor.Add(() =>
            {
                runs++;
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            int before = runs;
            s.Shutdown();
            reg.Value = 1;
            Assert.Equal(before, runs);
            Assert.Null(reactor.Add(() => runs++));
        }

        [Fact(DisplayName = "cs3.8-03 Runs queued during drain are dropped")]
        public void Rule_3_8_03_RunsQueuedDuringDrainDropped()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var reactor = new Reactor(s);
            int r1 = 0;
            int r2 = 0;
            Reaction first = null;
            reactor.Add(() =>
            {
                r2++;
                var unused = reg.Value;
            });
            first = reactor.Add(() =>
            {
                r1++;
                if (r1 > 1)
                    reg.Value = 5;
            });
            Harness.Barrier(s);
            Assert.Equal(1, r2);
            var hold = Harness.Gate(s);
            try
            {
                first.Trigger();
                FinishShutdown(s, hold);
            }
            finally
            {
                hold.Release();
            }

            Assert.Equal(2, r1);
            Assert.Equal(1, r2);
        }

        [Fact(DisplayName = "cs3.8-05 Shutdown works from any thread")]
        public void Rule_3_8_05_ShutdownFromAnyThread()
        {
            var fromTest = Harness.Create();
            var thread = Harness.ThreadOf(fromTest);
            fromTest.Shutdown();
            Assert.Null(Scheduler.ForThread(thread));

            var target = Harness.Create();
            var other = Harness.Create();
            var targetThread = Harness.ThreadOf(target);
            var done = new ManualResetEvent(false);
            Exception error = null;
            other.Queue(() =>
            {
                try
                {
                    target.Shutdown();
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
            Harness.Wait(done, "shutdown from other scheduler");
            Assert.Null(error);
            Assert.Null(Scheduler.ForThread(targetThread));
            bool ran = false;
            target.Queue(() => ran = true);
            Assert.False(ran);
        }

        [Fact(DisplayName = "cs3.8-06 Shutdown inside reaction throws")]
        public void Rule_3_8_06_ShutdownInsideReactionThrowsNotLogged()
        {
            using var log = new Log();
            var s = Harness.Create();
            bool caught = false;
            new Reactor(s).Add(() =>
            {
                try
                {
                    s.Shutdown();
                }
                catch (ReactiveException)
                {
                    caught = true;
                }
            });
            Harness.Barrier(s);
            Assert.True(caught);
            Assert.Equal(0, log.Count);
            bool later = false;
            s.Queue(() => later = true);
            Harness.Barrier(s);
            Assert.True(later);
        }

        [Fact(DisplayName = "cs3.8-07 Shutdown inside paused reaction throws")]
        public void Rule_3_8_07_ShutdownInsidePausedReactionThrows()
        {
            using var log = new Log();
            var s = Harness.Create();
            bool caught = false;
            new Reactor(s).Add(() =>
            {
                using (Reaction.Pause())
                {
                    try
                    {
                        s.Shutdown();
                    }
                    catch (ReactiveException)
                    {
                        caught = true;
                    }
                }
            });
            Harness.Barrier(s);
            Assert.True(caught);
            Assert.Equal(0, log.Count);
            bool later = false;
            s.Queue(() => later = true);
            Harness.Barrier(s);
            Assert.True(later);
        }

        [Fact(DisplayName = "cs3.8-08 Shutdown of other sched in run throws")]
        public void Rule_3_8_08_ShutdownInsideReactionOfOtherScheduler()
        {
            using var log = new Log();
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            bool caught = false;
            new Reactor(s2).Add(() =>
            {
                try
                {
                    s1.Shutdown();
                }
                catch (ReactiveException)
                {
                    caught = true;
                }
            });
            Harness.Barrier(s2);
            Assert.True(caught);
            Assert.Equal(0, log.Count);
            bool later = false;
            s1.Queue(() => later = true);
            Harness.Barrier(s1);
            Assert.True(later);
        }

        [Fact(DisplayName = "cs3.8-10 Second Shutdown waits for the first")]
        public void Rule_3_8_10_SecondShutdownWaits()
        {
            var s = Harness.Create();
            var hold = Harness.Gate(s);
            int runs = 0;
            try
            {
                s.Queue(() => runs++);
                var startedA = new ManualResetEvent(false);
                var returnedA = new ManualResetEvent(false);
                var callerA = Harness.CallShutdown(s, startedA, returnedA);
                Harness.WaitUntilBlocked(callerA, startedA);
                var startedB = new ManualResetEvent(false);
                var returnedB = new ManualResetEvent(false);
                var callerB = Harness.CallShutdown(s, startedB, returnedB);
                Harness.WaitUntilBlocked(callerB, startedB);
                Assert.False(returnedA.WaitOne(0));
                Assert.False(returnedB.WaitOne(0));
                hold.Release();
                Assert.True(callerA.Join(Harness.TimeoutMs), "first Shutdown did not finish");
                Assert.True(callerB.Join(Harness.TimeoutMs), "second Shutdown did not finish");
                Assert.True(returnedA.WaitOne(0));
                Assert.True(returnedB.WaitOne(0));
                Assert.Equal(1, runs);
            }
            finally
            {
                hold.Release();
            }
        }

        [Fact(DisplayName = "cs3.8-11 Shutdown after finish returns at once")]
        public void Rule_3_8_11_ShutdownAfterFinishedReturns()
        {
            using var log = new Log();
            var s = Harness.Create();
            s.Shutdown();
            log.Clear();
            Exception error = null;
            var done = new ManualResetEvent(false);
            var thread = new Thread(() =>
            {
                try
                {
                    s.Shutdown();
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
            thread.Start();
            Assert.True(done.WaitOne(1000), "second Shutdown did not return promptly");
            Assert.Null(error);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs3.8-12 Reaction added during drain never runs")]
        public void Rule_3_8_12_FirstRunCreatedDuringShutdownRefused()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            var hold = Harness.Gate(s);
            Reaction created = null;
            int runs = 0;
            try
            {
                s.Queue(() => created = reactor.Add(() => runs++));
                FinishShutdown(s, hold);
            }
            finally
            {
                hold.Release();
            }

            Assert.True(created == null || runs == 0);
            Assert.Equal(0, runs);
        }

        [Fact(DisplayName = "cs3.9-01 Reactive.Shutdown ends new schedulers")]
        public void Rule_3_9_01_SchedulersAnnouncedAutomatically()
        {
            var a = ThreadScheduler.CreateNew("a");
            var b = ThreadScheduler.CreateNew("b");
            Reactive.Shutdown();
            Assert.True(a.IsFinished);
            Assert.True(b.IsFinished);
        }

        [Fact(DisplayName = "cs3.10-01 One scheduler hosts many reactors")]
        public void Rule_3_10_01_OneSchedulerManyReactors()
        {
            var s = Harness.Create();
            var owner = Harness.ThreadOf(s);
            var reactions = new Reaction[100];
            int runs = 0;
            int bad = 0;
            for (int i = 0; i < reactions.Length; i++)
            {
                reactions[i] = new Reactor(s).Add(() =>
                {
                    if (Thread.CurrentThread != owner)
                        Interlocked.Increment(ref bad);
                    Interlocked.Increment(ref runs);
                });
            }

            Harness.Barrier(s);
            Assert.Equal(100, runs);
            for (int i = 0; i < reactions.Length; i++)
                reactions[i].Trigger();
            Harness.Barrier(s);
            Assert.Equal(200, runs);
            Assert.Equal(0, bad);
        }

        [Fact(DisplayName = "cs3.11-01 Reactive.Shutdown ends all schedulers")]
        public void Rule_3_11_01_ReactiveShutdownShutsAll()
        {
            var schedulers = new[] { Harness.Create(), Harness.Create(), Harness.Create() };
            var threads = new Thread[schedulers.Length];
            var holds = new Hold[schedulers.Length];
            var done = new ManualResetEvent[schedulers.Length];
            try
            {
                for (int i = 0; i < schedulers.Length; i++)
                {
                    threads[i] = Harness.ThreadOf(schedulers[i]);
                    holds[i] = Harness.Gate(schedulers[i]);
                    done[i] = new ManualResetEvent(false);
                    int index = i;
                    schedulers[i].Queue(() => done[index].Set());
                }

                var started = new ManualResetEvent(false);
                var caller = new Thread(() =>
                {
                    started.Set();
                    Reactive.Shutdown();
                })
                {
                    IsBackground = true
                };
                caller.Start();
                Harness.WaitUntilBlocked(caller, started);
                for (int i = 0; i < holds.Length; i++)
                    holds[i].Release();
                Assert.True(caller.Join(Harness.TimeoutMs));
                for (int i = 0; i < schedulers.Length; i++)
                {
                    Assert.True(done[i].WaitOne(0), "scheduler " + i + " did not drain");
                    Assert.Null(Scheduler.ForThread(threads[i]));
                }
            }
            finally
            {
                for (int i = 0; i < holds.Length; i++)
                {
                    if (holds[i] != null)
                        holds[i].Release();
                }
            }
        }

        [Fact(DisplayName = "cs3.11-02 Reactive.Shutdown informs all at once")]
        public void Rule_3_11_02_ReactiveShutdownInformsWithoutWaitingBetween()
        {
            var s1 = Harness.Create();
            var g1 = new ManualResetEvent(false);
            var s1Entered = new ManualResetEvent(false);
            s1.Queue(() =>
            {
                s1Entered.Set();
                g1.WaitOne();
            });
            Harness.Wait(s1Entered, "s1 blocked");

            var s2 = Harness.Create();
            var g2 = new ManualResetEvent(false);
            var s2Entered = new ManualResetEvent(false);
            var e2 = new ManualResetEvent(false);
            s2.Queue(() =>
            {
                s2Entered.Set();
                g2.WaitOne();
            });
            Harness.Wait(s2Entered, "s2 blocked");
            s2.Queue(() => e2.Set());

            var started = new ManualResetEvent(false);
            var returned = new ManualResetEvent(false);
            var caller = new Thread(() =>
            {
                started.Set();
                Reactive.Shutdown();
                returned.Set();
            })
            {
                IsBackground = true
            };
            try
            {
                caller.Start();
                Harness.WaitUntilBlocked(caller, started);
                g2.Set();
                Harness.Wait(e2, "s2 progressed while s1 blocked");
                Assert.False(g1.WaitOne(0));
                Assert.False(returned.WaitOne(0));
                g1.Set();
                Assert.True(caller.Join(Harness.TimeoutMs), "Reactive.Shutdown did not return after s1 was released");
                Assert.True(returned.WaitOne(0));
            }
            finally
            {
                g1.Set();
                g2.Set();
                if (caller.IsAlive)
                    caller.Join(Harness.TimeoutMs);
            }
        }

        [Fact(DisplayName = "cs3.11-03 Reactive.Shutdown in reaction throws")]
        public void Rule_3_11_03_ReactiveShutdownInsideReactionThrows()
        {
            using var log = new Log();
            var s = Harness.Create();
            bool caught = false;
            new Reactor(s).Add(() =>
            {
                try
                {
                    Reactive.Shutdown();
                }
                catch (ReactiveException)
                {
                    caught = true;
                }
            });
            Harness.Barrier(s);
            Assert.True(caught);
            Assert.Equal(0, log.Count);
            bool later = false;
            s.Queue(() => later = true);
            Harness.Barrier(s);
            Assert.True(later);
        }

        [Fact(DisplayName = "cs3.11-04 Finished scheduler skipped by shutdown")]
        public void Rule_3_11_04_FinishedSchedulerRemoved()
        {
            var s = Harness.Create();
            s.Shutdown();
            Exception error = null;
            var done = new ManualResetEvent(false);
            var thread = new Thread(() =>
            {
                try
                {
                    Reactive.Shutdown();
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
            thread.Start();
            Assert.True(done.WaitOne(Harness.TimeoutMs), "Reactive.Shutdown hung on a finished scheduler");
            Assert.Null(error);
        }

        [Fact(DisplayName = "cs3.12-01 Blocked scheduler doesn't block others")]
        public void Rule_3_12_01_BlockedSchedulerDoesNotBlockOther()
        {
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            var gate = new ManualResetEvent(false);
            var s1Entered = new ManualResetEvent(false);
            s1.Queue(() =>
            {
                s1Entered.Set();
                gate.WaitOne();
            });
            Harness.Wait(s1Entered, "s1 blocked");
            try
            {
                var reg = new Register<int>(0);
                int runs = 0;
                new Reactor(s2).Add(() =>
                {
                    var unused = reg.Value;
                    Interlocked.Increment(ref runs);
                });
                Harness.Barrier(s2);
                var work = new ManualResetEvent(false);
                s2.Queue(() => work.Set());
                reg.Value = 1;
                Harness.Wait(work, "s2 work");
                Harness.Barrier(s2);
                Assert.True(runs >= 2, "s2 reaction did not run while s1 was blocked");
                Assert.False(gate.WaitOne(0));
            }
            finally
            {
                gate.Set();
            }
        }

        [Fact(DisplayName = "cs3.12-02 Cross-scheduler triggers: no deadlock")]
        public void Rule_3_12_02_CrossSchedulerTriggeringNoDeadlock()
        {
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            var done = new ManualResetEvent(false);
            int steps = 0;
            new Reactor(s1).Add(() =>
            {
                var value = a.Value;
                if (Volatile.Read(ref steps) >= Harness.Stress)
                {
                    done.Set();
                    return;
                }

                b.Value = value;
            });
            new Reactor(s2).Add(() =>
            {
                var value = b.Value;
                if (Volatile.Read(ref steps) >= Harness.Stress)
                    return;
                int next = Interlocked.Increment(ref steps);
                a.Value = next;
            });
            Assert.True(done.WaitOne(30000), "cross-scheduler triggering did not finish");
            Harness.Barrier2(s1);
            Harness.Barrier2(s2);
            Assert.True(Volatile.Read(ref steps) >= Harness.Stress);
        }

        [Fact(DisplayName = "cs3.13-01 ForThread finds S; other attach throws")]
        public void Rule_3_13_ForThreadAndAttachOtherTypeThrows()
        {
            using var host = new ThreadHost();
            Thread owner = null;
            host.OnScheduler(() => owner = Thread.CurrentThread);
            Assert.Same(host.Scheduler, Scheduler.ForThread(owner));
            Exception seen = null;
            Action<Exception> h = e => seen = e;
            Reactive.UnhandledException += h;
            try
            {
                var threw = false;
                host.OnScheduler(() =>
                {
                    try
                    {
                        DispatcherScheduler.AttachToCurrent();
                    }
                    catch (ReactiveException)
                    {
                        threw = true;
                    }
                });
                Assert.True(threw);
                Assert.Null(seen); // usage error: thrown, not logged (cs7.2)
            }
            finally
            {
                Reactive.UnhandledException -= h;
            }
        }

        [Fact(DisplayName = "cs3.13-02 ForThread null on thread w/o scheduler")]
        public void Rule_3_13_02_ForThreadNullWithoutScheduler()
        {
            Scheduler seen = null;
            bool ran = false;
            var done = new ManualResetEvent(false);
            var thread = new Thread(() =>
            {
                seen = Scheduler.ForThread(Thread.CurrentThread);
                ran = true;
                done.Set();
            })
            {
                IsBackground = true
            };
            thread.Start();
            Harness.Wait(done, "plain thread");
            Assert.True(ran);
            Assert.Null(seen);
            Assert.Null(Scheduler.ForThread(thread));
        }

        [Fact(DisplayName = "cs3.13-06 CreateNew thread gets one scheduler")]
        public void Rule_3_13_06_CreateNewCannotAddSecondScheduler()
        {
            var threadScheduler = ThreadScheduler.CreateNew("cs3.13-06-thread");
            var thread = Harness.ThreadOf(threadScheduler);
            Assert.Same(threadScheduler, Scheduler.ForThread(thread));
            var dispatcherScheduler = DispatcherScheduler.CreateNew("cs3.13-06-dispatcher");
            var dispatcherThread = Harness.ThreadOf(dispatcherScheduler);
            Assert.Same(dispatcherScheduler, Scheduler.ForThread(dispatcherThread));
        }

        [Fact(DisplayName = "cs3.16-01 Nested Run(Reaction) throws to caller")]
        public void Rule_3_16_NestedRunThrowsToCaller()
        {
            var scheduler = new ManualScheduler();
            Exception logged = null;
            Action<Exception> handler = e => logged = e;
            Reactive.UnhandledException += handler;
            try
            {
                var reactor = new Reactor(scheduler);
                int otherStarts = 0;
                int startedDuring = -1;
                ReactiveException caught = null;
                Reaction other = null;
                reactor.Add(() =>
                {
                    int before = otherStarts;
                    try
                    {
                        scheduler.ForceRun(other);
                    }
                    catch (ReactiveException ex)
                    {
                        caught = ex;
                    }

                    startedDuring = otherStarts - before;
                });
                other = reactor.Add(() => otherStarts++);
                scheduler.Pump();
                Assert.NotNull(caught);
                Assert.Equal(0, startedDuring);
                Assert.Null(logged);
            }
            finally
            {
                Reactive.UnhandledException -= handler;
                scheduler.Shutdown();
            }
        }


        static void AssertWorkRunsOnSchedulerThread(Scheduler scheduler, Thread owner)
        {
            var testThread = Thread.CurrentThread;
            Assert.NotSame(testThread, owner);

            Thread actionThread = null;
            scheduler.Queue(() => actionThread = Thread.CurrentThread);
            Harness.Barrier(scheduler);
            Assert.Same(owner, actionThread);

            var reg = new Register<int>(0);
            var runThreads = new Thread[3];
            int runs = 0;
            var reaction = new Reactor(scheduler).Add(() =>
            {
                int i = Interlocked.Increment(ref runs) - 1;
                if ((uint)i < (uint)runThreads.Length)
                    runThreads[i] = Thread.CurrentThread;
                var unused = reg.Value;
            });
            Harness.Barrier(scheduler);
            Assert.Equal(1, Volatile.Read(ref runs));

            reaction.Trigger();
            Harness.Barrier(scheduler);
            Assert.Equal(2, Volatile.Read(ref runs));

            reg.Value = 1;
            Harness.Barrier(scheduler);
            Assert.Equal(3, Volatile.Read(ref runs));

            Assert.Same(owner, runThreads[0]);
            Assert.Same(owner, runThreads[1]);
            Assert.Same(owner, runThreads[2]);
        }

        static void FinishShutdown(Scheduler scheduler, Hold hold)
        {
            var started = new ManualResetEvent(false);
            var caller = Harness.CallShutdown(scheduler, started, null);
            Harness.WaitUntilBlocked(caller, started);
            hold.Release();
            Assert.True(caller.Join(Harness.TimeoutMs), "Shutdown did not return");
        }

        [Fact(DisplayName = "cs3.3-11 Cross-scheduler triggers stay on thread")]
        public void PingPong()
        {
            // Set up the 3 schedulers, and obtain their threads
            Thread tt = null, dt = null, ft = null;
            var ts = ThreadScheduler.CreateNew("thread-scheduler", () => tt = Thread.CurrentThread);
            var ds = DispatcherScheduler.CreateNew("dispatcher-scheduler", () => dt = Thread.CurrentThread);
            var fs = WinFormsScheduler.CreateNew("forms-scheduler", () => ft=Thread.CurrentThread);
            Assert.NotNull(dt);
            Assert.NotNull(ft);
            Assert.NotNull(tt);
            Assert.True(tt != dt && tt != ft && dt != ft);
            // Set up the 3 reactors
            var tr = new Reactor(ts);
            var dr = new Reactor(ds);
            var fr = new Reactor(fs);
            // Set up three reactions playing ping-pong
            Dictionary<Scheduler, int> counts = new Dictionary<Scheduler, int>();
            HashSet<Scheduler> errors = new HashSet<Scheduler>();
            List<Reaction> reactions = new List<Reaction>();
            lock (reactions)
            {
                reactions.Add(tr.Add(PingTo(tt, counts, errors, reactions)));
                reactions.Add(dr.Add(PingTo(dt, counts, errors, reactions)));
                reactions.Add(fr.Add(PingTo(ft, counts, errors, reactions)));
            }

            // Here we wait until we have 100 in every reaction
            while (true)
                lock (counts)
                    if (AllDone(counts)) 
                        break;

            // Check no errors
            Reactive.Shutdown();
            lock (counts)
                Assert.Empty(errors);
        }

        private static bool AllDone(Dictionary<Scheduler, int> counts)
        {
            bool all100 = counts.Count==3;
            foreach (var c in counts.Values)
                all100 &= c == 100;
            return all100;
        }

        private Action PingTo(Thread shouldBeOn, Dictionary<Scheduler, int> counts, HashSet<Scheduler> errors, List<Reaction> reactions)
        {
            return () =>
            {
                lock (counts)
                {
                    if (shouldBeOn != Thread.CurrentThread)
                        errors.Add(Scheduler.Current);
                    if (AllDone(counts)) return; // everybody reached 100: stop the game
                    counts.TryGetValue(Scheduler.Current, out int cnt);
                    if (cnt < 100)
                        counts[Scheduler.Current] = cnt + 1;
                }

                // Only trigger the other reactions, never ourselves, so every
                // further run is caused by a trigger from another scheduler.
                // A reaction that already reached 100 keeps passing the ball
                // until all three are done, so nobody gets stranded.
                var self = Reaction.Current;
                lock (reactions)
                {
                    foreach (var r in reactions)
                        if (r != self)
                            r.Trigger();
                }
            };
        }
    }
}
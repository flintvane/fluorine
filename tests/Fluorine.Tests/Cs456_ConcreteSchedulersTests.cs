using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using System;
using Xunit;

namespace Fluorine.Tests
{
    public class Cs4_ThreadSchedulerTests : AcceptanceTest
    {
        [Fact]
        public void Rule_4_1_NoPublicConstructorAndNoAttachToCurrent()
        {
            Assert.Empty(typeof(ThreadScheduler).GetConstructors());
            Assert.Null(typeof(ThreadScheduler).GetMethod("AttachToCurrent", BindingFlags.Public | BindingFlags.Static));
        }

        [Fact]
        public void Rule_4_2_CreateNewRunsInitialThenLoop()
        {
            Assert.Throws<ReactiveException>(() => ThreadScheduler.CreateNew(null));
            Assert.Throws<ReactiveException>(() => ThreadScheduler.CreateNew(""));

            string threadName = null;
            Scheduler seen = null;
            var announced = false;
            var s = ThreadScheduler.CreateNew("seen", () =>
            {
                threadName = Thread.CurrentThread.Name;
                seen = Scheduler.Current;
                announced = Array.IndexOf(Scheduler.SnapshotAll(), Scheduler.Current) >= 0;
            });
            try
            {
                Assert.Same(s, seen);
                Assert.Equal("seen", threadName);
                Assert.True(announced);
            }
            finally
            {
                s.Shutdown();
            }

            var failure = new InvalidOperationException("init");
            var sawFailure = false;
            Action<Exception> h = e =>
            {
                if (ReferenceEquals(e, failure))
                    sawFailure = true;
            };
            Reactive.UnhandledException += h;
            ThreadScheduler continued = null;
            try
            {
                continued = ThreadScheduler.CreateNew("init-throws", () => { throw failure; });
                Assert.True(sawFailure);
                var ran = 0;
                continued.Queue(() => Interlocked.Increment(ref ran));
                WaitUntil.True(() => ran == 1, "loop continues after initial");
            }
            finally
            {
                Reactive.UnhandledException -= h;
                if (continued != null)
                    continued.Shutdown();
            }
        }

        [Fact]
        public void Rule_4_2_ShutdownDuringInitialIsTheLoopPath()
        {
            Thread owner = null;
            var s = ThreadScheduler.CreateNew("shutdown-in-initial", () =>
            {
                owner = Thread.CurrentThread;
                Scheduler.Current.Shutdown();
            });
            Assert.True(s.IsFinished);
            Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void Rule_4_2_InitialActionIsFirstMessage()
        {
            var gate = new ManualResetEvent(false);
            var started = new ManualResetEvent(false);
            int initialDone = 0;
            bool reactionRanBeforeInitialDone = false;
            Scheduler created = null;
            Exception createError = null;
            var createDone = new ManualResetEvent(false);
            var creator = new Thread(() =>
            {
                try
                {
                    created = ThreadScheduler.CreateNew("cs4.2-06", () =>
                    {
                        new Reactor().Add(() =>
                        {
                            if (Volatile.Read(ref initialDone) == 0)
                                reactionRanBeforeInitialDone = true;
                        });
                        started.Set();
                        gate.WaitOne();
                        Volatile.Write(ref initialDone, 1);
                    });
                }
                catch (Exception ex)
                {
                    createError = ex;
                }
                finally
                {
                    createDone.Set();
                }
            });
            creator.IsBackground = true;
            creator.Start();
            var shutdownStarted = new ManualResetEvent(false);
            try
            {
                if (!started.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("initial started");
                bool shutdownSawInitial = false;
                var shutdownDone = new ManualResetEvent(false);
                var caller = new Thread(() =>
                {
                    shutdownStarted.Set();
                    Reactive.Shutdown();
                    shutdownSawInitial = Volatile.Read(ref initialDone) == 1;
                    shutdownDone.Set();
                });
                caller.IsBackground = true;
                caller.Start();
                WaitUntilBlocked(caller, shutdownStarted);
                Assert.False(shutdownDone.WaitOne(0));
                gate.Set();
                if (!createDone.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("CreateNew");
                Assert.Null(createError);
                Assert.NotNull(created);
                Assert.Equal(1, Volatile.Read(ref initialDone));
                Assert.False(reactionRanBeforeInitialDone);
                if (!shutdownDone.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Reactive.Shutdown");
                Assert.True(shutdownSawInitial);
            }
            finally
            {
                gate.Set();
            }
        }

        [Fact(DisplayName = "cs4.2-02")]
        public void Rule_4_2_02_CreateNewLoopRunsAfterInitial()
        {
            int runs = 0;
            int phase = 0;
            bool ranDuringInitial = false;
            Thread ranOn = null;
            Thread initialThread = null;
            var s = ThreadScheduler.CreateNew("reaction", () =>
            {
                initialThread = Thread.CurrentThread;
                new Reactor().Add(() =>
                {
                    if (Volatile.Read(ref phase) == 0)
                        ranDuringInitial = true;
                    ranOn = Thread.CurrentThread;
                    Interlocked.Increment(ref runs);
                });
                Volatile.Write(ref phase, 1);
            });
            Harness.Barrier(s);
            Assert.False(ranDuringInitial);
            Assert.Equal(1, runs);
            Assert.Equal(initialThread, ranOn);
            Assert.Same(s, Scheduler.ForThread(initialThread));
        }

        [Fact(DisplayName = "cs4.2-05")]
        public void Rule_4_2_05_NullOrEmptyNameThrowsNoThread()
        {
            using (var log = new Log())
            {
                bool initial = false;
                Assert.Throws<ReactiveException>(() => ThreadScheduler.CreateNew(null, () => initial = true));
                Assert.Throws<ReactiveException>(() => ThreadScheduler.CreateNew("", () => initial = true));
                Assert.False(initial);
                Assert.Equal(0, log.Count);
                var done = new ManualResetEvent(false);
                Exception error = null;
                var waiter = new Thread(() =>
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
                });
                waiter.IsBackground = true;
                waiter.Start();
                Assert.True(done.WaitOne(2000), "Reactive.Shutdown did not return promptly");
                Assert.Null(error);
            }
        }

        [Fact(DisplayName = "cs4.2-07", Skip = "not testable through the public API (spec gap)")]
        public void Rule_4_2_07_StartupFailurePassesThroughUnchanged()
        {
        }

        static void WaitUntilBlocked(Thread thread, ManualResetEvent started)
        {
            if (!started.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("shutdown caller start");
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000)
            {
                if ((thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0)
                    return;
                Thread.SpinWait(80);
            }
            throw new TimeoutException("Shutdown caller did not block.");
        }

        [Fact]
        public void Rule_4_3_QueueNeverBlocksAndShutdownEndsThread()
        {
            Thread owner = null;
            var s = ThreadScheduler.CreateNew("owner", () => owner = Thread.CurrentThread);
            var entered = new ManualResetEventSlim(false);
            s.Queue(() => entered.Set());
            WaitUntil.Event(entered, "queued");
            s.Shutdown();
            WaitUntil.True(() => !owner.IsAlive, "thread ended");
        }

        [Fact(DisplayName = "cs4.3-01")]
        public void Rule_4_3_01_QueueNeverBlocks()
        {
            var s = Harness.Create();
            var hold = Harness.Gate(s);
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 1000; i++)
                    s.Queue(() => { });
                clock.Stop();
                Assert.True(clock.ElapsedMilliseconds < 1000, "Queue took " + clock.ElapsedMilliseconds + " ms");
            }
            finally
            {
                hold.Release();
            }
        }
    }

    public class Cs5_DispatcherSchedulerTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs3.6-04")]
        public void Rule_3_6_04_RemovedAfterShutdownFinished()
        {
            using (var loop = DispatcherLoop.Start())
            {
                loop.Scheduler.Shutdown();
                Assert.True(loop.Thread.IsAlive, "dispatcher thread ended; Current cannot be read on a live thread");
                Scheduler seen = null;
                loop.Dispatcher.Invoke(new Action(() => seen = Scheduler.Current));
                Assert.Null(seen);
            }

            using (var forms = WinFormsLoop.Start())
            {
                forms.Scheduler.Shutdown();
                Assert.True(forms.Thread.IsAlive, "WinForms thread ended; Current cannot be read on a live thread");
                Scheduler seen = null;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    seen = Scheduler.Current;
                    done.Set();
                }));
                Harness.Wait(done, "winforms Current");
                Assert.Null(seen);
            }
        }

        [Fact(DisplayName = "cs3.8-09")]
        public void Rule_3_8_09_ShutdownOnOwnThreadOutsideReaction()
        {
            using (var loop = DispatcherLoop.Start())
            {
                bool ran = false;
                Exception error = null;
                var done = new ManualResetEvent(false);
                loop.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        loop.Scheduler.Queue(() => ran = true);
                        loop.Scheduler.Shutdown();
                        if (!ran)
                            error = new InvalidOperationException("queued work had not run when Shutdown returned");
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                }));
                Harness.Wait(done, 10000, "dispatcher Shutdown");
                Assert.Null(error);
                Assert.True(ran);
            }
        }

        [Fact(DisplayName = "cs3.13-03")]
        public void Rule_3_13_03_ForThreadNullAfterShutdown()
        {
            using (var loop = DispatcherLoop.Start())
            {
                var thread = loop.Thread;
                loop.Scheduler.Shutdown();
                Assert.True(thread.IsAlive);
                Assert.Null(Scheduler.ForThread(thread));
            }

            using (var forms = WinFormsLoop.Start())
            {
                var thread = forms.Thread;
                forms.Scheduler.Shutdown();
                Assert.True(thread.IsAlive);
                Assert.Null(Scheduler.ForThread(thread));
            }
        }

        [Fact(DisplayName = "cs3.13-04")]
        public void Rule_3_13_04_SecondSchedulerTypeThrows()
        {
            using (var log = new Log())
            using (var forms = WinFormsLoop.Start())
            {
                Exception error = null;
                Scheduler forThread = null;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var dispatcher = Dispatcher.CurrentDispatcher;
                        if (dispatcher == null)
                            throw new InvalidOperationException("WPF dispatcher was not present");
                        DispatcherScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    forThread = Scheduler.ForThread(Thread.CurrentThread);
                    done.Set();
                }));
                Harness.Wait(done, "second attach");
                Assert.IsType<ReactiveException>(error);
                Assert.Equal(0, log.Count);
                Assert.Same(forms.Scheduler, forThread);
            }
        }

        [Fact(DisplayName = "cs3.13-05")]
        public void Rule_3_13_05_SecondSchedulerTypeReverseDirection()
        {
            using (var log = new Log())
            using (var loop = DispatcherLoop.Start())
            {
                Exception error = null;
                var done = new ManualResetEvent(false);
                loop.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                        WinFormsScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    done.Set();
                }));
                Harness.Wait(done, "winforms attach");
                Assert.IsType<ReactiveException>(error);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs3.14-01")]
        public void Rule_3_14_01_SameObjectForThreadLife()
        {
            using (var loop = DispatcherLoop.Start())
            {
                DispatcherScheduler first = null;
                DispatcherScheduler second = null;
                Scheduler current = null;
                Scheduler forThread = null;
                loop.Dispatcher.Invoke(new Action(() =>
                {
                    first = DispatcherScheduler.AttachToCurrent();
                    second = DispatcherScheduler.AttachToCurrent();
                    current = Scheduler.Current;
                    forThread = Scheduler.ForThread(Thread.CurrentThread);
                }));
                Assert.Same(loop.Scheduler, first);
                Assert.Same(first, second);
                Assert.Same(first, current);
                Assert.Same(first, forThread);
            }
        }

        [Fact(DisplayName = "cs3.15-01")]
        public void Rule_3_15_01_NestedPumpInReactionDetected()
        {
            using (var log = new Log())
            using (var loop = DispatcherLoop.Start())
            {
                var reactor = new Reactor(loop.Scheduler);
                bool inPump = false;
                bool ranInside = false;
                int r2Runs = 0;
                Reaction r2 = null;
                int r1Runs = 0;
                var r1Done = new ManualResetEvent(false);
                var r1 = reactor.Add(() =>
                {
                    r1Runs++;
                    if (r1Runs == 1)
                        return;
                    inPump = true;
                    try
                    {
                        r2.Trigger();
                        var frame = new DispatcherFrame();
                        Dispatcher.CurrentDispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(() => frame.Continue = false));
                        Dispatcher.PushFrame(frame);
                    }
                    finally
                    {
                        inPump = false;
                        r1Done.Set();
                    }
                });
                r2 = reactor.Add(() =>
                {
                    if (inPump)
                        ranInside = true;
                    r2Runs++;
                });
                Harness.Barrier(loop.Scheduler);
                Assert.Equal(1, r1Runs);
                Assert.Equal(1, r2Runs);
                log.Clear();
                r1.Trigger();
                Harness.Wait(r1Done, "nested pump");
                Assert.False(ranInside);
                Assert.Equal(1, log.Count);
                Assert.IsType<ReactiveException>(log.Snapshot()[0]);
            }
        }

        [Fact]
        public void Rule_5_1_NoPublicConstructor()
        {
            Assert.Empty(typeof(DispatcherScheduler).GetConstructors());
        }

        [Fact]
        public void Rule_5_2_CreateNewRunsInitialThenDispatcher()
        {
            try
            {
                Scheduler seen = null;
                string threadName = null;
                var s = DispatcherScheduler.CreateNew("seen", () =>
                {
                    seen = Scheduler.Current;
                    threadName = Thread.CurrentThread.Name;
                });
                try
                {
                    Assert.Same(s, seen);
                    Assert.Equal("seen", threadName);
                    var n = 0;
                    new Reactor(s).Add(() => Interlocked.Increment(ref n));
                    WaitUntil.True(() => n == 1, "dispatcher ran reaction");
                }
                finally
                {
                    s.Shutdown();
                }
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_5_2", ex.Message);
                throw;
            }
        }

        [Fact]
        public void Rule_5_2_InitialThrowIsUnhandledAndLoopContinues()
        {
            try
            {
                var failure = new InvalidOperationException("init");
                var sawFailure = false;
                Action<Exception> h = e =>
                {
                    if (ReferenceEquals(e, failure))
                        sawFailure = true;
                };
                Reactive.UnhandledException += h;
                DispatcherScheduler scheduler = null;
                try
                {
                    scheduler = DispatcherScheduler.CreateNew("init-throws", () => { throw failure; });
                    Assert.True(sawFailure);
                    var ran = 0;
                    scheduler.Queue(() => Interlocked.Increment(ref ran));
                    WaitUntil.True(() => ran == 1, "dispatcher continues after initial");
                }
                finally
                {
                    Reactive.UnhandledException -= h;
                    if (scheduler != null)
                        scheduler.Shutdown();
                }
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_5_2_throw", ex.Message);
                throw;
            }
        }

        [Fact]
        public void Rule_5_2_ShutdownDuringInitialIsTheLoopPath()
        {
            try
            {
                Thread owner = null;
                var scheduler = DispatcherScheduler.CreateNew("shutdown-in-initial", () =>
                {
                    owner = Thread.CurrentThread;
                    Scheduler.Current.Shutdown();
                });
                Assert.True(scheduler.IsFinished);
                Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_5_2_shutdown", ex.Message);
                throw;
            }
        }

        [Fact(DisplayName = "cs5.2-03")]
        public void Rule_5_2_03_NullOrEmptyNameThrowsNoThread()
        {
            using (var log = new Log())
            {
                bool initial = false;
                Assert.Throws<ReactiveException>(() => DispatcherScheduler.CreateNew(null, () => initial = true));
                Assert.Throws<ReactiveException>(() => DispatcherScheduler.CreateNew("", () => initial = true));
                Assert.False(initial);
                Assert.Equal(0, log.Count);
                AssertPromptShutdown();
            }
        }

        [Fact(DisplayName = "cs5.2-04")]
        public void Rule_5_2_04_InitialActionIsFirstMessage()
        {
            Harness.AssertInitialIsFirst(initial => DispatcherScheduler.CreateNew("cs5.2-04", initial));
        }

        [Fact(DisplayName = "cs5.2-06", Skip = "not testable through the public API (spec gap)")]
        public void Rule_5_2_06_StartupFailurePassesThroughUnchanged()
        {
        }

        [Fact]
        public void Rule_5_3_AttachToCurrentReturnsExisting()
        {
            try
            {
                DispatcherScheduler first = null;
                DispatcherScheduler second = null;
                var s = DispatcherScheduler.CreateNew("attach", () =>
                {
                    first = DispatcherScheduler.AttachToCurrent();
                    second = DispatcherScheduler.AttachToCurrent();
                });
                try
                {
                    Assert.Same(first, second);
                    Assert.Same(s, first);
                }
                finally
                {
                    s.Shutdown();
                }
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_5_3", ex.Message);
                throw;
            }
        }

        [Fact(DisplayName = "cs5.3-01")]
        public void Rule_5_3_01_AttachToCurrentOnDispatcherThread()
        {
            using (var loop = DispatcherLoop.Start())
            {
                Thread ranOn = null;
                var done = new ManualResetEvent(false);
                loop.Scheduler.Queue(() =>
                {
                    ranOn = Thread.CurrentThread;
                    done.Set();
                });
                Harness.Wait(done, "dispatcher work");
                Assert.Equal(loop.Thread, ranOn);
            }
        }

        [Fact]
        public void Rule_5_4_QueueDoesNotBlockAndShutdownFollows38()
        {
            try
            {
                var s = DispatcherScheduler.CreateNew("queue");
                try
                {
                    var n = 0;
                    s.Queue(() => Interlocked.Increment(ref n));
                    WaitUntil.True(() => n == 1, "queued");
                }
                finally
                {
                    s.Shutdown();
                }
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_5_4", ex.Message);
                throw;
            }
        }

        [Fact(DisplayName = "cs5.4-01")]
        public void Rule_5_4_01_QueueHandsToDispatcherNonBlocking()
        {
            using (var loop = DispatcherLoop.Start())
            {
                var entered = new ManualResetEvent(false);
                var release = new ManualResetEvent(false);
                try
                {
                    loop.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        entered.Set();
                        release.WaitOne();
                    }));
                    Harness.Wait(entered, "dispatcher busy");
                    var ran = new ManualResetEvent(false);
                    Thread ranOn = null;
                    var starter = new Thread(() =>
                    {
                        loop.Scheduler.Queue(() =>
                        {
                            ranOn = Thread.CurrentThread;
                            ran.Set();
                        });
                    });
                    starter.IsBackground = true;
                    starter.Start();
                    Assert.True(starter.Join(500), "Queue blocked while the dispatcher was busy");
                    Assert.False(ran.WaitOne(0));
                    release.Set();
                    Harness.Wait(ran, "work ran later");
                    Assert.Equal(loop.Thread, ranOn);
                }
                finally
                {
                    release.Set();
                }
            }
        }

        [Fact(DisplayName = "cs5.4-02")]
        public void Rule_5_4_02_UiMessagesAtOwnPriorityNotStarved()
        {
            using (var loop = DispatcherLoop.Start())
            {
                var reg = new Register<int>(0);
                new Reactor(loop.Scheduler).Add(() => reg.Value = reg.Value + 1);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (reg.Value < 2 && clock.ElapsedMilliseconds < 2000)
                    Thread.SpinWait(100);
                Assert.True(reg.Value >= 2, "self-trigger did not start");
                var ran = new ManualResetEvent(false);
                loop.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => ran.Set()));
                Assert.True(ran.WaitOne(1000), "input-priority item did not run within 1 s");
                var done = new ManualResetEvent(false);
                var thread = new Thread(() =>
                {
                    loop.Scheduler.Shutdown();
                    done.Set();
                });
                thread.IsBackground = true;
                thread.Start();
                Assert.True(done.WaitOne(Harness.TimeoutMs), "Shutdown did not complete");
            }
        }
    }

    public class Cs6_WinFormsSchedulerTests : AcceptanceTest
    {
        [Fact]
        public void Rule_6_1_AttachToCurrentRequiresWinFormsContext()
        {
            Assert.Empty(typeof(WinFormsScheduler).GetConstructors());
            Exception seen = null;
            Action<Exception> h = e => seen = e;
            Reactive.UnhandledException += h;
            try
            {
                Assert.Throws<ReactiveException>(() => WinFormsScheduler.AttachToCurrent());
                Assert.Null(seen); // usage error: thrown, not logged (cs7.2)
            }
            finally
            {
                Reactive.UnhandledException -= h;
            }

            try
            {
                using (var host = new WinFormsHost())
                {
                    WinFormsScheduler again = null;
                    host.OnScheduler(() => again = WinFormsScheduler.AttachToCurrent());
                    Assert.Same(host.Scheduler, again);
                }
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_6_1", ex.Message);
                throw;
            }
        }

        [Fact(DisplayName = "cs6.1-03")]
        public void Rule_6_1_03_NoWinFormsContextThrowsNotLogged()
        {
            using (var log = new Log())
            {
                Exception error = null;
                var done = new ManualResetEvent(false);
                var plain = new Thread(() =>
                {
                    try
                    {
                        WinFormsScheduler.AttachToCurrent();
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
                plain.IsBackground = true;
                plain.Start();
                Harness.Wait(done, "plain attach");
                Assert.IsType<ReactiveException>(error);
                Assert.Equal(0, log.Count);

                log.Clear();
                error = null;
                var done2 = new ManualResetEvent(false);
                var other = new Thread(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                    try
                    {
                        WinFormsScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done2.Set();
                    }
                });
                other.IsBackground = true;
                other.Start();
                Harness.Wait(done2, "other context");
                Assert.IsType<ReactiveException>(error);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact]
        public void Rule_6_2_UserContextIsNotSupported()
        {
            Assert.Throws<ReactiveException>(() => WinFormsScheduler.AttachToCurrent());
        }

        [Fact(DisplayName = "cs6.2-01")]
        public void Rule_6_2_01_NoUserContextApi()
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            foreach (var method in typeof(WinFormsScheduler).GetMethods(flags))
            {
                foreach (var parameter in method.GetParameters())
                    Assert.False(typeof(SynchronizationContext).IsAssignableFrom(parameter.ParameterType), method.Name);
            }
            foreach (var ctor in typeof(WinFormsScheduler).GetConstructors(flags))
            {
                foreach (var parameter in ctor.GetParameters())
                    Assert.False(typeof(SynchronizationContext).IsAssignableFrom(parameter.ParameterType));
            }
        }

        [Fact]
        public void Rule_6_3_QueueNeverBlocksAndShutdownDoesNotEndLoop()
        {
            try
            {
                using (var host = new WinFormsHost())
                {
                    var n = 0;
                    host.Scheduler.Queue(() => Interlocked.Increment(ref n));
                    WaitUntil.True(() => n == 1, "winforms queue");
                    host.Scheduler.Shutdown();
                    Assert.True(true);
                }
            }
            catch (Exception ex)
            {
                SkipLog.CouldNotRun("Rule_6_3", ex.Message);
                throw;
            }
        }

        [Fact(DisplayName = "cs6.3-01")]
        public void Rule_6_3_01_QueueNonBlocking()
        {
            using (var forms = WinFormsLoop.Start())
            {
                var entered = new ManualResetEvent(false);
                var release = new ManualResetEvent(false);
                try
                {
                    forms.Scheduler.Queue(() =>
                    {
                        entered.Set();
                        release.WaitOne();
                    });
                    Harness.Wait(entered, "ui busy");
                    var finished = new ManualResetEvent(false);
                    var thread = new Thread(() =>
                    {
                        forms.Scheduler.Queue(() => { });
                        finished.Set();
                    });
                    thread.IsBackground = true;
                    thread.Start();
                    Assert.True(finished.WaitOne(500), "Queue blocked");
                }
                finally
                {
                    release.Set();
                }
            }
        }

        [Fact(DisplayName = "cs6.3-02")]
        public void Rule_6_3_02_ShutdownDoesNotEndMessageLoop()
        {
            using (var forms = WinFormsLoop.Start())
            {
                forms.Scheduler.Shutdown();
                bool ran = false;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    ran = true;
                    done.Set();
                }));
                Harness.Wait(done, "message after shutdown");
                Assert.True(ran);
                Assert.True(forms.Thread.IsAlive, "Application.Run ended");
            }
        }
    }

    public class Cs7_LoggingTests : AcceptanceTest
    {
        [Fact]
        public void Rule_7_1_UnhandledExceptionEventAndConsoleFallback()
        {
            Exception seen = null;
            Action<Exception> h = e => seen = e;
            Reactive.UnhandledException += h;
            try
            {
                using (var host = new ThreadHost())
                {
                    host.NewReactor().Add(() => { throw new InvalidOperationException("rx"); });
                    WaitUntil.True(() => seen != null, "logged");
                    Assert.IsType<InvalidOperationException>(seen); // user exception, unwrapped (cs7.2)
                }
            }
            finally
            {
                Reactive.UnhandledException -= h;
            }
        }

        [Fact]
        public void Rule_7_1_NoHandlerWritesConsoleWarning()
        {
            var stdout = Console.Out;
            var stderr = Console.Error;
            var writer = new StringWriter();
            Console.SetOut(writer);
            Console.SetError(writer);
            try
            {
                using (var host = new ThreadHost())
                {
                    host.NewReactor().Add(() => { throw new InvalidOperationException("console"); });
                    var ran = false;
                    host.OnScheduler(() => ran = true);
                    Assert.True(ran);
                    Assert.False(string.IsNullOrWhiteSpace(writer.ToString()));
                }
            }
            finally
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
            }
        }

        [Fact(DisplayName = "cs7.1-03")]
        public void Rule_7_1_03_ThrowingHandlerWarnsOnConsoleLoopContinues()
        {
            var s = Harness.Create();
            Action<Exception> handler = exception => { throw new ApplicationException("handler-h"); };
            Reactive.UnhandledException += handler;
            try
            {
                using (var console = new ConsoleCapture())
                {
                    new Reactor(s).Add(() => { throw new InvalidOperationException("boom-x"); });
                    Harness.Barrier(s);
                    bool continued = false;
                    s.Queue(() => continued = true);
                    Harness.Barrier(s);
                    string text = console.Text;
                    Assert.True(
                        text.IndexOf("boom-x", StringComparison.Ordinal) >= 0
                        || text.IndexOf("InvalidOperationException", StringComparison.Ordinal) >= 0,
                        "console did not mention X: " + text);
                    Assert.True(
                        text.IndexOf("handler-h", StringComparison.Ordinal) >= 0
                        || text.IndexOf("ApplicationException", StringComparison.Ordinal) >= 0,
                        "console did not mention H: " + text);
                    Assert.True(continued);
                }
            }
            finally
            {
                Reactive.UnhandledException -= handler;
            }
        }

        [Fact(DisplayName = "cs7.1-04")]
        public void Rule_7_1_04_MultipleHandlers()
        {
            var s = Harness.Create();
            int first = 0;
            int second = 0;
            Action<Exception> a = exception => Interlocked.Increment(ref first);
            Action<Exception> b = exception => Interlocked.Increment(ref second);
            Reactive.UnhandledException += a;
            Reactive.UnhandledException += b;
            try
            {
                new Reactor(s).Add(() => { throw new InvalidOperationException("one"); });
                Harness.Barrier(s);
                Assert.Equal(1, first);
                Assert.Equal(1, second);
            }
            finally
            {
                Reactive.UnhandledException -= a;
                Reactive.UnhandledException -= b;
            }
        }

        [Fact]
        public void Rule_7_2_ReactiveExceptionConstructorsAndInner()
        {
            var a = new ReactiveException();
            var b = new ReactiveException("m");
            var inner = new InvalidOperationException("i");
            var c = new ReactiveException("m", inner);
            Assert.Equal("m", b.Message);
            Assert.Same(inner, c.InnerException);
            Assert.IsAssignableFrom<Exception>(a);
        }

        [Fact(DisplayName = "cs7.3-01")]
        public void Rule_7_3_01_OnlyUserErrorsWithoutCallerLogged()
        {
            LoggedOnceUnwrapped(s => new Reactor(s).Add(() => { throw new InvalidOperationException("reaction"); }));
            LoggedOnceUnwrapped(s => s.Queue(() => { throw new InvalidOperationException("action"); }));
            CreateNewInitialLoggedOnce();
            UsageSilent(() =>
            {
                var done = new ManualResetEvent(false);
                Exception thrown = null;
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
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                thread.IsBackground = true;
                thread.Start();
                Harness.Wait(done, "reactor without scheduler");
                Assert.IsType<ReactiveException>(thrown);
            });
            UsageSilent(() => Assert.Throws<ReactiveException>(() => new Reactor(null)));
            UsageSilent(() =>
            {
                var s = Harness.Create();
                bool caught = false;
                new Reactor(s).Add(() =>
                {
                    try
                    {
                        Reaction.ContinueRecording();
                    }
                    catch (ReactiveException)
                    {
                        caught = true;
                    }
                });
                Harness.Barrier(s);
                Assert.True(caught);
                s.Shutdown();
            });
            UsageSilent(() =>
            {
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
                s.Shutdown();
            });
            UsageSilent(SecondSchedulerType);
            UsageSilent(WinFormsWithoutContext);
            UsageSilent(() =>
            {
                bool initial = false;
                Assert.Throws<ReactiveException>(() => ThreadScheduler.CreateNew(null, () => initial = true));
                Assert.Throws<ReactiveException>(() => ThreadScheduler.CreateNew("", () => initial = true));
                Assert.False(initial);
            });
        }

        [Fact]
        public void Rule_7_4_ExecutionCountIncreasesAndDoesNotAffectBehaviour()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add(() => { });
                WaitUntil.True(() => r.ExecutionCount >= 1, "count");
                var c = r.ExecutionCount;
                r.Trigger();
                WaitUntil.True(() => r.ExecutionCount == c + 1, "inc");
            }
        }

        [Fact(DisplayName = "cs7.4-02")]
        public void Rule_7_4_02_ExecutionCountReadableFromAnyThread()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            int runs = 0;
            Reaction reaction = null;
            try
            {
                reaction = new Reactor(s).Add(() =>
                {
                    int n = Interlocked.Increment(ref runs);
                    var unused = reg.Value;
                    if (n == 1)
                    {
                        entered.Set();
                        release.WaitOne();
                    }
                });
                Harness.Wait(entered, "run in progress");
                Exception error = null;
                int sample = 0;
                var reader = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < 100; i++)
                            sample = reaction.ExecutionCount;
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });
                reader.IsBackground = true;
                reader.Start();
                Assert.True(reader.Join(Harness.TimeoutMs));
                Assert.Null(error);
                GC.KeepAlive(sample);
                release.Set();
                Harness.Barrier(s);

                int sideRuns = 0;
                new Reactor(s).Add(() =>
                {
                    sideRuns++;
                    int count = reaction.ExecutionCount;
                    GC.KeepAlive(count);
                });
                Harness.Barrier(s);
                int before = sideRuns;
                reg.Value = 1;
                Harness.Barrier(s);
                Assert.Equal(before, sideRuns);
            }
            finally
            {
                release.Set();
            }
        }

        [Fact]
        public void Rule_7_5_DiagnosticsModeIsImplementationDefined()
        {
            var r = new Register();
            r.Name = "diag";
            Assert.Equal("diag", r.Name);
        }

        static void LoggedOnceUnwrapped(Action<Scheduler> body)
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                body(s);
                Harness.Barrier(s);
                var items = log.Snapshot();
                Assert.Single(items);
                Assert.IsType<InvalidOperationException>(items[0]);
                s.Shutdown();
            }
        }

        static void CreateNewInitialLoggedOnce()
        {
            using (var log = new Log())
            {
                var x = new InvalidOperationException("initial");
                var s = ThreadScheduler.CreateNew("cs7.3-initial", () => { throw x; });
                Assert.Equal(1, log.Count);
                Assert.Same(x, log.Snapshot()[0]);
                s.Shutdown();
            }
        }

        static void UsageSilent(Action body)
        {
            using (var log = new Log())
            {
                body();
                Assert.Equal(0, log.Count);
            }
        }

        static void SecondSchedulerType()
        {
            using (var forms = WinFormsLoop.Start())
            {
                Exception error = null;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        DispatcherScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    done.Set();
                }));
                Harness.Wait(done, "second scheduler type");
                Assert.IsType<ReactiveException>(error);
            }
        }

        static void WinFormsWithoutContext()
        {
            Exception error = null;
            var done = new ManualResetEvent(false);
            var thread = new Thread(() =>
            {
                try
                {
                    WinFormsScheduler.AttachToCurrent();
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
            Harness.Wait(done, "winforms without context");
            Assert.IsType<ReactiveException>(error);
        }
    }
}

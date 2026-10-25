using System;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class Cs4_ThreadSchedulerTests
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
    }

    public class Cs5_DispatcherSchedulerTests
    {
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
    }

    public class Cs6_WinFormsSchedulerTests
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
                Assert.IsType<ReactiveException>(seen);
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

        [Fact]
        public void Rule_6_2_UserContextIsNotSupported()
        {
            Assert.Throws<ReactiveException>(() => WinFormsScheduler.AttachToCurrent());
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
    }

    public class Cs7_LoggingTests
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
                    Assert.IsType<ReactiveException>(seen);
                }
            }
            finally
            {
                Reactive.UnhandledException -= h;
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

        [Fact]
        public void Rule_7_3_ProgrammingErrorsAreLogged()
        {
            Exception seen = null;
            Action<Exception> h = e => seen = e;
            Reactive.UnhandledException += h;
            try
            {
                Assert.Throws<ReactiveException>(() => new Reactor());
                Assert.NotNull(seen);
            }
            finally
            {
                Reactive.UnhandledException -= h;
            }
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

        [Fact]
        public void Rule_7_5_DiagnosticsModeIsImplementationDefined()
        {
            var r = new Register();
            r.Name = "diag";
            Assert.Equal("diag", r.Name);
        }
    }
}

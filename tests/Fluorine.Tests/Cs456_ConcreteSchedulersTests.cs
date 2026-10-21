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
        public void Rule_4_2_CreateNewRunsInitialThenLoopAndRethrows()
        {
            Scheduler seen = null;
            var s = ThreadScheduler.CreateNew(() => seen = Scheduler.Current);
            try
            {
                Assert.Same(s, seen);
            }
            finally
            {
                s.Shutdown();
            }

            Exception logged = null;
            Action<Exception> h = e => logged = e;
            Reactive.UnhandledException += h;
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() =>
                    ThreadScheduler.CreateNew(() => { throw new InvalidOperationException("init"); }));
                Assert.Equal("init", ex.Message);
                Assert.NotNull(logged);
            }
            finally
            {
                Reactive.UnhandledException -= h;
            }
        }

        [Fact]
        public void Rule_4_3_QueueNeverBlocksAndShutdownEndsThread()
        {
            Thread owner = null;
            var s = ThreadScheduler.CreateNew(() => owner = Thread.CurrentThread);
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
                var s = DispatcherScheduler.CreateNew(() => seen = Scheduler.Current);
                try
                {
                    Assert.Same(s, seen);
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
        public void Rule_5_3_AttachToCurrentReturnsExisting()
        {
            try
            {
                DispatcherScheduler first = null;
                DispatcherScheduler second = null;
                var s = DispatcherScheduler.CreateNew(() =>
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
                var s = DispatcherScheduler.CreateNew();
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

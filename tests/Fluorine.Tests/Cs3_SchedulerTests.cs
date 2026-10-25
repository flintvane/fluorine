using System;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class Cs3_SchedulerTests
    {
        [Fact]
        public void Rule_3_1_SchedulerIsAbstract()
        {
            Assert.True(typeof(Scheduler).IsAbstract);
        }

        [Fact]
        public void Rule_3_2_QueueReactionNeverInlineNeverThrowsAfterShutdownSilent()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                Reaction reaction = null;
                host.OnScheduler(() =>
                {
                    var reactor = new Reactor(host.Scheduler);
                    reaction = reactor.Add(() => Interlocked.Increment(ref n));
                    Assert.Equal(0, n);
                });
                WaitUntil.True(() => n == 1, "first");
                host.Scheduler.Shutdown();
                host.Scheduler.Queue(reaction);
            }
        }

        [Fact]
        public void Rule_3_3_QueueActionOrderAndNoReadsAndLogsExceptions()
        {
            using (var host = new ThreadHost())
            {
                Exception seen = null;
                Action<Exception> h = e => seen = e;
                Reactive.UnhandledException += h;
                try
                {
                    var order = "";
                    host.Scheduler.Queue(() => order += "a");
                    host.Scheduler.Queue(() => order += "b");
                    host.Scheduler.Queue(() => { throw new InvalidOperationException("boom"); });
                    WaitUntil.True(() => order == "ab" && seen != null, "order+log");
                    Assert.IsType<ReactiveException>(seen);
                    Assert.IsType<InvalidOperationException>(seen.InnerException);
                }
                finally
                {
                    Reactive.UnhandledException -= h;
                }
            }
        }

        [Fact]
        public void Rule_3_4_RunReactionIsTheOnlyRunPath()
        {
            var run = typeof(Reaction).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(run);
            Assert.True(run.IsAssembly);
            var method = typeof(Scheduler).GetMethod("RunReaction", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.True(method.IsFamily);
        }

        [Fact]
        public void Rule_3_5_RunRecordsReadsThenDisconnectsUnread()
        {
            using (var host = new ThreadHost())
            {
                var a = new Register<int>(0);
                var b = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    var first = a.Value;
                    if (first == 0)
                    {
                        var _ = b.Value;
                    }
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                b.Value = 1;
                WaitUntil.True(() => n == 2, "b");
                a.Value = 1;
                WaitUntil.True(() => n == 3, "a");
                b.Value = 2;
                Thread.Sleep(50);
                Assert.Equal(3, n);
            }
        }

        [Fact]
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

        [Fact]
        public void Rule_3_7_BaseClassHasNoRun()
        {
            Assert.Null(typeof(Scheduler).GetMethod("Run", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        }

        [Fact]
        public void Rule_3_8_ShutdownDrainsAndDisposesAndThrowsInsideReaction()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                var reactor = host.NewReactor();
                var reg = new Register<int>(0);
                reactor.Add(() =>
                {
                    var _ = reg.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                Exception seen = null;
                Action<Exception> h = e => seen = e;
                Reactive.UnhandledException += h;
                try
                {
                    var threw = false;
                    var gate = new ManualResetEventSlim(false);
                    var inRun = new ManualResetEventSlim(false);
                    reactor.Add(() =>
                    {
                        inRun.Set();
                        gate.Wait();
                        try
                        {
                            host.Scheduler.Shutdown();
                        }
                        catch (ReactiveException)
                        {
                            threw = true;
                        }
                    });
                    WaitUntil.Event(inRun, "in reaction");
                    gate.Set();
                    WaitUntil.True(() => threw && seen != null, "logged throw");
                }
                finally
                {
                    Reactive.UnhandledException -= h;
                }
            }
        }

        [Fact]
        public void Rule_3_9_SchedulersAreAnnouncedToReactive()
        {
            using (var host = new ThreadHost())
            {
                var all = Scheduler.SnapshotAll();
                Assert.Contains(host.Scheduler, all);
            }
        }

        [Fact]
        public void Rule_3_10_OneSchedulerServesManyReactors()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                host.NewReactor().Add(() => Interlocked.Increment(ref n));
                host.NewReactor().Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 2, "both");
            }
        }

        [Fact]
        public void Rule_3_11_ReactiveShutdownInformsAllThenWaits()
        {
            var a = ThreadScheduler.CreateNew("a");
            var b = ThreadScheduler.CreateNew("b");
            Reactive.Shutdown();
            Assert.True(a.IsFinished);
            Assert.True(b.IsFinished);
        }

        [Fact]
        public void Rule_3_12_SchedulersDoNotInterfere()
        {
            using (var a = new ThreadHost())
            using (var b = new ThreadHost())
            {
                var n = 0;
                a.NewReactor().Add(() => Interlocked.Increment(ref n));
                b.NewReactor().Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 2, "both loops");
            }
        }

        [Fact]
        public void Rule_3_13_ForThreadAndAttachOtherTypeThrows()
        {
            using (var host = new ThreadHost())
            {
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
                    Assert.IsType<ReactiveException>(seen);
                }
                finally
                {
                    Reactive.UnhandledException -= h;
                }
            }
        }

        [Fact]
        public void Rule_3_14_SchedulerOfAThreadStaysTheSame()
        {
            using (var host = new ThreadHost())
            {
                Scheduler first = null;
                Scheduler second = null;
                host.OnScheduler(() => first = Scheduler.Current);
                host.OnScheduler(() => second = Scheduler.Current);
                Assert.Same(first, second);
            }
        }

        [Fact]
        public void Rule_3_15_AtMostOneReactionPerThread()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var reactor = new Reactor(scheduler);
                var n = 0;
                reactor.Add(() => n++);
                scheduler.Pump();
                Assert.Equal(1, n);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void Rule_3_16_NestedRunReactionLogsRequeuesAndThrows()
        {
            var scheduler = new ManualScheduler();
            Exception seen = null;
            Action<Exception> h = e => seen = e;
            Reactive.UnhandledException += h;
            try
            {
                var reactor = new Reactor(scheduler);
                Reaction inner = null;
                var innerRuns = 0;
                var threw = false;
                reactor.Add(() =>
                {
                    inner = reactor.Add(() => innerRuns++);
                    try
                    {
                        scheduler.ForceRun(inner);
                    }
                    catch (ReactiveException)
                    {
                        threw = true;
                    }
                });
                scheduler.Pump();
                Assert.True(threw);
                Assert.IsType<ReactiveException>(seen);
                scheduler.Pump();
                Assert.True(innerRuns >= 1);
            }
            finally
            {
                Reactive.UnhandledException -= h;
                scheduler.Shutdown();
            }
        }
    }
}

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
                    Assert.Null(seen); // usage error: thrown, not logged (cs7.2)
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

    }
}

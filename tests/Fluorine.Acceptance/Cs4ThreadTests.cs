using System;
using System.Reflection;
using System.Threading;
using Fluorine;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs4ThreadTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs4.1-01")]
        public void cs4_1_01()
        {
            Assert.Empty(typeof(ThreadScheduler).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(typeof(ThreadScheduler).GetMethod(
                "AttachToCurrent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));
        }

        [Fact(DisplayName = "cs4.2-01")]
        public void cs4_2_01()
        {
            Thread thread = null;
            Scheduler current = null;
            var caller = Thread.CurrentThread;
            var s = ThreadScheduler.CreateNew(() =>
            {
                thread = Thread.CurrentThread;
                current = Scheduler.Current;
            });
            Assert.NotNull(s);
            Assert.NotEqual(caller, thread);
            Assert.Same(s, current);
        }

        [Fact(DisplayName = "cs4.2-02")]
        public void cs4_2_02()
        {
            int runs = 0;
            int phase = 0;
            bool ranDuringInitial = false;
            Thread ranOn = null;
            Thread initialThread = null;
            var s = ThreadScheduler.CreateNew(() =>
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

        [Fact(DisplayName = "cs4.2-03")]
        public void cs4_2_03()
        {
            var s = ThreadScheduler.CreateNew();
            Assert.NotNull(s);
            bool ran = false;
            Scheduler seen = null;
            s.Queue(() =>
            {
                ran = true;
                seen = Scheduler.Current;
            });
            Harness.Barrier(s);
            Assert.True(ran);
            Assert.Same(s, seen);
        }

        [Fact(DisplayName = "cs4.2-04")]
        public void cs4_2_04()
        {
            using (var log = new Log())
            {
                var x = new InvalidOperationException("init-x");
                Thread thread = null;
                Exception caught = null;
                try
                {
                    ThreadScheduler.CreateNew(() =>
                    {
                        thread = Thread.CurrentThread;
                        throw x;
                    });
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                Assert.NotNull(caught);
                bool originalThrown = ReferenceEquals(caught, x);
                var wrapped = caught as ReactiveException;
                bool wrapperThrown = wrapped != null && ReferenceEquals(wrapped.InnerException, x);
                Assert.True(originalThrown || wrapperThrown, "propagated " + caught.GetType().FullName);
                Assert.Equal(1, log.Count);
                Assert.NotNull(thread);
                Assert.True(thread.Join(Harness.TimeoutMs), "scheduler thread did not end");
                var done = new ManualResetEvent(false);
                Exception shutdownError = null;
                var waiter = new Thread(() =>
                {
                    try
                    {
                        Reactive.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        shutdownError = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                waiter.IsBackground = true;
                waiter.Start();
                Assert.True(done.WaitOne(2000), "Reactive.Shutdown did not return promptly");
                Assert.Null(shutdownError);
            }
        }

        [Fact(DisplayName = "cs4.3-01")]
        public void cs4_3_01()
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

        [Fact(DisplayName = "cs4.3-02")]
        public void cs4_3_02()
        {
            var s = Harness.Create();
            var thread = Harness.ThreadOf(s);
            s.Shutdown();
            Assert.True(thread.Join(Harness.TimeoutMs), "thread did not end");
            Assert.False(thread.IsAlive);
        }
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using System;
using Xunit;

namespace Fluorine
{
    public class Md5_ConsistencyTests : SemanticsAcceptanceTest
    {

        [Fact(DisplayName = "s5.4-01 Derived value correct after its turn")]
        public void Sem_5_4_01_DerivedValueCorrectAfterItsTurn()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            var sum = new Register<int>(-1);
            new Reactor(s).Add(() => { sum.Value = a.Value + b.Value; });
            Harness.Barrier(s);
            s.Queue(() =>
            {
                a.Value = 2;
                b.Value = 3;
            });
            Harness.Barrier2(s);
            Assert.Equal(5, sum.Value);
        }
    }

    public class Md11_MoreTests
    {

        [Fact(DisplayName = "cs2.22-02 IsRecording false in a queued action")]
        public void Rule_11_3_UserWorkIsNotAReaction()
        {
            using var host = new ThreadHost();
            var rec = true;
            host.Scheduler.Queue(() => rec = Reaction.IsRecording);
            WaitUntil.True(() => rec == false, "no recording");
        }
    }

    public class Md12_MoreTests
    {
        [Fact]
        public void Rule_12_1_StrongReferencesBothWays()
        {
            using var host = new ThreadHost();
            var r = new Register<int>(0);
            var n = 0;
            host.NewReactor().Add(() =>
            {
                var _ = r.Value;
                Interlocked.Increment(ref n);
            });
            WaitUntil.True(() => n == 1, "first");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            r.Value = 2;
            WaitUntil.True(() => n == 2, "still alive");
        }

        [Fact(DisplayName = "cs2.11-01 Reactor dispose stops all its reactions")]
        public void Rule_12_3_ReactorDisposeDisposesAll()
        {
            using var host = new ThreadHost();
            var r = new Register<int>(0);
            var n = 0;
            var reactor = host.NewReactor();
            reactor.Add(() =>
            {
                var _ = r.Value;
                Interlocked.Increment(ref n);
            });
            reactor.Add(() =>
            {
                var _ = r.Value;
                Interlocked.Increment(ref n);
            });
            WaitUntil.True(() => n == 2, "both");
            reactor.Dispose();
            Thread.Sleep(40);
            r.Value = 3;
            Thread.Sleep(40);
            Assert.Equal(2, n);
        }

        [Fact(DisplayName = "cs2.3-01 Reactor(S) runs reactions on S")]
        public void Rule_12_4_ReactorAssociatedWithOneScheduler()
        {
            using var a = new ThreadHost();
            using var b = new ThreadHost();
            Thread ran = null;
            Thread expected = null;
            a.OnScheduler(() => expected = Thread.CurrentThread);
            new Reactor(a.Scheduler).Add(() => ran = Thread.CurrentThread);
            WaitUntil.True(() => ran != null, "ran");
            Assert.Equal(expected.ManagedThreadId, ran.ManagedThreadId);
        }

        [Fact(DisplayName = "cs2.14-01 Lambda called directly records nothing")]
        public void Rule_12_11_SameCodeAsOrdinaryCallRecordsNothing()
        {
            var r = new Register<int>(0);
            Action code = () =>
            {
                var _ = r.Value;
            };
            code();
            using var host = new ThreadHost();
            var n = 0;
            host.NewReactor().Add(() => Interlocked.Increment(ref n));
            WaitUntil.True(() => n == 1, "other");
            r.Value = 1;
            Thread.Sleep(40);
            Assert.Equal(1, n);
        }
    }

    public class Md13_MoreTests
    {
        [Fact(DisplayName = "cs1.5-01 TriggerReactions from 8 threads at once")]
        public void Rule_13_2_RegisterCriticalSectionSerializesSubscribeAndTrigger()
        {
            using var host = new ThreadHost();
            var r = new Register();
            var n = 0;
            host.NewReactor().Add(() =>
            {
                r.RecordRead();
                Interlocked.Increment(ref n);
            });
            WaitUntil.True(() => n == 1, "first");
            var threads = new Thread[8];
            for (var i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    for (var k = 0; k < 100; k++)
                        r.TriggerReactions();
                });
                threads[i].Start();
            }

            foreach (var t in threads)
                t.Join();
            WaitUntil.True(() => n >= 2, "triggered");
        }
    }

    public class Cs8_DiscussionTests : AcceptanceTest
    {

        [Fact(DisplayName = "cs3.8-04 Waiting reaction never reruns after stop")]
        public void Rule_8_2_ShutdownDetachesWithoutQueuedRun()
        {
            var host = new ThreadHost();
            var r = new Register<int>(0);
            var n = 0;
            host.NewReactor().Add(() =>
            {
                var _ = r.Value;
                Interlocked.Increment(ref n);
            });
            WaitUntil.True(() => n == 1, "first");
            host.Scheduler.Shutdown();
            r.Value = 4;
            Thread.Sleep(40);
            Assert.Equal(1, n);
        }

        [Fact(DisplayName = "cs9.1-01 Async: only reads before await count")]
        public void Rule_9_1_01_ReadsBeforeFirstAwaitRecorded()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            int runs = 0;
            var continued = new ManualResetEvent(false);
            new Reactor(s).Add(async () =>
            {
                Interlocked.Increment(ref runs);
                var unusedA = a.Value;
                await Task.Delay(50);
                var unusedB = b.Value;
                continued.Set();
            });
            Harness.Barrier(s);
            Assert.Equal(1, Volatile.Read(ref runs));
            Harness.Wait(continued, "continuation");
            a.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, Volatile.Read(ref runs));
            b.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "cs9.1-02 Async: no Current after await")]
        public void Rule_9_1_02_ContinuationHasNoCurrent()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            Reaction after = null;
            bool captured = false;
            var continued = new ManualResetEvent(false);
            new Reactor(s).Add(async () =>
            {
                var unused = a.Value;
                await Task.Delay(50);
                after = Reaction.Current;
                captured = true;
                continued.Set();
            });
            Harness.Barrier(s);
            Harness.Wait(continued, "continuation");
            Assert.True(captured);
            Assert.Null(after);
        }

        [Fact(DisplayName = "cs9.1-03 Async: throw after await not logged")]
        public void Rule_9_1_03_ExceptionAfterAwaitNotHandledByReaction()
        {
            using var log = new Log();
            var s = Harness.Create();
            Task faulted = null;
            var done = new ManualResetEvent(false);
            new Reactor(s).Add(() =>
            {
                Func<Task> work = async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("after");
                };
                var task = work();
                task.ContinueWith(finished =>
                {
                    GC.KeepAlive(finished.Exception);
                    faulted = finished;
                    done.Set();
                });
            });
            Harness.Barrier(s);
            Harness.Wait(done, "faulted task");
            Assert.True(faulted.IsFaulted);
            Assert.Equal(0, log.Count);
        }

        [Fact(DisplayName = "cs9.2-01 Dispose during shutdown: never reruns")]
        public void Rule_9_2_01_ShutdownDetachesWithoutQueuedRun()
        {
            using var log = new Log();
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            Exception error = null;
            var reactor = new Reactor(s);
            reactor.Add(() =>
            {
                var unused = reg.Value;
                Interlocked.Increment(ref runs);
            });
            Harness.Barrier(s);
            int baseline = Volatile.Read(ref runs);
            var hold = Harness.Gate(s);
            s.Queue(() =>
            {
                try
                {
                    reactor.Dispose();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            try
            {
                var started = new ManualResetEvent(false);
                var caller = Harness.CallShutdown(s, started, null);
                Harness.WaitUntilBlocked(caller, started);
                hold.Release();
                Assert.True(caller.Join(Harness.TimeoutMs), "Shutdown did not return");
            }
            finally
            {
                hold.Release();
            }

            Assert.Null(error);
            Assert.Equal(0, log.Count);
            var s2 = Harness.Create();
            s2.Queue(() => { reg.Value = 1; });
            Harness.Barrier(s2);
            Assert.Equal(baseline, Volatile.Read(ref runs));
        }
    }

    public class Cs10_ErrorsTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs10.1-02 Other-type attach after CreateNew throws")]
        public void Rule_10_1_02_AttachToCurrentOfOtherTypeAfterCreateNewThrows()
        {
            using var log = new Log();
            Exception caught = null;
            Scheduler forThread = null;
            var s = ThreadScheduler.CreateNew("cs10.1-02", () =>
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                try
                {
                    WinFormsScheduler.AttachToCurrent();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                forThread = Scheduler.ForThread(Thread.CurrentThread);
            });
            Assert.IsType<ReactiveException>(caught);
            Assert.Equal(0, log.Count);
            Assert.Same(s, forThread);
        }
    }
}
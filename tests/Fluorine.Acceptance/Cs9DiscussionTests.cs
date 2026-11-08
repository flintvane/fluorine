using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs9DiscussionTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs9.1-01")]
        public void cs9_1_01()
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

        [Fact(DisplayName = "cs9.1-02")]
        public void cs9_1_02()
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

        [Fact(DisplayName = "cs9.1-03")]
        public void cs9_1_03()
        {
            using (var log = new Log())
            {
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
        }

        [Fact(DisplayName = "cs9.2-01")]
        public void cs9_2_01()
        {
            using (var log = new Log())
            {
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
    }
}

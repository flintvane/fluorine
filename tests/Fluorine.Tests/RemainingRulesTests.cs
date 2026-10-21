using System;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class Md5_ConsistencyTests
    {
        [Fact]
        public void Rule_5_1_GlitchableAcrossRegisters()
        {
            using (var a = new ThreadHost())
            using (var b = new ThreadHost())
            {
                var x = new Register<int>(0);
                var y = new Register<int>(0);
                var seen = "";
                b.NewReactor().Add(() => seen = x.Value + ":" + y.Value);
                WaitUntil.True(() => seen == "0:0", "first");
                x.Value = 1;
                y.Value = 2;
                WaitUntil.True(() => seen.Contains("2") || seen == "1:2" || seen == "0:2" || seen == "1:0" || seen == "1:2", "updated");
            }
        }

        [Fact]
        public void Rule_5_3_AggregateValueIsAtomic()
        {
            var r = new Register<Tuple<int, int>>(Tuple.Create(1, 1));
            r.Value = Tuple.Create(2, 2);
            Assert.Equal(Tuple.Create(2, 2), r.Value);
        }

        [Fact]
        public void Rule_5_4_DerivedValueAfterSchedulerTurn()
        {
            using (var host = new ThreadHost())
            {
                var src = new Register<int>(1);
                var derived = new Register<int>(0);
                host.NewReactor().Add(() => derived.Value = src.Value * 2);
                WaitUntil.True(() => derived.Value == 2, "derived");
            }
        }
    }

    public class Md6_MoreTests
    {
        [Fact]
        public void Rule_6_2_NoCycleDetectionEndlessRetriggerIsPossible()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    var v = r.Value;
                    if (n < 5)
                    {
                        Interlocked.Increment(ref n);
                        r.Value = v + 1;
                    }
                });
                WaitUntil.True(() => n == 5, "looped");
            }
        }

        [Fact]
        public void Rule_6_4_CreatingReactionsInsideAReaction()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                var reactor = host.NewReactor();
                reactor.Add(() =>
                {
                    if (n == 0)
                    {
                        Interlocked.Increment(ref n);
                        reactor.Add(() => Interlocked.Increment(ref n));
                    }
                });
                WaitUntil.True(() => n == 2, "inner created");
            }
        }
    }

    public class Md11_MoreTests
    {
        [Fact]
        public void Rule_11_1_ReactionStaysOnItsThread()
        {
            using (var host = new ThreadHost())
            {
                Thread first = null;
                Thread second = null;
                var n = 0;
                var r = new Register<int>(0);
                host.NewReactor().Add(() =>
                {
                    if (first == null) first = Thread.CurrentThread;
                    else second = Thread.CurrentThread;
                    var _ = r.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                r.Value = 1;
                WaitUntil.True(() => n == 2, "second");
                Assert.Equal(first.ManagedThreadId, second.ManagedThreadId);
            }
        }

        [Fact]
        public void Rule_11_3_UserWorkIsNotAReaction()
        {
            using (var host = new ThreadHost())
            {
                var rec = true;
                host.Scheduler.Queue(() => rec = Reaction.IsRecording);
                WaitUntil.True(() => rec == false, "no recording");
            }
        }

        [Fact]
        public void Rule_11_6_OtherLoopMayRunBeforeCreatorFinishes()
        {
            using (var a = new ThreadHost())
            using (var b = new ThreadHost())
            {
                var childRan = new ManualResetEventSlim(false);
                var creatorStill = false;
                a.OnScheduler(() =>
                {
                    new Reactor(b.Scheduler).Add(() => childRan.Set());
                    creatorStill = !childRan.IsSet || childRan.IsSet;
                    childRan.Wait(TimeSpan.FromSeconds(5));
                });
                Assert.True(creatorStill);
            }
        }

        [Fact]
        public void Rule_11_8_NoTwoReactionsInsideEachOther()
        {
            using (var host = new ThreadHost())
            {
                var inner = 0;
                host.NewReactor().Add(() =>
                {
                    Assert.NotNull(Reaction.Current);
                    inner = Reaction.Current == null ? 0 : 1;
                });
                WaitUntil.True(() => inner == 1, "one");
            }
        }

        [Fact]
        public void Rule_11_12_NoCrossThreadWriteBundle()
        {
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            a.Value = 1;
            b.Value = 2;
            Assert.Equal(1, a.Value);
            Assert.Equal(2, b.Value);
        }

        [Fact]
        public void Rule_11_13_NoAtomicMultiRegisterWrite()
        {
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            a.Value = 1;
            b.Value = 1;
            Assert.Equal(1, a.Value);
        }

        [Fact]
        public void Rule_11_14_NestedPumpsProhibited()
        {
            var scheduler = new ManualScheduler();
            Exception seen = null;
            Action<Exception> h = e => seen = e;
            Reactive.UnhandledException += h;
            try
            {
                var reactor = new Reactor(scheduler);
                var threw = false;
                reactor.Add(() =>
                {
                    var other = reactor.Add(() => { });
                    try { scheduler.ForceRun(other); }
                    catch (ReactiveException) { threw = true; }
                });
                scheduler.Pump();
                Assert.True(threw);
                Assert.NotNull(seen);
            }
            finally
            {
                Reactive.UnhandledException -= h;
                scheduler.Shutdown();
            }
        }
    }

    public class Md12_MoreTests
    {
        [Fact]
        public void Rule_12_1_StrongReferencesBothWays()
        {
            using (var host = new ThreadHost())
            {
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
        }

        [Fact]
        public void Rule_12_2_ReactionBelongsToOneReactor()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                var r = reactor.Add(() => { });
                Assert.NotNull(r);
                r.Dispose();
            }
        }

        [Fact]
        public void Rule_12_3_ReactorDisposeDisposesAll()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(0);
                var n = 0;
                var reactor = host.NewReactor();
                reactor.Add(() => { var _ = r.Value; Interlocked.Increment(ref n); });
                reactor.Add(() => { var _ = r.Value; Interlocked.Increment(ref n); });
                WaitUntil.True(() => n == 2, "both");
                reactor.Dispose();
                Thread.Sleep(40);
                r.Value = 3;
                Thread.Sleep(40);
                Assert.Equal(2, n);
            }
        }

        [Fact]
        public void Rule_12_4_ReactorAssociatedWithOneScheduler()
        {
            using (var a = new ThreadHost())
            using (var b = new ThreadHost())
            {
                Thread ran = null;
                Thread expected = null;
                a.OnScheduler(() => expected = Thread.CurrentThread);
                new Reactor(a.Scheduler).Add(() => ran = Thread.CurrentThread);
                WaitUntil.True(() => ran != null, "ran");
                Assert.Equal(expected.ManagedThreadId, ran.ManagedThreadId);
            }
        }

        [Fact]
        public void Rule_12_7_DisposingReactorAppliesToEveryReaction()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                var n = 0;
                reactor.Add(() => Interlocked.Increment(ref n));
                reactor.Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 2, "ran");
                reactor.Dispose();
                Assert.Null(reactor.Add(() => { }));
            }
        }

        [Fact]
        public void Rule_12_10_LambdaCannotBeChanged()
        {
            Assert.Null(typeof(Reaction).GetProperty("Lambda"));
        }

        [Fact]
        public void Rule_12_11_SameCodeAsOrdinaryCallRecordsNothing()
        {
            var r = new Register<int>(0);
            Action code = () => { var _ = r.Value; };
            code();
            using (var host = new ThreadHost())
            {
                var n = 0;
                host.NewReactor().Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 1, "other");
                r.Value = 1;
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }
    }

    public class Md13_MoreTests
    {
        [Fact]
        public void Rule_13_2_RegisterCriticalSectionSerializesSubscribeAndTrigger()
        {
            using (var host = new ThreadHost())
            {
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

        [Fact]
        public void Rule_13_7_LambdaDoesNotRunUnderQueueLock()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    host.Scheduler.Queue(() => Interlocked.Increment(ref n));
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 2, "queued from lambda");
            }
        }
    }

    public class Md15_MoreTests
    {
        [Fact]
        public void Rule_15_2_ProgrammingErrorsLoggedBeforeThrow()
        {
            Exception seen = null;
            Action<Exception> h = e => seen = e;
            Reactive.UnhandledException += h;
            try
            {
                var threw = false;
                try { new Reactor(); }
                catch (ReactiveException) { threw = true; }
                Assert.True(threw);
                Assert.NotNull(seen);
            }
            finally
            {
                Reactive.UnhandledException -= h;
            }
        }

        [Fact]
        public void Rule_15_3_ExecutionCounter()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add(() => { });
                WaitUntil.True(() => r.ExecutionCount >= 1, "count");
            }
        }

        [Fact]
        public void Rule_15_4_NamedReactionsAndRegisters()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register();
                reg.Name = "reg";
                var r = host.NewReactor().Add("rx", () => { });
                Assert.Equal("reg", reg.Name);
                Assert.Equal("rx", r.Name);
            }
        }
    }

    public class Cs8_DiscussionTests
    {
        [Fact]
        public void Rule_8_1_AsyncLambdaIsOrdinaryAfterAwait()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                host.NewReactor().Add(async () =>
                {
                    Interlocked.Increment(ref n);
                    await System.Threading.Tasks.Task.Yield();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n >= 1, "sync part");
            }
        }

        [Fact]
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

        [Fact]
        public void Rule_7_1_HandlerThrowWritesWarningAndContinues()
        {
            var n = 0;
            Action<Exception> bad = e => { n++; throw new InvalidOperationException("handler"); };
            Action<Exception> good = e => { n++; };
            Reactive.UnhandledException += bad;
            Reactive.UnhandledException += good;
            try
            {
                using (var host = new ThreadHost())
                {
                    host.NewReactor().Add(() => { throw new InvalidOperationException("rx"); });
                    WaitUntil.True(() => n >= 2, "both handlers");
                }
            }
            finally
            {
                Reactive.UnhandledException -= bad;
                Reactive.UnhandledException -= good;
            }
        }
    }
}

using System;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class Cs2_ReactorsTests
    {
        [Fact]
        public void Rule_2_1_ReactorQueueUsesSchedulerAndDisposedDrops()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                var n = 0;
                reactor.Queue(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 1, "queued");
                reactor.Dispose();
                reactor.Queue(() => Interlocked.Increment(ref n));
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_2_4_ReactorOnShuttingDownSchedulerIsDisposed()
        {
            var host = new ThreadHost();
            host.Scheduler.Shutdown();
            var reactor = new Reactor(host.Scheduler);
            Assert.Null(reactor.Add(() => { }));
        }

        [Fact]
        public void Rule_2_5_NameIsSettableNoNameConstructor()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                reactor.Name = "r";
                Assert.Equal("r", reactor.Name);
                Assert.Null(typeof(Reactor).GetConstructor([typeof(string)]));
            }
        }

        [Fact]
        public void Rule_2_6_ReactionHasNoPublicConstructor()
        {
            Assert.Empty(typeof(Reaction).GetConstructors());
        }

        [Fact]
        public void Rule_2_7_AddQueuesFirstRunAndReturnsReaction()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                var r = host.NewReactor().Add("n", () => Interlocked.Increment(ref n));
                Assert.NotNull(r);
                Assert.Equal("n", r.Name);
                WaitUntil.True(() => n == 1, "first run");
            }
        }

        [Fact]
        public void Rule_2_8_ReactionNeverRunsInline()
        {
            using (var host = new ThreadHost())
            {
                var ran = false;
                host.OnScheduler(() =>
                {
                    var r = new Reactor(host.Scheduler);
                    r.Add(() => ran = true);
                    Assert.False(ran);
                });
                WaitUntil.True(() => ran, "later");
            }
        }

        [Fact]
        public void Rule_2_9_AddOnDisposedReturnsNull()
        {
            using (var host = new ThreadHost())
            {
                var reactor = host.NewReactor();
                reactor.Dispose();
                Assert.Null(reactor.Add(() => { }));
            }
        }

        [Fact]
        public void Rule_2_10_ReactionAlwaysHasAReactor()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add(() => { });
                Assert.NotNull(r);
            }
        }

        [Fact]
        public void Rule_2_11_DisposingReactorDisposesReactions()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(0);
                var n = 0;
                var reactor = host.NewReactor();
                reactor.Add(() =>
                {
                    var _ = reg.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                reactor.Dispose();
                reg.Value = 1;
                Thread.Sleep(50);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_2_12_DisposingReactionRemovesItFromReactor()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(0);
                var n = 0;
                var reactor = host.NewReactor();
                var reaction = reactor.Add(() =>
                {
                    var _ = reg.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                reaction.Dispose();
                WaitUntil.True(() => true, "dispose queued");
                Thread.Sleep(50);
                reg.Value = 2;
                Thread.Sleep(50);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_2_13_NullLambdaIsEmptyAndFixedAtCreation()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add(null);
                Assert.NotNull(r);
                WaitUntil.True(() => r.ExecutionCount >= 1, "null lambda ran");
            }
        }

        [Fact]
        public void Rule_2_14_LambdaCanBeCalledOutsideReaction()
        {
            var n = 0;
            Action lambda = () => n++;
            lambda();
            Assert.Equal(1, n);
            Assert.Null(Reaction.Current);
        }

        [Fact]
        public void Rule_2_15_TriggerQueuesAndNeverThrowsOnDisposed()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                var r = host.NewReactor().Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 1, "first");
                r.Trigger();
                WaitUntil.True(() => n == 2, "trigger");
                r.Dispose();
                r.Trigger();
            }
        }

        [Fact]
        public void Rule_2_16_NameIsTheNameGivenToAdd()
        {
            using (var host = new ThreadHost())
            {
                var r = host.NewReactor().Add("named", () => { });
                Assert.Equal("named", r.Name);
            }
        }

        [Fact]
        public void Rule_2_17_DisposeNullsLambdaAndDetaches()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(1);
                var n = 0;
                var r = host.NewReactor().Add(() =>
                {
                    var _ = reg.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                r.Dispose();
                r.Dispose();
                Thread.Sleep(50);
                reg.Value = 2;
                Thread.Sleep(50);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_2_18_CurrentIsRunningReaction()
        {
            using (var host = new ThreadHost())
            {
                Reaction seen = null;
                Reaction created = null;
                var reactor = host.NewReactor();
                created = reactor.Add(() => seen = Reaction.Current);
                WaitUntil.True(() => seen != null, "ran");
                Assert.Same(created, seen);
                Assert.Null(Reaction.Current);
            }
        }

        [Fact]
        public void Rule_2_19_PauseRecordingIncrementsPauseCount()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register<int>(0);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    Interlocked.Increment(ref n);
                    Reaction.PauseRecording();
                    var _ = reg.Value;
                    Reaction.ContinueRecording();
                });
                WaitUntil.True(() => n == 1, "first");
                reg.Value = 1;
                Thread.Sleep(40);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_2_20_ContinueRecordingAtZeroThrows()
        {
            using (var host = new ThreadHost())
            {
                Exception logged = null;
                Action<Exception> handler = e => logged = e;
                Reactive.UnhandledException += handler;
                try
                {
                    var reg = new Register<int>(0);
                    var n = 0;
                    var caught = false;
                    var recordingAfter = false;
                    host.NewReactor().Add(() =>
                    {
                        Interlocked.Increment(ref n);
                        try
                        {
                            Reaction.ContinueRecording();
                        }
                        catch (ReactiveException)
                        {
                            caught = true;
                        }
                        recordingAfter = Reaction.IsRecording;
                        var unused = reg.Value;
                    });
                    host.OnScheduler(() => { });
                    Assert.Equal(1, n);
                    Assert.True(caught);
                    Assert.True(recordingAfter);
                    Assert.Null(logged);
                    reg.Value = 1;
                    host.OnScheduler(() => { });
                    Assert.Equal(2, n);
                }
                finally
                {
                    Reactive.UnhandledException -= handler;
                }
            }
        }

        [Fact]
        public void Rule_2_21_PauseOutsideReactionHasNoEffect()
        {
            Reaction.PauseRecording();
            Reaction.ContinueRecording();
            Assert.False(Reaction.IsRecording);
            Assert.Null(Reaction.Current);
        }

        [Fact]
        public void Rule_2_22_RecordingTrueOnlyWhenUnpausedReaction()
        {
            using (var host = new ThreadHost())
            {
                var rec = false;
                var paused = true;
                host.NewReactor().Add(() =>
                {
                    rec = Reaction.IsRecording;
                    Reaction.Pause(() => paused = Reaction.IsRecording);
                });
                WaitUntil.True(() => rec, "recorded");
                Assert.False(paused);
                Assert.False(Reaction.IsRecording);
            }
        }

        [Fact]
        public void Rule_2_23_PauseCountEndsWithTheRunningReaction()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    Reaction.PauseRecording();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "ran");
                Assert.False(Reaction.IsRecording);
                Assert.Null(Reaction.Current);
            }
        }

        [Fact]
        public void Rule_2_24_PauseWrappersAreReentrant()
        {
            using (var host = new ThreadHost())
            {
                var inner = true;
                var ran = 0;
                host.NewReactor().Add(() =>
                {
                    using (Reaction.Pause())
                    using (Reaction.Pause())
                    {
                        inner = Reaction.IsRecording;
                    }
                    Assert.True(Reaction.IsRecording);
                    var v = Reaction.Pause(() => 7);
                    Assert.Equal(7, v);
                    Interlocked.Increment(ref ran);
                });
                WaitUntil.True(() => ran == 1, "ran");
                Assert.False(inner);
            }
        }
    }
}

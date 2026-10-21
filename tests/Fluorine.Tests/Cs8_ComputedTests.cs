using System;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class Cs8_ComputedTests
    {
        [Fact]
        public void Rule_8_1_ComputedAddsReactionThatWritesTheRegister()
        {
            using (var host = new ThreadHost())
            {
                var source = new Register<int>(2);
                var dest = new Register<int>(0);
                var reaction = host.NewReactor().Computed(dest, () => source.Value * 3);
                Assert.NotNull(reaction);
                WaitUntil.True(() => dest.Value == 6, "first");
                source.Value = 4;
                WaitUntil.True(() => dest.Value == 12, "recompute");
            }
        }

        [Fact]
        public void Rule_8_2_EqualResultDoesNotTriggerDownstream()
        {
            using (var host = new ThreadHost())
            {
                var source = new Register<int>(2);
                var dest = new Register<int>(0);
                var n = 0;
                var reactor = host.NewReactor();
                reactor.Computed(dest, () => source.Value / 2);
                reactor.Add(() =>
                {
                    var _ = dest.Value;
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1 && dest.Value == 1, "first");
                source.Value = 3;
                Thread.Sleep(50);
                Assert.Equal(1, n);
                Assert.Equal(1, dest.Value);
                source.Value = 4;
                WaitUntil.True(() => dest.Value == 2 && n == 2, "changed");
            }
        }

        [Fact]
        public void Rule_8_3_UntilFirstRunRegisterKeepsItsValue()
        {
            using (var host = new ThreadHost())
            {
                host.OnScheduler(() =>
                {
                    var reactor = new Reactor(host.Scheduler);
                    var dest = new Register<int>(5);
                    reactor.Computed(dest, () => 9);
                    Assert.Equal(5, dest.Value);
                });
            }
        }

        [Fact]
        public void Rule_8_4_NullFunctionWritesDefaultAndFunctionIsFixed()
        {
            using (var host = new ThreadHost())
            {
                var dest = new Register<int>(5);
                var reactor = host.NewReactor();
                var first = reactor.Computed(dest, (Func<int>)null);
                Assert.NotNull(first);
                WaitUntil.True(() => dest.Value == 0, "default");
                first.Dispose();
                var source = new Register<int>(3);
                reactor.Computed(dest, () => source.Value);
                WaitUntil.True(() => dest.Value == 3, "second computation");
                source.Value = 8;
                WaitUntil.True(() => dest.Value == 8, "follows new function");
            }
        }

        [Fact]
        public void Rule_8_5_ExceptionLeavesRegisterUnchanged()
        {
            using (var host = new ThreadHost())
            {
                var dest = new Register<int>(4);
                Exception seen = null;
                Action<Exception> h = e => seen = e;
                Reactive.UnhandledException += h;
                try
                {
                    host.NewReactor().Computed(dest, () =>
                    {
                        throw new InvalidOperationException("compute");
                    });
                    WaitUntil.True(() => seen != null, "logged");
                    Assert.IsType<InvalidOperationException>(seen);
                    Assert.Equal(4, dest.Value);
                }
                finally
                {
                    Reactive.UnhandledException -= h;
                }
            }
        }

        [Fact]
        public void Rule_8_6_DisposeOldAfterStoreAndTrigger()
        {
            using (var host = new ThreadHost())
            {
                var first = new Tracked();
                var dest = new Register<Tracked>(first);
                var second = new Tracked();
                var go = new Register<int>(0);
                host.NewReactor().Computed(dest, () => go.Value == 0 ? first : second, true);
                WaitUntil.True(() => dest.Value == first, "initial");
                Assert.Equal(0, first.Disposed);
                go.Value = 1;
                WaitUntil.True(() => dest.Value == second && first.Disposed == 1, "replaced");
                Assert.Same(second, dest.Value);
                Assert.Equal(0, second.Disposed);
            }
        }

        [Fact]
        public void Rule_8_7_DisposedReactorReturnsNullAndKeepsLastValue()
        {
            using (var host = new ThreadHost())
            {
                var source = new Register<int>(1);
                var dest = new Register<int>(0);
                var reactor = host.NewReactor();
                reactor.Computed(dest, () => source.Value);
                WaitUntil.True(() => dest.Value == 1, "first");
                reactor.Dispose();
                Assert.Null(reactor.Computed(dest, () => 99));
                source.Value = 2;
                Thread.Sleep(50);
                Assert.Equal(1, dest.Value);
            }
        }

        sealed class Tracked : IDisposable
        {
            public int Disposed;

            public void Dispose()
            {
                Interlocked.Increment(ref Disposed);
            }
        }
    }
}

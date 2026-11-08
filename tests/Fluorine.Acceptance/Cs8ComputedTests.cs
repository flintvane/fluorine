using System;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs8ComputedTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs8.1-04")]
        public void cs8_1_04()
        {
            var s = Harness.Create();
            var dest = new Register<int>(0);
            var a = new Register<int>(2);
            var reactor = new Reactor(s);
            var reaction = reactor.Computed(dest, () => a.Value * 10);
            Harness.Barrier(s);
            Assert.NotNull(reaction);
            Assert.Equal(20, dest.Value);
        }

        [Fact(DisplayName = "cs8.1-05")]
        public void cs8_1_05()
        {
            var s = Harness.Create();
            var dest = new Register<int>(0);
            var a = new Register<int>(2);
            Thread seen = null;
            var reactor = new Reactor(s);
            reactor.Computed(dest, () =>
            {
                seen = Thread.CurrentThread;
                return a.Value * 10;
            });
            Harness.Barrier(s);
            a.Value = 3;
            Harness.Barrier2(s);
            Assert.Equal(30, dest.Value);
            Assert.Equal(Harness.ThreadOf(s), seen);
        }

        [Fact(DisplayName = "cs8.1-06")]
        public void cs8_1_06()
        {
            var s = Harness.Create();
            var dest = new Register<int>(0);
            var a = new Register<int>(2);
            int runs = 0;
            var reactor = new Reactor(s);
            var reaction = reactor.Computed(dest, () =>
            {
                Interlocked.Increment(ref runs);
                return a.Value * 10;
            });
            Harness.Barrier(s);
            Assert.Equal(20, dest.Value);
            reaction.Trigger();
            Harness.Barrier(s);
            Assert.Equal(2, Volatile.Read(ref runs));
            reaction.Dispose();
            a.Value = 4;
            Harness.Barrier2(s);
            Assert.Equal(20, dest.Value);
            Assert.Equal(2, Volatile.Read(ref runs));

            var dest2 = new Register<int>(0);
            var a2 = new Register<int>(1);
            int runs2 = 0;
            var reactor2 = new Reactor(s);
            reactor2.Computed(dest2, () =>
            {
                Interlocked.Increment(ref runs2);
                return a2.Value * 10;
            });
            Harness.Barrier(s);
            Assert.Equal(10, dest2.Value);
            reactor2.Dispose();
            a2.Value = 3;
            Harness.Barrier2(s);
            Assert.Equal(10, dest2.Value);
            Assert.Equal(1, Volatile.Read(ref runs2));
        }

        [Fact(DisplayName = "cs8.2-01")]
        public void cs8_2_01()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var dest = new Register<bool>();
            int runs = 0;
            var reactor = new Reactor(s);
            reactor.Computed(dest, () => a.Value > 0);
            reactor.Add(() =>
            {
                var unused = dest.Value;
                Interlocked.Increment(ref runs);
            });
            Harness.Barrier2(s);
            a.Value = 5;
            a.Value = 7;
            Harness.Barrier2(s);
            Assert.Equal(2, Volatile.Read(ref runs));
            Assert.True(dest.Value);
        }

        [Fact(DisplayName = "cs8.2-02")]
        public void cs8_2_02()
        {
            var s = Harness.Create();
            var a = new Register<int>(2);
            var dest = new Register<int>(0);
            var reactor = new Reactor(s);
            reactor.Computed(dest, () => a.Value * 10);
            Harness.Barrier(s);
            dest.Value = 99;
            Assert.Equal(99, dest.Value);
            a.Value = 4;
            Harness.Barrier2(s);
            Assert.Equal(40, dest.Value);
        }

        [Fact(DisplayName = "cs8.2-03")]
        public void cs8_2_03()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var dest = new Register<EqualBox>();
            var reactor = new Reactor(s);
            reactor.Computed(dest, () =>
            {
                var unused = a.Value;
                return new EqualBox { N = 1 };
            });
            Harness.Barrier(s);
            var first = dest.Value;
            int runs = 0;
            reactor.Add(() =>
            {
                var unused = dest.Value;
                Interlocked.Increment(ref runs);
            });
            Harness.Barrier(s);
            int afterSubscribe = Volatile.Read(ref runs);
            a.Value = 1;
            Harness.Barrier2(s);
            Assert.Same(first, dest.Value);
            Assert.Equal(afterSubscribe, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "cs8.3-01")]
        public void cs8_3_01()
        {
            var s = Harness.Create();
            Register<int> dest = null;
            int immediate = -1;
            var computed = new ManualResetEvent(false);
            s.Queue(() =>
            {
                dest = new Register<int>(5);
                new Reactor().Computed(dest, () => 1);
                immediate = dest.Value;
                computed.Set();
            });
            Harness.Wait(computed, "computed queued");
            Assert.Equal(5, immediate);
            Harness.Barrier(s);
            Assert.Equal(1, dest.Value);
        }

        [Fact(DisplayName = "cs8.4-01")]
        public void cs8_4_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var dest = new Register<int>(7);
                var reaction = new Reactor(s).Computed(dest, null);
                Harness.Barrier(s);
                Assert.NotNull(reaction);
                Assert.Equal(0, dest.Value);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs8.4-02")]
        public void cs8_4_02()
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var property in typeof(Reaction).GetProperties(flags))
            {
                if (typeof(Delegate).IsAssignableFrom(property.PropertyType))
                    Assert.False(property.CanWrite);
            }
            foreach (var method in typeof(Reaction).GetMethods(flags))
            {
                if (method.IsSpecialName)
                    continue;
                foreach (var parameter in method.GetParameters())
                    Assert.False(typeof(Delegate).IsAssignableFrom(parameter.ParameterType));
            }

            var s = Harness.Create();
            var dest = new Register<int>(0);
            var reactor = new Reactor(s);
            var first = reactor.Computed(dest, () => 1);
            var second = reactor.Computed(dest, () => 2);
            Assert.NotSame(first, second);
        }

        [Fact(DisplayName = "cs8.5-01")]
        public void cs8_5_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var dest = new Register<int>(4);
                var a = new Register<int>(0);
                var thrown = new InvalidOperationException("computed");
                var reactor = new Reactor(s);
                reactor.Computed(dest, () =>
                {
                    var value = a.Value;
                    if (value == 1)
                        throw thrown;
                    return 4;
                });
                Harness.Barrier(s);
                int runs = 0;
                reactor.Add(() =>
                {
                    var unused = dest.Value;
                    Interlocked.Increment(ref runs);
                });
                Harness.Barrier(s);
                int afterSubscribe = Volatile.Read(ref runs);
                a.Value = 1;
                Harness.Barrier2(s);
                Assert.Equal(1, log.Count);
                Assert.Same(thrown, log.Snapshot()[0]);
                Assert.Equal(4, dest.Value);
                Assert.Equal(afterSubscribe, Volatile.Read(ref runs));
            }
        }

        [Fact(DisplayName = "cs8.5-02")]
        public void cs8_5_02()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var dest = new Register<int>(4);
                var a = new Register<int>(0);
                var thrown = new InvalidOperationException("computed");
                new Reactor(s).Computed(dest, () =>
                {
                    var value = a.Value;
                    if (value == 1)
                        throw thrown;
                    return value == 0 ? 4 : value + 10;
                });
                Harness.Barrier(s);
                a.Value = 1;
                Harness.Barrier2(s);
                a.Value = 2;
                Harness.Barrier2(s);
                Assert.Equal(12, dest.Value);
            }
        }

        [Fact(DisplayName = "cs8.6-01")]
        public void cs8_6_01()
        {
            var s = Harness.Create();
            var which = new Register<int>(0);
            var first = new DisposableBox();
            var second = new DisposableBox();
            var dest = new Register<DisposableBox>();
            new Reactor(s).Computed(dest, () =>
            {
                var unused = which.Value;
                return unused == 0 ? first : second;
            }, true);
            Harness.Barrier(s);
            which.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(1, first.Disposed);
            Assert.Equal(0, second.Disposed);
        }

        [Fact(DisplayName = "cs8.6-02")]
        public void cs8_6_02()
        {
            var s = Harness.Create();
            var s2 = Harness.Create();
            var which = new Register<int>(0);
            var runs = new int[1];
            var first = new WatchingBox { Runs = runs, Other = s2 };
            var second = new WatchingBox { Runs = runs, Other = s2 };
            var dest = new Register<WatchingBox>();
            first.Dest = dest;
            second.Dest = dest;
            new Reactor(s).Computed(dest, () =>
            {
                var unused = which.Value;
                return unused == 0 ? first : second;
            }, true);
            Harness.Barrier(s);
            new Reactor(s2).Add(() =>
            {
                var value = dest.Value;
                if (value != null)
                    Interlocked.Increment(ref runs[0]);
            });
            Harness.Barrier2(s2);
            int baseline = Volatile.Read(ref runs[0]);
            which.Value = 1;
            Harness.Barrier2(s);
            Assert.Same(second, first.Seen);
            Assert.True(first.DownstreamAtDispose > baseline);
        }

        [Fact(DisplayName = "cs8.6-03")]
        public void cs8_6_03()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);

            var sameInput = new Register<int>(0);
            var held = new DisposableBox();
            var sameDest = new Register<DisposableBox>();
            reactor.Computed(sameDest, () =>
            {
                var unused = sameInput.Value;
                return held;
            }, true);
            Harness.Barrier(s);
            sameInput.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(0, held.Disposed);

            var pick = new Register<int>(0);
            var oldBox = new DisposableBox();
            var newBox = new DisposableBox();
            var replaced = new Register<DisposableBox>();
            reactor.Computed(replaced, () => pick.Value == 0 ? oldBox : newBox, false);
            Harness.Barrier(s);
            pick.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(0, oldBox.Disposed);
            Assert.Equal(0, newBox.Disposed);

            var number = new Register<int>(1);
            var plain = new Register<int>(0);
            Assert.Null(Record.Exception(() => reactor.Computed(plain, () => number.Value, true)));
            Harness.Barrier(s);
            number.Value = 2;
            Harness.Barrier2(s);
            Assert.Equal(2, plain.Value);
        }

        [Fact(DisplayName = "cs8.7-01")]
        public void cs8_7_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var disposed = new Reactor(s);
                disposed.Dispose();
                var dest = new Register<int>(3);
                bool ran = false;
                var result = disposed.Computed(dest, () =>
                {
                    ran = true;
                    return 9;
                });
                Harness.Barrier(s);
                Assert.Null(result);
                Assert.False(ran);
                Assert.Equal(3, dest.Value);

                var s2 = Harness.Create();
                var onShutdown = new Reactor(s2);
                s2.Shutdown();
                var dest2 = new Register<int>(3);
                bool ran2 = false;
                var result2 = onShutdown.Computed(dest2, () =>
                {
                    ran2 = true;
                    return 9;
                });
                Assert.Null(result2);
                Assert.False(ran2);
                Assert.Equal(3, dest2.Value);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs8.7-02")]
        public void cs8_7_02()
        {
            var s = Harness.Create();
            var a = new Register<int>(1);
            var dest = new Register<int>(0);
            var reactor = new Reactor(s);
            var reaction = reactor.Computed(dest, () => a.Value * 10);
            Harness.Barrier(s);
            Assert.Equal(10, dest.Value);
            reaction.Dispose();
            a.Value = 2;
            Harness.Barrier2(s);
            Assert.Equal(10, dest.Value);

            var a2 = new Register<int>(1);
            var dest2 = new Register<int>(0);
            var reactor2 = new Reactor(s);
            reactor2.Computed(dest2, () => a2.Value * 10);
            Harness.Barrier(s);
            Assert.Equal(10, dest2.Value);
            reactor2.Dispose();
            a2.Value = 3;
            Harness.Barrier2(s);
            Assert.Equal(10, dest2.Value);
        }

        sealed class EqualBox
        {
            public int N;

            public override bool Equals(object obj)
            {
                var other = obj as EqualBox;
                return other != null && other.N == N;
            }

            public override int GetHashCode()
            {
                return N;
            }
        }

        sealed class DisposableBox : IDisposable
        {
            public int Disposed;

            public void Dispose()
            {
                Disposed++;
            }
        }

        sealed class WatchingBox : IDisposable
        {
            public Register<WatchingBox> Dest;
            public Scheduler Other;
            public int[] Runs;
            public WatchingBox Seen;
            public int DownstreamAtDispose;

            public void Dispose()
            {
                Seen = Dest.Value;
                var marker = new ManualResetEvent(false);
                Other.Queue(() => marker.Set());
                Harness.Wait(marker, "downstream marker");
                DownstreamAtDispose = Volatile.Read(ref Runs[0]);
            }
        }
    }
}

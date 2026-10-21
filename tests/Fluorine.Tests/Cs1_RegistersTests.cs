using System;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class Cs1_RegistersTests
    {
        [Fact]
        public void Rule_1_1_RegisterRemembersReaders()
        {
            using (var host = new ThreadHost())
            {
                var signal = new Register();
                var n = 0;
                var reactor = host.NewReactor();
                reactor.Add(() =>
                {
                    signal.RecordRead();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                signal.TriggerReactions();
                WaitUntil.True(() => n == 2, "triggered");
            }
        }

        [Fact]
        public void Rule_1_2_RegisterTHoldsTypedValue()
        {
            var r = new Register<int>(7);
            Assert.Equal(7, r.Value);
        }

        [Fact]
        public void Rule_1_3_RecordReadSubscribesCurrentWhenRecording()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register();
                var n = 0;
                var reactor = host.NewReactor();
                reactor.Add(() =>
                {
                    Interlocked.Increment(ref n);
                    if (n == 1)
                    {
                        Reaction.Pause(() => reg.RecordRead());
                        return;
                    }
                    reg.RecordRead();
                });
                WaitUntil.True(() => n == 1, "first");
                reg.TriggerReactions();
                Thread.Sleep(50);
                Assert.Equal(1, n);
            }
        }

        [Fact]
        public void Rule_1_4_TriggerReactionsTriggersConnected()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register();
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    reg.RecordRead();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                reg.TriggerReactions();
                WaitUntil.True(() => n == 2, "second");
            }
        }

        [Fact]
        public void Rule_1_5_RecordReadAndTriggerFromAnyThread()
        {
            using (var host = new ThreadHost())
            {
                var reg = new Register();
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    reg.RecordRead();
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                var worker = new Thread(() => reg.TriggerReactions());
                worker.Start();
                worker.Join();
                WaitUntil.True(() => n == 2, "from worker");
            }
        }

        [Fact]
        public void Rule_1_6_EmptyConstructorUsesDefaultT()
        {
            Assert.Equal(0, new Register<int>().Value);
            Assert.Null(new Register<string>().Value);
        }

        [Fact]
        public void Rule_1_7_InitialConstructorDoesNotTrigger()
        {
            using (var host = new ThreadHost())
            {
                var n = 0;
                var reactor = host.NewReactor();
                reactor.Add(() => Interlocked.Increment(ref n));
                WaitUntil.True(() => n == 1, "first");
                var r = new Register<int>(5);
                Thread.Sleep(30);
                Assert.Equal(1, n);
                Assert.Equal(5, r.Value);
            }
        }

        [Fact]
        public void Rule_1_8_ValueGetterRecordsReadSetterWrites()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(1);
                var seen = 0;
                host.NewReactor().Add(() => { seen = r.Value; });
                WaitUntil.True(() => seen == 1, "first");
                r.Value = 2;
                WaitUntil.True(() => seen == 2, "write");
            }
        }

        [Fact]
        public void Rule_1_9_NameIsSettableAndNoNameConstructor()
        {
            var r = new Register();
            r.Name = "alpha";
            Assert.Equal("alpha", r.Name);
            var typed = new Register<int>();
            typed.Name = "beta";
            Assert.Equal("beta", typed.Name);
            Assert.Null(typeof(Register).GetConstructor(new[] { typeof(string) }));
            Assert.Null(typeof(Register<int>).GetConstructor(new[] { typeof(string) }));
        }

        [Fact]
        public void Rule_1_10_NameIsRetained()
        {
            var r = new Register();
            r.Name = "kept";
            Assert.Equal("kept", r.Name);
        }

        [Fact]
        public void Rule_1_11_RegistersCanBeInheritedAndCoreMembersAreNotVirtual()
        {
            var r = new NamedRegister();
            r.Value = 3;
            Assert.Equal(3, r.Value);
            Assert.False(typeof(Register).GetMethod("RecordRead").IsVirtual);
            Assert.False(typeof(Register).GetMethod("TriggerReactions").IsVirtual);
            Assert.False(typeof(Register<int>).GetProperty("Value").GetGetMethod().IsVirtual);
            Assert.False(typeof(Register<int>).GetProperty("Value").GetSetMethod().IsVirtual);
        }

        [Fact]
        public void Rule_1_12_BackingValueIsProtected()
        {
            var r = new NamedRegister();
            r.SetStored(9);
            Assert.Equal(9, r.PeekStored());
        }

        [Fact]
        public void Rule_1_13_ConstructorsAndValueFromAnyThread()
        {
            Register<int> created = null;
            var t = new Thread(() =>
            {
                created = new Register<int>(4);
                created.Value = 5;
            });
            t.Start();
            t.Join();
            Assert.Equal(5, created.Value);
        }

        [Fact]
        public void Rule_1_14_SetValueReturnsWhetherChanged()
        {
            var r = new Register<int>(1);
            Assert.False(r.SetValue(1));
            Assert.True(r.SetValue(2));
            Assert.Equal(2, r.Value);
        }

        [Fact]
        public void Rule_1_15_SetValueOutOldValueDoesNotRecordRead()
        {
            using (var host = new ThreadHost())
            {
                var r = new Register<int>(1);
                var n = 0;
                host.NewReactor().Add(() =>
                {
                    int old;
                    r.SetValue(1, out old);
                    Interlocked.Increment(ref n);
                });
                WaitUntil.True(() => n == 1, "first");
                r.Value = 5;
                Thread.Sleep(40);
                Assert.Equal(1, n);
                int old2;
                Assert.False(r.SetValue(5, out old2));
                Assert.Equal(5, old2);
                Assert.True(r.SetValue(8, out old2));
                Assert.Equal(5, old2);
            }
        }

        [Fact]
        public void Rule_1_16_EqualityIsObjectEqualsNotIEquatableAlone()
        {
            var r = new Register<EquatableOnly>(new EquatableOnly(1));
            Assert.True(r.SetValue(new EquatableOnly(1))); // different instances; object.Equals is referential
            var s = new Register<string>("a");
            Assert.False(s.SetValue("a"));
        }

        [Fact]
        public void Rule_1_17_EqualityExceptionReachesCallerNothingStored()
        {
            var a = new EqThrower();
            var r = new Register<EqThrower>(a);
            Assert.Throws<InvalidOperationException>(() => r.Value = new EqThrower());
            Assert.Same(a, r.Value);
        }

        [Fact]
        public void Rule_1_18_LargeStructAssignmentDoesNotTear()
        {
            var r = new Register<BigStruct>(new BigStruct(1, 1, 1, 1));
            var errors = 0;
            var stop = new ManualResetEventSlim(false);
            var readers = new Thread[4];
            for (var i = 0; i < readers.Length; i++)
            {
                readers[i] = new Thread(() =>
                {
                    while (!stop.IsSet)
                    {
                        var v = r.Value;
                        if (v.A != v.B || v.A != v.C || v.A != v.D)
                            Interlocked.Increment(ref errors);
                    }
                });
                readers[i].Start();
            }
            for (var i = 0; i < 10000; i++)
            {
                var x = i;
                r.Value = new BigStruct(x, x, x, x);
            }
            stop.Set();
            foreach (var t in readers)
                t.Join();
            Assert.Equal(0, errors);
        }

        sealed class NamedRegister : Register<int>
        {
            public void SetStored(int v)
            {
                StoredValue = v;
            }

            public int PeekStored()
            {
                return StoredValue;
            }
        }

        sealed class EquatableOnly : IEquatable<EquatableOnly>
        {
            public readonly int N;
            public EquatableOnly(int n) { N = n; }
            public bool Equals(EquatableOnly other) { return other != null && N == other.N; }
            public override int GetHashCode() { return N; }
        }

        struct BigStruct
        {
            public readonly long A, B, C, D;
            public BigStruct(long a, long b, long c, long d)
            {
                A = a; B = b; C = c; D = d;
            }
        }
    }
}

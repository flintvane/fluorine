using System.Reflection;
using System.Threading;
using System;
using Xunit;

namespace Fluorine
{
    public class Cs1_RegistersTests : AcceptanceTest
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

        [Fact(DisplayName = "cs1.3-01")]
        public void Rule_1_3_01_RecordReadOutsideReactionDoesNothing()
        {
            using (var log = new Log())
            {
                var sig = new Register();
                var ex = Record.Exception(() =>
                {
                    sig.RecordRead();
                    sig.TriggerReactions();
                });
                Assert.Null(ex);
                Assert.Equal(0, log.Count);
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

        [Fact(DisplayName = "cs1.4-01")]
        public void Rule_1_4_01_ManualTriggerRunsAllConnected()
        {
            var s = Harness.Create();
            var sig = new Register();
            var reactor = new Reactor(s);
            int a = 0;
            int b = 0;
            reactor.Add(() =>
            {
                sig.RecordRead();
                a++;
            });
            reactor.Add(() =>
            {
                sig.RecordRead();
                b++;
            });
            Harness.Barrier(s);
            sig.TriggerReactions();
            Harness.Barrier(s);
            Assert.Equal(2, a);
            Assert.Equal(2, b);
        }

        [Fact(DisplayName = "cs1.4-02")]
        public void Rule_1_4_02_ManualTriggerIgnoresEqualityKeepsValue()
        {
            var s = Harness.Create();
            var reg = new Register<int>(5);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            reg.TriggerReactions();
            Harness.Barrier(s);
            Assert.Equal(2, runs);
            Assert.Equal(5, reg.Value);
        }

        [Fact(DisplayName = "cs1.4-03")]
        public void Rule_1_4_03_TriggerWithoutReadersLogsNothing()
        {
            using (var log = new Log())
            {
                var sig = new Register();
                var ex = Record.Exception(() => sig.TriggerReactions());
                Assert.Null(ex);
                Assert.Equal(0, log.Count);
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

        [Fact(DisplayName = "cs1.7-02")]
        public void Rule_1_7_02_InitialValueCtorTriggersNothing()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var other = Harness.Create();
                var a = new Register<int>(0);
                int runs = 0;
                int otherRuns = 0;
                new Reactor(other).Add(() => otherRuns++);
                Harness.Barrier(other);
                new Reactor(s).Add(() =>
                {
                    var unused = a.Value;
                    var created = new Register<int>(3);
                    Assert.Equal(3, created.Value);
                    runs++;
                });
                Harness.Barrier(s);
                Harness.Barrier(other);
                Assert.Equal(1, runs);
                Assert.Equal(1, otherRuns);
                Assert.Equal(0, log.Count);
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
            var r = new Register
            {
                Name = "alpha"
            };
            Assert.Equal("alpha", r.Name);
            var typed = new Register<int>
            {
                Name = "beta"
            };
            Assert.Equal("beta", typed.Name);
            Assert.Null(typeof(Register).GetConstructor([typeof(string)]));
            Assert.Null(typeof(Register<int>).GetConstructor([typeof(string)]));
        }

        [Fact(DisplayName = "cs1.9-01")]
        public void Rule_1_9_01_NameSettable()
        {
            var reg = new Register<int>();
            var ex = Record.Exception(() => reg.Name = "x");
            Assert.Null(ex);
            foreach (var ctor in typeof(Register).GetConstructors())
            {
                var parameters = ctor.GetParameters();
                Assert.False(parameters.Length == 1 && parameters[0].ParameterType == typeof(string));
            }
            foreach (var ctor in typeof(Register<int>).GetConstructors())
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length == 0)
                    continue;
                Assert.False(parameters[0].ParameterType == typeof(string), "Register<T> constructor starts with string");
            }
        }

        [Fact]
        public void Rule_1_10_NameIsRetained()
        {
            var r = new Register
            {
                Name = "kept"
            };
            Assert.Equal("kept", r.Name);
        }

        [Fact]
        public void Rule_1_11_RegistersCanBeInheritedAndCoreMembersAreNotVirtual()
        {
            var r = new NamedRegister
            {
                Value = 3
            };
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
                created = new Register<int>(4)
                {
                    Value = 5
                };
            });
            t.Start();
            t.Join();
            Assert.Equal(5, created.Value);
        }

        [Fact(DisplayName = "cs1.13-01")]
        public void Rule_1_13_01_CtorAndValueFromAnyThread()
        {
            var shared = new Register<int>(-1);
            var written = new int[8];
            Exception error = null;
            var threads = new Thread[8];
            var ready = new CountdownEvent(8);
            var go = new ManualResetEvent(false);
            for (int i = 0; i < threads.Length; i++)
            {
                int id = i;
                threads[i] = new Thread(() =>
                {
                    ready.Signal();
                    go.WaitOne();
                    try
                    {
                        var local = new Register<int>();
                        int read = local.Value;
                        if (read != 0)
                            throw new InvalidOperationException("default was " + read);
                        local.Value = id + 1;
                        if (local.Value != id + 1)
                            throw new InvalidOperationException("local write lost");
                        written[id] = (id + 1) * 10;
                        shared.Value = written[id];
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref error, ex, null);
                    }
                })
                {
                    IsBackground = true
                };
                threads[i].Start();
            }
            Assert.True(ready.Wait(Harness.TimeoutMs), "workers ready");
            go.Set();
            for (int i = 0; i < threads.Length; i++)
                Assert.True(threads[i].Join(Harness.TimeoutMs));
            Assert.Null(error);
            int finalValue = shared.Value;
            bool found = false;
            for (int i = 0; i < written.Length; i++)
            {
                if (written[i] == finalValue)
                    found = true;
            }
            Assert.True(found, "final value " + finalValue + " was not one of the written values");
        }

        [Fact]
        public void Rule_1_14_SetValueReturnsWhetherChanged()
        {
            var r = new Register<int>(1);
            Assert.False(r.SetValue(1));
            Assert.True(r.SetValue(2));
            Assert.Equal(2, r.Value);
        }

        [Fact(DisplayName = "cs1.14-01")]
        public void Rule_1_14_01_SetValueTrueOnChange()
        {
            var s = Harness.Create();
            var reg = new Register<int>(1);
            int runs = 0;
            int seen = 0;
            new Reactor(s).Add(() =>
            {
                seen = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            Assert.True(reg.SetValue(2));
            Assert.Equal(2, reg.Value);
            Harness.Barrier(s);
            Assert.Equal(2, runs);
            Assert.Equal(2, seen);
        }

        [Fact(DisplayName = "cs1.14-02")]
        public void Rule_1_14_02_SetValueFalseOnEqual()
        {
            var s = Harness.Create();
            var reg = new Register<int>(2);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            int before = runs;
            Assert.False(reg.SetValue(2));
            Harness.Barrier(s);
            Assert.Equal(before, runs);
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
                Assert.False(r.SetValue(5, out var old2));
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

        [Fact(DisplayName = "cs1.16-01")]
        public void Rule_1_16_01_EqualityUsesObjectEquals()
        {
            var s = Harness.Create();
            var first = new EqualByValue(1);
            var reg = new Register<EqualByValue>(first);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            int before = runs;
            reg.Value = new EqualByValue(1);
            Harness.Barrier(s);
            Assert.Equal(before, runs);
            Assert.Same(first, reg.Value);
        }

        [Fact(DisplayName = "cs1.16-02")]
        public void Rule_1_16_02_IEquatableAloneDoesNotCount()
        {
            var s = Harness.Create();
            var reg = new Register<AccEquatableOnly>(new AccEquatableOnly(1));
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            int before = runs;
            var other = new AccEquatableOnly(1);
            Assert.False(object.Equals(reg.Value, other));
            Assert.True(((IEquatable<AccEquatableOnly>)reg.Value).Equals(other));
            reg.Value = other;
            Harness.Barrier(s);
            Assert.True(runs > before, "observed runs=" + runs + " expected a trigger");
        }

        [Fact(DisplayName = "cs1.16-03")]
        public void Rule_1_16_03_NullHandling()
        {
            var s = Harness.Create();
            var reg = new Register<string>(null);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            int before = runs;
            reg.Value = null;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
            reg.Value = "a";
            Harness.Barrier(s);
            Assert.Equal(before + 1, runs);
            reg.Value = null;
            Harness.Barrier(s);
            Assert.Equal(before + 2, runs);
        }

        [Fact]
        public void Rule_1_17_EqualityExceptionReachesCallerNothingStored()
        {
            var a = new EqThrower();
            var r = new Register<EqThrower>(a);
            Assert.Throws<InvalidOperationException>(() => r.Value = new EqThrower());
            Assert.Same(a, r.Value);
        }

        [Fact(DisplayName = "cs1.17-01")]
        public void Rule_1_17_01_ThrowingEqualityReachesWriter()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var original = new ThrowingEquals();
                var reg = new Register<ThrowingEquals>(original);
                int runs = 0;
                new Reactor(s).Add(() =>
                {
                    var unused = reg.Value;
                    runs++;
                });
                Harness.Barrier(s);
                int before = runs;
                var thrown = Assert.Throws<InvalidOperationException>(() => reg.Value = new ThrowingEquals());
                Assert.Equal("eq", thrown.Message);
                Assert.Same(original, reg.Value);
                Harness.Barrier(s);
                Assert.Equal(before, runs);
                Assert.Equal(0, log.Count);
            }
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

        [Fact(DisplayName = "cs1.18-01")]
        public void Rule_1_18_01_LargeStructDoesNotTear()
        {
            var reg = new Register<Big>(new Big(0));
            int reads = 0;
            int mismatch = 0;
            int stop = 0;
            var readers = new Thread[4];
            for (int i = 0; i < readers.Length; i++)
            {
                readers[i] = new Thread(() =>
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        Big value = reg.Value;
                        Interlocked.Increment(ref reads);
                        if (value.A != value.B || value.B != value.C || value.C != value.D)
                            Interlocked.Increment(ref mismatch);
                    }
                })
                {
                    IsBackground = true
                };
                readers[i].Start();
            }
            var writers = new Thread[4];
            for (int i = 0; i < writers.Length; i++)
            {
                int id = i;
                writers[i] = new Thread(() =>
                {
                    for (int k = 0; k < Harness.Stress / 4; k++)
                    {
                        long n = id + (long)k * 4 + 1;
                        reg.Value = new Big(n);
                    }
                })
                {
                    IsBackground = true
                };
                writers[i].Start();
            }
            for (int i = 0; i < writers.Length; i++)
                Assert.True(writers[i].Join(30000), "writer did not finish");
            Volatile.Write(ref stop, 1);
            for (int i = 0; i < readers.Length; i++)
                Assert.True(readers[i].Join(Harness.TimeoutMs));
            Assert.True(reads > 0, "readers performed no reads");
            Assert.Equal(0, mismatch);
            Big finalValue = reg.Value;
            Assert.True(finalValue.A == finalValue.B && finalValue.B == finalValue.C && finalValue.C == finalValue.D);
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

        sealed class EquatableOnly(int n) : IEquatable<EquatableOnly>
        {
            public readonly int N = n;
            public bool Equals(EquatableOnly other) { return other != null && N == other.N; }
            public override int GetHashCode() { return N; }
        }

        struct BigStruct(long a, long b, long c, long d)
        {
            public readonly long A = a, B = b, C = c, D = d;
        }

        sealed class BackingProbe(string initial) : Register<string>(initial)
        {
            public object ReadBacking()
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var property in typeof(Register<string>).GetProperties(flags))
                {
                    if (property.PropertyType != typeof(string))
                        continue;
                    var getter = property.GetGetMethod(true);
                    if (getter == null || !getter.IsFamily)
                        continue;
                    return property.GetValue(this, null);
                }
                foreach (var field in typeof(Register<string>).GetFields(flags))
                {
                    if (field.FieldType == typeof(string) && field.IsFamily)
                        return field.GetValue(this);
                }
                throw new InvalidOperationException("no protected backing member");
            }
        }

        struct Big(long value)
        {
            public long A = value;
            public long B = value;
            public long C = value;
            public long D = value;
        }

        sealed class EqualByValue(int token)
        {
            public readonly int Token = token;

            public override bool Equals(object obj)
            {
                return obj is EqualByValue;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        sealed class AccEquatableOnly(int id) : IEquatable<AccEquatableOnly>
        {
            public readonly int Id = id;

            public bool Equals(AccEquatableOnly other)
            {
                return true;
            }
        }

        sealed class ThrowingEquals
        {
            public override bool Equals(object obj)
            {
                throw new InvalidOperationException("eq");
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}

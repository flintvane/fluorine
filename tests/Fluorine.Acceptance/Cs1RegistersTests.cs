using System;
using System.Reflection;
using System.Threading;
using Fluorine;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs1RegistersTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs1.1-01")]
        public void cs1_1_01()
        {
            var s = Harness.Create();
            var sig = new Register();
            var reactor = new Reactor(s);
            int n = 0;
            reactor.Add(() =>
            {
                sig.RecordRead();
                n++;
            });
            Harness.Barrier(s);
            sig.TriggerReactions();
            Harness.Barrier(s);
            Assert.Equal(2, n);
        }

        [Fact(DisplayName = "cs1.2-01")]
        public void cs1_2_01()
        {
            Assert.True(new Register<int>() is Register);
        }

        [Fact(DisplayName = "cs1.3-01")]
        public void cs1_3_01()
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

        [Fact(DisplayName = "cs1.3-02")]
        public void cs1_3_02()
        {
            var s = Harness.Create();
            var sig = new Register();
            int n = 0;
            new Reactor(s).Add(() =>
            {
                using (Reaction.Pause())
                    sig.RecordRead();
                n++;
            });
            Harness.Barrier(s);
            sig.TriggerReactions();
            Harness.Barrier(s);
            Assert.Equal(1, n);
        }

        [Fact(DisplayName = "cs1.4-01")]
        public void cs1_4_01()
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
        public void cs1_4_02()
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
        public void cs1_4_03()
        {
            using (var log = new Log())
            {
                var sig = new Register();
                var ex = Record.Exception(() => sig.TriggerReactions());
                Assert.Null(ex);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs1.5-01")]
        public void cs1_5_01()
        {
            var s = Harness.Create();
            var sig = new Register();
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                sig.RecordRead();
                Interlocked.Increment(ref runs);
            });
            Harness.Barrier(s);
            int before = runs;
            Exception error = null;
            var threads = new Thread[8];
            var ready = new CountdownEvent(8);
            var go = new ManualResetEvent(false);
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    ready.Signal();
                    go.WaitOne();
                    try
                    {
                        for (int n = 0; n < 100; n++)
                            sig.TriggerReactions();
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref error, ex, null);
                    }
                });
                threads[i].IsBackground = true;
                threads[i].Start();
            }
            Assert.True(ready.Wait(Harness.TimeoutMs), "workers ready");
            go.Set();
            for (int i = 0; i < threads.Length; i++)
                Assert.True(threads[i].Join(Harness.TimeoutMs));
            Assert.Null(error);
            Harness.Barrier(s);
            Assert.True(runs >= before + 1, "observed extra runs=" + (runs - before) + " expected at least 1");
        }

        [Fact(DisplayName = "cs1.6-01")]
        public void cs1_6_01()
        {
            Assert.Equal(0, new Register<int>().Value);
            Assert.Null(new Register<string>().Value);
        }

        [Fact(DisplayName = "cs1.7-01")]
        public void cs1_7_01()
        {
            Assert.Equal(7, new Register<int>(7).Value);
        }

        [Fact(DisplayName = "cs1.7-02")]
        public void cs1_7_02()
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

        [Fact(DisplayName = "cs1.8-01")]
        public void cs1_8_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, runs);
        }

        [Fact(DisplayName = "cs1.8-02")]
        public void cs1_8_02()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            int seen = -1;
            new Reactor(s).Add(() =>
            {
                seen = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, runs);
            Assert.Equal(1, seen);
        }

        [Fact(DisplayName = "cs1.8-03")]
        public void cs1_8_03()
        {
            var s = Harness.Create();
            var reg = new Register<int>(1);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            int before = runs;
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
        }

        [Fact(DisplayName = "cs1.9-01")]
        public void cs1_9_01()
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

        [Fact(DisplayName = "cs1.9-02", Skip = "blocked (gap G10)")]
        public void cs1_9_02()
        {
        }

        [Fact(DisplayName = "cs1.11-01")]
        public void cs1_11_01()
        {
            Assert.False(typeof(Register).IsSealed);
            Assert.False(typeof(Register<int>).IsSealed);
            var value = typeof(Register<int>).GetProperty("Value");
            Assert.NotNull(value);
            Assert.False(value.GetGetMethod().IsVirtual);
            Assert.False(value.GetSetMethod().IsVirtual);
            var record = typeof(Register).GetMethod("RecordRead");
            var trigger = typeof(Register).GetMethod("TriggerReactions");
            Assert.NotNull(record);
            Assert.NotNull(trigger);
            Assert.False(record.IsVirtual);
            Assert.False(trigger.IsVirtual);
        }

        [Fact(DisplayName = "cs1.12-01")]
        public void cs1_12_01()
        {
            var probe = new BackingProbe("stored");
            Assert.Equal("stored", probe.ReadBacking());
            Assert.Equal("stored", probe.Value);

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            int protectedMembers = 0;
            foreach (var property in typeof(Register<string>).GetProperties(flags))
            {
                if (property.PropertyType != typeof(string))
                    continue;
                var getter = property.GetGetMethod(true);
                if (getter != null && getter.IsFamily)
                    protectedMembers++;
                var setter = property.GetSetMethod(true);
                if (setter != null && setter.IsPublic && property.Name != "Value" && property.Name != "Name")
                    Assert.Fail("public setter bypass " + property.Name);
            }
            foreach (var field in typeof(Register<string>).GetFields(flags))
            {
                if (field.FieldType != typeof(string))
                    continue;
                if (field.IsFamily)
                    protectedMembers++;
                Assert.False(field.IsPublic, "public field bypass " + field.Name);
            }
            Assert.True(protectedMembers >= 1, "no protected backing member");
        }

        sealed class BackingProbe : Register<string>
        {
            public BackingProbe(string initial)
                : base(initial)
            {
            }

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

        [Fact(DisplayName = "cs1.13-01")]
        public void cs1_13_01()
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
                });
                threads[i].IsBackground = true;
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

        [Fact(DisplayName = "cs1.14-01")]
        public void cs1_14_01()
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
        public void cs1_14_02()
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

        [Fact(DisplayName = "cs1.15-01")]
        public void cs1_15_01()
        {
            var reg = new Register<int>(1);
            int old;
            Assert.True(reg.SetValue(2, out old));
            Assert.Equal(1, old);
        }

        [Fact(DisplayName = "cs1.15-02")]
        public void cs1_15_02()
        {
            var reg = new Register<int>(2);
            int old;
            Assert.False(reg.SetValue(2, out old));
            Assert.Equal(2, old);
        }

        [Fact(DisplayName = "cs1.15-03")]
        public void cs1_15_03()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                int unused;
                reg.SetValue(1, out unused);
            });
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            var done = new ManualResetEvent(false);
            var writer = new Thread(() =>
            {
                reg.Value = 2;
                done.Set();
            });
            writer.IsBackground = true;
            writer.Start();
            Harness.Wait(done, "foreign write");
            Harness.Barrier(s);
            Assert.Equal(1, runs);
        }

        [Fact(DisplayName = "cs1.16-01")]
        public void cs1_16_01()
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
        public void cs1_16_02()
        {
            var s = Harness.Create();
            var reg = new Register<EquatableOnly>(new EquatableOnly(1));
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                var unused = reg.Value;
                runs++;
            });
            Harness.Barrier(s);
            int before = runs;
            var other = new EquatableOnly(1);
            Assert.False(object.Equals(reg.Value, other));
            Assert.True(((IEquatable<EquatableOnly>)reg.Value).Equals(other));
            reg.Value = other;
            Harness.Barrier(s);
            Assert.True(runs > before, "observed runs=" + runs + " expected a trigger");
        }

        [Fact(DisplayName = "cs1.16-03")]
        public void cs1_16_03()
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

        [Fact(DisplayName = "cs1.17-01")]
        public void cs1_17_01()
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

        [Fact(DisplayName = "cs1.18-01")]
        public void cs1_18_01()
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
                });
                readers[i].IsBackground = true;
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
                });
                writers[i].IsBackground = true;
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

        struct Big
        {
            public long A;
            public long B;
            public long C;
            public long D;

            public Big(long value)
            {
                A = value;
                B = value;
                C = value;
                D = value;
            }
        }

        sealed class EqualByValue
        {
            public readonly int Token;

            public EqualByValue(int token)
            {
                Token = token;
            }

            public override bool Equals(object obj)
            {
                return obj is EqualByValue;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        sealed class EquatableOnly : IEquatable<EquatableOnly>
        {
            public readonly int Id;

            public EquatableOnly(int id)
            {
                Id = id;
            }

            public bool Equals(EquatableOnly other)
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

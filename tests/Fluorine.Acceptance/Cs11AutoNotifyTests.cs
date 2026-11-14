using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs11AutoNotifyTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs11.1-01")]
        public void cs11_1_01()
        {
            Assert.True(typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(AutoNotify)));
            Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(AutoNotify)));
            var ctor = typeof(AutoNotify).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            Assert.NotNull(ctor);
            Assert.True(ctor.IsPublic || ctor.IsFamily || ctor.IsFamilyOrAssembly);
            Assert.NotNull(typeof(AutoNotifyAttribute));
            var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
                typeof(AutoNotifyAttribute),
                typeof(AttributeUsageAttribute));
            Assert.NotNull(usage);
            Assert.True((usage.ValidOn & AttributeTargets.Property) != 0);
        }

        [Fact(DisplayName = "cs11.1-02")]
        public void cs11_1_02()
        {
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            GC.KeepAlive(s1);
            var reg = new Register<int>(0);
            Simple o = null;
            s2.Queue(() => o = new Simple(reg));
            Harness.Barrier(s2);
            var h = new Handler();
            o.PropertyChanged += h.On;
            Harness.Barrier2(s2);
            reg.Value = 1;
            Harness.Barrier2(s2);
            Assert.Equal(1, h.Count);
            Assert.Equal(Harness.ThreadOf(s2), h.Thread);
        }

        [Fact(DisplayName = "cs11.2-01")]
        public void cs11_2_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            DerivedEmpty o = null;
            s.Queue(() => o = new DerivedEmpty(reg));
            Harness.Barrier(s);
            var h = new Handler();
            o.PropertyChanged += h.On;
            Harness.Barrier2(s);
            reg.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(1, h.Count);
            Assert.Equal("X", h.Name);
        }

        [Fact(DisplayName = "cs11.2-02")]
        public void cs11_2_02()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            OverrideKeep o = null;
            s.Queue(() => o = new OverrideKeep(a, b));
            Harness.Barrier(s);
            var h = new Handler();
            o.PropertyChanged += h.On;
            Harness.Barrier2(s);
            b.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(1, h.Count);
            Assert.Equal("X", h.Name);
        }

        [Fact(DisplayName = "cs11.2-03")]
        public void cs11_2_03()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var a = new Register<int>(0);
                var b = new Register<int>(0);
                NewUnmarked o = null;
                s.Queue(() => o = new NewUnmarked(a, b));
                Harness.Barrier(s);
                var h = new Handler();
                o.PropertyChanged += h.On;
                Harness.Barrier2(s);
                b.Value = 1;
                Harness.Barrier2(s);
                Assert.Equal(0, h.CountFor("X"));
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs11.2-04")]
        public void cs11_2_04()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            NewMarked o = null;
            s.Queue(() => o = new NewMarked(a, b));
            Harness.Barrier(s);
            var h = new Handler();
            o.PropertyChanged += h.On;
            Harness.Barrier2(s);
            b.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(1, h.Count);
            Assert.Equal("X", h.Name);
        }

        [Fact(DisplayName = "cs11.2-05")]
        public void cs11_2_05()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reg = new Register<int>(0);
                var c = new Register<int>(0);
                WithUnmarked o = null;
                s.Queue(() => o = new WithUnmarked(reg, c));
                Harness.Barrier(s);
                var h = new Handler();
                o.PropertyChanged += h.On;
                Harness.Barrier2(s);
                c.Value = 1;
                Harness.Barrier2(s);
                Assert.Equal(0, h.Count);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs11.3-01")]
        public void cs11_3_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            Simple o = null;
            s.Queue(() => o = new Simple(reg));
            Harness.Barrier(s);
            var h = new Handler();
            o.PropertyChanged += h.On;
            Harness.Barrier2(s);
            reg.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(1, h.Count);
            Assert.Same(o, h.Sender);
            Assert.Equal("X", h.Name);
            Assert.Equal(Harness.ThreadOf(s), h.Thread);
        }

        [Fact(DisplayName = "cs11.3-02")]
        public void cs11_3_02()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            Pair o = null;
            s.Queue(() => o = new Pair(a, b));
            Harness.Barrier(s);
            var h = new Handler();
            o.PropertyChanged += h.On;
            Harness.Barrier2(s);
            a.Value = 1;
            Harness.Barrier2(s);
            b.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(new[] { "X", "Y" }, h.Names.ToArray());
        }

        [Fact(DisplayName = "cs11.3-03")]
        public void cs11_3_03()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var g = new InvalidOperationException("getter-g");
                var k = new ApplicationException("handler-k");
                var regA = new Register<int>(0);
                ThrowingGetter aObj = null;
                s.Queue(() => aObj = new ThrowingGetter(regA, g));
                Harness.Barrier(s);
                aObj.PropertyChanged += (sender, args) => { };
                Harness.Barrier2(s);
                log.Clear();
                regA.Value = 1;
                Harness.Barrier2(s);
                bool aRan = false;
                s.Queue(() => aRan = true);
                Harness.Barrier(s);
                Assert.Equal(1, log.Count);
                Assert.Same(g, log.Snapshot()[0]);
                Assert.IsNotType<ReactiveException>(log.Snapshot()[0]);
                Assert.True(aRan);

                log.Clear();
                var regB = new Register<int>(0);
                Simple bObj = null;
                s.Queue(() => bObj = new Simple(regB));
                Harness.Barrier(s);
                bObj.PropertyChanged += (sender, args) => { throw k; };
                Harness.Barrier2(s);
                log.Clear();
                regB.Value = 1;
                Harness.Barrier2(s);
                bool bRan = false;
                s.Queue(() => bRan = true);
                Harness.Barrier(s);
                Assert.Equal(1, log.Count);
                Assert.Same(k, log.Snapshot()[0]);
                Assert.IsNotType<ReactiveException>(log.Snapshot()[0]);
                Assert.True(bRan);
            }
        }

        [Fact(DisplayName = "cs11.4-01")]
        public void cs11_4_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            Simple o = null;
            s.Queue(() => o = new Simple(reg));
            Harness.Barrier(s);
            var h = new Handler();
            o.PropertyChanged += h.On;
            Harness.Barrier2(s);
            Assert.Equal(0, h.Count);
        }

        [Fact(DisplayName = "cs11.4-02")]
        public void cs11_4_02()
        {
            var s = Harness.Create();
            CtorDone o = null;
            s.Queue(() =>
            {
                o = new CtorDone();
                o.PropertyChanged += (sender, args) => { };
            });
            Harness.Barrier2(s);
            foreach (var done in o.Recorded)
                Assert.True(done);
        }

        [Fact(DisplayName = "cs11.4-03")]
        public void cs11_4_03()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            Simple o = null;
            s.Queue(() => o = new Simple(reg));
            Harness.Barrier(s);
            var h = new Handler();
            int seen = -1;
            o.PropertyChanged += (sender, args) =>
            {
                h.On(sender, args);
                seen = o.X;
            };
            Harness.Barrier2(s);
            reg.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(1, h.Count);
            Assert.Equal("X", h.Name);
            Assert.Equal(1, seen);
        }

        [Fact(DisplayName = "cs11.4-04")]
        public void cs11_4_04()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            Parity o = null;
            s.Queue(() => o = new Parity(reg));
            Harness.Barrier(s);
            var h = new Handler();
            o.PropertyChanged += h.On;
            Harness.Barrier2(s);
            reg.Value = 1;
            Harness.Barrier2(s);
            reg.Value = 3;
            Harness.Barrier2(s);
            reg.TriggerReactions();
            Harness.Barrier2(s);
            reg.Value = 4;
            Harness.Barrier2(s);
            Assert.Equal(2, h.Count);
        }

        [Fact(DisplayName = "cs11.4-05")]
        public void cs11_4_05()
        {
            var s = Harness.Create();
            var regA = new Register<int>(0);
            Simple a = null;
            var hA = new Handler();
            int seenA = -1;
            s.Queue(() =>
            {
                a = new Simple(regA);
                a.PropertyChanged += (sender, args) =>
                {
                    hA.On(sender, args);
                    seenA = a.X;
                };
                regA.Value = 1;
            });
            Harness.Barrier2(s);
            Assert.Equal(1, hA.Count);
            Assert.Equal("X", hA.Name);
            Assert.Equal(1, seenA);

            var regB = new Register<int>(0);
            Simple b = null;
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            s.Queue(() =>
            {
                b = new Simple(regB);
                entered.Set();
                release.WaitOne();
            });
            Harness.Wait(entered, "gated ctor");
            var hB = new Handler();
            int seenB = -1;
            b.PropertyChanged += (sender, args) =>
            {
                hB.On(sender, args);
                seenB = b.X;
            };
            regB.Value = 1;
            release.Set();
            Harness.Barrier2(s);
            Assert.Equal(1, hB.Count);
            Assert.Equal("X", hB.Name);
            Assert.Equal(1, seenB);
        }

        [Fact(DisplayName = "cs11.4-06")]
        public void cs11_4_06()
        {
            var s = Harness.Create();
            var regA = new Register<int>(0);
            Simple a = null;
            s.Queue(() => a = new Simple(regA));
            Harness.Barrier(s);
            var hA = new Handler();
            a.PropertyChanged += hA.On;
            Harness.Barrier2(s);
            regA.Value = 1;
            Harness.Barrier2(s);
            regA.Value = 2;
            Harness.Barrier2(s);
            regA.Value = 1;
            Harness.Barrier2(s);
            Assert.Equal(3, hA.Count);

            var regB = new Register<int>(0);
            Simple b = null;
            s.Queue(() => b = new Simple(regB));
            Harness.Barrier(s);
            var hB = new Handler();
            b.PropertyChanged += hB.On;
            Harness.Barrier2(s);
            using (var hold = Harness.Gate(s))
            {
                regB.Value = 5;
                regB.Value = 0;
                hold.Release();
            }
            Harness.Barrier2(s);
            Assert.Equal(0, hB.Count);
        }

        [Fact(DisplayName = "cs11.5-01")]
        public void cs11_5_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reg = new Register<int>(0);
                Simple o = null;
                s.Queue(() => o = new Simple(reg));
                Harness.Barrier(s);
                var h = new Handler();
                o.PropertyChanged += h.On;
                Harness.Barrier2(s);
                reg.Value = 1;
                Harness.Barrier2(s);
                Assert.Equal(1, h.Count);
                var ex = Record.Exception(() => o.Dispose());
                Assert.Null(ex);
                Harness.Barrier2(s);
                log.Clear();
                int after = h.Count;
                reg.Value = 7;
                Harness.Barrier2(s);
                Assert.Equal(after, h.Count);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs11.5-02")]
        public void cs11_5_02()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reg = new Register<int>(0);
                Simple o = null;
                var h = new Handler();
                s.Queue(() =>
                {
                    o = new Simple(reg);
                    o.PropertyChanged += h.On;
                    o.Dispose();
                });
                Harness.Barrier2(s);
                log.Clear();
                reg.Value = 1;
                Harness.Barrier2(s);
                Assert.Equal(0, h.Count);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs11.6-01")]
        public void cs11_6_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            Simple o = null;
            s.Queue(() => o = new Simple(reg));
            Harness.Barrier2(s);
            reg.Value = 1;
            Harness.Barrier2(s);
            var h = new Handler();
            int seen = -1;
            using (var hold = Harness.Gate(s))
            {
                o.PropertyChanged += (sender, args) =>
                {
                    h.On(sender, args);
                    seen = o.X;
                };
                reg.Value = 2;
                hold.Release();
            }
            Harness.Barrier2(s);
            Assert.Equal(1, h.Count);
            Assert.Equal("X", h.Name);
            Assert.Equal(2, seen);
        }

        sealed class Handler
        {
            readonly List<string> _names = new();

            public int Count;
            public object Sender;
            public string Name;
            public Thread Thread;
            public List<string> Names { get { return _names; } }

            public void On(object sender, PropertyChangedEventArgs args)
            {
                Count++;
                Sender = sender;
                Name = args.PropertyName;
                Thread = Thread.CurrentThread;
                _names.Add(args.PropertyName);
            }

            public int CountFor(string propertyName)
            {
                int n = 0;
                foreach (var name in _names)
                {
                    if (name == propertyName)
                        n++;
                }
                return n;
            }
        }

        sealed class Simple : AutoNotify
        {
            public readonly Register<int> Reg;

            public Simple(Register<int> reg)
            {
                Reg = reg;
            }

            [AutoNotify]
            public int X { get { return Reg.Value; } }
        }

        class MarkedBase : AutoNotify
        {
            public readonly Register<int> Reg;

            public MarkedBase(Register<int> reg)
            {
                Reg = reg;
            }

            [AutoNotify]
            public virtual int X { get { return Reg.Value; } }
        }

        sealed class DerivedEmpty : MarkedBase
        {
            public DerivedEmpty(Register<int> reg)
                : base(reg)
            {
            }
        }

        class OverrideBase : AutoNotify
        {
            public readonly Register<int> A;

            public OverrideBase(Register<int> a)
            {
                A = a;
            }

            [AutoNotify]
            public virtual int X { get { return A.Value; } }
        }

        sealed class OverrideKeep : OverrideBase
        {
            public readonly Register<int> B;

            public OverrideKeep(Register<int> a, Register<int> b)
                : base(a)
            {
                B = b;
            }

            public override int X { get { return B.Value; } }
        }

        sealed class NewUnmarked : MarkedBase
        {
            public readonly Register<int> B;

            public NewUnmarked(Register<int> a, Register<int> b)
                : base(a)
            {
                B = b;
            }

            public new int X { get { return B.Value; } }
        }

        sealed class NewMarked : MarkedBase
        {
            public readonly Register<int> B;

            public NewMarked(Register<int> a, Register<int> b)
                : base(a)
            {
                B = b;
            }

            [AutoNotify]
            public new int X { get { return B.Value; } }
        }

        sealed class WithUnmarked : AutoNotify
        {
            public readonly Register<int> Reg;
            public readonly Register<int> C;

            public WithUnmarked(Register<int> reg, Register<int> c)
            {
                Reg = reg;
                C = c;
            }

            [AutoNotify]
            public int X { get { return Reg.Value; } }

            public int Y { get { return C.Value; } }
        }

        sealed class Pair : AutoNotify
        {
            public readonly Register<int> A;
            public readonly Register<int> B;

            public Pair(Register<int> a, Register<int> b)
            {
                A = a;
                B = b;
            }

            [AutoNotify]
            public int X { get { return A.Value; } }

            [AutoNotify]
            public int Y { get { return B.Value; } }
        }

        sealed class ThrowingGetter : AutoNotify
        {
            public readonly Register<int> Reg;
            readonly Exception _thrown;

            public ThrowingGetter(Register<int> reg, Exception thrown)
            {
                Reg = reg;
                _thrown = thrown;
            }

            [AutoNotify]
            public int X
            {
                get
                {
                    var value = Reg.Value;
                    if (value == 1)
                        throw _thrown;
                    return value;
                }
            }
        }

        sealed class CtorDone : AutoNotify
        {
            public bool Done;
            public readonly List<bool> Recorded = new();

            public CtorDone()
            {
                Done = true;
            }

            [AutoNotify]
            public int X
            {
                get
                {
                    if (Reaction.Current != null)
                        Recorded.Add(Done);
                    return 0;
                }
            }
        }

        sealed class Parity : AutoNotify
        {
            public readonly Register<int> Reg;

            public Parity(Register<int> reg)
            {
                Reg = reg;
            }

            [AutoNotify]
            public int P { get { return Reg.Value % 2; } }
        }
    }
}

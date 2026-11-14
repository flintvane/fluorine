using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System;
using Xunit;

namespace Fluorine
{
    public class Cs11_AutoNotifyTests : AcceptanceTest
    {
        [Fact]
        public void Rule_11_1_ConstructorWithoutSchedulerThrows()
        {
            Assert.Null(Scheduler.Current);
            Exception logged = null;
            Action<Exception> handler = e => logged = e;
            Reactive.UnhandledException += handler;
            try
            {
                Assert.Throws<ReactiveException>(() => new Bare());
                Assert.Null(logged);
            }
            finally
            {
                Reactive.UnhandledException -= handler;
            }
        }

        [Fact(DisplayName = "cs11.1-02 AutoNotify reactor on constructing sched")]
        public void Rule_11_1_02_ReactorOnConstructingThreadScheduler()
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

        [Fact(DisplayName = "cs11.4-02 First run after derived constructor")]
        public void Rule_11_4_ChangeBeforeFirstRunStillNotifies()
        {
            using var host = new DispatcherHost();
            CtorOrder model = null;
            host.OnScheduler(() => model = new CtorOrder());
            host.Drain();
            Assert.Equal(new[] { "read:1", "ctor", "read:7", "event:Value" }, model.Log);
        }

        [Fact(DisplayName = "cs11.4-01 No event for initial value; change fires")]
        public void Rule_11_4_ChangedValueRaisesOnSchedulerThread()
        {
            using var host = new DispatcherHost();
            Counter model = null;
            var names = new List<string>();
            Thread raisedOn = null;
            host.OnScheduler(() =>
            {
                model = new Counter();
                model.PropertyChanged += (sender, args) =>
                {
                    names.Add(args.PropertyName);
                    raisedOn = Thread.CurrentThread;
                };
            });
            host.Drain();
            Assert.Empty(names);

            model.Source.Value = 4;
            host.Drain();
            Assert.Equal(new[] { "Value" }, names);
            Assert.Same(host.Thread, raisedOn);
            Assert.NotSame(Thread.CurrentThread, raisedOn);
        }

        [Fact(DisplayName = "cs11.5-01 Dispose ends notifications")]
        public void Rule_11_5_DisposeStopsNotifications()
        {
            using var host = new DispatcherHost();
            Counting model = null;
            var names = new List<string>();
            host.OnScheduler(() =>
            {
                model = new Counting();
                model.PropertyChanged += (sender, args) => names.Add(args.PropertyName);
            });
            host.Drain();
            // One read establishes the baseline at attach; the queued run reads again.
            Assert.Equal(2, Volatile.Read(ref model.Reads));

            model.Source.Value = 2;
            host.Drain();
            Assert.Equal(new[] { "Value" }, names);
            var reads = Volatile.Read(ref model.Reads);

            model.Dispose();
            model.Dispose();
            model.Source.Value = 3;
            host.Drain();
            Assert.Equal(new[] { "Value" }, names);
            Assert.Equal(reads, Volatile.Read(ref model.Reads));

            Counting quiet = null;
            host.OnScheduler(() =>
            {
                quiet = new Counting();
                quiet.Dispose();
                quiet.PropertyChanged += (sender, args) => names.Add(args.PropertyName);
            });
            quiet.Source.Value = 9;
            host.Drain();
            Assert.Equal(new[] { "Value" }, names);
            // Attach still reads the baseline. The disposed reactor adds no reaction.
            Assert.Equal(1, Volatile.Read(ref quiet.Reads));
        }

        [Fact(DisplayName = "cs11.3-02 Each marked property notifies alone")]
        public void Rule_11_3_MarkedPropertiesNotifyIndependently()
        {
            using var host = new DispatcherHost();
            Pair pair = null;
            var names = new List<string>();
            host.OnScheduler(() =>
            {
                pair = new Pair();
                pair.PropertyChanged += (sender, args) => names.Add(args.PropertyName);
            });
            host.Drain();
            Assert.Empty(names);

            pair.Left.Value = 2;
            host.Drain();
            Assert.Equal(new[] { "Number" }, names);

            pair.Right.Value = "b";
            host.Drain();
            Assert.Equal(new[] { "Number", "Word" }, names);

            pair.Hidden.Value = 5;
            host.Drain();
            Assert.Equal(new[] { "Number", "Word", "Secret" }, names);
        }

        [Fact(DisplayName = "cs11.2-02 Override keeps the AutoNotify mark")]
        public void Rule_11_2_OverrideKeepsAttributeAndUsesDerivedGetter()
        {
            using var host = new DispatcherHost();
            DerivedVm model = null;
            var names = new List<string>();
            host.OnScheduler(() =>
            {
                model = new DerivedVm();
                model.PropertyChanged += (sender, args) => names.Add(args.PropertyName);
            });
            host.Drain();
            Assert.Empty(names);
            Assert.True(Volatile.Read(ref model.DerivedReads) >= 1);
            Assert.Equal(0, Volatile.Read(ref model.BaseReads));

            model.Source.Value = 2;
            host.Drain();
            Assert.Empty(names);

            model.Source.Value = 10;
            host.Drain();
            Assert.Equal(new[] { "Amount" }, names);
        }

        [Fact(DisplayName = "cs11.2-01 Derived class inherits property mark")]
        public void Rule_11_2_01_MarkInheritedByDerivedClass()
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

        [Fact(DisplayName = "cs11.2-03 `new` property without mark: no event")]
        public void Rule_11_2_03_NewWithoutAttributeIsNotMarked()
        {
            using var log = new Log();
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

        [Fact(DisplayName = "cs11.2-04 `new` property with mark notifies")]
        public void Rule_11_2_04_NewWithAttributeIsMarked()
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

        [Fact(DisplayName = "cs11.2-05 Unmarked property never notifies")]
        public void Rule_11_2_05_UnmarkedPropertyNeverNotifies()
        {
            using var log = new Log();
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

        [Fact(DisplayName = "cs11.3-01 Event on scheduler thread, right name")]
        public void Rule_11_3_01_EventRaisedOnSchedulerThreadWithPropertyName()
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

        [Fact(DisplayName = "cs11.3-03 Throwing getter/handler logged unwrapped")]
        public void Rule_11_3_03_ThrowingGetterOrHandlerIsReactionUserCode()
        {
            using var log = new Log();
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

        [Fact(DisplayName = "cs11.4-03 Later change notifies exactly once")]
        public void Rule_11_4_03_LaterChangeNotifiesOnce()
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

        [Fact(DisplayName = "cs11.4-04 Equal value on rerun raises no event")]
        public void Rule_11_4_04_RerunWithEqualValueRaisesNothing()
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

        [Fact(DisplayName = "cs11.4-05 Change before first run is notified")]
        public void Rule_11_4_05_ChangeBeforeFirstQueuedRunIsNotified()
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

        [Fact(DisplayName = "cs11.4-06 Compares with last stored value")]
        public void Rule_11_4_06_ComparesWithLastStoredValue()
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

        [Fact(DisplayName = "cs11.5-02 Dispose before first run: no events")]
        public void Rule_11_5_02_DisposeBeforeFirstQueuedRun()
        {
            using var log = new Log();
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

        [Fact(DisplayName = "cs11.6-01 Late handler gets first later change")]
        public void Rule_11_6_01_FirstChangeAfterLateHandlerIsNotified()
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

        [Fact]
        public void GetterExceptionReachesTheSubscriberUnwrapped()
        {
            using var host = new DispatcherHost();
            Exception logged = null;
            var names = new List<string>();
            Action<Exception> handler = e => logged = e;
            Reactive.UnhandledException += handler;
            try
            {
                InvalidOperationException thrown = null;
                host.OnScheduler(() =>
                {
                    var model = new ThrowingModel();
                    thrown = Assert.Throws<InvalidOperationException>(() =>
                        model.PropertyChanged += (sender, args) => names.Add(args.PropertyName));
                });
                host.Drain();
                Assert.Equal("getter", thrown.Message);
                Assert.Empty(names);
                Assert.Null(logged);
            }
            finally
            {
                Reactive.UnhandledException -= handler;
            }
        }

        [Fact]
        public void ReactiveMethodsRunAfterConstructorAndTrackReads()
        {
            using var host = new DispatcherHost();
            MethodModel model = null;
            host.OnScheduler(() =>
            {
                model = new MethodModel();
                Assert.Empty(model.Seen);
                Assert.Equal(0, model.SecretRuns);
            });
            host.Drain();
            Assert.Equal(new[] { 1 }, model.Seen);
            Assert.Equal(1, model.SecretRuns);
            Assert.Equal(0, model.WithArgRuns);
            Assert.Equal(0, model.NonVoidRuns);
            Assert.Equal(0, model.GenericRuns);
            Assert.Equal(0, model.UnmarkedRuns);

            model.Source.Value = 3;
            host.Drain();
            Assert.Equal(new[] { 1, 3 }, model.Seen);
            Assert.Equal(2, model.SecretRuns);
            Assert.Equal(0, model.WithArgRuns);
            Assert.Equal(0, model.NonVoidRuns);
            Assert.Equal(0, model.GenericRuns);
            Assert.Equal(0, model.UnmarkedRuns);
        }

        [Fact]
        public void ReactiveMethodExceptionIsLoggedUnwrapped()
        {
            using var host = new DispatcherHost();
            Exception logged = null;
            Action<Exception> handler = e => logged = e;
            Reactive.UnhandledException += handler;
            try
            {
                host.OnScheduler(() => new ThrowingMethod());
                host.Drain();
                Assert.IsType<InvalidOperationException>(logged);
                Assert.Equal("method", logged.Message);
            }
            finally
            {
                Reactive.UnhandledException -= handler;
            }
        }

        [Fact]
        public void ReactiveMethodStopsAfterDispose()
        {
            using var host = new DispatcherHost();
            MethodModel model = null;
            host.OnScheduler(() => model = new MethodModel());
            host.Drain();
            Assert.Equal(1, model.SecretRuns);

            model.Dispose();
            model.Source.Value = 8;
            host.Drain();
            Assert.Equal(new[] { 1 }, model.Seen);
            Assert.Equal(1, model.SecretRuns);
        }

        [Fact]
        public void ReactiveVirtualMethodRunsDerivedBodyOnceAndNewMethodIsSeparate()
        {
            using var host = new DispatcherHost();
            DerivedMethods model = null;
            host.OnScheduler(() => model = new DerivedMethods());
            host.Drain();
            Assert.Single(model.Log, line => line == "derived:1");
            Assert.Contains("own", model.Log);
            Assert.Contains("hidden", model.Log);
            Assert.Contains("fresh", model.Log);
            Assert.DoesNotContain("base:1", model.Log);

            model.Source.Value = 4;
            host.Drain();
            Assert.Single(model.Log, line => line == "derived:4");
        }

        [Fact]
        public void ReactiveAbstractMethodRunsConcreteOverride()
        {
            using var host = new DispatcherHost();
            ConcreteMethod model = null;
            host.OnScheduler(() => model = new ConcreteMethod());
            host.Drain();
            Assert.Equal(new[] { "concrete:1" }, model.Log);
            model.Source.Value = 2;
            host.Drain();
            Assert.Equal(new[] { "concrete:1", "concrete:2" }, model.Log);
        }

        [Fact]
        public void ReactiveMethodOnGenericClassTracksItsRegister()
        {
            using var host = new DispatcherHost();
            Box<string> model = null;
            host.OnScheduler(() => model = new Box<string>("a"));
            host.Drain();
            Assert.Equal("a", model.Seen);
            Assert.Equal(1, model.Runs);
            model.Source.Value = "b";
            host.Drain();
            Assert.Equal("b", model.Seen);
            Assert.Equal(2, model.Runs);
        }

        [Fact]
        public void ReactorScansReactiveMethodsOnAnyObject()
        {
            using var host = new DispatcherHost();
            PlainReactive plain = null;
            Reactor reactor = null;
            host.OnScheduler(() =>
            {
                plain = new PlainReactive();
                reactor = new Reactor();
                Assert.Empty(plain.Seen);
                reactor.AddReactiveMethods(plain);
                Assert.Empty(plain.Seen);
            });
            host.Drain();
            Assert.Equal(new[] { 1 }, plain.Seen);

            plain.Source.Value = 4;
            host.Drain();
            Assert.Equal(new[] { 1, 4 }, plain.Seen);

            reactor.Dispose();
            plain.Source.Value = 9;
            host.Drain();
            Assert.Equal(new[] { 1, 4 }, plain.Seen);
        }

        [Fact]
        public void AddReactiveMethodsRejectsNullAndIgnoresDisposedReactor()
        {
            using var host = new DispatcherHost();
            host.OnScheduler(() =>
            {
                var reactor = new Reactor();
                Assert.Throws<ReactiveException>(() => reactor.AddReactiveMethods(null));

                var plain = new PlainReactive();
                reactor.Dispose();
                reactor.AddReactiveMethods(plain);
                Assert.Empty(plain.Seen);
            });
            host.Drain();
        }

        [Fact]
        public void ReactiveMethodAndAutoNotifyPropertyShareTheObject()
        {
            using var host = new DispatcherHost();
            Mixed model = null;
            var names = new List<string>();
            host.OnScheduler(() =>
            {
                model = new Mixed();
                model.PropertyChanged += (sender, args) => names.Add(args.PropertyName);
            });
            host.Drain();
            Assert.Equal(1, model.Runs);
            Assert.Empty(names);

            model.Source.Value = 6;
            host.Drain();
            Assert.Equal(2, model.Runs);
            Assert.Equal(new[] { "Value" }, names);
        }

        sealed class DispatcherHost : IDisposable
        {
            public DispatcherScheduler Scheduler { get; private set; }
            public Thread Thread { get; private set; }

            public DispatcherHost()
            {
                Scheduler = DispatcherScheduler.CreateNew("test", () => Thread = Thread.CurrentThread);
            }

            public void OnScheduler(Action action)
            {
                var done = new ManualResetEventSlim(false);
                Exception error = null;
                Scheduler.Queue(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                WaitUntil.Event(done, "OnScheduler");
                if (error != null)
                    throw new Exception("Queued action failed.", error);
            }

            public void Drain()
            {
                OnScheduler(() => { });
            }

            public void Dispose()
            {
                if (Scheduler != null)
                    Scheduler.Shutdown();
            }
        }

        sealed class Bare : AutoNotify
        {
        }

        sealed class CtorOrder : AutoNotify
        {
            public readonly List<string> Log = [];
            public readonly Register<int> Source = new(1);

            public CtorOrder()
            {
                PropertyChanged += (sender, args) => Log.Add("event:" + args.PropertyName);
                Source.Value = 7;
                Log.Add("ctor");
            }

            [AutoNotify]
            public int Value
            {
                get
                {
                    Log.Add("read:" + Source.Value);
                    return Source.Value;
                }
            }
        }

        sealed class Counter : AutoNotify
        {
            public readonly Register<int> Source = new(1);

            [AutoNotify]
            public int Value => Source.Value;
        }

        sealed class Counting : AutoNotify
        {
            public readonly Register<int> Source = new(1);
            public int Reads;

            [AutoNotify]
            public int Value
            {
                get
                {
                    Interlocked.Increment(ref Reads);
                    return Source.Value;
                }
            }
        }

        sealed class Pair : AutoNotify
        {
            public readonly Register<int> Left = new(1);
            public readonly Register<string> Right = new("a");
            public readonly Register<int> Hidden = new(1);

            [AutoNotify]
            public int Number => Left.Value;

            [AutoNotify]
            public string Word => Right.Value;

            [AutoNotify]
            int Secret => Hidden.Value;
        }

        abstract class BaseVm : AutoNotify
        {
            public readonly Register<int> Source = new(1);
            public int BaseReads;

            [AutoNotify]
            public virtual int Amount
            {
                get
                {
                    Interlocked.Increment(ref BaseReads);
                    return Source.Value;
                }
            }
        }

        sealed class DerivedVm : BaseVm
        {
            public int DerivedReads;

            public override int Amount
            {
                get
                {
                    Interlocked.Increment(ref DerivedReads);
                    return Source.Value < 10 ? 0 : 1;
                }
            }
        }

        sealed class ThrowingModel : AutoNotify
        {
            [AutoNotify]
            public int Bad
            {
                get { throw new InvalidOperationException("getter"); }
            }
        }

        sealed class MethodModel : AutoNotify
        {
            public readonly Register<int> Source = new(1);
            public readonly List<int> Seen = [];
            public int SecretRuns;
            public int WithArgRuns;
            public int NonVoidRuns;
            public int GenericRuns;
            public int UnmarkedRuns;

            [Reactive]
            void Watch()
            {
                Seen.Add(Source.Value);
            }

            [Reactive]
            void Secret()
            {
                SecretRuns++;
                if (Source.Value < 0)
                    SecretRuns--;
            }

            [Reactive]
            void HasParameter(int value)
            {
                WithArgRuns += value;
            }

            [Reactive]
            int NotVoid()
            {
                NonVoidRuns++;
                return Source.Value;
            }

            [Reactive]
            void Generic<T>()
            {
                GenericRuns++;
            }

            void Unmarked()
            {
                UnmarkedRuns++;
            }

            [Reactive]
            static void StaticWatch()
            {
            }
        }

        class BaseMethods : AutoNotify
        {
            public readonly Register<int> Source = new(1);
            public readonly List<string> Log = [];

            [Reactive]
            public virtual void Watch()
            {
                Log.Add("base:" + Source.Value);
            }

            [Reactive]
            public void Own()
            {
                Log.Add("own");
            }

            [Reactive]
            void Hidden()
            {
                Log.Add("hidden");
            }
        }

        sealed class DerivedMethods : BaseMethods
        {
            public override void Watch()
            {
                Log.Add("derived:" + Source.Value);
            }

            [Reactive]
            public new void Own()
            {
                Log.Add("fresh");
            }
        }

        abstract class AbstractMethods : AutoNotify
        {
            public readonly Register<int> Source = new(1);
            public readonly List<string> Log = [];

            [Reactive]
            public abstract void Watch();
        }

        sealed class ConcreteMethod : AbstractMethods
        {
            public override void Watch()
            {
                Log.Add("concrete:" + Source.Value);
            }
        }

        sealed class Box<T>(T value) : AutoNotify
        {
            public readonly Register<T> Source = new(value);
            public T Seen;
            public int Runs;

            [Reactive]
            void Watch()
            {
                Runs++;
                Seen = Source.Value;
            }
        }

        sealed class PlainReactive
        {
            public readonly Register<int> Source = new(1);
            public readonly List<int> Seen = [];

            [Reactive]
            public void Watch()
            {
                Seen.Add(Source.Value);
            }

            [Reactive]
            public void TakesArgument(int value)
            {
                Seen.Add(value);
            }
        }

        sealed class Mixed : AutoNotify
        {
            public readonly Register<int> Source = new(1);
            public int Runs;

            [AutoNotify]
            public int Value => Source.Value;

            [Reactive]
            void Watch()
            {
                Runs++;
                if (Source.Value < 0)
                    Runs--;
            }
        }

        sealed class ThrowingMethod : AutoNotify
        {
            [Reactive]
            void Boom()
            {
                throw new InvalidOperationException("method");
            }
        }

        sealed class Handler
        {
            readonly List<string> _names = [];

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

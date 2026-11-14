using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Fluorine.Tests
{
    public class Cs11_AutoNotifyTests
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

        [Fact]
        public void Rule_11_4_ChangeBeforeFirstRunStillNotifies()
        {
            using (var host = new DispatcherHost())
            {
                CtorOrder model = null;
                host.OnScheduler(() => model = new CtorOrder());
                host.Drain();
                Assert.Equal(new[] { "read:1", "ctor", "read:7", "event:Value" }, model.Log);
            }
        }

        [Fact]
        public void Rule_11_4_ChangedValueRaisesOnSchedulerThread()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void Rule_11_4_EqualGetterValueDoesNotRaise()
        {
            using (var host = new DispatcherHost())
            {
                Stable model = null;
                var names = new List<string>();
                host.OnScheduler(() =>
                {
                    model = new Stable();
                    model.PropertyChanged += (sender, args) => names.Add(args.PropertyName);
                });
                host.Drain();
                var readsAfterFirst = Volatile.Read(ref model.Reads);
                Assert.True(readsAfterFirst >= 1);
                Assert.Empty(names);

                model.Source.Value = 3;
                host.Drain();
                Assert.True(Volatile.Read(ref model.Reads) > readsAfterFirst);
                Assert.Empty(names);

                model.Source.Value = 4;
                host.Drain();
                Assert.Equal(new[] { "Half" }, names);
            }
        }

        [Fact]
        public void Rule_11_5_DisposeStopsNotifications()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void Rule_11_3_MarkedPropertiesNotifyIndependently()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void Rule_11_2_OverrideKeepsAttributeAndUsesDerivedGetter()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void GetterExceptionReachesTheSubscriberUnwrapped()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void ReactiveMethodsRunAfterConstructorAndTrackReads()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void ReactiveMethodExceptionIsLoggedUnwrapped()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void ReactiveMethodStopsAfterDispose()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void ReactiveVirtualMethodRunsDerivedBodyOnceAndNewMethodIsSeparate()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void ReactiveAbstractMethodRunsConcreteOverride()
        {
            using (var host = new DispatcherHost())
            {
                ConcreteMethod model = null;
                host.OnScheduler(() => model = new ConcreteMethod());
                host.Drain();
                Assert.Equal(new[] { "concrete:1" }, model.Log);
                model.Source.Value = 2;
                host.Drain();
                Assert.Equal(new[] { "concrete:1", "concrete:2" }, model.Log);
            }
        }

        [Fact]
        public void ReactiveMethodOnGenericClassTracksItsRegister()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void ReactorScansReactiveMethodsOnAnyObject()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void AddReactiveMethodsRejectsNullAndIgnoresDisposedReactor()
        {
            using (var host = new DispatcherHost())
            {
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
        }

        [Fact]
        public void ReactiveMethodAndAutoNotifyPropertyShareTheObject()
        {
            using (var host = new DispatcherHost())
            {
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

        sealed class Mark(int n)
        {
            public readonly int N = n;

            public override bool Equals(object obj)
            {
                var other = obj as Mark;
                return other != null && other.N == N;
            }

            public override int GetHashCode()
            {
                return N;
            }
        }

        sealed class Stable : AutoNotify
        {
            public readonly Register<int> Source = new(2);
            public int Reads;

            [AutoNotify]
            public Mark Half
            {
                get
                {
                    Interlocked.Increment(ref Reads);
                    return new Mark(Source.Value / 2);
                }
            }
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
    }
}

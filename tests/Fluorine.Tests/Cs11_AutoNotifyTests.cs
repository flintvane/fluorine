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
        public void Rule_11_4_FirstRunIsQueuedAndRaisesNothing()
        {
            using (var host = new DispatcherHost())
            {
                CtorOrder model = null;
                host.OnScheduler(() => model = new CtorOrder());
                host.Drain();
                Assert.Equal(new[] { "ctor", "read:7" }, model.Log);
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
                Assert.Equal(1, Volatile.Read(ref model.Reads));

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
                Assert.Equal(0, Volatile.Read(ref quiet.Reads));
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
        public void GetterExceptionIsLoggedUnwrapped()
        {
            using (var host = new DispatcherHost())
            {
                Exception logged = null;
                var names = new List<string>();
                Action<Exception> handler = e => logged = e;
                Reactive.UnhandledException += handler;
                try
                {
                    host.OnScheduler(() =>
                    {
                        var model = new ThrowingModel();
                        model.PropertyChanged += (sender, args) => names.Add(args.PropertyName);
                    });
                    host.Drain();
                    Assert.Empty(names);
                    Assert.IsType<InvalidOperationException>(logged);
                    Assert.Equal("getter", logged.Message);
                }
                finally
                {
                    Reactive.UnhandledException -= handler;
                }
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
            public readonly List<string> Log = new List<string>();
            public readonly Register<int> Source = new Register<int>(1);

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
            public readonly Register<int> Source = new Register<int>(1);

            [AutoNotify]
            public int Value => Source.Value;
        }

        sealed class Mark
        {
            public readonly int N;

            public Mark(int n)
            {
                N = n;
            }

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
            public readonly Register<int> Source = new Register<int>(2);
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
            public readonly Register<int> Source = new Register<int>(1);
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
            public readonly Register<int> Left = new Register<int>(1);
            public readonly Register<string> Right = new Register<string>("a");
            public readonly Register<int> Hidden = new Register<int>(1);

            [AutoNotify]
            public int Number => Left.Value;

            [AutoNotify]
            public string Word => Right.Value;

            [AutoNotify]
            int Secret => Hidden.Value;
        }

        abstract class BaseVm : AutoNotify
        {
            public readonly Register<int> Source = new Register<int>(1);
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
    }
}

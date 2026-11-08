using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Fluorine.GeneratorTests
{
    public class HouseholdTests
    {
        [Fact]
        public void GeneratedPropertyReadsAndWritesTheRegister()
        {
            var property = typeof(Household).GetProperty("HappyHousewife");
            Assert.NotNull(property!.GetCustomAttribute<AutoNotifyAttribute>());
            Assert.NotNull(property.GetCustomAttribute<ReactiveAttribute>());

            var field = typeof(Household).GetField(
                "happyHousewife",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            Assert.Equal(typeof(Register<int>), field.FieldType);

            var count = typeof(Counter).GetProperty("Count");
            Assert.NotNull(count!.GetCustomAttribute<AutoNotifyAttribute>());
            Assert.Null(count.GetCustomAttribute<ReactiveAttribute>());
            var countField = typeof(Counter).GetField(
                "count",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.Equal(typeof(Register<int>), countField!.FieldType);
        }

        [Fact]
        public void FirstRunIsSilentAndAChangeNotifies()
        {
            using var host = new DispatcherHost();
            Household model = null;
            var names = new List<string>();
            Thread raisedOn = null;
            host.OnScheduler(() =>
            {
                model = new Household();
                model.PropertyChanged += (_, args) =>
                {
                    names.Add(args.PropertyName);
                    raisedOn = Thread.CurrentThread;
                };
            });
            host.Drain();
            Assert.Empty(names);
            Assert.Equal(7, model.HappyHousewife);
            Assert.Equal(14, model.Twice);

            model.HappyHousewife = 8;
            host.Drain();
            Assert.Equal(2, names.Count);
            Assert.Contains("HappyHousewife", names);
            Assert.Contains("Twice", names);
            Assert.Same(host.Thread, raisedOn);

            model.HappyHousewife = 8;
            host.Drain();
            Assert.Equal(2, names.Count);

            model.Title = "ada";
            host.Drain();
            Assert.Equal(3, names.Count);
            Assert.Contains("Title", names);
        }

        [Fact]
        public void WritingTheRegisterNotifiesAndDisposeStopsIt()
        {
            using var host = new DispatcherHost();
            Household model = null;
            var names = new List<string>();
            host.OnScheduler(() =>
            {
                model = new Household();
                model.PropertyChanged += (_, args) => names.Add(args.PropertyName);
            });
            host.Drain();

            var field = typeof(Household).GetField(
                "happyHousewife",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var register = (Register<int>)field!.GetValue(model);
            register.Value = 3;
            host.Drain();
            Assert.Equal(2, names.Count);
            Assert.Contains("HappyHousewife", names);
            Assert.Contains("Twice", names);
            Assert.Equal(3, model.HappyHousewife);

            model.Dispose();
            register.Value = 4;
            host.Drain();
            Assert.Equal(2, names.Count);
            Assert.Equal(4, model.HappyHousewife);
        }

        [Fact]
        public void HandWrittenAutoNotifyStillNotifies()
        {
            using var host = new DispatcherHost();
            Plain model = null;
            var names = new List<string>();
            host.OnScheduler(() =>
            {
                model = new Plain();
                model.PropertyChanged += (_, args) => names.Add(args.PropertyName);
            });
            host.Drain();
            Assert.Empty(names);

            model.Source.Value = 5;
            host.Drain();
            Assert.Equal(new[] { "Value" }, names);
        }

        [Fact]
        public void AutoNotifyPartialPropertyNotifiesThroughItsRegister()
        {
            using var host = new DispatcherHost();
            Counter model = null;
            var names = new List<string>();
            host.OnScheduler(() =>
            {
                model = new Counter();
                model.PropertyChanged += (_, args) => names.Add(args.PropertyName);
            });
            host.Drain();
            Assert.Empty(names);

            model.Count = 2;
            host.Drain();
            Assert.Equal(new[] { "Count" }, names);
        }

        sealed class DispatcherHost : IDisposable
        {
            private DispatcherScheduler Scheduler { get; set; }
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
                if (!done.Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("OnScheduler");
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
    }
}

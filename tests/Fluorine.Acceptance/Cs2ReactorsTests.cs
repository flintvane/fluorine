using System;
using System.Reflection;
using System.Threading;
using Fluorine;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs2ReactorsTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs2.1-01")]
        public void cs2_1_01()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            Thread tid = null;
            reactor.Queue(() => tid = Thread.CurrentThread);
            Harness.Barrier(s);
            Assert.Equal(Harness.ThreadOf(s), tid);
        }

        [Fact(DisplayName = "cs2.1-02")]
        public void cs2_1_02()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reactor = new Reactor(s);
                reactor.Dispose();
                bool ran = false;
                var ex = Record.Exception(() => reactor.Queue(() => ran = true));
                Assert.Null(ex);
                // A barrier on a live scheduler still runs; the disposed reactor must not have queued.
                Harness.Barrier(s);
                Assert.False(ran);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.1-03")]
        public void cs2_1_03()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            var order = new Order();
            var hold = Harness.Gate(s);
            try
            {
                reactor.Add(() => order.Add("r1"));
                reactor.Queue(() => order.Add("a"));
                reactor.Add(() => order.Add("r2"));
            }
            finally
            {
                hold.Release();
            }
            Harness.Barrier2(s);
            Assert.Equal(new[] { "r1", "a", "r2" }, order.Snapshot());
        }

        [Fact(DisplayName = "cs2.2-01")]
        public void cs2_2_01()
        {
            var s = Harness.Create();
            Thread tid = null;
            var owner = Harness.ThreadOf(s);
            s.Queue(() =>
            {
                var reactor = new Reactor();
                reactor.Add(() => tid = Thread.CurrentThread);
            });
            Harness.Barrier2(s);
            Assert.Equal(owner, tid);
        }

        [Fact(DisplayName = "cs2.2-02")]
        public void cs2_2_02()
        {
            using (var log = new Log())
            {
                Exception thrown = null;
                int seen = 0;
                var done = new ManualResetEvent(false);
                var thread = new Thread(() =>
                {
                    try
                    {
                        var reactor = new Reactor();
                        GC.KeepAlive(reactor);
                    }
                    catch (Exception ex)
                    {
                        thrown = ex;
                        seen = log.Count;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                thread.IsBackground = true;
                thread.Start();
                Harness.Wait(done, "reactor ctor");
                Assert.IsType<ReactiveException>(thrown);
                Assert.Equal(1, seen);
                Assert.Equal(1, log.Count);
                Assert.IsType<ReactiveException>(log.Snapshot()[0]);
            }
        }

        [Fact(DisplayName = "cs2.2-03")]
        public void cs2_2_03()
        {
            var s1 = Harness.Create();
            var s2 = Harness.Create();
            Reactor reactor = null;
            s1.Queue(() => reactor = new Reactor());
            Harness.Barrier(s1);
            var owner = Harness.ThreadOf(s1);
            Thread ranOn = null;
            s2.Queue(() => reactor.Add(() => ranOn = Thread.CurrentThread));
            Harness.Barrier(s2);
            Harness.Barrier2(s1);
            Assert.Equal(owner, ranOn);
        }

        [Fact(DisplayName = "cs2.3-01")]
        public void cs2_3_01()
        {
            var s = Harness.Create();
            Assert.Null(Scheduler.Current);
            Thread ranOn = null;
            var reactor = new Reactor(s);
            reactor.Add(() => ranOn = Thread.CurrentThread);
            Harness.Barrier(s);
            Assert.Equal(Harness.ThreadOf(s), ranOn);
        }

        [Fact(DisplayName = "cs2.3-02")]
        public void cs2_3_02()
        {
            using (var log = new Log())
            {
                var ex = Assert.Throws<ReactiveException>(() => new Reactor(null));
                Assert.Same(ex, log.Snapshot()[0]);
                Assert.Equal(1, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.4-01")]
        public void cs2_4_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                s.Shutdown();
                Reactor reactor = null;
                var ex = Record.Exception(() => reactor = new Reactor(s));
                Assert.Null(ex);
                bool ran = false;
                Assert.Null(reactor.Add(() => ran = true));
                reactor.Queue(() => ran = true);
                Assert.False(ran);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.4-02")]
        public void cs2_4_02()
        {
            var s = Harness.Create();
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            s.Queue(() =>
            {
                entered.Set();
                release.WaitOne();
            });
            Harness.Wait(entered, "drain gate");
            Reaction created = null;
            int runs = 0;
            s.Queue(() =>
            {
                created = new Reactor(s).Add(() => runs++);
            });
            var started = new ManualResetEvent(false);
            var caller = Harness.CallShutdown(s, started, null);
            try
            {
                Harness.WaitUntilBlocked(caller, started);
                release.Set();
                Assert.True(caller.Join(Harness.TimeoutMs), "shutdown did not return");
            }
            finally
            {
                release.Set();
            }
            Assert.Null(created);
            Assert.Equal(0, runs);
        }

        [Fact(DisplayName = "cs2.5-01")]
        public void cs2_5_01()
        {
            var s = Harness.Create();
            var reactor = new Reactor(s);
            var ex = Record.Exception(() => reactor.Name = "reactor-name");
            Assert.Null(ex);
            var ctors = typeof(Reactor).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.Equal(2, ctors.Length);
            bool empty = false;
            bool scheduler = false;
            foreach (var ctor in ctors)
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length == 0)
                    empty = true;
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Scheduler))
                    scheduler = true;
                foreach (var parameter in parameters)
                    Assert.NotEqual(typeof(string), parameter.ParameterType);
            }
            Assert.True(empty);
            Assert.True(scheduler);
        }

        [Fact(DisplayName = "cs2.6-01")]
        public void cs2_6_01()
        {
            var ctors = typeof(Reaction).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.Empty(ctors);
        }

        [Fact(DisplayName = "cs2.7-01")]
        public void cs2_7_01()
        {
            var s = Harness.Create();
            int runs = 0;
            var reaction = new Reactor(s).Add(() => runs++);
            Harness.Barrier(s);
            Assert.NotNull(reaction);
            Assert.Equal(1, runs);
        }

        [Fact(DisplayName = "cs2.7-02", Skip = "blocked (gap G10)")]
        public void cs2_7_02()
        {
        }

        [Fact(DisplayName = "cs2.8-01")]
        public void cs2_8_01()
        {
            var s = Harness.Create();
            bool ran = false;
            s.Queue(() =>
            {
                new Reactor().Add(() => ran = true);
                Assert.False(ran);
            });
            Harness.Barrier2(s);
            Assert.True(ran);
        }

        [Fact(DisplayName = "cs2.9-01")]
        public void cs2_9_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reactor = new Reactor(s);
                reactor.Dispose();
                bool ran = false;
                var reaction = reactor.Add(() => ran = true);
                Harness.Barrier(s);
                Assert.Null(reaction);
                Assert.False(ran);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.11-01")]
        public void cs2_11_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var reactor = new Reactor(s);
            int a = 0, b = 0, c = 0;
            reactor.Add(() => { a++; var unused = reg.Value; });
            reactor.Add(() => { b++; var unused = reg.Value; });
            reactor.Add(() => { c++; var unused = reg.Value; });
            Harness.Barrier(s);
            reactor.Dispose();
            Harness.Barrier(s);
            int a0 = a, b0 = b, c0 = c;
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(a0, a);
            Assert.Equal(b0, b);
            Assert.Equal(c0, c);
        }

        [Fact(DisplayName = "cs2.12-01")]
        public void cs2_12_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var reactor = new Reactor(s);
            int n1 = 0, n2 = 0;
            var r1 = reactor.Add(() => { n1++; var unused = reg.Value; });
            reactor.Add(() => { n2++; var unused = reg.Value; });
            Harness.Barrier(s);
            r1.Dispose();
            int n1Before = n1;
            int n2Before = n2;
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(n1Before, n1);
            Assert.True(n2 > n2Before, "r2 did not run");
            var disposeEx = Record.Exception(() => reactor.Dispose());
            Assert.Null(disposeEx);
            int n2AfterDispose = n2;
            reg.Value = 2;
            Harness.Barrier(s);
            Assert.Equal(n2AfterDispose, n2);
        }

        [Fact(DisplayName = "cs2.13-01")]
        public void cs2_13_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reaction = new Reactor(s).Add(null);
                Harness.Barrier(s);
                Assert.NotNull(reaction);
                var ex = Record.Exception(() => reaction.Trigger());
                Harness.Barrier(s);
                Assert.Null(ex);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.13-02")]
        public void cs2_13_02()
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            foreach (var property in typeof(Reaction).GetProperties(flags))
            {
                if (!typeof(Delegate).IsAssignableFrom(property.PropertyType))
                    continue;
                Assert.Null(property.GetSetMethod());
            }
            foreach (var field in typeof(Reaction).GetFields(flags))
                Assert.False(typeof(Delegate).IsAssignableFrom(field.FieldType));
        }

        [Fact(DisplayName = "cs2.14-01")]
        public void cs2_14_01()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reg = new Register<int>(0);
                int calls = 0;
                Action f = () =>
                {
                    calls++;
                    var unused = reg.Value;
                };
                var ex = Record.Exception(() => f());
                Assert.Null(ex);
                Assert.Equal(1, calls);
                reg.Value = 1;
                Harness.Barrier(s);
                Assert.Equal(1, calls);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.15-01")]
        public void cs2_15_01()
        {
            var s = Harness.Create();
            int runs = 0;
            var reaction = new Reactor(s).Add(() => runs++);
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            reaction.Trigger();
            Harness.Barrier(s);
            Assert.Equal(2, runs);
        }

        [Fact(DisplayName = "cs2.15-02")]
        public void cs2_15_02()
        {
            var s = Harness.Create();
            int runs = 0;
            var reaction = new Reactor(s).Add(() => runs++);
            Harness.Barrier(s);
            s.Queue(() =>
            {
                int before = runs;
                reaction.Trigger();
                Assert.Equal(before, runs);
            });
            Harness.Barrier2(s);
            Assert.Equal(2, runs);
        }

        [Fact(DisplayName = "cs2.15-03")]
        public void cs2_15_03()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                int runs = 0;
                var reaction = new Reactor(s).Add(() => runs++);
                Harness.Barrier(s);
                reaction.Dispose();
                Harness.Barrier(s);
                int before = runs;
                var ex = Record.Exception(() => reaction.Trigger());
                Harness.Barrier(s);
                Assert.Null(ex);
                Assert.Equal(before, runs);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.16-01", Skip = "blocked (gap G10)")]
        public void cs2_16_01()
        {
        }

        [Fact(DisplayName = "cs2.17-01")]
        public void cs2_17_01()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            int runs = 0;
            var reaction = new Reactor(s).Add(() =>
            {
                runs++;
                var unusedA = a.Value;
                var unusedB = b.Value;
            });
            Harness.Barrier(s);
            reaction.Dispose();
            Harness.Barrier(s);
            int before = runs;
            a.Value = 1;
            b.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
        }

        [Fact(DisplayName = "cs2.17-02")]
        public void cs2_17_02()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            var reaction = new Reactor(s).Add(() =>
            {
                runs++;
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            var ex = Record.Exception(() => reaction.Dispose());
            Assert.Null(ex);
            Harness.Barrier(s);
            int before = runs;
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
        }

        [Fact(DisplayName = "cs2.17-03")]
        public void cs2_17_03()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reaction = new Reactor(s).Add(() => { });
                Harness.Barrier(s);
                var ex = Record.Exception(() =>
                {
                    reaction.Dispose();
                    reaction.Dispose();
                });
                Harness.Barrier(s);
                Assert.Null(ex);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs2.17-04")]
        public void cs2_17_04()
        {
            var s = Harness.Create();
            int runs = 0;
            var reaction = new Reactor(s).Add(() => runs++);
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            var hold = Harness.Gate(s);
            try
            {
                reaction.Trigger();
                reaction.Dispose();
            }
            finally
            {
                hold.Release();
            }
            Harness.Barrier(s);
            Assert.Equal(1, runs);
        }

        [Fact(DisplayName = "cs2.17-05")]
        public void cs2_17_05()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            int n = 0;
            Reaction self = null;
            self = new Reactor(s).Add(() =>
            {
                var unusedA = a.Value;
                self.Dispose();
                var unusedB = b.Value;
                n++;
            });
            Harness.Barrier(s);
            Assert.Equal(1, n);
            a.Value = 1;
            b.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(1, n);
        }

        [Fact(DisplayName = "cs2.17-06")]
        public void cs2_17_06()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            int runs = 0;
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                runs++;
                var unused = a.Value;
                entered.Set();
                release.WaitOne();
                var unusedB = b.Value;
            });
            try
            {
                Harness.Wait(entered, "run entered");
                var ex = Record.Exception(() => reaction.Dispose());
                Assert.Null(ex);
                release.Set();
                Harness.Barrier2(s);
                int before = runs;
                a.Value = 1;
                b.Value = 2;
                Harness.Barrier2(s);
                Assert.Equal(1, before);
                Assert.Equal(before, runs);
            }
            finally
            {
                release.Set();
            }
        }

        [Fact(DisplayName = "cs2.18-01")]
        public void cs2_18_01()
        {
            var s = Harness.Create();
            Reaction seen = null;
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() => seen = Reaction.Current);
            Harness.Barrier(s);
            Assert.Same(reaction, seen);
        }

        [Fact(DisplayName = "cs2.18-02")]
        public void cs2_18_02()
        {
            var s = Harness.Create();
            Reaction inAction = null;
            bool actionRan = false;
            s.Queue(() =>
            {
                actionRan = true;
                inAction = Reaction.Current;
            });
            Harness.Barrier(s);
            Assert.True(actionRan);
            Assert.Null(inAction);
            Assert.Null(Reaction.Current);
        }

        [Fact(DisplayName = "cs2.18-03")]
        public void cs2_18_03()
        {
            var s = Harness.Create();
            Reaction seen = null;
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                using (Reaction.Pause())
                    seen = Reaction.Current;
            });
            Harness.Barrier(s);
            Assert.Same(reaction, seen);
        }

        [Fact(DisplayName = "cs2.19-01")]
        public void cs2_19_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                Reaction.PauseRecording();
                var unused = reg.Value;
                Reaction.ContinueRecording();
            });
            Harness.Barrier(s);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(1, runs);
        }

        [Fact(DisplayName = "cs2.20-01")]
        public void cs2_20_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                Reaction.PauseRecording();
                Reaction.ContinueRecording();
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, runs);
        }

        [Fact(DisplayName = "cs2.20-02")]
        public void cs2_20_02()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var reg = new Register<int>(0);
                int runs = 0;
                new Reactor(s).Add(() =>
                {
                    runs++;
                    if (runs == 1)
                        Reaction.ContinueRecording();
                    var unused = reg.Value;
                });
                Harness.Barrier(s);
                Assert.Equal(1, log.Count);
                Assert.IsType<ReactiveException>(log.Snapshot()[0]);
                reg.Value = 1;
                Harness.Barrier(s);
                Assert.Equal(2, runs);
            }
        }

        [Fact(DisplayName = "cs2.21-01")]
        public void cs2_21_01()
        {
            var s = Harness.Create();
            var ex = Record.Exception(() =>
            {
                Reaction.PauseRecording();
                Reaction.ContinueRecording();
                Reaction.ContinueRecording();
            });
            Assert.Null(ex);
            Assert.False(Reaction.Recording);
            Exception actionError = null;
            bool recording = true;
            s.Queue(() =>
            {
                try
                {
                    Reaction.PauseRecording();
                    Reaction.ContinueRecording();
                    Reaction.ContinueRecording();
                    recording = Reaction.Recording;
                }
                catch (Exception ex2)
                {
                    actionError = ex2;
                }
            });
            Harness.Barrier(s);
            Assert.Null(actionError);
            Assert.False(recording);
        }

        [Fact(DisplayName = "cs2.22-01")]
        public void cs2_22_01()
        {
            var s = Harness.Create();
            bool recording = false;
            new Reactor(s).Add(() => recording = Reaction.Recording);
            Harness.Barrier(s);
            Assert.True(recording);
        }

        [Fact(DisplayName = "cs2.22-02")]
        public void cs2_22_02()
        {
            var s = Harness.Create();
            bool insidePause = true;
            bool inAction = true;
            new Reactor(s).Add(() =>
            {
                using (Reaction.Pause())
                    insidePause = Reaction.Recording;
            });
            s.Queue(() => inAction = Reaction.Recording);
            Harness.Barrier(s);
            Assert.False(insidePause);
            Assert.False(inAction);
            Assert.False(Reaction.Recording);
        }

        [Fact(DisplayName = "cs2.23-01")]
        public void cs2_23_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            Reaction reaction = null;
            reaction = new Reactor(s).Add(() =>
            {
                runs++;
                if (runs == 1)
                    Reaction.PauseRecording();
                else
                {
                    var unused = reg.Value;
                }
            });
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            reaction.Trigger();
            Harness.Barrier(s);
            Assert.Equal(2, runs);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(3, runs);
        }

        [Fact(DisplayName = "cs2.24-01")]
        public void cs2_24_01()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            bool recordingAfter = false;
            new Reactor(s).Add(() =>
            {
                runs++;
                using (Reaction.Pause())
                {
                    var unused = reg.Value;
                }
                recordingAfter = Reaction.Recording;
            });
            Harness.Barrier(s);
            Assert.True(recordingAfter);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(1, runs);
        }

        [Fact(DisplayName = "cs2.24-02")]
        public void cs2_24_02()
        {
            var s = Harness.Create();
            var a = new Register<int>(1);
            var b = new Register<int>(2);
            int runs = 0;
            int seenA = 0;
            int seenB = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                Reaction.Pause(() => seenA = a.Value);
                seenB = Reaction.Pause(() => b.Value);
            });
            Harness.Barrier(s);
            Assert.Equal(1, seenA);
            Assert.Equal(2, seenB);
            int before = runs;
            a.Value = 5;
            b.Value = 6;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
        }

        [Fact(DisplayName = "cs2.24-03")]
        public void cs2_24_03()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                try
                {
                    Reaction.Pause(() => { throw new InvalidOperationException("x"); });
                }
                catch (InvalidOperationException)
                {
                }
                var unused = reg.Value;
            });
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            reg.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, runs);
        }

        [Fact(DisplayName = "cs2.24-04")]
        public void cs2_24_04()
        {
            var s = Harness.Create();
            var c = new Register<int>(0);
            var d = new Register<int>(0);
            int runs = 0;
            new Reactor(s).Add(() =>
            {
                runs++;
                Reaction.Pause(() =>
                {
                    Reaction.Pause(() => { });
                    var unused = c.Value;
                });
                var unusedD = d.Value;
            });
            Harness.Barrier(s);
            int before = runs;
            c.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(before, runs);
            d.Value = 1;
            Harness.Barrier(s);
            Assert.True(runs > before, "d did not retrigger");
        }
    }
}

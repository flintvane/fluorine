using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class UiSchedulerTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs3.6-04")]
        public void cs3_6_04()
        {
            using (var loop = DispatcherLoop.Start())
            {
                loop.Scheduler.Shutdown();
                Assert.True(loop.Thread.IsAlive, "dispatcher thread ended; Current cannot be read on a live thread");
                Scheduler seen = null;
                loop.Dispatcher.Invoke(new Action(() => seen = Scheduler.Current));
                Assert.Null(seen);
            }

            using (var forms = WinFormsLoop.Start())
            {
                forms.Scheduler.Shutdown();
                Assert.True(forms.Thread.IsAlive, "WinForms thread ended; Current cannot be read on a live thread");
                Scheduler seen = null;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    seen = Scheduler.Current;
                    done.Set();
                }));
                Harness.Wait(done, "winforms Current");
                Assert.Null(seen);
            }
        }

        [Fact(DisplayName = "cs3.8-09")]
        public void cs3_8_09()
        {
            using (var loop = DispatcherLoop.Start())
            {
                bool ran = false;
                Exception error = null;
                var done = new ManualResetEvent(false);
                loop.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        loop.Scheduler.Queue(() => ran = true);
                        loop.Scheduler.Shutdown();
                        if (!ran)
                            error = new InvalidOperationException("queued work had not run when Shutdown returned");
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                }));
                Harness.Wait(done, 10000, "dispatcher Shutdown");
                Assert.Null(error);
                Assert.True(ran);
            }
        }

        [Fact(DisplayName = "cs3.13-03")]
        public void cs3_13_03()
        {
            using (var loop = DispatcherLoop.Start())
            {
                var thread = loop.Thread;
                loop.Scheduler.Shutdown();
                Assert.True(thread.IsAlive);
                Assert.Null(Scheduler.ForThread(thread));
            }

            using (var forms = WinFormsLoop.Start())
            {
                var thread = forms.Thread;
                forms.Scheduler.Shutdown();
                Assert.True(thread.IsAlive);
                Assert.Null(Scheduler.ForThread(thread));
            }
        }

        [Fact(DisplayName = "cs3.13-04")]
        public void cs3_13_04()
        {
            using (var log = new Log())
            using (var forms = WinFormsLoop.Start())
            {
                Exception error = null;
                Scheduler forThread = null;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var dispatcher = Dispatcher.CurrentDispatcher;
                        if (dispatcher == null)
                            throw new InvalidOperationException("WPF dispatcher was not present");
                        DispatcherScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    forThread = Scheduler.ForThread(Thread.CurrentThread);
                    done.Set();
                }));
                Harness.Wait(done, "second attach");
                Assert.IsType<ReactiveException>(error);
                Assert.Equal(0, log.Count);
                Assert.Same(forms.Scheduler, forThread);
            }
        }

        [Fact(DisplayName = "cs3.13-05")]
        public void cs3_13_05()
        {
            using (var log = new Log())
            using (var loop = DispatcherLoop.Start())
            {
                Exception error = null;
                var done = new ManualResetEvent(false);
                loop.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                        WinFormsScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    done.Set();
                }));
                Harness.Wait(done, "winforms attach");
                Assert.IsType<ReactiveException>(error);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs3.14-01")]
        public void cs3_14_01()
        {
            using (var loop = DispatcherLoop.Start())
            {
                DispatcherScheduler first = null;
                DispatcherScheduler second = null;
                Scheduler current = null;
                Scheduler forThread = null;
                loop.Dispatcher.Invoke(new Action(() =>
                {
                    first = DispatcherScheduler.AttachToCurrent();
                    second = DispatcherScheduler.AttachToCurrent();
                    current = Scheduler.Current;
                    forThread = Scheduler.ForThread(Thread.CurrentThread);
                }));
                Assert.Same(loop.Scheduler, first);
                Assert.Same(first, second);
                Assert.Same(first, current);
                Assert.Same(first, forThread);
            }
        }

        [Fact(DisplayName = "cs3.15-01")]
        public void cs3_15_01()
        {
            using (var log = new Log())
            using (var loop = DispatcherLoop.Start())
            {
                var reactor = new Reactor(loop.Scheduler);
                bool inPump = false;
                bool ranInside = false;
                int r2Runs = 0;
                Reaction r2 = null;
                int r1Runs = 0;
                var r1Done = new ManualResetEvent(false);
                var r1 = reactor.Add(() =>
                {
                    r1Runs++;
                    if (r1Runs == 1)
                        return;
                    inPump = true;
                    try
                    {
                        r2.Trigger();
                        var frame = new DispatcherFrame();
                        Dispatcher.CurrentDispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(() => frame.Continue = false));
                        Dispatcher.PushFrame(frame);
                    }
                    finally
                    {
                        inPump = false;
                        r1Done.Set();
                    }
                });
                r2 = reactor.Add(() =>
                {
                    if (inPump)
                        ranInside = true;
                    r2Runs++;
                });
                Harness.Barrier(loop.Scheduler);
                Assert.Equal(1, r1Runs);
                Assert.Equal(1, r2Runs);
                log.Clear();
                r1.Trigger();
                Harness.Wait(r1Done, "nested pump");
                Assert.False(ranInside);
                Assert.Equal(1, log.Count);
                Assert.IsType<ReactiveException>(log.Snapshot()[0]);
            }
        }

        [Fact(DisplayName = "cs5.1-01")]
        public void cs5_1_01()
        {
            Assert.Empty(typeof(DispatcherScheduler).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact(DisplayName = "cs5.2-01")]
        public void cs5_2_01()
        {
            Scheduler seen = null;
            Dispatcher dispatcher = null;
            Thread initialThread = null;
            var s = DispatcherScheduler.CreateNew("initial", () =>
            {
                seen = Scheduler.Current;
                dispatcher = Dispatcher.CurrentDispatcher;
                initialThread = Thread.CurrentThread;
            });
            Assert.NotEqual(Thread.CurrentThread, initialThread);
            Assert.Equal("initial", initialThread.Name);
            Assert.Same(s, seen);
            Assert.NotNull(dispatcher);
            bool ran = false;
            Thread ranOn = null;
            s.Queue(() =>
            {
                ran = true;
                ranOn = Thread.CurrentThread;
            });
            Harness.Barrier(s);
            Assert.True(ran);
            Assert.Equal(initialThread, ranOn);
        }

        [Fact(DisplayName = "cs5.2-02")]
        public void cs5_2_02()
        {
            using (var log = new Log())
            {
                var x = new InvalidOperationException("init-x");
                Thread thread = null;
                var s = DispatcherScheduler.CreateNew("init-throws", () =>
                {
                    thread = Thread.CurrentThread;
                    throw x;
                });
                Assert.NotNull(s);
                Assert.Equal(1, log.Count);
                Assert.Same(x, log.Snapshot()[0]);
                bool ran = false;
                s.Queue(() => ran = true);
                Harness.Barrier(s);
                Assert.True(ran);
                s.Shutdown();
                Assert.True(thread.Join(Harness.TimeoutMs), "dispatcher thread did not end");
                AssertPromptShutdown();
            }
        }

        [Fact(DisplayName = "cs5.2-03")]
        public void cs5_2_03()
        {
            using (var log = new Log())
            {
                bool initial = false;
                Assert.Throws<ReactiveException>(() => DispatcherScheduler.CreateNew(null, () => initial = true));
                Assert.Throws<ReactiveException>(() => DispatcherScheduler.CreateNew("", () => initial = true));
                Assert.False(initial);
                Assert.Equal(0, log.Count);
                AssertPromptShutdown();
            }
        }

        [Fact(DisplayName = "cs5.2-04")]
        public void cs5_2_04()
        {
            Harness.AssertInitialIsFirst(initial => DispatcherScheduler.CreateNew("cs5.2-04", initial));
        }

        [Fact(DisplayName = "cs5.2-05")]
        public void cs5_2_05()
        {
            Thread initialThread = null;
            var s = DispatcherScheduler.CreateNew("cs5.2-05", () => initialThread = Thread.CurrentThread);
            bool ran = false;
            Thread ranOn = null;
            s.Queue(() =>
            {
                ran = true;
                ranOn = Thread.CurrentThread;
            });
            Harness.Barrier(s);
            Assert.True(ran);
            Assert.Equal(initialThread, ranOn);
        }

        [Fact(DisplayName = "cs5.2-06", Skip = "not testable through the public API (spec gap)")]
        public void cs5_2_06()
        {
        }

        [Fact(DisplayName = "cs5.3-01")]
        public void cs5_3_01()
        {
            using (var loop = DispatcherLoop.Start())
            {
                Thread ranOn = null;
                var done = new ManualResetEvent(false);
                loop.Scheduler.Queue(() =>
                {
                    ranOn = Thread.CurrentThread;
                    done.Set();
                });
                Harness.Wait(done, "dispatcher work");
                Assert.Equal(loop.Thread, ranOn);
            }
        }

        [Fact(DisplayName = "cs5.3-02")]
        public void cs5_3_02()
        {
            using (var loop = DispatcherLoop.Start())
            {
                DispatcherScheduler again = null;
                loop.Dispatcher.Invoke(new Action(() => again = DispatcherScheduler.AttachToCurrent()));
                Assert.Same(loop.Scheduler, again);
            }
        }

        [Fact(DisplayName = "cs5.4-01")]
        public void cs5_4_01()
        {
            using (var loop = DispatcherLoop.Start())
            {
                var entered = new ManualResetEvent(false);
                var release = new ManualResetEvent(false);
                try
                {
                    loop.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        entered.Set();
                        release.WaitOne();
                    }));
                    Harness.Wait(entered, "dispatcher busy");
                    var ran = new ManualResetEvent(false);
                    Thread ranOn = null;
                    var starter = new Thread(() =>
                    {
                        loop.Scheduler.Queue(() =>
                        {
                            ranOn = Thread.CurrentThread;
                            ran.Set();
                        });
                    });
                    starter.IsBackground = true;
                    starter.Start();
                    Assert.True(starter.Join(500), "Queue blocked while the dispatcher was busy");
                    Assert.False(ran.WaitOne(0));
                    release.Set();
                    Harness.Wait(ran, "work ran later");
                    Assert.Equal(loop.Thread, ranOn);
                }
                finally
                {
                    release.Set();
                }
            }
        }

        [Fact(DisplayName = "cs5.4-02")]
        public void cs5_4_02()
        {
            using (var loop = DispatcherLoop.Start())
            {
                var reg = new Register<int>(0);
                new Reactor(loop.Scheduler).Add(() => reg.Value = reg.Value + 1);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (reg.Value < 2 && clock.ElapsedMilliseconds < 2000)
                    Thread.SpinWait(100);
                Assert.True(reg.Value >= 2, "self-trigger did not start");
                var ran = new ManualResetEvent(false);
                loop.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => ran.Set()));
                Assert.True(ran.WaitOne(1000), "input-priority item did not run within 1 s");
                var done = new ManualResetEvent(false);
                var thread = new Thread(() =>
                {
                    loop.Scheduler.Shutdown();
                    done.Set();
                });
                thread.IsBackground = true;
                thread.Start();
                Assert.True(done.WaitOne(Harness.TimeoutMs), "Shutdown did not complete");
            }
        }

        [Fact(DisplayName = "cs6.1-01")]
        public void cs6_1_01()
        {
            using (var forms = WinFormsLoop.Start())
            {
                Thread ranOn = null;
                var done = new ManualResetEvent(false);
                forms.Scheduler.Queue(() =>
                {
                    ranOn = Thread.CurrentThread;
                    done.Set();
                });
                Harness.Wait(done, "winforms work");
                Assert.Equal(forms.Thread, ranOn);
            }
        }

        [Fact(DisplayName = "cs6.1-02")]
        public void cs6_1_02()
        {
            using (var forms = WinFormsLoop.Start())
            {
                WinFormsScheduler again = null;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    again = WinFormsScheduler.AttachToCurrent();
                    done.Set();
                }));
                Harness.Wait(done, "second attach");
                Assert.Same(forms.Scheduler, again);
            }
        }

        [Fact(DisplayName = "cs6.1-03")]
        public void cs6_1_03()
        {
            using (var log = new Log())
            {
                Exception error = null;
                var done = new ManualResetEvent(false);
                var plain = new Thread(() =>
                {
                    try
                    {
                        WinFormsScheduler.AttachToCurrent();
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
                plain.IsBackground = true;
                plain.Start();
                Harness.Wait(done, "plain attach");
                Assert.IsType<ReactiveException>(error);
                Assert.Equal(0, log.Count);

                log.Clear();
                error = null;
                var done2 = new ManualResetEvent(false);
                var other = new Thread(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                    try
                    {
                        WinFormsScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done2.Set();
                    }
                });
                other.IsBackground = true;
                other.Start();
                Harness.Wait(done2, "other context");
                Assert.IsType<ReactiveException>(error);
                Assert.Equal(0, log.Count);
            }
        }

        [Fact(DisplayName = "cs6.2-01")]
        public void cs6_2_01()
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            foreach (var method in typeof(WinFormsScheduler).GetMethods(flags))
            {
                foreach (var parameter in method.GetParameters())
                    Assert.False(typeof(SynchronizationContext).IsAssignableFrom(parameter.ParameterType), method.Name);
            }
            foreach (var ctor in typeof(WinFormsScheduler).GetConstructors(flags))
            {
                foreach (var parameter in ctor.GetParameters())
                    Assert.False(typeof(SynchronizationContext).IsAssignableFrom(parameter.ParameterType));
            }
        }

        [Fact(DisplayName = "cs6.3-01")]
        public void cs6_3_01()
        {
            using (var forms = WinFormsLoop.Start())
            {
                var entered = new ManualResetEvent(false);
                var release = new ManualResetEvent(false);
                try
                {
                    forms.Scheduler.Queue(() =>
                    {
                        entered.Set();
                        release.WaitOne();
                    });
                    Harness.Wait(entered, "ui busy");
                    var finished = new ManualResetEvent(false);
                    var thread = new Thread(() =>
                    {
                        forms.Scheduler.Queue(() => { });
                        finished.Set();
                    });
                    thread.IsBackground = true;
                    thread.Start();
                    Assert.True(finished.WaitOne(500), "Queue blocked");
                }
                finally
                {
                    release.Set();
                }
            }
        }

        [Fact(DisplayName = "cs6.3-02")]
        public void cs6_3_02()
        {
            using (var forms = WinFormsLoop.Start())
            {
                forms.Scheduler.Shutdown();
                bool ran = false;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    ran = true;
                    done.Set();
                }));
                Harness.Wait(done, "message after shutdown");
                Assert.True(ran);
                Assert.True(forms.Thread.IsAlive, "Application.Run ended");
            }
        }

        static void AssertPromptShutdown()
        {
            var done = new ManualResetEvent(false);
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Reactive.Shutdown();
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
            thread.IsBackground = true;
            thread.Start();
            Assert.True(done.WaitOne(2000), "Reactive.Shutdown did not return promptly");
            Assert.Null(error);
        }

    }

    sealed class DispatcherLoop : IDisposable
    {
        public Thread Thread;
        public Dispatcher Dispatcher;
        public DispatcherScheduler Scheduler;
        readonly ManualResetEvent _ready = new ManualResetEvent(false);
        Exception _error;

        public static DispatcherLoop Start()
        {
            var loop = new DispatcherLoop();
            var thread = new Thread(loop.Run);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Name = "acceptance-dispatcher";
            loop.Thread = thread;
            thread.Start();
            Harness.Wait(loop._ready, 10000, "dispatcher loop");
            if (loop._error != null)
                throw new InvalidOperationException("dispatcher loop failed", loop._error);
            return loop;
        }

        void Run()
        {
            try
            {
                Dispatcher = Dispatcher.CurrentDispatcher;
                Scheduler = DispatcherScheduler.AttachToCurrent();
                _ready.Set();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _error = ex;
                _ready.Set();
            }
        }

        public void Dispose()
        {
            try
            {
                if (Scheduler != null)
                    Scheduler.Shutdown();
            }
            catch (Exception)
            {
            }
            try
            {
                if (Dispatcher != null && !Dispatcher.HasShutdownStarted)
                    Dispatcher.InvokeShutdown();
            }
            catch (Exception)
            {
            }
            if (Thread != null)
                Thread.Join(Harness.TimeoutMs);
        }
    }

    sealed class WinFormsLoop : IDisposable
    {
        public Thread Thread;
        public Form Form;
        public WinFormsScheduler Scheduler;
        readonly ManualResetEvent _ready = new ManualResetEvent(false);
        Exception _error;

        public static WinFormsLoop Start()
        {
            var loop = new WinFormsLoop();
            var thread = new Thread(loop.Run);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Name = "acceptance-winforms";
            loop.Thread = thread;
            thread.Start();
            Harness.Wait(loop._ready, 15000, "winforms loop");
            if (loop._error != null)
                throw new InvalidOperationException("winforms loop failed", loop._error);
            return loop;
        }

        void Run()
        {
            try
            {
                Form = new Form();
                Form.ShowInTaskbar = false;
                Form.FormBorderStyle = FormBorderStyle.None;
                Form.StartPosition = FormStartPosition.Manual;
                Form.Location = new System.Drawing.Point(-20000, -20000);
                Form.Load += delegate
                {
                    try
                    {
                        Scheduler = WinFormsScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        _error = ex;
                    }
                    _ready.Set();
                };
                Application.Run(Form);
            }
            catch (Exception ex)
            {
                _error = ex;
                _ready.Set();
            }
        }

        public void Dispose()
        {
            try
            {
                if (Scheduler != null)
                    Scheduler.Shutdown();
            }
            catch (Exception)
            {
            }
            try
            {
                if (Form != null && Form.IsHandleCreated)
                    Form.BeginInvoke(new Action(Application.ExitThread));
            }
            catch (Exception)
            {
            }
            if (Thread != null)
                Thread.Join(Harness.TimeoutMs);
        }
    }
}

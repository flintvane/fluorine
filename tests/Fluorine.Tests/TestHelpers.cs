using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace Fluorine.Tests
{
    static class WaitUntil
    {
        public static void True(Func<bool> condition, string name)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(15))
                    throw new TimeoutException(name);
                Thread.Sleep(5);
            }
        }

        public static void Event(ManualResetEventSlim evt, string name)
        {
            if (!evt.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException(name);
        }
    }

    static class SkipLog
    {
        public static readonly ConcurrentBag<string> Entries = new ConcurrentBag<string>();

        public static void CouldNotRun(string test, string reason)
        {
            var line = test + ": " + reason;
            Entries.Add(line);
            Console.WriteLine("COULD NOT RUN " + line);
        }
    }

    sealed class ThreadHost : IDisposable
    {
        public ThreadScheduler Scheduler { get; private set; }

        public ThreadHost(Action initial = null)
        {
            Scheduler = ThreadScheduler.CreateNew("test", initial);
        }

        public Reactor NewReactor()
        {
            return new Reactor(Scheduler);
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

        public void Dispose()
        {
            if (Scheduler != null)
                Scheduler.Shutdown();
        }
    }

    sealed class ManualScheduler : Scheduler
    {
        readonly Queue<Action> _queue = new Queue<Action>();

        protected override void Schedule(Action action)
        {
            lock (this)
            {
                if (HasStartedShutdown)
                    return;
                _queue.Enqueue(action);
            }
        }

        protected override void DrainOnOwnerThread()
        {
            Pump();
        }

        public void Pump()
        {
            while (true)
            {
                Action work;
                lock (this)
                {
                    if (_queue.Count == 0)
                        return;
                    work = _queue.Dequeue();
                }
                work();
            }
        }

        public void ForceRun(Reaction reaction)
        {
            reaction.Run();
        }
    }

    sealed class WinFormsHost : IDisposable
    {
        readonly Thread _thread;
        readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        readonly ManualResetEventSlim _exited = new ManualResetEventSlim(false);
        Form _form;
        Exception _startError;

        public WinFormsScheduler Scheduler { get; private set; }
        public SynchronizationContext Context { get; private set; }

        public WinFormsHost()
        {
            _thread = new Thread(() =>
            {
                try
                {
                    WindowsFormsSynchronizationContext.AutoInstall = true;
                    _form = new Form
                    {
                        WindowState = FormWindowState.Minimized,
                        ShowInTaskbar = false,
                        Opacity = 0,
                        Width = 1,
                        Height = 1
                    };
                    var unused = _form.Handle;
                    if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                    {
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    }
                    Context = SynchronizationContext.Current;
                    Scheduler = WinFormsScheduler.AttachToCurrent();
                    _ready.Set();
                    Application.Run(_form);
                }
                catch (Exception ex)
                {
                    _startError = ex;
                    _ready.Set();
                }
                finally
                {
                    _exited.Set();
                }
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Name = "Fluorine WinForms test";
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("WinForms host did not start.");
            if (Scheduler == null)
                throw new InvalidOperationException("WinForms host failed: " + _startError);
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
            WaitUntil.Event(done, "WinForms OnScheduler");
            if (error != null)
                throw new Exception("Queued action failed.", error);
        }

        public void Dispose()
        {
            try
            {
                if (Scheduler != null && !Scheduler.HasStartedShutdown)
                    Scheduler.Shutdown();
            }
            catch
            {
            }
            try
            {
                var form = _form;
                var ctx = Context;
                if (form != null && ctx != null)
                {
                    ctx.Post(_ =>
                    {
                        try
                        {
                            if (!form.IsDisposed)
                                form.Close();
                            Application.ExitThread();
                        }
                        catch
                        {
                        }
                    }, null);
                }
            }
            catch
            {
            }
            _exited.Wait(TimeSpan.FromSeconds(10));
        }
    }

    sealed class EqThrower
    {
        public override bool Equals(object obj)
        {
            throw new InvalidOperationException("equality threw");
        }

        public override int GetHashCode()
        {
            return 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public sealed class ThreadScheduler : Scheduler
    {
        readonly Queue<Action> _queue = new Queue<Action>();

        ThreadScheduler()
        {
        }

        public static ThreadScheduler CreateNew(Action initial = null)
        {
            Exception error = null;
            ThreadScheduler created = null;
            var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                ThreadScheduler scheduler = null;
                try
                {
                    scheduler = new ThreadScheduler();
                    initial?.Invoke();
                    created = scheduler;
                }
                catch (Exception ex)
                {
                    error = ex;
                    scheduler?.AbandonBeforeStart();
                }
                finally
                {
                    ready.Set();
                }

                if (error != null || scheduler == null)
                    return;
                scheduler.RunLoop();
            });
            thread.IsBackground = true;
            thread.Name = "Fluorine ThreadScheduler";
            thread.Start();
            ready.Wait();
            if (error != null)
                throw error;
            return created;
        }

        protected override void Schedule(Action action)
        {
            if (action == null)
                return;
            lock (this)
            {
                if (HasStartedShutdown)
                    return;
                _queue.Enqueue(action);
                Monitor.Pulse(this);
            }
        }

        protected override void OnShutdownRequested()
        {
            lock (this)
                Monitor.PulseAll(this);
        }

        protected override void DrainOnOwnerThread()
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
                InvokeWork(work);
            }
        }

        void RunLoop()
        {
            while (true)
            {
                Action work;
                lock (this)
                {
                    while (_queue.Count == 0 && !HasStartedShutdown)
                        Monitor.Wait(this);
                    if (_queue.Count == 0)
                        break;
                    work = _queue.Dequeue();
                }
                InvokeWork(work);
            }
            CompleteShutdown();
        }

        void InvokeWork(Action work)
        {
            try
            {
                work?.Invoke();
            }
            catch (Exception ex)
            {
                Reactive.OnUnhandledException(ex);
            }
        }

        void AbandonBeforeStart()
        {
            RequestShutdown();
            CompleteShutdown();
        }
    }
}

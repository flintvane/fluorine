using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public sealed class ThreadScheduler : Scheduler
    {
        readonly Queue<Action> _queue = new Queue<Action>();

        ThreadScheduler(bool announceImmediately)
            : base(announceImmediately)
        {
        }

        public static ThreadScheduler CreateNew(string name, Action initial = null)
        {
            return StartOwnedThread(
                name,
                ApartmentState.MTA,
                typeof(ThreadScheduler),
                () => new ThreadScheduler(false),
                (scheduler, action) => scheduler.EnqueueStartup(action),
                scheduler => scheduler.RunLoop(),
                initial);
        }

        void EnqueueStartup(Action action)
        {
            lock (this)
            {
                _queue.Enqueue(action);
                Monitor.Pulse(this);
            }
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
    }
}

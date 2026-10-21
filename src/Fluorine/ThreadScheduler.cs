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
                    if (initial != null)
                        initial();
                    created = scheduler;
                }
                catch (Exception ex)
                {
                    error = ex;
                    if (scheduler != null)
                        scheduler.AbandonBeforeStart();
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
            {
                var wrapped = error as ReactiveException ?? new ReactiveException("CreateNew initial action failed.", error);
                Reactive.Raise(wrapped);
                throw error;
            }
            return created;
        }

        protected override void Schedule(Action action)
        {
            if (action == null)
                return;
            lock (Gate)
            {
                if (HasStartedShutdown)
                    return;
                _queue.Enqueue(action);
                Monitor.Pulse(Gate);
            }
        }

        protected override void OnShutdownRequested()
        {
            lock (Gate)
                Monitor.PulseAll(Gate);
        }

        protected override void DrainOnOwnerThread()
        {
            while (true)
            {
                Action work;
                lock (Gate)
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
                lock (Gate)
                {
                    while (_queue.Count == 0 && !HasStartedShutdown)
                        Monitor.Wait(Gate);
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
                if (work != null)
                    work();
            }
            catch (ReactiveException)
            {
            }
            catch (Exception ex)
            {
                Reactive.Raise(new ReactiveException("Unhandled exception on scheduler thread.", ex));
            }
        }

        void AbandonBeforeStart()
        {
            RequestShutdown();
            CompleteShutdown();
        }
    }
}

using System;
using System.Threading;
using System.Windows.Threading;

namespace Fluorine
{
    public sealed class DispatcherScheduler : Scheduler
    {
        [ThreadStatic] static int t_executing;

        readonly Dispatcher _dispatcher;
        readonly bool _ownsDispatcher;
        int _pending;
        DispatcherFrame _drainFrame;

        DispatcherScheduler(Dispatcher dispatcher, bool ownsDispatcher)
        {
            _dispatcher = dispatcher;
            _ownsDispatcher = ownsDispatcher;
        }

        public static DispatcherScheduler CreateNew(Action initial = null)
        {
            Exception error = null;
            DispatcherScheduler created = null;
            var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                DispatcherScheduler scheduler = null;
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    scheduler = new DispatcherScheduler(dispatcher, true);
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
                Dispatcher.Run();
            });
            thread.IsBackground = true;
            thread.Name = "Fluorine DispatcherScheduler";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            if (error != null)
            {
                var wrapped = error as ReactiveException ??
                              new ReactiveException("CreateNew initial action failed.", error);
                Reactive.Raise(wrapped);
                throw error;
            }

            return created;
        }

        public static DispatcherScheduler AttachToCurrent()
        {
            if (Current is DispatcherScheduler existing)
                return existing;
            if (Current != null)
            {
                var ex = new ReactiveException("This thread already has a scheduler of another type.");
                Reactive.Raise(ex);
                throw ex;
            }

            var dispatcher = Dispatcher.FromThread(Thread.CurrentThread) ?? Dispatcher.CurrentDispatcher;
            return new DispatcherScheduler(dispatcher, false);
        }

        protected override int ExecutingWorkDepth => t_executing;

        protected override int PendingWork => _pending;

        protected override void Schedule(Action action)
        {
            if (action == null)
                return;
            lock (Gate)
            {
                if (HasStartedShutdown)
                    return;
                _pending++;
            }

            try
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => InvokeWork(action)));
            }
            catch
            {
                NoteWorkFinished();
            }
        }

        protected override void OnShutdownRequested()
        {
            var postSentinel = false;
            lock (Gate)
            {
                if (_pending == 0 && !IsOwnerThread)
                    postSentinel = true;
            }

            if (!postSentinel)
                return;
            try
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(CompleteShutdown));
            }
            catch
            {
                CompleteShutdown();
            }
        }

        protected override void DrainOnOwnerThread()
        {
            PumpUntilPendingEquals(t_executing);
        }

        protected override void OnShutdownFinished()
        {
            var frame = _drainFrame;
            if (frame != null)
                frame.Continue = false;
            if (_ownsDispatcher)
            {
                _dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }
        }

        void InvokeWork(Action action)
        {
            t_executing++;
            try
            {
                if (action != null)
                    action();
            }
            catch (ReactiveException)
            {
            }
            catch (Exception ex)
            {
                Reactive.Raise(new ReactiveException("Unhandled exception on scheduler thread.", ex));
            }
            finally
            {
                t_executing--;
                NoteWorkFinished();
            }
        }

        void NoteWorkFinished()
        {
            var complete = false;
            lock (Gate)
            {
                if (_pending > 0)
                    _pending--;
                if (HasStartedShutdown && !IsFinished && _pending <= t_executing)
                {
                    if (t_executing == 0)
                        complete = true;
                    if (_drainFrame != null && _pending <= t_executing)
                        _drainFrame.Continue = false;
                }
            }

            if (complete)
                CompleteShutdown();
        }

        void PumpUntilPendingEquals(int target)
        {
            while (true)
            {
                lock (Gate)
                {
                    if (_pending <= target)
                        return;
                    _drainFrame = new DispatcherFrame();
                }

                Dispatcher.PushFrame(_drainFrame);
                lock (Gate)
                {
                    if (_pending <= target)
                        return;
                }
            }
        }

        void AbandonBeforeStart()
        {
            RequestShutdown();
            CompleteShutdown();
        }
    }
}
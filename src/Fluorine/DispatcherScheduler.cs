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

        DispatcherScheduler(Dispatcher dispatcher, bool ownsDispatcher, bool announceImmediately)
            : base(announceImmediately)
        {
            _dispatcher = dispatcher;
            _ownsDispatcher = ownsDispatcher;
        }

        public static DispatcherScheduler CreateNew(string name, Action initial = null)
        {
            return StartOwnedThread(
                name,
                ApartmentState.STA,
                typeof(DispatcherScheduler),
                () => new DispatcherScheduler(Dispatcher.CurrentDispatcher, true, false),
                (scheduler, action) => scheduler.EnqueueStartup(action),
                _ => Dispatcher.Run(),
                initial);
        }

        public static DispatcherScheduler AttachToCurrent()
        {
            if (Current is DispatcherScheduler existing)
                return existing;
            if (Current != null)
                throw new ReactiveException("This thread already has a scheduler of another type.");

            ClaimCurrentThread(typeof(DispatcherScheduler));
            try
            {
                return new DispatcherScheduler(Dispatcher.CurrentDispatcher, false, true);
            }
            finally
            {
                ReleaseClaim();
            }
        }

        void EnqueueStartup(Action action)
        {
            lock (this)
                _pending++;
            try
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => InvokeWork(action)));
            }
            catch
            {
                NoteWorkFinished();
                throw;
            }
        }

        protected override int ExecutingWorkDepth => t_executing;

        protected override int PendingWork => _pending;

        protected override void Schedule(Action action)
        {
            if (action == null)
                return;
            lock (this)
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
                throw;
            }
        }

        protected override void OnShutdownRequested()
        {
            var postSentinel = false;
            lock (this)
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
                throw;
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
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Reactive.OnUnhandledException(ex);
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
            lock (this)
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
                lock (this)
                {
                    if (_pending <= target)
                        return;
                    _drainFrame = new DispatcherFrame();
                }

                Dispatcher.PushFrame(_drainFrame);
                lock (this)
                {
                    if (_pending <= target)
                        return;
                }
            }
        }
    }
}
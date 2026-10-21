using System;
using System.Threading;
using System.Windows.Forms;

namespace Fluorine
{
    public sealed class WinFormsScheduler : Scheduler
    {
        [ThreadStatic]
        static int t_executing;

        readonly SynchronizationContext _context;
        int _pending;

        WinFormsScheduler(SynchronizationContext context)
        {
            _context = context;
        }

        public static WinFormsScheduler AttachToCurrent()
        {
            if (Current is WinFormsScheduler existing)
                return existing;
            if (Current != null)
                throw new ReactiveException("This thread already has a scheduler of another type.");

            var context = SynchronizationContext.Current as WindowsFormsSynchronizationContext;
            if (context == null)
                throw new ReactiveException("WinFormsScheduler requires a WindowsFormsSynchronizationContext on the calling thread.");
            return new WinFormsScheduler(context);
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
                _context.Post(_ => InvokeWork(action), null);
            }
            catch
            {
                NoteWorkFinished();
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
                _context.Post(_ => CompleteShutdown(), null);
            }
            catch
            {
                CompleteShutdown();
            }
        }

        protected override void DrainOnOwnerThread()
        {
            while (true)
            {
                int pending;
                lock (this)
                    pending = _pending;
                if (pending <= t_executing)
                    return;
                Application.DoEvents();
            }
        }

        protected override void OnShutdownFinished()
        {
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
                if (HasStartedShutdown && !IsFinished && _pending <= 0 && t_executing == 0)
                    complete = true;
            }
            if (complete)
                CompleteShutdown();
        }
    }
}

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
            var existing = Current as WinFormsScheduler;
            if (existing != null)
                return existing;
            if (Current != null)
            {
                var ex = new ReactiveException("This thread already has a scheduler of another type.");
                Reactive.Raise(ex);
                throw ex;
            }

            var context = SynchronizationContext.Current as WindowsFormsSynchronizationContext;
            if (context == null)
            {
                var ex = new ReactiveException("WinFormsScheduler requires a WindowsFormsSynchronizationContext on the calling thread.");
                Reactive.Raise(ex);
                throw ex;
            }
            return new WinFormsScheduler(context);
        }

        protected override int ExecutingWorkDepth
        {
            get { return t_executing; }
        }

        protected override int PendingWork
        {
            get { return _pending; }
        }

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
            lock (Gate)
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
                lock (Gate)
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
                if (HasStartedShutdown && !IsFinished && _pending <= 0 && t_executing == 0)
                    complete = true;
            }
            if (complete)
                CompleteShutdown();
        }
    }
}

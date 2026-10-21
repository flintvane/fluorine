using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public abstract class Scheduler
    {
        [ThreadStatic]
        static Scheduler t_current;

        [ThreadStatic]
        static int t_drainDepth;

        static readonly object StaticLock = new object();
        static readonly Dictionary<Thread, Scheduler> ByThread = new Dictionary<Thread, Scheduler>();
        static readonly List<Scheduler> All = new List<Scheduler>();

        protected readonly object Gate = new object();
        readonly HashSet<Reactor> _reactors = new HashSet<Reactor>();
        readonly List<Reaction> _deferred = new List<Reaction>();
        readonly ManualResetEventSlim _finished = new ManualResetEventSlim(false);

        const int StateRunning = 0;
        const int StateShuttingDown = 1;
        const int StateFinished = 2;

        int _state;
        Thread _ownerThread;

        protected Scheduler()
        {
            InstallOnCurrentThread();
        }

        public static Scheduler Current
        {
            get { return t_current; }
        }

        public static Scheduler ForThread(Thread thread)
        {
            if (thread == null)
                return null;
            lock (StaticLock)
            {
                Scheduler scheduler;
                if (ByThread.TryGetValue(thread, out scheduler))
                    return scheduler;
                return null;
            }
        }

        protected Thread OwnerThread
        {
            get { return _ownerThread; }
        }

        protected bool IsOwnerThread
        {
            get { return _ownerThread != null && Thread.CurrentThread == _ownerThread; }
        }

        internal bool HasStartedShutdown
        {
            get { return Volatile.Read(ref _state) >= StateShuttingDown; }
        }

        internal bool IsFinished
        {
            get { return Volatile.Read(ref _state) == StateFinished; }
        }

        public void Queue(Action action)
        {
            try
            {
                if (action == null)
                    return;
                Schedule(() => RunUserAction(action));
            }
            catch
            {
            }
        }

        public void Queue(Reaction reaction)
        {
            try
            {
                if (reaction == null)
                    return;
                Schedule(() => RunReaction(reaction));
            }
            catch
            {
            }
        }

        public void Shutdown()
        {
            ThrowIfInsideReaction();
            RequestShutdown();
            if (IsOwnerThread)
                DrainAndFinishOnOwnerThread();
            if (t_drainDepth > 0 && IsOwnerThread)
                return;
            _finished.Wait();
        }

        internal static Scheduler[] SnapshotAll()
        {
            lock (StaticLock)
                return All.ToArray();
        }

        internal void RequestShutdown()
        {
            var first = false;
            lock (Gate)
            {
                if (_state >= StateShuttingDown)
                    return;
                _state = StateShuttingDown;
                first = true;
            }
            if (first)
                OnShutdownRequested();
        }

        internal void WaitUntilFinished()
        {
            _finished.Wait();
        }

        internal void DrainAndFinishOnOwnerThread()
        {
            if (!IsOwnerThread)
                return;
            if (t_drainDepth > 0)
                return;
            t_drainDepth++;
            try
            {
                DrainOnOwnerThread();
                CompleteShutdown();
            }
            finally
            {
                t_drainDepth--;
            }
        }

        internal bool TryAddReactor(Reactor reactor)
        {
            lock (Gate)
            {
                if (_state >= StateShuttingDown)
                    return false;
                _reactors.Add(reactor);
                return true;
            }
        }

        internal void RemoveReactor(Reactor reactor)
        {
            lock (Gate)
                _reactors.Remove(reactor);
        }

        protected abstract void Schedule(Action action);

        protected virtual void OnShutdownRequested()
        {
        }

        protected virtual void DrainOnOwnerThread()
        {
        }

        protected virtual void OnShutdownFinished()
        {
        }

        protected virtual int ExecutingWorkDepth
        {
            get { return 0; }
        }

        protected virtual int PendingWork
        {
            get { return 0; }
        }

        protected void RunUserAction(Action action)
        {
            try
            {
                if (action != null)
                    action();
            }
            catch (Exception ex)
            {
                Reactive.Raise(new ReactiveException("Unhandled exception from queued action.", ex));
            }
        }

        protected void RunReaction(Reaction reaction)
        {
            if (reaction == null)
                return;
            if (Reaction.Current != null)
            {
                var ex = new ReactiveException("A second reaction cannot start while another reaction is running on this thread.");
                Reactive.Raise(ex);
                lock (_deferred)
                    _deferred.Add(reaction);
                throw ex;
            }

            try
            {
                reaction.Run();
            }
            finally
            {
                FlushDeferred();
            }
        }

        protected bool AcceptWork()
        {
            lock (Gate)
                return _state < StateShuttingDown;
        }

        protected void CompleteShutdown()
        {
            lock (Gate)
            {
                if (_state == StateFinished)
                    return;
                _state = StateFinished;
            }

            DisposeAllReactors();
            Uninstall();
            try
            {
                OnShutdownFinished();
            }
            catch
            {
            }
            _finished.Set();
        }

        protected void InstallOnCurrentThread()
        {
            var existing = t_current;
            if (existing != null && existing != this)
            {
                var ex = new ReactiveException(
                    existing.GetType() == GetType()
                        ? "This thread already has a scheduler."
                        : "This thread already has a scheduler of another type.");
                Reactive.Raise(ex);
                throw ex;
            }

            _ownerThread = Thread.CurrentThread;
            t_current = this;
            lock (StaticLock)
            {
                Scheduler previous;
                if (ByThread.TryGetValue(_ownerThread, out previous) && previous != this)
                {
                    var ex = new ReactiveException(
                        previous.GetType() == GetType()
                            ? "This thread already has a scheduler."
                            : "This thread already has a scheduler of another type.");
                    Reactive.Raise(ex);
                    throw ex;
                }
                ByThread[_ownerThread] = this;
                if (!All.Contains(this))
                    All.Add(this);
            }
        }

        void FlushDeferred()
        {
            Reaction[] snapshot;
            lock (_deferred)
            {
                if (_deferred.Count == 0)
                    return;
                snapshot = _deferred.ToArray();
                _deferred.Clear();
            }
            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    Queue(snapshot[i]);
                }
                catch
                {
                }
            }
        }

        void DisposeAllReactors()
        {
            Reactor[] snapshot;
            lock (Gate)
            {
                snapshot = new Reactor[_reactors.Count];
                _reactors.CopyTo(snapshot);
                _reactors.Clear();
            }
            for (var i = 0; i < snapshot.Length; i++)
                snapshot[i].DisposeFromShutdown();
        }

        void Uninstall()
        {
            if (t_current == this)
                t_current = null;
            lock (StaticLock)
            {
                Scheduler mapped;
                if (_ownerThread != null && ByThread.TryGetValue(_ownerThread, out mapped) && mapped == this)
                    ByThread.Remove(_ownerThread);
                All.Remove(this);
            }
        }

        static void ThrowIfInsideReaction()
        {
            if (Reaction.Current == null)
                return;
            var ex = new ReactiveException("Cannot shut down from inside a reaction.");
            Reactive.Raise(ex);
            throw ex;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public abstract class Scheduler
    {
        [ThreadStatic] static Scheduler t_current;

        [ThreadStatic] static int t_drainDepth;

        static readonly object StaticLock = new object();
        static readonly Dictionary<Thread, Scheduler> ByThread = new Dictionary<Thread, Scheduler>();
        static readonly List<Scheduler> All = new List<Scheduler>();

        protected readonly object Gate = new object();
        readonly HashSet<Reactor> _reactors = new HashSet<Reactor>();
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

        public static Scheduler Current => t_current;

        public static Scheduler ForThread(Thread thread)
        {
            if (thread == null)
                return null;
            lock (StaticLock)
            {
                if (ByThread.TryGetValue(thread, out var scheduler))
                    return scheduler;
                return null;
            }
        }

        protected Thread OwnerThread => _ownerThread;

        protected bool IsOwnerThread => _ownerThread != null && Thread.CurrentThread == _ownerThread;

        internal bool HasStartedShutdown => Volatile.Read(ref _state) >= StateShuttingDown;

        internal bool IsFinished => Volatile.Read(ref _state) == StateFinished;

        public void Queue(Action action)
        {
            if (action == null)
                return;
            Schedule(() => RunUserAction(action));
        }

        public void Queue(Reaction reaction)
        {
            if (reaction == null)
                return;
            Schedule(reaction.Run);
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
            lock (Gate)
            {
                if (_state >= StateShuttingDown)
                    return;
                _state = StateShuttingDown;
            }
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

        protected virtual int ExecutingWorkDepth => 0;

        protected virtual int PendingWork => 0;

        protected void RunUserAction(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Reactive.OnUnhandledException(ex);
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
            OnShutdownFinished();
            _finished.Set();
        }

        protected void InstallOnCurrentThread()
        {
            var existing = t_current;
            if (existing != null && existing != this)
                throw new ReactiveException(
                    existing.GetType() == GetType()
                        ? "This thread already has a scheduler."
                        : "This thread already has a scheduler of another type.");

            _ownerThread = Thread.CurrentThread;
            t_current = this;
            lock (StaticLock)
            {
                if (ByThread.TryGetValue(_ownerThread, out var previous) && previous != this)
                    throw new ReactiveException(
                        previous.GetType() == GetType()
                            ? "This thread already has a scheduler."
                            : "This thread already has a scheduler of another type.");

                ByThread[_ownerThread] = this;
                if (!All.Contains(this))
                    All.Add(this);
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

            foreach (var t in snapshot)
                t.DisposeFromShutdown();
        }

        void Uninstall()
        {
            if (t_current == this)
                t_current = null;
            lock (StaticLock)
            {
                if (_ownerThread != null && ByThread.TryGetValue(_ownerThread, out var mapped) && mapped == this)
                    ByThread.Remove(_ownerThread);
                All.Remove(this);
            }
        }

        static void ThrowIfInsideReaction()
        {
            if (Reaction.Current == null)
                return;
            throw new ReactiveException("Cannot shut down from inside a reaction.");
        }
    }
}
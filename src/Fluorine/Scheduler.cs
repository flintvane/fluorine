using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public abstract class Scheduler
    {
        [ThreadStatic] static Scheduler t_current;

        [ThreadStatic] static Type t_claimType;

        [ThreadStatic] static int t_drainDepth;

        static readonly object StaticLock = new object();
        static readonly Dictionary<Thread, Scheduler> ByThread = new Dictionary<Thread, Scheduler>();
        static readonly List<Scheduler> All = new List<Scheduler>();

        readonly HashSet<Reactor> _reactors = new HashSet<Reactor>();
        readonly ManualResetEventSlim _finished = new ManualResetEventSlim(false);

        const int StateRunning = 0;
        const int StateShuttingDown = 1;
        const int StateFinished = 2;

        int _state;
        Thread _ownerThread;

        protected Scheduler()
            : this(true)
        {
        }

        internal Scheduler(bool announceImmediately)
        {
            if (announceImmediately)
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
            lock (this)
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
            lock (this)
            {
                if (_state >= StateShuttingDown)
                    return false;
                _reactors.Add(reactor);
                return true;
            }
        }

        internal void RemoveReactor(Reactor reactor)
        {
            lock (this)
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
            lock (this)
                return _state < StateShuttingDown;
        }

        protected void CompleteShutdown()
        {
            lock (this)
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
            AdoptCurrentThread(true);
        }

        internal void BindWithoutPublishing()
        {
            AdoptCurrentThread(false);
        }

        internal void Publish()
        {
            var thread = _ownerThread;
            if (thread == null || t_current != this)
                throw new ReactiveException("Could not create the scheduler.");

            lock (StaticLock)
            {
                if (ByThread.TryGetValue(thread, out var mapped) && mapped != this)
                {
                    if (t_current == this)
                        t_current = null;
                    _ownerThread = null;
                    throw AlreadyHas(mapped.GetType(), GetType());
                }

                ByThread[thread] = this;
                if (!All.Contains(this))
                    All.Add(this);
            }
        }

        internal void AbandonUnstarted()
        {
            RequestShutdown();
            CompleteShutdown();
        }

        internal static void ClaimCurrentThread(Type schedulerType)
        {
            ThrowIfOccupied(schedulerType);
            t_claimType = schedulerType;
        }

        internal static void ReleaseClaim()
        {
            t_claimType = null;
        }

        // The first message is queued before publication. Reactive.Shutdown can see a
        // scheduler as soon as it is announced, which is before CreateNew returns.
        protected static T StartOwnedThread<T>(
            string name,
            ApartmentState apartment,
            Type schedulerType,
            Func<T> construct,
            Action<T, Action> enqueueFirst,
            Action<T> runLoop,
            Action initial) where T : Scheduler
        {
            if (string.IsNullOrEmpty(name))
                throw new ReactiveException("CreateNew requires a thread name.");

            Exception error = null;
            T created = null;
            var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                T scheduler = null;
                try
                {
                    ClaimCurrentThread(schedulerType);
                    try
                    {
                        scheduler = construct();
                        scheduler.BindWithoutPublishing();
                        enqueueFirst(scheduler, () =>
                        {
                            try
                            {
                                if (initial != null)
                                    initial();
                            }
                            catch (Exception ex)
                            {
                                Reactive.OnUnhandledException(ex);
                            }

                            ready.Set();
                        });
                        scheduler.Publish();
                        created = scheduler;
                    }
                    catch (Exception ex)
                    {
                        error = AsInitializationFailure(ex);
                        try
                        {
                            if (scheduler != null)
                                scheduler.AbandonUnstarted();
                        }
                        catch (Exception)
                        {
                        }

                        ReleaseClaim();
                        ready.Set();
                        return;
                    }

                    try
                    {
                        runLoop(scheduler);
                    }
                    catch (Exception ex)
                    {
                        Reactive.OnUnhandledException(ex);
                        try
                        {
                            if (!scheduler.IsFinished)
                                scheduler.AbandonUnstarted();
                        }
                        catch (Exception)
                        {
                        }

                        if (!ready.IsSet)
                        {
                            error = AsInitializationFailure(ex);
                            ready.Set();
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = AsInitializationFailure(ex);
                    ReleaseClaim();
                    ready.Set();
                }
            });

            thread.IsBackground = true;
            thread.Name = name;
            thread.SetApartmentState(apartment);
            thread.Start();
            ready.Wait();

            if (error != null)
                throw error;
            return created;
        }

        static Exception AsInitializationFailure(Exception exception)
        {
            var reactive = exception as ReactiveException;
            if (reactive != null)
                return reactive;
            return new ReactiveException("Could not create the scheduler.", exception);
        }

        static void ThrowIfOccupied(Type incoming)
        {
            var existing = t_current;
            if (existing == null)
            {
                lock (StaticLock)
                {
                    if (ByThread.TryGetValue(Thread.CurrentThread, out var mapped))
                        existing = mapped;
                }
            }

            if (existing != null)
                throw AlreadyHas(existing.GetType(), incoming);
            if (t_claimType != null)
                throw AlreadyHas(t_claimType, incoming);
        }

        static ReactiveException AlreadyHas(Type existing, Type incoming)
        {
            if (existing == incoming)
                return new ReactiveException("This thread already has a scheduler.");
            return new ReactiveException("This thread already has a scheduler of another type.");
        }

        void AdoptCurrentThread(bool publish)
        {
            var thread = Thread.CurrentThread;
            if (t_current != null && t_current != this)
                throw AlreadyHas(t_current.GetType(), GetType());

            lock (StaticLock)
            {
                if (ByThread.TryGetValue(thread, out var mapped) && mapped != this)
                    throw AlreadyHas(mapped.GetType(), GetType());

                _ownerThread = thread;
                t_current = this;
                t_claimType = null;
                if (!publish)
                    return;

                ByThread[thread] = this;
                if (!All.Contains(this))
                    All.Add(this);
            }
        }

        void DisposeAllReactors()
        {
            Reactor[] snapshot;
            lock (this)
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
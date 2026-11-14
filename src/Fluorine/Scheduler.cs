using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public abstract class Scheduler
    {
        [ThreadStatic] static Scheduler _tCurrent;

        [ThreadStatic] static Type _tClaimType;

        [ThreadStatic] static int _tDrainDepth;

        static readonly object StaticLock = new();
        static readonly Dictionary<Thread, Scheduler> ByThread = new();
        static readonly List<Scheduler> All = [];

        readonly HashSet<Reactor> _reactors = [];
        readonly ManualResetEventSlim _finished = new(false);

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

        public static Scheduler Current => _tCurrent;

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

        /// <summary>
        /// This redirect allows the author of a scheduler, to perform the effective run of a reaction
        /// It is here to hide the internal Reaction.Run, which might otherwise be abused by the API
        /// users
        /// </summary>
        protected void Run(Reaction reaction)
            => reaction.Run();

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
            Schedule(() => Run(reaction));
        }

        public void Shutdown()
        {
            ThrowIfInsideReaction();
            RequestShutdown();
            if (IsOwnerThread)
                DrainAndFinishOnOwnerThread();
            if (_tDrainDepth > 0 && IsOwnerThread)
                return;
            _finished.Wait();
        }

        internal static Scheduler[] SnapshotAll()
        {
            lock (StaticLock)
                return [.. All];
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
            if (_tDrainDepth > 0)
                return;
            _tDrainDepth++;
            try
            {
                DrainOnOwnerThread();
                CompleteShutdown();
            }
            finally
            {
                _tDrainDepth--;
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
            lock (StaticLock)
            {
                if (ByThread.TryGetValue(thread, out var mapped) && mapped != this)
                {
                    if (_tCurrent == this)
                        _tCurrent = null;
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
            _tClaimType = schedulerType;
        }

        internal static void ReleaseClaim()
        {
            _tClaimType = null;
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
                                initial?.Invoke();
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
                        error = ex;
                        try
                        {
                            scheduler?.AbandonUnstarted();
                        }
                        catch (Exception nested)
                        {
                            error = new AggregateException(ex, nested);
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
                        catch (Exception e2)
                        {
                            Reactive.OnUnhandledException(e2);
                        }

                        if (!ready.IsSet)
                        {
                            error = ex;
                            ready.Set();
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    ReleaseClaim();
                    ready.Set();
                }
            });

            thread.IsBackground = true;
            thread.Name = name;
            // Only the Windows UI schedulers ask for STA; MTA works on every platform.
#pragma warning disable CA1416
            thread.SetApartmentState(apartment);
#pragma warning restore CA1416
            thread.Start();
            ready.Wait();

            if (error != null)
                throw error;
            return created;
        }

        static void ThrowIfOccupied(Type incoming)
        {
            var existing = _tCurrent;
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
            if (_tClaimType != null)
                throw AlreadyHas(_tClaimType, incoming);
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
            if (_tCurrent != null && _tCurrent != this)
                throw AlreadyHas(_tCurrent.GetType(), GetType());

            lock (StaticLock)
            {
                if (ByThread.TryGetValue(thread, out var mapped) && mapped != this)
                    throw AlreadyHas(mapped.GetType(), GetType());

                _ownerThread = thread;
                _tCurrent = this;
                _tClaimType = null;
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
            if (_tCurrent == this)
                _tCurrent = null;
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
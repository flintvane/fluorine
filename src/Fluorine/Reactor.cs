using System;
using System.Collections.Generic;

namespace Fluorine
{
    public class Reactor : IDisposable
    {
        readonly Scheduler _scheduler;
        readonly HashSet<Reaction> _reactions = new HashSet<Reaction>();
        int _disposed;

        public Reactor()
            : this(Scheduler.Current)
        {
        }

        public Reactor(Scheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ReactiveException("A reactor requires a scheduler.");
            if (!scheduler.TryAddReactor(this))
                _disposed = 1;
        }

        public string Name { get; set; }

        public void Queue(Action action)
        {
            if (_disposed != 0)
                return;
            _scheduler.Queue(action);
        }

        internal void Queue(Reaction reaction)
        {
            if (reaction == null)
                return;
            _scheduler.Queue(reaction);
        }

        public Reaction Add(Action lambda)
        {
            return Add(null, lambda);
        }

        public Reaction Add(string name, Action lambda)
        {
            if (_disposed != 0)
                return null;
            if (_scheduler != null && _scheduler.HasStartedShutdown)
                return null;

            var reaction = new Reaction(this, name, lambda ?? (() => { }));
            lock (this)
            {
                if (_disposed != 0)
                    return null;
                _reactions.Add(reaction);
            }
            reaction.RequestRun();
            return reaction;
        }

        public Reaction Computed<T>(Register<T> register, Func<T> func, bool disposeOld = false)
        {
            return Add(() =>
            {
                var result = func != null ? func() : default(T);
                if (!disposeOld)
                {
                    register.SetValue(result);
                    return;
                }

                if (register.SetValue(result, out var oldValue) && oldValue is IDisposable disposable)
                    disposable.Dispose();
            });
        }

        virtual public void Dispose()
        {
            DisposeCore(false);
        }

        internal void DisposeFromShutdown()
        {
            DisposeCore(true);
        }

        void DisposeCore(bool fromShutdown)
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                if (!fromShutdown)
                    return;
            }

            Reaction[] snapshot;
            lock (this)
            {
                snapshot = new Reaction[_reactions.Count];
                _reactions.CopyTo(snapshot);
                _reactions.Clear();
            }

            foreach (var t in snapshot)
            {
                if (fromShutdown)
                    t.UnsubscribeStale(0); // stamps are run counts, never 0
                t.Dispose();
            }

            _scheduler?.RemoveReactor(this);
        }

        internal void Remove(Reaction reaction)
        {
            lock (this)
                _reactions.Remove(reaction);
        }
    }
}

using System;
using System.Collections.Generic;

namespace Fluorine
{
    public sealed class Reactor : IDisposable
    {
        readonly Scheduler _scheduler;
        readonly HashSet<Reaction> _reactions = new HashSet<Reaction>();
        readonly object _sync = new object();
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
            lock (_sync)
            {
                if (_disposed != 0)
                    return null;
                _reactions.Add(reaction);
            }
            reaction.RequestRun();
            return reaction;
        }

        public void Dispose()
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
            lock (_sync)
            {
                snapshot = new Reaction[_reactions.Count];
                _reactions.CopyTo(snapshot);
                _reactions.Clear();
            }

            foreach (var t in snapshot)
            {
                if (fromShutdown)
                {
                    t.MarkDeletionCandidates();
                    t.UnsubscribeStale();
                }
                t.Dispose();
            }

            _scheduler?.RemoveReactor(this);
        }

        internal void Remove(Reaction reaction)
        {
            lock (_sync)
                _reactions.Remove(reaction);
        }
    }
}

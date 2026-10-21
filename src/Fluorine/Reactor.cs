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
            if (scheduler == null)
            {
                var ex = new ReactiveException("A reactor requires a scheduler.");
                Reactive.Raise(ex);
                throw ex;
            }

            _scheduler = scheduler;
            if (!scheduler.TryAddReactor(this))
                _disposed = 1;
        }

        public string Name { get; set; }

        public void Queue(Action action)
        {
            try
            {
                if (_disposed != 0)
                    return;
                _scheduler.Queue(action);
            }
            catch
            {
            }
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

            var reaction = new Reaction(this, _scheduler, name, lambda ?? new Action(() => { }));
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

            for (var i = 0; i < snapshot.Length; i++)
            {
                if (fromShutdown)
                    snapshot[i].DisconnectFromRegisters();
                snapshot[i].Dispose();
            }

            if (_scheduler != null)
                _scheduler.RemoveReactor(this);
        }

        internal void Remove(Reaction reaction)
        {
            lock (_sync)
                _reactions.Remove(reaction);
        }
    }
}

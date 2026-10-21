using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public sealed class Reaction : IDisposable
    {
        [ThreadStatic]
        static Reaction t_current;

        readonly Reactor _reactor;
        readonly Scheduler _scheduler;
        readonly Dictionary<Register, bool> _deps = new Dictionary<Register, bool>();
        Action _lambda;
        int _queued;
        int _disposed;
        int _disposeOnce;
        int _pauseCount;
        int _executionCount;

        internal Reaction(Reactor reactor, Scheduler scheduler, string name, Action lambda)
        {
            _reactor = reactor;
            _scheduler = scheduler;
            Name = name;
            _lambda = lambda ?? new Action(() => { });
        }

        public static Reaction Current
        {
            get { return t_current; }
        }

        public static bool Recording
        {
            get
            {
                var current = t_current;
                return current != null && current._pauseCount == 0;
            }
        }

        public string Name { get; set; }

        public int ExecutionCount
        {
            get { return Volatile.Read(ref _executionCount); }
        }

        public static void PauseRecording()
        {
            var current = t_current;
            if (current == null)
                return;
            current._pauseCount++;
        }

        public static void ContinueRecording()
        {
            var current = t_current;
            if (current == null)
                return;
            if (current._pauseCount <= 0)
            {
                Reactive.Raise(new ReactiveException("ContinueRecording called with a pause count of zero."));
                return;
            }
            current._pauseCount--;
        }

        public static IDisposable Pause()
        {
            PauseRecording();
            return new PauseScope();
        }

        public static void Pause(Action action)
        {
            PauseRecording();
            try
            {
                if (action != null)
                    action();
            }
            finally
            {
                ContinueRecording();
            }
        }

        public static T Pause<T>(Func<T> func)
        {
            PauseRecording();
            try
            {
                if (func == null)
                    return default(T);
                return func();
            }
            finally
            {
                ContinueRecording();
            }
        }

        public void Trigger()
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                RequestRun();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeOnce, 1) != 0)
                return;
            _lambda = null;
            Volatile.Write(ref _disposed, 1);
            if (_reactor != null)
                _reactor.Remove(this);
            RequestRun();
        }

        internal bool IsDisposed
        {
            get { return Volatile.Read(ref _disposed) != 0; }
        }

        internal void RequestRun()
        {
            try
            {
                if (Interlocked.CompareExchange(ref _queued, 1, 0) == 0)
                    _scheduler.Queue(this);
            }
            catch
            {
            }
        }

        internal void Subscribe(Register register)
        {
            if (register == null)
                return;
            if (Volatile.Read(ref _disposed) != 0)
                return;
            bool marked;
            if (_deps.TryGetValue(register, out marked))
            {
                _deps[register] = false;
                return;
            }
            _deps[register] = false;
            register.AddListener(this);
        }

        internal void DisconnectFromRegisters()
        {
            if (_deps.Count == 0)
                return;
            var keys = new Register[_deps.Count];
            _deps.Keys.CopyTo(keys, 0);
            _deps.Clear();
            for (var i = 0; i < keys.Length; i++)
                keys[i].RemoveListener(this);
        }

        internal void Run()
        {
            Interlocked.Exchange(ref _queued, 0);
            var lambda = _lambda;
            if (lambda == null)
            {
                DisconnectFromRegisters();
                return;
            }

            MarkDeletionCandidates();
            t_current = this;
            _pauseCount = 0;
            try
            {
                lambda();
            }
            catch (Exception ex)
            {
                Reactive.Raise(new ReactiveException("Unhandled exception from reaction.", ex));
            }
            finally
            {
                _pauseCount = 0;
                t_current = null;
                if (Volatile.Read(ref _disposed) != 0)
                    DisconnectFromRegisters();
                else
                    UnsubscribeStale();
                Interlocked.Increment(ref _executionCount);
            }
        }

        void MarkDeletionCandidates()
        {
            if (_deps.Count == 0)
                return;
            var keys = new Register[_deps.Count];
            _deps.Keys.CopyTo(keys, 0);
            for (var i = 0; i < keys.Length; i++)
                _deps[keys[i]] = true;
        }

        void UnsubscribeStale()
        {
            if (_deps.Count == 0)
                return;
            List<Register> stale = null;
            foreach (var pair in _deps)
            {
                if (!pair.Value)
                    continue;
                if (stale == null)
                    stale = new List<Register>();
                stale.Add(pair.Key);
            }
            if (stale == null)
                return;
            for (var i = 0; i < stale.Count; i++)
            {
                var register = stale[i];
                _deps.Remove(register);
                register.RemoveListener(this);
            }
        }

        sealed class PauseScope : IDisposable
        {
            int _done;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) != 0)
                    return;
                ContinueRecording();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public sealed class Reaction : IDisposable
    {
        [ThreadStatic]
        static Reaction t_current;
        public static Reaction Current => t_current;

        [ThreadStatic]
        static int t_pause;
        public static bool IsRecording => t_current != null && t_pause == 0;

        readonly Reactor _reactor;
        readonly Scheduler _scheduler;
        readonly Dictionary<Register, bool> _deps = new Dictionary<Register, bool>();
        Action _lambda;
        int _queued;
        int _disposed;
        int _disposeOnce;
        int _executionCount;

        internal Reaction(Reactor reactor, Scheduler scheduler, string name, Action lambda)
        {
            _reactor = reactor;
            _scheduler = scheduler;
            Name = name;
            _lambda = lambda ?? (() => { });
        }

        public string Name { get; set; }

        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void PauseRecording()
        {
            if (t_current == null)
                return;
            t_pause++;
        }

        public static void ContinueRecording()
        {
            if (t_current == null)
                return;
            if (t_pause <= 0)
            {
                Reactive.Raise(new ReactiveException("ContinueRecording called with a pause count of zero."));
                return;
            }
            t_pause--;
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
                action?.Invoke();
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
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                RequestRun();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeOnce, 1) != 0)
                return;
            _lambda = null;
            Volatile.Write(ref _disposed, 1);
            _reactor?.Remove(this);
            RequestRun();
        }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal void RequestRun()
        {
            if (Interlocked.CompareExchange(ref _queued, 1, 0) == 0)
                _scheduler.Queue(this);
        }

        internal void Subscribe(Register register)
        {
            if (register == null)
                return;
            if (Volatile.Read(ref _disposed) != 0)
                return;
            if (_deps.TryGetValue(register, out _))
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
            t_pause = 0;
            t_current = this;
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
                t_pause = 0;
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

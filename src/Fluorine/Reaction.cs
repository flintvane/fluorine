using System;
using System.Collections.Generic;
using System.Threading;

namespace Fluorine
{
    public sealed class Reaction : IDisposable
    {
        [ThreadStatic] static Reaction t_current;
        public static Reaction Current => t_current;

        [ThreadStatic] static int t_pause;
        public static bool IsRecording => t_current != null && t_pause == 0;

        readonly Reactor _reactor;
        readonly Dictionary<Register, int> _deps = new Dictionary<Register, int>();
        Action _lambda;
        int _queued;
        int _executionCount;

        internal Reaction(Reactor reactor, string name, Action lambda)
        {
            Name = name;
            _reactor = reactor;
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
                throw new ReactiveException("ContinueRecording called with a pause count of zero.");
            t_pause--;
        }

        public static PauseScope Pause()
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
                return func();
            }
            finally
            {
                ContinueRecording();
            }
        }

        public void Trigger()
        {
            if (Volatile.Read(ref _lambda) == null)
                return;
            RequestRun();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _lambda, null) == null)
                return;
            _reactor.Remove(this);
            RequestRun();
        }

        internal void RequestRun()
        {
            if (Interlocked.CompareExchange(ref _queued, 1, 0) == 0)
                _reactor.Queue(this);
        }

        internal void Subscribe(Register register)
        {
            if (_deps.TryGetValue(register, out _))
            {
                _deps[register] = _executionCount;
                return;
            }

            _deps.Add(register, _executionCount);
            register.AddListener(this);
        }

        internal void Run()
        {
            if (Current != null)
                throw new ReactiveException(
                    "A second reaction cannot start while another reaction is running on this thread.");
            Interlocked.Exchange(ref _queued, 0);
            Interlocked.Increment(ref _executionCount);
            var lambda = Volatile.Read(ref _lambda);
            t_pause = 0;
            t_current = this;
            try
            {
                lambda?.Invoke();
            }
            catch (Exception ex)
            {
                Reactive.OnUnhandledException(ex);
            }
            finally
            {
                UnsubscribeStale(_executionCount);
                t_pause = 0;
                t_current = null;
            }
        }

        internal void UnsubscribeStale(int keep)
        {
            if (_deps.Count == 0) return;
            List<Register> stale = null;
            foreach (var pair in _deps)
                if (pair.Value != keep)
                {
                    stale = stale ?? new List<Register>();
                    stale.Add(pair.Key);
                }

            if (stale == null) return;
            foreach (var register in stale)
            {
                _deps.Remove(register);
                register.RemoveListener(this);
            }
        }

        public struct PauseScope : IDisposable
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Fluorine.Acceptance
{
    public abstract class AcceptanceTest : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (Reaction.Current != null)
                    return;
                Reactive.Shutdown();
            }
            catch (Exception)
            {
            }
        }
    }

    public static class Harness
    {
        public const int TimeoutMs = 5000;
        public const int Stress = 10000;

        public static ThreadScheduler Create()
        {
            return ThreadScheduler.CreateNew("acceptance");
        }

        public static void Barrier(Scheduler scheduler)
        {
            var evt = new ManualResetEvent(false);
            scheduler.Queue(() => evt.Set());
            Wait(evt, "Barrier");
        }

        public static void Barrier2(Scheduler scheduler)
        {
            Barrier(scheduler);
            Barrier(scheduler);
        }

        public static Thread ThreadOf(Scheduler scheduler)
        {
            Thread thread = null;
            var evt = new ManualResetEvent(false);
            scheduler.Queue(() =>
            {
                thread = Thread.CurrentThread;
                evt.Set();
            });
            Wait(evt, "ThreadOf");
            return thread;
        }

        public static Hold Gate(Scheduler scheduler)
        {
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            scheduler.Queue(() =>
            {
                entered.Set();
                release.WaitOne();
            });
            Wait(entered, "scheduler gate");
            return new Hold(release);
        }

        public static void Wait(WaitHandle handle, string what)
        {
            Wait(handle, TimeoutMs, what);
        }

        public static void Wait(WaitHandle handle, int timeoutMs, string what)
        {
            if (!handle.WaitOne(timeoutMs))
                throw new TimeoutException(what + " timed out after " + timeoutMs + " ms.");
        }

        /// <summary>
        /// Observes that <paramref name="thread"/> has entered a wait. Used when a shutdown
        /// caller must be known to have requested shutdown before a gate opens. The library
        /// has no public shutting-down flag. This spins; it does not sleep.
        /// </summary>
        public static void WaitUntilBlocked(Thread thread, ManualResetEvent started)
        {
            Wait(started, "shutdown caller start");
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < TimeoutMs)
            {
                var state = thread.ThreadState;
                if ((state & System.Threading.ThreadState.WaitSleepJoin) != 0)
                    return;
                Thread.SpinWait(80);
            }
            throw new TimeoutException("Shutdown caller did not block.");
        }

        public static void AssertInitialIsFirst(Func<Action, Scheduler> create)
        {
            var gate = new ManualResetEvent(false);
            var started = new ManualResetEvent(false);
            int initialDone = 0;
            bool reactionRanBeforeInitialDone = false;
            Scheduler created = null;
            Exception createError = null;
            var createDone = new ManualResetEvent(false);
            var creator = new Thread(() =>
            {
                try
                {
                    created = create(() =>
                    {
                        new Reactor().Add(() =>
                        {
                            if (Volatile.Read(ref initialDone) == 0)
                                reactionRanBeforeInitialDone = true;
                        });
                        started.Set();
                        gate.WaitOne();
                        Volatile.Write(ref initialDone, 1);
                    });
                }
                catch (Exception ex)
                {
                    createError = ex;
                }
                finally
                {
                    createDone.Set();
                }
            });
            creator.IsBackground = true;
            creator.Start();
            var shutdownStarted = new ManualResetEvent(false);
            try
            {
                Wait(started, "initial started");
                bool shutdownSawInitial = false;
                var shutdownDone = new ManualResetEvent(false);
                var caller = new Thread(() =>
                {
                    shutdownStarted.Set();
                    Reactive.Shutdown();
                    shutdownSawInitial = Volatile.Read(ref initialDone) == 1;
                    shutdownDone.Set();
                });
                caller.IsBackground = true;
                caller.Start();
                WaitUntilBlocked(caller, shutdownStarted);
                if (shutdownDone.WaitOne(0))
                    throw new InvalidOperationException("Reactive.Shutdown returned before the initial action finished.");
                gate.Set();
                Wait(createDone, "CreateNew");
                if (createError != null)
                    throw new InvalidOperationException("CreateNew failed.", createError);
                if (created == null)
                    throw new InvalidOperationException("CreateNew returned null.");
                if (Volatile.Read(ref initialDone) != 1)
                    throw new InvalidOperationException("Initial action did not finish.");
                if (reactionRanBeforeInitialDone)
                    throw new InvalidOperationException("Other work ran before the initial action finished.");
                Wait(shutdownDone, "Reactive.Shutdown");
                if (!shutdownSawInitial)
                    throw new InvalidOperationException("Reactive.Shutdown finished before the initial action.");
            }
            finally
            {
                gate.Set();
            }
        }

        public static Thread CallShutdown(Scheduler scheduler, ManualResetEvent started, ManualResetEvent returned)
        {
            var thread = new Thread(() =>
            {
                started.Set();
                scheduler.Shutdown();
                if (returned != null)
                    returned.Set();
            });
            thread.IsBackground = true;
            thread.Start();
            return thread;
        }
    }

    public sealed class Hold(ManualResetEvent release) : IDisposable
    {
        int _done;

        public void Release()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0)
                return;
            release.Set();
        }

        public void Dispose()
        {
            Release();
        }
    }

    public sealed class Log : IDisposable
    {
        readonly List<Exception> _items = [];

        public Log()
        {
            Reactive.UnhandledException += On;
        }

        void On(Exception exception)
        {
            lock (_items)
                _items.Add(exception);
        }

        public int Count
        {
            get
            {
                lock (_items)
                    return _items.Count;
            }
        }

        public void Clear()
        {
            lock (_items)
                _items.Clear();
        }

        public Exception[] Snapshot()
        {
            lock (_items)
                return [.. _items];
        }

        public void Dispose()
        {
            Reactive.UnhandledException -= On;
        }
    }

    public sealed class ConsoleCapture : IDisposable
    {
        readonly TextWriter _out;
        readonly TextWriter _error;
        readonly StringWriter _writer = new StringWriter();

        public ConsoleCapture()
        {
            _out = Console.Out;
            _error = Console.Error;
            Console.SetOut(_writer);
            Console.SetError(_writer);
        }

        public string Text
        {
            get { return _writer.ToString(); }
        }

        public void Dispose()
        {
            Console.SetOut(_out);
            Console.SetError(_error);
            _writer.Dispose();
        }
    }

    public sealed class Order
    {
        readonly List<string> _items = [];

        public void Add(string item)
        {
            lock (_items)
                _items.Add(item);
        }

        public string[] Snapshot()
        {
            lock (_items)
                return [.. _items];
        }
    }

    /// <summary>
    /// User scheduler. Work runs only when <see cref="Pump"/> is called, on the caller.
    /// </summary>
    public sealed class ProbeScheduler : Scheduler
    {
        readonly Queue<Action> _queue = new Queue<Action>();
        int _scheduleCalls;

        public int ScheduleCalls
        {
            get { return Volatile.Read(ref _scheduleCalls); }
        }

        protected override void Schedule(Action action)
        {
            if (action == null)
                return;
            if (!AcceptWork())
                return;
            Interlocked.Increment(ref _scheduleCalls);
            lock (_queue)
                _queue.Enqueue(action);
        }

        protected override void DrainOnOwnerThread()
        {
            Pump();
        }

        public void Pump()
        {
            while (true)
            {
                Action action;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                        return;
                    action = _queue.Dequeue();
                }
                action();
            }
        }

        public void RunDirect(Reaction reaction)
        {
            Run(reaction);
        }
    }

    /// <summary>
    /// Smallest user scheduler: only <see cref="Schedule"/> is overridden (cs3.1).
    /// </summary>
    public sealed class OnlyScheduleScheduler : Scheduler
    {
        readonly Queue<Action> _queue = new Queue<Action>();

        protected override void Schedule(Action action)
        {
            if (action == null)
                return;
            if (!AcceptWork())
                return;
            lock (_queue)
                _queue.Enqueue(action);
        }

        public void Pump()
        {
            while (true)
            {
                Action action;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                        return;
                    action = _queue.Dequeue();
                }
                action();
            }
        }
    }
}

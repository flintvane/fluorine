using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using System;
using Xunit;

namespace Fluorine.Tests
{
    static class WaitUntil
    {
        public static void True(Func<bool> condition, string name)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(15))
                    throw new TimeoutException(name);
                Thread.Sleep(5);
            }
        }

        public static void Event(ManualResetEventSlim evt, string name)
        {
            if (!evt.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException(name);
        }
    }

    static class SkipLog
    {
        public static readonly ConcurrentBag<string> Entries = [];

        public static void CouldNotRun(string test, string reason)
        {
            var line = test + ": " + reason;
            Entries.Add(line);
            Console.WriteLine("COULD NOT RUN " + line);
        }
    }

    sealed class ThreadHost(Action initial = null) : IDisposable
    {
        public ThreadScheduler Scheduler { get; private set; } = ThreadScheduler.CreateNew("test", initial);

        public Reactor NewReactor()
        {
            return new Reactor(Scheduler);
        }

        public void OnScheduler(Action action)
        {
            var done = new ManualResetEventSlim(false);
            Exception error = null;
            Scheduler.Queue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            WaitUntil.Event(done, "OnScheduler");
            if (error != null)
                throw new Exception("Queued action failed.", error);
        }

        public void Dispose()
        {
            if (Scheduler != null)
                Scheduler.Shutdown();
        }
    }

    sealed class ManualScheduler : Scheduler
    {
        readonly Queue<Action> _queue = new();

        protected override void Schedule(Action action)
        {
            lock (this)
            {
                if (HasStartedShutdown)
                    return;
                _queue.Enqueue(action);
            }
        }

        protected override void DrainOnOwnerThread()
        {
            Pump();
        }

        public void Pump()
        {
            while (true)
            {
                Action work;
                lock (this)
                {
                    if (_queue.Count == 0)
                        return;
                    work = _queue.Dequeue();
                }
                work();
            }
        }

        public void ForceRun(Reaction reaction) 
            => Run(reaction);
    }

    sealed class WinFormsHost : IDisposable
    {
        readonly ManualResetEventSlim _ready = new(false);
        readonly ManualResetEventSlim _exited = new(false);
        private Form _form;
        private Exception _startError;

        public WinFormsScheduler Scheduler { get; private set; }
        public SynchronizationContext Context { get; private set; }

        public WinFormsHost()
        {
            var thread = new Thread(() =>
            {
                try
                {
                    WindowsFormsSynchronizationContext.AutoInstall = true;
                    _form = new Form
                    {
                        WindowState = FormWindowState.Minimized,
                        ShowInTaskbar = false,
                        Opacity = 0,
                        Width = 1,
                        Height = 1
                    };
                    var unused = _form.Handle;
                    if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                    {
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    }
                    Context = SynchronizationContext.Current;
                    Scheduler = WinFormsScheduler.AttachToCurrent();
                    _ready.Set();
                    Application.Run(_form);
                }
                catch (Exception ex)
                {
                    _startError = ex;
                    _ready.Set();
                }
                finally
                {
                    _exited.Set();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Fluorine WinForms test";
            thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("WinForms host did not start.");
            if (Scheduler == null)
                throw new InvalidOperationException("WinForms host failed: " + _startError);
        }

        public void OnScheduler(Action action)
        {
            var done = new ManualResetEventSlim(false);
            Exception error = null;
            Scheduler.Queue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            WaitUntil.Event(done, "WinForms OnScheduler");
            if (error != null)
                throw new Exception("Queued action failed.", error);
        }

        public void Dispose()
        {
            try
            {
                if (Scheduler is { HasStartedShutdown: false })
                    Scheduler.Shutdown();
            }
            catch
            {
            }
            try
            {
                var form = _form;
                var ctx = Context;
                if (form != null && ctx != null)
                {
                    ctx.Post(_ =>
                    {
                        try
                        {
                            if (!form.IsDisposed)
                                form.Close();
                            Application.ExitThread();
                        }
                        catch
                        {
                        }
                    }, null);
                }
            }
            catch
            {
            }
            _exited.Wait(TimeSpan.FromSeconds(10));
        }
    }

    sealed class EqThrower
    {
        public override bool Equals(object obj)
        {
            throw new InvalidOperationException("equality threw");
        }

        public override int GetHashCode()
        {
            return 0;
        }
    }

    // ---- Acceptance harness (from the former tests/Fluorine.Acceptance/Harness.cs); public API only. ----

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

        protected static void AssertPromptShutdown()
        {
            var done = new ManualResetEvent(false);
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Reactive.Shutdown();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            thread.IsBackground = true;
            thread.Start();
            Assert.True(done.WaitOne(2000), "Reactive.Shutdown did not return promptly");
            Assert.Null(error);
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

        readonly StringWriter _writer = new();

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

    public sealed class ProbeScheduler : Scheduler
    {
        readonly Queue<Action> _queue = new();

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

    public sealed class OnlyScheduleScheduler : Scheduler
    {
        readonly Queue<Action> _queue = new();

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

    sealed class DispatcherLoop : IDisposable
    {
        public Thread Thread;

        public Dispatcher Dispatcher;

        public DispatcherScheduler Scheduler;

        readonly ManualResetEvent _ready = new(false);

        Exception _error;

        public static DispatcherLoop Start()
        {
            var loop = new DispatcherLoop();
            var thread = new Thread(loop.Run);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Name = "acceptance-dispatcher";
            loop.Thread = thread;
            thread.Start();
            Harness.Wait(loop._ready, 10000, "dispatcher loop");
            if (loop._error != null)
                throw new InvalidOperationException("dispatcher loop failed", loop._error);
            return loop;
        }

        void Run()
        {
            try
            {
                Dispatcher = Dispatcher.CurrentDispatcher;
                Scheduler = DispatcherScheduler.AttachToCurrent();
                _ready.Set();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _error = ex;
                _ready.Set();
            }
        }

        public void Dispose()
        {
            try
            {
                if (Scheduler != null)
                    Scheduler.Shutdown();
            }
            catch (Exception)
            {
            }
            try
            {
                if (Dispatcher != null && !Dispatcher.HasShutdownStarted)
                    Dispatcher.InvokeShutdown();
            }
            catch (Exception)
            {
            }
            if (Thread != null)
                Thread.Join(Harness.TimeoutMs);
        }
    }

    sealed class WinFormsLoop : IDisposable
    {
        public Thread Thread;

        public Form Form;

        public WinFormsScheduler Scheduler;

        readonly ManualResetEvent _ready = new(false);

        Exception _error;

        public static WinFormsLoop Start()
        {
            var loop = new WinFormsLoop();
            var thread = new Thread(loop.Run);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Name = "acceptance-winforms";
            loop.Thread = thread;
            thread.Start();
            Harness.Wait(loop._ready, 15000, "winforms loop");
            if (loop._error != null)
                throw new InvalidOperationException("winforms loop failed", loop._error);
            return loop;
        }

        void Run()
        {
            try
            {
                Form = new Form();
                Form.ShowInTaskbar = false;
                Form.FormBorderStyle = FormBorderStyle.None;
                Form.StartPosition = FormStartPosition.Manual;
                Form.Location = new System.Drawing.Point(-20000, -20000);
                Form.Load += delegate
                {
                    try
                    {
                        Scheduler = WinFormsScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        _error = ex;
                    }
                    _ready.Set();
                };
                Application.Run(Form);
            }
            catch (Exception ex)
            {
                _error = ex;
                _ready.Set();
            }
        }

        public void Dispose()
        {
            try
            {
                if (Scheduler != null)
                    Scheduler.Shutdown();
            }
            catch (Exception)
            {
            }
            try
            {
                if (Form != null && Form.IsHandleCreated)
                    Form.BeginInvoke(new Action(Application.ExitThread));
            }
            catch (Exception)
            {
            }
            if (Thread != null)
                Thread.Join(Harness.TimeoutMs);
        }
    }

    // Helpers of the former Fluorine.Acceptance Cs12CollectionsTests class, shared by the classes that hold its tests.
    public abstract class CollectionAcceptanceTest : AcceptanceTest
    {
        protected static readonly K k1 = new(1);

        protected static readonly K k1p = new(1);

        protected static readonly K k2 = new(2);

        protected static readonly K k3 = new(3);

        protected static void RunAnyThreadStress(bool set)
        {
            var sc = new ReactiveSet<K>();
            var dc = new ReactiveDictionary<K, string>();
            var errors = new Exception[8];
            var mirrors = new HashSet<int>[8];
            var values = new Dictionary<int, string>[8];
            var threads = new Thread[8];
            for (int i = 0; i < 8; i++)
            {
                int index = i;
                mirrors[index] = new HashSet<int>();
                values[index] = new Dictionary<int, string>();
                threads[index] = new Thread(() =>
                {
                    try
                    {
                        int start = 1000 * index;
                        var rng = new Random(index + 1);
                        for (int n = 0; n < Harness.Stress; n++)
                        {
                            int id = start + rng.Next(100);
                            var key = new K(id);
                            if (set)
                            {
                                if (rng.Next(2) == 0)
                                {
                                    if (sc.Add(key))
                                        mirrors[index].Add(id);
                                }
                                else
                                {
                                    if (sc.Remove(key))
                                        mirrors[index].Remove(id);
                                }
                                var unusedCount = sc.Count;
                                var unusedValue = sc.Value;
                                var unusedContains = sc.Contains(key);
                            }
                            else
                            {
                                int op = rng.Next(3);
                                if (op == 0)
                                {
                                    if (dc.Add(key, "v" + id))
                                    {
                                        mirrors[index].Add(id);
                                        values[index][id] = "v" + id;
                                    }
                                }
                                else if (op == 1)
                                {
                                    string next = "s" + n;
                                    if (dc.SetValue(key, next))
                                    {
                                        mirrors[index].Add(id);
                                        values[index][id] = next;
                                    }
                                }
                                else
                                {
                                    if (dc.Remove(key))
                                    {
                                        mirrors[index].Remove(id);
                                        values[index].Remove(id);
                                    }
                                }
                                var unusedCount = dc.Count;
                                var unusedValue = dc.Value;
                                var unusedContains = dc.ContainsKey(key);
                                string unused;
                                dc.TryGetValue(key, out unused);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors[index] = ex;
                    }
                });
                threads[index].IsBackground = true;
                threads[index].Start();
            }
            foreach (var thread in threads)
                Assert.True(thread.Join(Harness.TimeoutMs * 4), "stress thread did not finish");
            foreach (var error in errors)
                Assert.Null(error);

            int total = 0;
            for (int i = 0; i < 8; i++)
            {
                int start = 1000 * i;
                for (int id = start; id < start + 100; id++)
                {
                    var key = new K(id);
                    bool expected = mirrors[i].Contains(id);
                    if (set)
                    {
                        Assert.Equal(expected, sc.Contains(key));
                    }
                    else
                    {
                        Assert.Equal(expected, dc.ContainsKey(key));
                        string v;
                        Assert.Equal(expected, dc.TryGetValue(key, out v));
                        if (expected)
                            Assert.Equal(values[i][id], v);
                    }
                    if (expected)
                        total++;
                }
            }
            if (set)
                Assert.Equal(total, sc.Count);
            else
                Assert.Equal(total, dc.Count);
        }

        protected static void AssertAtomicClear(bool set)
        {
            var sc = new ReactiveSet<K>();
            var dc = new ReactiveDictionary<K, string>();
            int stop = 0;
            int mismatch = 0;
            var writer = new Thread(() =>
            {
                for (int n = 0; n < Harness.Stress; n++)
                {
                    if (set)
                    {
                        for (int i = 0; i < 10; i++)
                            sc.Add(new K(i));
                        sc.Clear();
                    }
                    else
                    {
                        for (int i = 0; i < 10; i++)
                            dc[new K(i)] = "v" + i;
                        dc.Clear();
                    }
                }
                Volatile.Write(ref stop, 1);
            });
            var reader = new Thread(() =>
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    if (set)
                    {
                        var v = sc.Value;
                        int count = v.Count;
                        for (int i = 0; i < 10; i++)
                        {
                            bool has = v.Contains(new K(i));
                            if (has != (i < count))
                                Interlocked.Increment(ref mismatch);
                        }
                    }
                    else
                    {
                        var v = dc.Value;
                        int count = v.Count;
                        for (int i = 0; i < 10; i++)
                        {
                            bool has = v.ContainsKey(new K(i));
                            if (has != (i < count))
                                Interlocked.Increment(ref mismatch);
                        }
                    }
                }
            });
            writer.IsBackground = true;
            reader.IsBackground = true;
            reader.Start();
            writer.Start();
            Assert.True(writer.Join(Harness.TimeoutMs * 4));
            Assert.True(reader.Join(Harness.TimeoutMs));
            Assert.Equal(0, mismatch);
        }

        protected static void PresenceAddRemove(Action<ReactiveSet<K>> add)
        {
            var s = Harness.Create();
            var sc = new ReactiveSet<K>();
            int n = 0;
            var seen = new List<bool>();
            new Reactor(s).Add(() => { seen.Add(sc.Contains(k1)); n++; });
            Harness.Barrier(s);
            add(sc);
            Harness.Barrier(s);
            sc.Remove(k1);
            Harness.Barrier(s);
            Assert.Equal(3, n);
            Assert.Equal(new[] { false, true, false }, seen.ToArray());
        }

        protected static void PresenceAddRemoveDict(Action<ReactiveDictionary<K, string>> add)
        {
            var s = Harness.Create();
            var dc = new ReactiveDictionary<K, string>();
            int n = 0;
            var seen = new List<bool>();
            new Reactor(s).Add(() => { seen.Add(dc.ContainsKey(k1)); n++; });
            Harness.Barrier(s);
            add(dc);
            Harness.Barrier(s);
            dc.Remove(k1);
            Harness.Barrier(s);
            Assert.Equal(3, n);
            Assert.Equal(new[] { false, true, false }, seen.ToArray());
        }

        protected static void AssertRose(ref int n, Action write, Scheduler s)
        {
            int before = n;
            write();
            Harness.Barrier(s);
            Assert.Equal(before + 1, n);
        }

        protected static void AssertThrowsUnwrapped(Exception expected, Action action)
        {
            var thrown = Record.Exception(action);
            Assert.Same(expected, thrown);
            Assert.IsNotType<ReactiveException>(thrown);
        }

        protected static void AssertArgNull(Action action)
        {
            var thrown = Record.Exception(action);
            Assert.IsType<ArgumentNullException>(thrown);
            Assert.IsNotType<ReactiveException>(thrown);
        }

        protected static void AssertFoundUntorn(bool found, Big value, ref int mismatch)
        {
            if (!found)
            {
                Interlocked.Increment(ref mismatch);
                return;
            }
            if (value.A != value.B || value.B != value.C || value.C != value.D)
                Interlocked.Increment(ref mismatch);
        }

        protected static void AssertVersionSurface(Type collection, bool set)
        {
            var version = collection.GetNestedType("Version");
            Assert.NotNull(version);
            Assert.NotNull(version.GetProperty("Count"));
            var diff = version.GetMethod("Diff");
            Assert.NotNull(diff);
            Assert.True(diff.ReturnType.IsArray);
            Assert.Equal("Change", diff.ReturnType.GetElementType().Name);
            Assert.Equal(0, version.GetMember("SameAs").Length);
            if (set)
            {
                Assert.NotNull(version.GetMethod("Contains"));
            }
            else
            {
                Assert.NotNull(version.GetMethod("ContainsKey"));
                Assert.NotNull(version.GetMethod("TryGetValue"));
                var indexer = version.GetProperty("Item");
                Assert.NotNull(indexer);
                Assert.NotNull(indexer.GetGetMethod());
                Assert.Null(indexer.GetSetMethod());
            }

            var change = collection.GetNestedType("Change");
            Assert.NotNull(change);
            Assert.NotNull(change.GetProperty("Kind"));
            Assert.Equal("ChangeKind", change.GetProperty("Kind").PropertyType.Name);
            if (set)
            {
                Assert.NotNull(change.GetProperty("Item"));
                Assert.NotNull(change.GetProperty("Previous"));
            }
            else
            {
                Assert.NotNull(change.GetProperty("Key"));
                Assert.NotNull(change.GetProperty("Value"));
                Assert.NotNull(change.GetProperty("PreviousValue"));
            }

            var kind = collection.GetNestedType("ChangeKind");
            Assert.NotNull(kind);
            Assert.True(kind.IsEnum);
            Assert.True(Enum.IsDefined(kind, Enum.Parse(kind, "Added")));
            Assert.True(Enum.IsDefined(kind, Enum.Parse(kind, "Removed")));
            if (set)
                Assert.True(Enum.IsDefined(kind, Enum.Parse(kind, "Replaced")));
            else
                Assert.True(Enum.IsDefined(kind, Enum.Parse(kind, "Updated")));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static WeakReference MakeSetVersion(out ReactiveSet<K>.Version v1)
        {
            var c = new ReactiveSet<K>();
            for (int i = 0; i < 100; i++)
                c.Add(new K(i));
            var v0 = c.Value;
            var wr = new WeakReference(v0);
            c.Add(new K(100));
            c.Remove(new K(0));
            v1 = c.Value;
            return wr;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static WeakReference MakeDictVersion(out ReactiveDictionary<K, string>.Version v1)
        {
            var c = new ReactiveDictionary<K, string>();
            for (int i = 0; i < 100; i++)
                c.Add(new K(i), "v" + i);
            var v0 = c.Value;
            var wr = new WeakReference(v0);
            c.Add(new K(100), "x");
            c.Remove(new K(0));
            c[new K(1)] = "z";
            v1 = c.Value;
            return wr;
        }

        protected static string SetTag(ReactiveSet<K>.Change change)
        {
            string item = change.Item == null ? "null" : change.Item.Id.ToString();
            string previous = change.Previous == null ? "null" : change.Previous.Id.ToString();
            return change.Kind + ":" + item + ":" + previous;
        }

        protected static string DictTag(ReactiveDictionary<K, string>.Change change)
        {
            string key = change.Key == null ? "null" : change.Key.Id.ToString();
            return change.Kind + ":" + key + ":" + change.Value + ":" + change.PreviousValue;
        }

        protected static string[] Tags<T>(T[] changes, Func<T, string> tag)
        {
            var tags = new string[changes.Length];
            for (int i = 0; i < changes.Length; i++)
                tags[i] = tag(changes[i]);
            return tags;
        }

        protected static void AssertSetTags(ReactiveSet<K>.Change[] actual, params string[] expected)
        {
            AssertMultiset(Tags(actual, SetTag), expected);
        }

        protected static void AssertDictTags(ReactiveDictionary<K, string>.Change[] actual, params string[] expected)
        {
            AssertMultiset(Tags(actual, DictTag), expected);
        }

        protected static void AssertMultiset(string[] actual, params string[] expected)
        {
            var left = (string[])actual.Clone();
            var right = (string[])expected.Clone();
            Array.Sort(left);
            Array.Sort(right);
            Assert.Equal(right, left);
        }

        public sealed class K
        {
            public readonly int Id;

            public K(int id)
            {
                Id = id;
            }

            public override bool Equals(object obj)
            {
                var other = obj as K;
                return other != null && other.Id == Id;
            }

            public override int GetHashCode()
            {
                return Id;
            }
        }

        protected sealed class AccBoom
        {
            readonly Exception _thrown;

            public AccBoom(Exception thrown)
            {
                _thrown = thrown;
            }

            public override bool Equals(object obj)
            {
                throw _thrown;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        protected struct Big
        {
            public long A;
            public long B;
            public long C;
            public long D;

            public Big(long value)
            {
                A = value;
                B = value;
                C = value;
                D = value;
            }
        }
    }

    // Helpers of the former Fluorine.Acceptance SemanticsTests class, shared by the classes that hold its tests.
    public abstract class SemanticsAcceptanceTest : AcceptanceTest
    {
        protected static void NotePeak(ref int peak, int now)
        {
            int prev;
            do
            {
                prev = Volatile.Read(ref peak);
                if (now <= prev)
                    return;
            }
            while (Interlocked.CompareExchange(ref peak, now, prev) != prev);
        }

        protected static Thread[] StartThreads(int count, Action body)
        {
            var threads = new Thread[count];
            for (int i = 0; i < count; i++)
            {
                threads[i] = new Thread(() => body());
                threads[i].IsBackground = true;
                threads[i].Start();
            }
            return threads;
        }

        protected static void JoinAll(Thread[] threads)
        {
            for (int i = 0; i < threads.Length; i++)
                Assert.True(threads[i].Join(Harness.TimeoutMs), "worker timed out");
        }

        protected static string[] Snapshot(List<string> items)
        {
            lock (items)
                return [.. items];
        }

        protected static string Last(List<string> items)
        {
            lock (items)
                return items[items.Count - 1];
        }

        protected sealed class ReenterEqual
        {
            public Register<ReenterEqual> Reg;
            public int Id;

            public override bool Equals(object obj)
            {
                if (object.ReferenceEquals(this, obj))
                    return true;
                var done = new ManualResetEvent(false);
                Exception error = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        if (Reg != null)
                        {
                            Reg.RecordRead();
                            Reg.TriggerReactions();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                thread.IsBackground = true;
                thread.Start();
                if (!done.WaitOne(Harness.TimeoutMs))
                    throw new TimeoutException("equality re-entered the register and deadlocked");
                thread.Join();
                if (error != null)
                    throw new InvalidOperationException("re-entrant register call failed", error);
                var other = obj as ReenterEqual;
                return other != null && other.Id == Id;
            }

            public override int GetHashCode()
            {
                return Id;
            }
        }
    }
}

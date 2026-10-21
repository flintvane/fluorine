using System;
using System.Diagnostics;
using System.Threading;

namespace StructTearing
{
    // 32 bytes: four independent 64-bit fields. A single assignment is several stores.
    struct Quad
    {
        public long A, B, C, D;

        public Quad(int x)
        {
            A = x;
            B = x;
            C = x;
            D = x;
        }

        public bool Torn
        {
            get { return A != B  || A != C/* || A != D*/; }
        }
    }

    static class Program
    {
        static Quad _unlocked;
        static Quad _locked;
        static readonly object Gate = new object();

        static long _unlockedReads, _unlockedTears;
        static long _lockedReads, _lockedTears;

        static volatile bool _stop;

        static void Main()
        {
            const int seconds = 3;
            Console.WriteLine("Struct assignment tearing on {0} ({1}-bit process)",
                Environment.Version, IntPtr.Size * 8);
            Console.WriteLine("Quad is {0} bytes. Running {1}s.",
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Quad)), seconds);
            Console.WriteLine();

            var threads = new[]
            {
                new Thread(() => UnlockedWriter(1)) { IsBackground = true },
                new Thread(() => UnlockedWriter(-1)) { IsBackground = true },
                new Thread(UnlockedReader) { IsBackground = true },
                new Thread(UnlockedReader) { IsBackground = true },
                new Thread(UnlockedReader) { IsBackground = true },
                new Thread(() => LockedWriter(1)) { IsBackground = true },
                new Thread(() => LockedWriter(-1)) { IsBackground = true },
                new Thread(LockedReader) { IsBackground = true },
                new Thread(LockedReader) { IsBackground = true },
            };
            foreach (var t in threads)
                t.Start();

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                Thread.Sleep(250);
                Console.WriteLine(
                    "{0:0.0}s  unlocked: {1:N0} reads, {2:N0} torn   |   locked: {3:N0} reads, {4:N0} torn",
                    sw.Elapsed.TotalSeconds,
                    Interlocked.Read(ref _unlockedReads), Interlocked.Read(ref _unlockedTears),
                    Interlocked.Read(ref _lockedReads), Interlocked.Read(ref _lockedTears));
            }

            _stop = true;
            foreach (var t in threads)
                t.Join();

            Console.WriteLine();
            Console.WriteLine("Unlocked: {0:N0} reads, {1:N0} torn ({2:P4})",
                _unlockedReads, _unlockedTears,
                _unlockedReads == 0 ? 0 : (double)_unlockedTears / _unlockedReads);
            Console.WriteLine("Locked:   {0:N0} reads, {1:N0} torn",
                _lockedReads, _lockedTears);
        }

        static void UnlockedWriter(int sign)
        {
            int n = 0;
            while (!_stop)
            {
                n++;
                _unlocked = new Quad(sign * n);
            }
        }

        static void UnlockedReader()
        {
            while (!_stop)
            {
                var v = _unlocked;
                Interlocked.Increment(ref _unlockedReads);
                if (v.Torn)
                    Interlocked.Increment(ref _unlockedTears);
            }
        }

        static void LockedWriter(int sign)
        {
            int n = 0;
            while (!_stop)
            {
                n++;
                var next = new Quad(sign * n);
                lock (Gate)
                    _locked = next;
            }
        }

        static void LockedReader()
        {
            while (!_stop)
            {
                Quad v;
                lock (Gate)
                    v = _locked;
                Interlocked.Increment(ref _lockedReads);
                if (v.Torn)
                    Interlocked.Increment(ref _lockedTears);
            }
        }
    }
}

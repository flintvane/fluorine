using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Xunit;

namespace Fluorine
{
    // What each test locks in for the docs: ../ReactiveQueue-for-docs.md
    public class ReactiveQueueTests
    {
        [Fact]
        public void PushPopAndTryPopAreFifo()
        {
            var queue = new ReactiveQueue<string>();
            var same = new Same();
            queue.Push("a");
            queue.Push(null);
            queue.Push("a");

            Assert.Equal("a", queue.Pop());
            Assert.Null(queue.Pop());
            Assert.Equal("a", queue.Pop());
            var empty = Assert.Throws<InvalidOperationException>(() => queue.Pop());
            Assert.Equal("The queue is empty.", empty.Message);

            string missing = "sentinel";
            Assert.False(queue.TryPop(out missing));
            Assert.Null(missing);

            var numbers = new ReactiveQueue<int>();
            int zero = 5;
            Assert.False(numbers.TryPop(out zero));
            Assert.Equal(0, zero);
            numbers.Push(0);
            Assert.True(numbers.TryPop(out zero));
            Assert.Equal(0, zero);

            var instances = new ReactiveQueue<Same>();
            instances.Push(same);
            instances.Push(same);
            Assert.Same(same, instances.Pop());
            Assert.Same(same, instances.Pop());
        }

        [Fact]
        public void ConstructorStoresTheSequenceAndRejectsNull()
        {
            var queue = new ReactiveQueue<string>(new[] { "a", null, "a" });
            Assert.Equal("a", queue.Pop());
            Assert.Null(queue.Pop());
            Assert.Equal("a", queue.Pop());
            Assert.False(queue.TryPop(out _));

            var empty = new ReactiveQueue<int>(new int[0]);
            Assert.False(empty.TryPop(out _));
            Assert.Throws<ArgumentNullException>(() => new ReactiveQueue<int>((int[])null));
        }

        [Fact]
        public void ThereIsNoPicture()
        {
            var type = typeof(ReactiveQueue<int>);
            Assert.True(typeof(Register).IsAssignableFrom(type));
            Assert.False(type.BaseType.IsGenericType);
            Assert.Null(type.GetProperty("Value"));
            Assert.Null(type.GetNestedType("Version"));
            Assert.False(typeof(System.Collections.IEnumerable).IsAssignableFrom(type));
        }

        [Fact]
        public void EqualityIsNotConsulted()
        {
            var queue = new ReactiveQueue<Boom>();
            var item = new Boom();
            queue.Push(item);
            queue.Push(item);
            Assert.Same(item, queue.Pop());
            Assert.Same(item, queue.Pop());
        }

        [Fact]
        public void AnEqualElementIsStillANewHead()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<int>();
                var runs = 0;
                var seen = new List<int>();
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    if (queue.TryPop(out var item))
                        seen.Add(item);
                });
                scheduler.Pump();
                Assert.Equal(1, runs);

                queue.Push(1);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(new[] { 1 }, seen.ToArray());

                queue.Push(1);
                scheduler.Pump();
                Assert.Equal(3, runs);
                Assert.Equal(new[] { 1, 1 }, seen.ToArray());
                scheduler.Pump();
                Assert.Equal(3, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void WaitingTryPopRerunsWhenPushMakesAHead()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<string>();
                var runs = 0;
                var found = false;
                string seen = "no";
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    found = queue.TryPop(out seen);
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.False(found);
                Assert.Null(seen);

                queue.Push(null);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.True(found);
                Assert.Null(seen);
                scheduler.Pump();
                Assert.Equal(2, runs);

                queue.Push("b");
                scheduler.Pump();
                Assert.Equal(3, runs);
                Assert.Equal("b", seen);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void OnePopPerRunTakesBufferedItemsWithoutAnEmptyFollowUp()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<int>(new[] { 1, 2, 3 });
                var runs = 0;
                var seen = new List<int>();
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    if (queue.TryPop(out var item))
                        seen.Add(item);
                });
                scheduler.Pump();
                Assert.Equal(new[] { 1, 2, 3 }, seen.ToArray());
                Assert.Equal(3, runs);
                scheduler.Pump();
                Assert.Equal(3, runs);
                Assert.False(queue.TryPop(out _));
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void DrainLoopFollowsUpOnceWhenAPopUncoversTheNextHead()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<int>(new[] { 1, 2, 3 });
                var runs = 0;
                var seen = new List<int>();
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    while (queue.TryPop(out var item))
                        seen.Add(item);
                });
                scheduler.Pump();
                Assert.Equal(new[] { 1, 2, 3 }, seen.ToArray());
                Assert.Equal(2, runs);
                scheduler.Pump();
                Assert.Equal(2, runs);

                queue.Push(4);
                queue.Push(5);
                scheduler.Pump();
                Assert.Equal(new[] { 1, 2, 3, 4, 5 }, seen.ToArray());
                Assert.Equal(4, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void PushFromASubscribedEmptyPopRerunsAndDeliversThatElement()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<string>();
                var runs = 0;
                string seen = null;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    if (queue.TryPop(out var item))
                        seen = item;
                    else
                        queue.Push("a");
                });
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal("a", seen);
                scheduler.Pump();
                Assert.Equal(2, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void PushBeforeAnyPopDoesNotRescheduleThatRun()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<string>();
                var runs = 0;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    if (runs == 1)
                    {
                        queue.Push("a");
                        Assert.True(queue.TryPop(out var item));
                        Assert.Equal("a", item);
                    }
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.False(queue.TryPop(out _));
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void PausedPopConsumesAndDoesNotSubscribe()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<string>();
                queue.Push("hidden");
                var runs = 0;
                string paused = null;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    Reaction.Pause(() => queue.TryPop(out paused));
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal("hidden", paused);

                queue.Push("later");
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal("later", queue.Pop());
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void SkippingPopDropsTheHeadWatch()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<string>();
                var runs = 0;
                var watch = true;
                var seen = new List<string>();
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    if (!watch)
                        return;
                    if (queue.TryPop(out var item))
                        seen.Add(item);
                });
                scheduler.Pump();
                Assert.Equal(1, runs);

                queue.Push("a");
                queue.Push("b");
                watch = false;
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Empty(seen);

                queue.Push("c");
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal("a", queue.Pop());
                Assert.Equal("b", queue.Pop());
                Assert.Equal("c", queue.Pop());
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void DisposingTheReactionDropsTheHeadWatch()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<string>();
                var runs = 0;
                var reaction = new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    queue.TryPop(out _);
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                var afterDispose = reaction.ExecutionCount;
                reaction.Dispose();
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.True(reaction.ExecutionCount > afterDispose);
                var settled = reaction.ExecutionCount;

                queue.Push("z");
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(settled, reaction.ExecutionCount);
                Assert.Equal("z", queue.Pop());
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void PopOnEmptySubscribesThenThrows()
        {
            var scheduler = new ManualScheduler();
            var errors = new List<Exception>();
            Action<Exception> handler = errors.Add;
            Reactive.UnhandledException += handler;
            try
            {
                var queue = new ReactiveQueue<int>();
                var runs = 0;
                var got = -1;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    got = queue.Pop();
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(-1, got);
                var error = Assert.Single(errors);
                Assert.IsType<InvalidOperationException>(error);
                Assert.Equal("The queue is empty.", error.Message);

                queue.Push(4);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(4, got);
                Assert.Single(errors);
                scheduler.Pump();
                Assert.Equal(2, runs);
            }
            finally
            {
                Reactive.UnhandledException -= handler;
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void OutsidePopRemovesTheHeadTheReactionWouldHaveTaken()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<string>();
                queue.Push("a");
                queue.Push("b");
                Assert.Equal("a", queue.Pop());

                var runs = 0;
                var seen = new List<string>();
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    if (queue.TryPop(out var item))
                        seen.Add(item);
                });
                scheduler.Pump();
                Assert.Equal(new[] { "b" }, seen.ToArray());
                Assert.Equal(1, runs);
                scheduler.Pump();
                Assert.Equal(1, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void CompetingPopsShareTheItems()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var queue = new ReactiveQueue<int>();
                var first = new List<int>();
                var second = new List<int>();
                new Reactor(scheduler).Add(() =>
                {
                    if (queue.TryPop(out var item))
                        first.Add(item);
                });
                new Reactor(scheduler).Add(() =>
                {
                    if (queue.TryPop(out var item))
                        second.Add(item);
                });
                scheduler.Pump();
                for (var i = 0; i < 6; i++)
                    queue.Push(i);
                scheduler.Pump();

                var taken = first.Concat(second).OrderBy(item => item).ToArray();
                Assert.Equal(Enumerable.Range(0, 6), taken);
                Assert.Equal(taken.Length, first.Concat(second).Distinct().Count());
                scheduler.Pump();
                Assert.Equal(6, first.Count + second.Count);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void AReactionKeepsUpWithProducersOnOtherThreads()
        {
            using (var host = new ThreadHost())
            {
                const int producers = 4;
                const int each = 200;
                var total = producers * each;
                var queue = new ReactiveQueue<int>();
                var got = new List<int>();
                var done = new ManualResetEventSlim(false);
                host.NewReactor().Add(() =>
                {
                    while (queue.TryPop(out var item))
                    {
                        got.Add(item);
                        if (got.Count == total)
                            done.Set();
                    }
                });

                var threads = new Thread[producers];
                var error = new Exception[1];
                for (var p = 0; p < producers; p++)
                {
                    var id = p;
                    threads[p] = new Thread(() =>
                    {
                        try
                        {
                            var start = id * each;
                            for (var i = 0; i < each; i++)
                                queue.Push(start + i);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.CompareExchange(ref error[0], ex, null);
                        }
                    });
                    threads[p].Start();
                }

                foreach (var thread in threads)
                    thread.Join();
                if (error[0] != null)
                    throw new Exception("Producer failed.", error[0]);
                WaitUntil.Event(done, "queue consumer");

                Assert.Equal(total, got.Count);
                Assert.Equal(total, got.Distinct().Count());
                for (var p = 0; p < producers; p++)
                {
                    var slice = got.Where(item => item / each == p).ToArray();
                    Assert.Equal(Enumerable.Range(p * each, each), slice);
                }
            }
        }

        [Fact]
        public void ConcurrentPushAndPopDoesNotTearAStruct()
        {
            const int producers = 4;
            const int each = 2500;
            var total = producers * each;
            var queue = new ReactiveQueue<Box>();
            var threads = new Thread[producers];
            var error = new Exception[1];
            for (var p = 0; p < producers; p++)
            {
                var id = p;
                threads[p] = new Thread(() =>
                {
                    try
                    {
                        var start = id * each;
                        for (var i = 0; i < each; i++)
                            queue.Push(new Box(start + i));
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref error[0], ex, null);
                    }
                });
                threads[p].Start();
            }

            var got = new List<long>(total);
            var started = Stopwatch.StartNew();
            while (got.Count < total)
            {
                if (queue.TryPop(out var box))
                {
                    if (box.A != box.B || box.B != box.C || box.C != box.D)
                        throw new InvalidOperationException("Torn queue element " + box.A);
                    got.Add(box.A);
                    continue;
                }

                if (error[0] != null)
                    throw new Exception("Producer failed.", error[0]);
                if (started.Elapsed > TimeSpan.FromSeconds(15))
                    throw new TimeoutException("struct pop");
                Thread.Sleep(0);
            }

            foreach (var thread in threads)
                thread.Join();
            Assert.Equal(total, got.Distinct().Count());
            for (var p = 0; p < producers; p++)
            {
                var slice = got.Where(item => item / each == p).ToArray();
                Assert.Equal(Enumerable.Range(p * each, each).Select(item => (long)item), slice);
            }
        }

        struct Box
        {
            public Box(long value)
            {
                A = value;
                B = value;
                C = value;
                D = value;
            }

            public long A;
            public long B;
            public long C;
            public long D;
        }

        sealed class Same
        {
        }

        sealed class Boom
        {
            public override bool Equals(object obj) => throw new InvalidOperationException("equals");
            public override int GetHashCode() => throw new InvalidOperationException("hash");
        }
    }
}

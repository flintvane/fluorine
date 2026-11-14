using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fluorine.Tests
{
    public class ReactiveListTests
    {
        [Fact]
        public void AddInsertRemoveAndClearFollowListOrder()
        {
            var list = new ReactiveList<string>();
            list.Add("a");
            list.Insert(0, "b");
            list.Insert(2, "c");
            Assert.Equal(new[] { "b", "a", "c" }, list.ToArray());
            Assert.Equal(1, list.IndexOf("a"));
            Assert.True(list.Contains("c"));
            Assert.True(list.Remove("a"));
            Assert.False(list.Remove("a"));
            list.RemoveAt(0);
            Assert.Equal(new[] { "c" }, list.ToArray());
            list.Clear();
            var cleared = list.Value;
            Assert.Equal(0, list.Count);
            list.Clear();
            Assert.Same(cleared, list.Value);
            list.CheckInvariants();
        }

        [Fact]
        public void ConstructorKeepsDuplicatesAndRejectsNull()
        {
            var list = new ReactiveList<int>(new[] { 1, 1, 2 });
            Assert.Equal(new[] { 1, 1, 2 }, list.ToArray());
            Assert.True(list.Contains(1));
            Assert.Equal(0, list.IndexOf(1));
            Assert.Throws<ArgumentNullException>(() => new ReactiveList<int>((int[])null));
            var empty = new ReactiveList<string>(new string[0]);
            Assert.Equal(0, empty.Count);
            Assert.True(empty.Contains(null) == false);
        }

        [Fact]
        public void SetValueUsesObjectEqualsAndKeepsTheStoredInstance()
        {
            var list = new ReactiveList<ByValue>();
            list.Add(null);
            var first = new ByValue(1);
            var equal = new ByValue(1);
            Assert.True(list.SetValue(0, first, out var missing));
            Assert.Null(missing);
            Assert.False(list.SetValue(0, equal, out var same));
            Assert.Same(first, same);
            Assert.Same(first, list[0]);
            Assert.Same(list.Value, list.Value);

            var equatable = new ReactiveList<EquatableOnly>();
            equatable.Add(new EquatableOnly(1));
            var picture = equatable.Value;
            Assert.True(equatable.SetValue(0, new EquatableOnly(1)));
            Assert.NotSame(picture, equatable.Value);
        }

        [Fact]
        public void EqualsThrowLeavesTheStoredValue()
        {
            var list = new ReactiveList<Boom>();
            var stored = new Boom();
            list.Add(stored);
            Boom old = null;
            var thrown = Assert.Throws<InvalidOperationException>(() => list.SetValue(0, new Boom(), out old));
            Assert.Equal("boom", thrown.Message);
            Assert.Same(stored, old);
            Assert.Same(stored, list[0]);
            Assert.Equal(1, list.Count);
            list.CheckInvariants();
        }

        [Fact]
        public void NullIsAnElement()
        {
            var list = new ReactiveList<string>();
            list.Add(null);
            list.Add("a");
            list.Insert(1, null);
            Assert.Equal(new string[] { null, null, "a" }, list.ToArray());
            Assert.True(list.Contains(null));
            Assert.Equal(0, list.IndexOf(null));
            Assert.True(list.Remove(null));
            Assert.True(list.Contains(null));
            Assert.True(list.Remove(null));
            Assert.False(list.Contains(null));
            list.CheckInvariants();
        }

        [Fact]
        public void OutOfRangeSetThrowsAndGetterPastTheEndSubscribes()
        {
            var list = new ReactiveList<int>();
            list.Add(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => list[1] = 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(3, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => list[-1]);
            Assert.Equal(0, list.WatchedIndexes);
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Value[1]);
        }

        [Fact]
        public void DiffIsThePictureComparedByIndex()
        {
            var list = new ReactiveList<string>();
            list.Add("A");
            list.Add("B");
            list.Add("C");
            var before = list.Value;
            list.Insert(0, "X");
            Assert.Equal(new[]
            {
                "Updated:0:A->X",
                "Updated:1:B->A",
                "Updated:2:C->B",
                "Added:3:C"
            }, Names(list.Value.Diff(before)));

            var inserted = list.Value;
            list.RemoveAt(0);
            Assert.Equal(new[]
            {
                "Updated:0:X->A",
                "Updated:1:A->B",
                "Updated:2:B->C",
                "Removed:3:C"
            }, Names(list.Value.Diff(inserted)));

            var tail = list.Value;
            list.Add("D");
            var added = Assert.Single(list.Value.Diff(tail));
            Assert.Equal(ReactiveList<string>.ChangeKind.Added, added.Kind);
            Assert.Equal(3, added.Index);
            Assert.Equal("D", added.Value);
            Assert.Null(added.Previous);
            Assert.Equal(new[] { "Added:0:A", "Added:1:B", "Added:2:C", "Added:3:D" }, Names(list.Value.Diff(null)));
        }

        [Fact]
        public void SeparatelyBuiltEqualListsDiffEmpty()
        {
            var first = new ReactiveList<int>(new[] { 1, 2, 3 });
            var second = new ReactiveList<int>();
            second.Add(1);
            second.Insert(1, 2);
            second.Add(3);
            Assert.Empty(first.Value.Diff(second.Value));
        }

        [Fact]
        public void AppendSharesTheEarlierBlocks()
        {
            var list = new ReactiveList<int>();
            for (var i = 0; i < 200; i++)
                list.Add(i);
            var before = list.Value;
            list.Add(200);
            var added = Assert.Single(list.Value.Diff(before));
            Assert.Equal(200, added.Index);
            var shared = ReactiveList<int>.Version.Shared(before, list.Value);
            Assert.True(shared > list.Value.NodeCount / 2);
            list.CheckInvariants();
        }

        [Fact]
        public void RandomEditsMatchAListAndThePictureDiff()
        {
            var random = new Random(16260980);
            for (var trial = 0; trial < 30; trial++)
            {
                var list = new ReactiveList<int>();
                var model = new List<int>();
                ReactiveList<int>.Version seen = null;
                for (var step = 0; step < 40; step++)
                {
                    Edit(random, list, model);
                    Assert.Equal(model, list.ToArray());
                    Assert.Equal(model.Count, list.Count);
                    for (var i = 0; i < model.Count; i++)
                        Assert.Equal(model[i], list[i]);
                    Assert.Equal(model.Contains(7), list.Contains(7));
                    Assert.Equal(model.IndexOf(7), list.IndexOf(7));
                    Assert.Equal(PictureDiff(seen, model), Names(list.Value.Diff(seen)));
                    list.CheckInvariants();
                    seen = list.Value;
                }
            }
        }

        [Fact]
        public void ContainsRerunsOnlyWhenPresenceCrossesZero()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var list = new ReactiveList<int>();
                var runs = 0;
                var present = false;
                new Reactor(scheduler).Add(() =>
                {
                    present = list.Contains(7);
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.False(present);
                Assert.Equal(1, list.WatchedPresences);

                list.Add(1);
                scheduler.Pump();
                Assert.Equal(1, runs);

                list.Add(7);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.True(present);

                list.Add(7);
                scheduler.Pump();
                Assert.Equal(2, runs);

                list.Remove(7);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.True(present);

                list[0] = 7;
                scheduler.Pump();
                Assert.Equal(2, runs);

                Assert.True(list.Remove(7));
                Assert.True(list.Remove(7));
                scheduler.Pump();
                Assert.Equal(3, runs);
                Assert.False(present);

                list.Clear();
                scheduler.Pump();
                Assert.Equal(3, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void IndexReadFollowsThePosition()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var list = new ReactiveList<int>(new[] { 7, 7, 1, 2, 3 });
                var runs = 0;
                var seen = 0;
                new Reactor(scheduler).Add(() =>
                {
                    seen = list[1];
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(7, seen);

                list.Insert(4, 9);
                scheduler.Pump();
                Assert.Equal(1, runs);

                list.Insert(0, 7);
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(7, seen);

                list.Insert(1, 4);
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(4, seen);

                list.RemoveAt(1);
                scheduler.Pump();
                Assert.Equal(3, runs);
                Assert.Equal(7, seen);

                list[1] = 8;
                scheduler.Pump();
                Assert.Equal(4, runs);
                Assert.Equal(8, seen);

                list[1] = 8;
                scheduler.Pump();
                Assert.Equal(4, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void IndexerGetterSubscribesThenThrowsWhenTheIndexIsPastTheEnd()
        {
            var scheduler = new ManualScheduler();
            var seen = new List<Exception>();
            Action<Exception> handler = seen.Add;
            Reactive.UnhandledException += handler;
            try
            {
                var list = new ReactiveList<string>();
                var runs = 0;
                string text = null;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    text = list[1];
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Null(text);
                var error = Assert.Single(seen);
                Assert.IsType<ArgumentOutOfRangeException>(error);
                Assert.Equal(1, list.WatchedIndexes);

                list.Add("no");
                scheduler.Pump();
                Assert.Equal(1, runs);

                list.Add("yes");
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal("yes", text);

                list.Clear();
                scheduler.Pump();
                Assert.Equal(3, runs);
            }
            finally
            {
                Reactive.UnhandledException -= handler;
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void IndexOfRerunsOnAnyPictureChange()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var list = new ReactiveList<int>(new[] { 7, 1 });
                var runs = 0;
                var index = -1;
                new Reactor(scheduler).Add(() =>
                {
                    index = list.IndexOf(7);
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, index);
                Assert.Equal(0, list.WatchedIndexes);
                list[1] = 2;
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(0, index);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void CountRerunsWhenAValueChanges()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var list = new ReactiveList<int>(new[] { 1 });
                var runs = 0;
                var count = 0;
                new Reactor(scheduler).Add(() =>
                {
                    count = list.Count;
                    runs++;
                });
                scheduler.Pump();
                list[0] = 2;
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(1, count);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void PausedAndVersionReadsDoNotSubscribe()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var list = new ReactiveList<int>(new[] { 1, 2 });
                var picture = list.Value;
                var runs = 0;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    Reaction.Pause(() =>
                    {
                        list.Contains(1);
                        var unused = list[0];
                    });
                    Assert.True(picture.Contains(1));
                    Assert.Equal(0, picture.IndexOf(1));
                    Assert.Equal(1, picture[0]);
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, list.WatchedPresences);
                Assert.Equal(0, list.WatchedIndexes);
                list.Add(3);
                scheduler.Pump();
                Assert.Equal(1, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void WatchesDisposeOnTheirOwn()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var list = new ReactiveList<int>();
                var presence = new Reactor(scheduler).Add(() => list.Contains(7));
                var index = new Reactor(scheduler).Add(() => { var unused = list[1]; });
                scheduler.Pump();
                Assert.Equal(1, list.WatchedPresences);
                Assert.Equal(1, list.WatchedIndexes);
                presence.Dispose();
                scheduler.Pump();
                Assert.Equal(0, list.WatchedPresences);
                index.Dispose();
                scheduler.Pump();
                Assert.Equal(0, list.WatchedIndexes);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        static void Edit(Random random, ReactiveList<int> list, List<int> model)
        {
            var roll = random.Next(6);
            if (model.Count == 0 || roll == 0)
            {
                var value = random.Next(8);
                if (random.Next(2) == 0 || model.Count == 0)
                {
                    list.Add(value);
                    model.Add(value);
                }
                else
                {
                    var index = random.Next(model.Count + 1);
                    list.Insert(index, value);
                    model.Insert(index, value);
                }

                return;
            }

            if (roll == 1)
            {
                var index = random.Next(model.Count);
                var value = random.Next(8);
                list[index] = value;
                model[index] = value;
                return;
            }

            if (roll == 2)
            {
                var index = random.Next(model.Count);
                list.RemoveAt(index);
                model.RemoveAt(index);
                return;
            }

            if (roll == 3)
            {
                var value = random.Next(8);
                Assert.Equal(model.Remove(value), list.Remove(value));
                return;
            }

            if (roll == 4 && random.Next(8) == 0)
            {
                list.Clear();
                model.Clear();
                return;
            }

            var set = random.Next(model.Count);
            var next = model[set];
            list.SetValue(set, next);
            model[set] = next;
        }

        static string[] PictureDiff(ReactiveList<int>.Version previous, List<int> newer)
        {
            var older = previous == null ? new List<int>() : previous.ToList();
            var changes = new List<string>();
            var limit = Math.Min(older.Count, newer.Count);
            for (var i = 0; i < limit; i++)
            {
                if (older[i] != newer[i])
                    changes.Add("Updated:" + i + ":" + older[i] + "->" + newer[i]);
            }

            for (var i = limit; i < newer.Count; i++)
                changes.Add("Added:" + i + ":" + newer[i]);
            for (var i = limit; i < older.Count; i++)
                changes.Add("Removed:" + i + ":" + older[i]);
            return changes.ToArray();
        }

        static string[] Names(ReactiveList<string>.Change[] changes)
        {
            return changes.Select(change =>
            {
                if (change.Kind == ReactiveList<string>.ChangeKind.Updated)
                    return change.Kind + ":" + change.Index + ":" + change.Previous + "->" + change.Value;
                return change.Kind + ":" + change.Index + ":" + change.Value;
            }).ToArray();
        }

        static string[] Names(ReactiveList<int>.Change[] changes)
        {
            return changes.Select(change =>
            {
                if (change.Kind == ReactiveList<int>.ChangeKind.Updated)
                    return change.Kind + ":" + change.Index + ":" + change.Previous + "->" + change.Value;
                return change.Kind + ":" + change.Index + ":" + change.Value;
            }).ToArray();
        }

        sealed class ByValue(int number)
        {
            public int Number { get; } = number;
            public override bool Equals(object obj) => obj is ByValue other && other.Number == Number;
            public override int GetHashCode() => Number;
        }

        sealed class EquatableOnly(int number) : IEquatable<EquatableOnly>
        {
            public int Number { get; } = number;
            public bool Equals(EquatableOnly other) => other != null && other.Number == Number;
        }

        sealed class Boom
        {
            public override bool Equals(object obj) => throw new InvalidOperationException("boom");
            public override int GetHashCode() => 0;
        }
    }
}

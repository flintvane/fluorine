using System;
using System.Linq;
using Xunit;

namespace Fluorine
{
    public class ReactiveArrayTests
    {
        [Fact]
        public void LengthIsFixedAndSlotsStartAtDefault()
        {
            var array = new ReactiveArray<string>(3);
            Assert.Equal(3, array.Length);
            Assert.Equal(3, array.Count);
            Assert.Equal([null, null, null], array.ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReactiveArray<int>(-1));
            Assert.Throws<ArgumentNullException>(() => new ReactiveArray<int>((int[])null));
            Assert.Null(typeof(ReactiveArray<int>).GetMethod("Contains"));
        }

        [Fact]
        public void ConstructorKeepsOrderAndDuplicates()
        {
            var array = new ReactiveArray<int>([2, 2, 1]);
            Assert.Equal([2, 2, 1], array.ToArray());
            Assert.Equal(3, array.Length);
            var empty = new ReactiveArray<int>(new int[0]);
            Assert.Equal(0, empty.Length);
            Assert.Same(empty.Value, empty.Value);
        }

        [Fact]
        public void SetValueUsesObjectEqualsAndKeepsTheStoredInstance()
        {
            var array = new ReactiveArray<ByValue>(2);
            var first = new ByValue(1);
            var equal = new ByValue(1);
            Assert.True(array.SetValue(0, first, out var missing));
            Assert.Null(missing);
            Assert.False(array.SetValue(0, equal, out var same));
            Assert.Same(first, same);
            Assert.Same(first, array[0]);
            Assert.Same(array.Value, array.Value);

            var other = new ByValue(2);
            Assert.True(array.SetValue(0, other, out var previous));
            Assert.Same(first, previous);
            Assert.Same(other, array[0]);

            var equatable = new ReactiveArray<EquatableOnly>(1)
            {
                [0] = new EquatableOnly(1)
            };
            var picture = equatable.Value;
            Assert.True(equatable.SetValue(0, new EquatableOnly(1)));
            Assert.NotSame(picture, equatable.Value);
        }

        [Fact]
        public void EqualsThrowLeavesTheStoredValue()
        {
            var array = new ReactiveArray<Boom>(1);
            var stored = new Boom();
            Assert.True(array.SetValue(0, stored));
            Boom old = null;
            var thrown = Assert.Throws<InvalidOperationException>(() => array.SetValue(0, new Boom(), out old));
            Assert.Equal("boom", thrown.Message);
            Assert.Same(stored, old);
            Assert.Same(stored, array[0]);
            array.CheckInvariants();
        }

        [Fact]
        public void OutOfRangeReadsAndWritesDoNotSubscribe()
        {
            var array = new ReactiveArray<int>([1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => array[1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => array[-1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => array[1] = 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => array.Value[1]);
            Assert.Equal(0, array.WatchedIndexes);
            Assert.Equal(1, array[0]);
        }

        [Fact]
        public void DiffReportsUpdatedSlotsInIndexOrder()
        {
            var array = new ReactiveArray<int>([1, 2, 3]);
            var before = array.Value;
            Assert.False(array.SetValue(1, 2));
            Assert.Same(before, array.Value);
            array[0] = 9;
            array[2] = 8;
            Assert.Equal(["Updated:0:1->9", "Updated:2:3->8"], Names(array.Value.Diff(before)));
            Assert.Empty(array.Value.Diff(array.Value));
            Assert.Equal(["Added:0:9", "Added:1:2", "Added:2:8"], Names(array.Value.Diff(null)));

            var shorter = new ReactiveArray<int>([9]);
            Assert.Equal(["Removed:1:2", "Removed:2:8"], Names(shorter.Value.Diff(array.Value)));
            var updated = array.Value.Diff(before)[0];
            Assert.Equal(ReactiveArray<int>.ChangeKind.Updated, updated.Kind);
            Assert.Equal(9, updated.Value);
            Assert.Equal(1, updated.Previous);
        }

        [Fact]
        public void SeparatelyBuiltEqualArraysDiffEmpty()
        {
            var first = new ReactiveArray<int>([1, 2, 3]);
            var second = new ReactiveArray<int>(3)
            {
                [0] = 1,
                [1] = 2,
                [2] = 3
            };
            Assert.Empty(first.Value.Diff(second.Value));
            Assert.False(first.Value == second.Value);
        }

        [Fact]
        public void OneChangedSlotDiffsAsThatSlotAndSharesTheRest()
        {
            var array = new ReactiveArray<int>(1000);
            for (var i = 0; i < array.Length; i++)
                array[i] = i;
            var before = array.Value;
            array[500] = -1;
            var updated = Assert.Single(array.Value.Diff(before));
            Assert.Equal(500, updated.Index);
            Assert.Equal(-1, updated.Value);
            Assert.Equal(500, updated.Previous);
            var shared = ReactiveArray<int>.Version.Shared(before, array.Value);
            Assert.True(shared > array.Value.NodeCount / 2);
            array.CheckInvariants();
        }

        [Fact]
        public void IndexReadRerunsOnlyForThatSlot()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var array = new ReactiveArray<int>([0, 0, 0]);
                var runs = 0;
                var seen = 0;
                new Reactor(scheduler).Add(() =>
                {
                    seen = array[1];
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(1, array.WatchedIndexes);

                array[0] = 4;
                scheduler.Pump();
                Assert.Equal(1, runs);

                array[1] = 5;
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(5, seen);

                array[1] = 5;
                scheduler.Pump();
                Assert.Equal(2, runs);

                array[2] = 6;
                scheduler.Pump();
                Assert.Equal(2, runs);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void CountRerunsWhenASlotChanges()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var array = new ReactiveArray<int>(2);
                var runs = 0;
                var length = 0;
                new Reactor(scheduler).Add(() =>
                {
                    length = array.Length;
                    runs++;
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(2, length);
                array[0] = 1;
                scheduler.Pump();
                Assert.Equal(2, runs);
                Assert.Equal(2, length);
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
                var array = new ReactiveArray<int>([1, 2]);
                var picture = array.Value;
                var runs = 0;
                new Reactor(scheduler).Add(() =>
                {
                    runs++;
                    Reaction.Pause(() =>
                    {
                        var unused = array[0];
                    });
                    Assert.Equal(1, picture[0]);
                    Assert.Equal([1, 2], picture.ToArray());
                });
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(0, array.WatchedIndexes);
                array[0] = 9;
                scheduler.Pump();
                Assert.Equal(1, runs);
                Assert.Equal(1, picture[0]);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        [Fact]
        public void IndexWatchesDisposeOnTheirOwn()
        {
            var scheduler = new ManualScheduler();
            try
            {
                var array = new ReactiveArray<int>(3);
                var first = new Reactor(scheduler).Add(() => { var unused = array[1]; });
                var second = new Reactor(scheduler).Add(() => { var unused = array[1]; });
                scheduler.Pump();
                Assert.Equal(1, array.WatchedIndexes);
                first.Dispose();
                scheduler.Pump();
                Assert.Equal(1, array.WatchedIndexes);
                second.Dispose();
                scheduler.Pump();
                Assert.Equal(0, array.WatchedIndexes);
            }
            finally
            {
                scheduler.Shutdown();
            }
        }

        static string[] Names(ReactiveArray<int>.Change[] changes)
        {
            return
            [
                .. changes.Select(change =>
                {
                    if (change.Kind == ReactiveArray<int>.ChangeKind.Updated)
                        return change.Kind + ":" + change.Index + ":" + change.Previous + "->" + change.Value;
                    return change.Kind + ":" + change.Index + ":" + change.Value;
                })
            ];
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

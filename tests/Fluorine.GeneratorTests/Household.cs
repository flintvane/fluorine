namespace Fluorine.GeneratorTests
{
    public partial class Household : AutoNotify
    {
        public Household()
        {
            HappyHousewife = 7;
        }

        [Reactive]
        public partial int HappyHousewife { get; set; }

        [Reactive]
        public partial string Title { get; set; }

        [AutoNotify]
        public int Twice => HappyHousewife * 2;
    }

    public partial class Counter : AutoNotify
    {
        [AutoNotify]
        public partial int Count { get; set; }
    }

    public sealed class Plain : AutoNotify
    {
        public readonly Register<int> Source = new(1);

        [AutoNotify]
        public int Value => Source.Value;
    }
}

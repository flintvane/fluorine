using System;
using System.Collections.Generic;

namespace Fluorine
{
    /// <summary>
    /// A FIFO queue. <see cref="Pop"/> and <see cref="TryPop"/> subscribe to whether a new
    /// element is available at the head. <see cref="Push"/> adds at the tail.
    /// There is no picture and no version: elements are taken, not snapshotted.
    /// A push notifies only when the queue was empty, because only then is the pushed
    /// element the new head. A pop notifies only when another element is left at the head.
    /// Creating one needs no scheduler. Push and pop can be called from any thread.
    /// </summary>
    public sealed class ReactiveQueue<T> : Register
    {
        readonly Queue<T> _items = new Queue<T>();

        public ReactiveQueue()
        {
        }

        public ReactiveQueue(IEnumerable<T> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            foreach (var item in items)
                _items.Enqueue(item);
        }

        public void Push(T item)
        {
            bool headAppeared;
            lock (this)
            {
                headAppeared = _items.Count == 0;
                _items.Enqueue(item);
            }

            if (headAppeared)
                TriggerReactions();
        }

        public T Pop() 
            => TryPop(out var item) ? item : throw new InvalidOperationException("The queue is empty.");

        public bool TryPop(out T item)
        {
            bool taken;
            lock (this)
            {
                // Subscribe and observe under the same lock, so a push cannot
                // make a head visible without this reaction being a listener.
                RecordRead();
                taken = _items.Count > 0;
                item = !taken ? default : _items.Dequeue();
            }

            if (taken)
                TriggerReactions();
            return taken;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Fluorine
{
    public class Register
    {
        readonly object _sync = new object();
        readonly HashSet<Reaction> _listeners = new HashSet<Reaction>();

        public string Name { get; set; }

        internal object Sync
        {
            get { return _sync; }
        }

        public void RecordRead()
        {
            if (!Reaction.Recording)
                return;
            var current = Reaction.Current;
            if (current == null)
                return;
            current.Subscribe(this);
        }

        public void TriggerReactions()
        {
            Reaction[] snapshot;
            lock (_sync)
            {
                snapshot = new Reaction[_listeners.Count];
                _listeners.CopyTo(snapshot);
                for (var i = 0; i < snapshot.Length; i++)
                    snapshot[i].Trigger();
            }
        }

        internal void AddListener(Reaction reaction)
        {
            if (reaction == null)
                return;
            lock (_sync)
                _listeners.Add(reaction);
        }

        internal void RemoveListener(Reaction reaction)
        {
            if (reaction == null)
                return;
            lock (_sync)
                _listeners.Remove(reaction);
        }
    }

    public class Register<T> : Register
    {
        T _value;

        public Register()
        {
            _value = default(T);
        }

        public Register(T initial)
        {
            _value = initial;
        }

        protected T StoredValue
        {
            get
            {
                lock (Sync)
                    return _value;
            }
            set
            {
                lock (Sync)
                    _value = value;
            }
        }

        public T Value
        {
            get
            {
                RecordRead();
                lock (Sync)
                    return _value;
            }
            set
            {
                Write(value);
            }
        }

        public bool SetValue(T newValue)
        {
            return Write(newValue);
        }

        public bool SetValue(T newValue, out T oldValue)
        {
            lock (Sync)
                oldValue = _value;
            return Write(newValue, oldValue);
        }

        bool Write(T newValue)
        {
            T snapshot;
            lock (Sync)
                snapshot = _value;
            return Write(newValue, snapshot);
        }

        bool Write(T newValue, T snapshot)
        {
            if (object.Equals(newValue, snapshot))
                return false;
            lock (Sync)
                _value = newValue;
            TriggerReactions();
            return true;
        }
    }
}

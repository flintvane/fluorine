using System;
using System.Collections.Generic;

namespace Fluorine
{
    public class Register
    {
        readonly HashSet<Reaction> _listeners = new HashSet<Reaction>();

        public string Name { get; set; }

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
            lock (this)
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
            lock (this)
                _listeners.Add(reaction);
        }

        internal void RemoveListener(Reaction reaction)
        {
            if (reaction == null)
                return;
            lock (this)
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
                lock (this)
                    return _value;
            }
            set
            {
                lock (this)
                    _value = value;
            }
        }

        public T Value
        {
            get
            {
                RecordRead();
                lock (this)
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
            lock (this)
                oldValue = _value;
            return Write(newValue, oldValue);
        }

        bool Write(T newValue)
        {
            T snapshot;
            lock (this)
                snapshot = _value;
            return Write(newValue, snapshot);
        }

        bool Write(T newValue, T snapshot)
        {
            if (object.Equals(newValue, snapshot))
                return false;
            lock (this)
                _value = newValue;
            TriggerReactions();
            return true;
        }
    }
}

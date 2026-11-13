using System.Collections.Generic;

namespace Fluorine
{
    public class Register
    {
        readonly HashSet<Reaction> _listeners = [];

        public string Name { get; set; }

        public void RecordRead()
        {
            if (!Reaction.IsRecording)
                return;
            var current = Reaction.Current;
            current?.Subscribe(this);
        }

        // Listener operations lock this. A derived register may share another object's lock.
        protected virtual object SyncRoot => this;

        public void TriggerReactions()
        {
            lock (SyncRoot)
            {
                var snapshot = new Reaction[_listeners.Count];
                _listeners.CopyTo(snapshot);
                for (var i = 0; i < snapshot.Length; i++)
                    snapshot[i].Trigger();
            }
        }

        internal virtual bool AddListener(Reaction reaction)
        {
            if (reaction == null)
                return false;
            lock (SyncRoot)
                return AddListenerLocked(reaction);
        }

        internal virtual bool RemoveListener(Reaction reaction)
        {
            if (reaction == null)
                return false;
            lock (SyncRoot)
                return RemoveListenerLocked(reaction);
        }

        // Caller holds SyncRoot.
        protected bool AddListenerLocked(Reaction reaction)
        {
            return _listeners.Add(reaction);
        }

        // Caller holds SyncRoot.
        protected bool RemoveListenerLocked(Reaction reaction)
        {
            return _listeners.Remove(reaction);
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
            set => Write(value);
        }

        public bool SetValue(T newValue)
        {
            return Write(newValue);
        }


        bool Write(T newValue)
        {
            T snapshot;
            lock (this)
                snapshot = _value;
            return Write(newValue, snapshot);
        }
        
        public bool SetValue(T newValue, out T oldValue)
        {
            lock (this)
                oldValue = _value;
            return Write(newValue, oldValue);
        }

        private bool Write(T newValue, T snapshot)
        {
            if (Equals(newValue, snapshot))
                return false;
            lock (this)
                _value = newValue;
            TriggerReactions();
            return true;
        }
    }
}
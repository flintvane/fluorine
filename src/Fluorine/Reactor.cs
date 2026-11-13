using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Fluorine
{
    public class Reactor : IDisposable
    {
        static readonly Dictionary<MethodInfo, Action<object>> ReactiveCalls = new();

        readonly Scheduler _scheduler;
        readonly HashSet<Reaction> _reactions = [];
        int _disposed;

        public Reactor() : this(Scheduler.Current)
        {
        }

        public Reactor(Scheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ReactiveException("A reactor requires a scheduler.");
            if (!scheduler.TryAddReactor(this))
                _disposed = 1;
        }

        public string Name { get; set; }

        public void Queue(Action action)
        {
            if (_disposed != 0)
                return;
            _scheduler.Queue(action);
        }

        internal void Queue(Reaction reaction)
        {
            if (reaction == null)
                return;
            _scheduler.Queue(reaction);
        }

        public Reaction Add(Action lambda) => Add(null, lambda);

        public Reaction Add(string name, Action lambda)
        {
            if (_disposed != 0)
                return null;
            if (_scheduler != null && _scheduler.HasStartedShutdown)
                return null;

            var reaction = new Reaction(this, name, lambda ?? (() => { }));
            lock (this)
            {
                if (_disposed != 0)
                    return null;
                _reactions.Add(reaction);
            }

            reaction.RequestRun();
            return reaction;
        }

        public Reaction Computed<T>(Register<T> register, Func<T> func, bool disposeOld = false)
        {
            return Add(() =>
            {
                var result = func != null ? func() : default(T);
                if (!disposeOld)
                {
                    register.SetValue(result);
                    return;
                }

                if (register.SetValue(result, out var oldValue) && oldValue is IDisposable disposable)
                    disposable.Dispose();
            });
        }

        // Parameterless void instance methods marked [Reactive] become reactions.
        // A marked virtual method is one reaction; the override on the instance runs.
        // A method declared with new is a separate reaction. The first run is queued.
        public void AddReactiveMethods(object instance)
        {
            if (instance == null)
                throw new ReactiveException("AddReactiveMethods requires an object.");
            if (_disposed != 0)
                return;

            var seen = new HashSet<MethodInfo>();
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var methods = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var method in methods)
                {
                    if (!IsReactiveMethod(method))
                        continue;
                    if (!seen.Add(method.GetBaseDefinition()))
                        continue;

                    var callable = method.IsAbstract ? ConcreteImplementation(instance.GetType(), method) : method;
                    if (callable == null)
                        continue;

                    var call = CachedCall(callable);
                    Add(callable.Name, () => call(instance));
                }
            }
        }

        static bool IsReactiveMethod(MethodInfo method)
        {
            if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
                return false;
            if (method.IsSpecialName)
                return false;
            if (method.ReturnType != typeof(void))
                return false;
            if (method.GetParameters().Length != 0)
                return false;
            return method.IsDefined(typeof(ReactiveAttribute), false);
        }

        // An abstract method has no body. Call the most derived concrete override.
        static MethodInfo ConcreteImplementation(Type runtimeType, MethodInfo marked)
        {
            var slot = marked.GetBaseDefinition();
            for (var type = runtimeType; type != null; type = type.BaseType)
            {
                var declared = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var candidate in declared)
                {
                    if (candidate.IsAbstract || candidate.ContainsGenericParameters)
                        continue;
                    if (candidate.GetParameters().Length != 0 || candidate.ReturnType != typeof(void))
                        continue;
                    if (candidate.GetBaseDefinition().Equals(slot))
                        return candidate;
                }
            }

            return null;
        }

        static Action<object> CachedCall(MethodInfo method)
        {
            lock (ReactiveCalls)
            {
                if (ReactiveCalls.TryGetValue(method, out var call))
                    return call;
                call = CompileCall(method);
                ReactiveCalls.Add(method, call);
                return call;
            }
        }

        // MethodInfo.Invoke would wrap a thrown exception in TargetInvocationException.
        // A direct call lets it reach the reaction as the method threw it (cs 7.2, 7.3).
        static Action<object> CompileCall(MethodInfo method)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var call = Expression.Call(
                Expression.Convert(instance, method.DeclaringType),
                method);
            return Expression.Lambda<Action<object>>(call, instance).Compile();
        }

        public virtual void Dispose()
        {
            DisposeCore(false);
        }

        internal void DisposeFromShutdown()
        {
            DisposeCore(true);
        }

        void DisposeCore(bool fromShutdown)
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                if (!fromShutdown)
                    return;
            }

            Reaction[] snapshot;
            lock (this)
            {
                snapshot = new Reaction[_reactions.Count];
                _reactions.CopyTo(snapshot);
                _reactions.Clear();
            }

            foreach (var t in snapshot)
            {
                if (fromShutdown)
                    t.UnsubscribeStale(0); // stamps are run counts, never 0
                t.Dispose();
            }

            _scheduler?.RemoveReactor(this);
        }

        internal void Remove(Reaction reaction)
        {
            lock (this)
                _reactions.Remove(reaction);
        }

        public void Clear()
        {
            lock (this)
            {
                foreach (var a in _reactions.ToList())
                    a.Dispose();
            }
        }
    }
}
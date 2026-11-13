using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace Fluorine
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class AutoNotifyAttribute : Attribute
    {
    }

    public class AutoNotify : Reactor, INotifyPropertyChanged
    {
        static readonly Dictionary<MethodInfo, Func<object, object>> Getters =
            new Dictionary<MethodInfo, Func<object, object>>();

        PropertyChangedEventHandler _propertyChanged;
        int _wired;

        // Method reactions are armed here. Their first run is queued, so it happens
        // after the derived constructor. Property reactions stay lazy until
        // PropertyChanged has a subscriber (9.4).
        public AutoNotify()
        {
            AddReactiveMethods(this);
        }

        public event PropertyChangedEventHandler PropertyChanged
        {
            add
            {
                lock (this)
                    _propertyChanged += value;
                Wire();
            }
            remove
            {
                lock (this)
                    _propertyChanged -= value;
            }
        }

        // 9.4: the reactions exist only once something listens.
        void Wire()
        {
            if (Interlocked.CompareExchange(ref _wired, 1, 0) != 0)
                return;

            var seen = new HashSet<string>();
            for (var type = GetType(); type != null && type != typeof(AutoNotify); type = type.BaseType)
            {
                var properties = type.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var property in properties)
                {
                    if (property.GetIndexParameters().Length != 0)
                        continue;
                    if (!seen.Add(property.Name))
                        continue;
                    var method = property.GetGetMethod(true);
                    if (method == null || !IsMarked(property))
                        continue;
                    Arm(property.Name, CachedGetter(method));
                }
            }
        }

        // GetCustomAttributes does not walk an override back to the attribute on the base property.
        static bool IsMarked(PropertyInfo property)
        {
            var getter = property.GetGetMethod(true);
            if (getter == null)
                return false;
            var slot = getter.GetBaseDefinition();
            for (var type = property.DeclaringType; type != null && type != typeof(AutoNotify); type = type.BaseType)
            {
                var declared = DeclaredProperty(type, property.Name);
                if (declared == null)
                    continue;
                var declaredGetter = declared.GetGetMethod(true);
                if (declaredGetter == null || !declaredGetter.GetBaseDefinition().Equals(slot))
                    continue;
                if (declared.GetCustomAttributes(typeof(AutoNotifyAttribute), false).Length != 0)
                    return true;
            }
            return false;
        }

        static PropertyInfo DeclaredProperty(Type type, string name)
        {
            var properties = type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (var candidate in properties)
            {
                if (candidate.Name == name && candidate.GetIndexParameters().Length == 0)
                    return candidate;
            }
            return null;
        }

        void Arm(string name, Func<object, object> read)
        {
            object previous =  read(this);
            Add(name, () =>
            {
                var value = read(this);
                if (Equals(previous, value)) return;
                previous = value;
                PropertyChangedEventHandler handler;
                lock (this)
                    handler = _propertyChanged;
                using(Reaction.Pause())
                    handler?.Invoke(this, new PropertyChangedEventArgs(name));
            });
        }

        static Func<object, object> CachedGetter(MethodInfo method)
        {
            lock (Getters)
            {
                if (Getters.TryGetValue(method, out var getter))
                    return getter;
                getter = CompileGetter(method);
                Getters.Add(method, getter);
                return getter;
            }
        }

        // PropertyInfo.GetValue would wrap a getter exception in TargetInvocationException.
        // A direct call lets it reach the reaction as the getter threw it (cs 7.2, 7.3).
        static Func<object, object> CompileGetter(MethodInfo method)
        {
            var openType = typeof(Func<,>).MakeGenericType(method.DeclaringType, method.ReturnType);
            var open = Delegate.CreateDelegate(openType, method);
            var instance = Expression.Parameter(typeof(object), "instance");
            var call = Expression.Invoke(
                Expression.Constant(open),
                Expression.Convert(instance, method.DeclaringType));
            var body = Expression.Convert(call, typeof(object));
            return Expression.Lambda<Func<object, object>>(body, instance).Compile();
        }
    }
}

using System;

namespace Fluorine
{
    // Marks a partial property. The source generator writes a Register<T> for it
    // and the get/set accessors, and marks that property [AutoNotify].
    // Also marks a parameterless void method. AutoNotify adds each such method
    // on the instance as a reaction on its reactor.
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, Inherited = false)]
    public sealed class ReactiveAttribute : Attribute
    {
    }
}

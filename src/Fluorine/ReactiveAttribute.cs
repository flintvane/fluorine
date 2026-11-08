using System;

namespace Fluorine
{
    // Marks a partial property. The source generator writes a Register<T> for it
    // and the get/set accessors, and marks that property [AutoNotify].
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class ReactiveAttribute : Attribute
    {
    }
}

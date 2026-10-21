using System;

namespace Fluorine
{
    public class ReactiveException : Exception
    {
        public ReactiveException()
        {
        }

        public ReactiveException(string message)
            : base(message)
        {
        }

        public ReactiveException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}

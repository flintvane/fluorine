using System;

namespace Fluorine
{
    public static class Reactive
    {
        // TODO -  Why is this called in such retarded manner?
        public static event Action<Exception> UnhandledException;

        public static void Shutdown()
        {
            if (Reaction.Current != null)
            {
                var ex = new ReactiveException("Cannot shut down from inside a reaction.");
                Raise(ex);
                throw ex;
            }

            var schedulers = Scheduler.SnapshotAll();
            for (var i = 0; i < schedulers.Length; i++)
                schedulers[i].RequestShutdown();

            var current = Scheduler.Current;
            if (current != null)
                current.DrainAndFinishOnOwnerThread();

            for (var i = 0; i < schedulers.Length; i++)
                schedulers[i].WaitUntilFinished();
        }

        internal static void Raise(ReactiveException exception)
        {
            if (exception == null)
                return;

            var handler = UnhandledException;
            if (handler == null)
            {
                Console.WriteLine("Fluorine warning: " + exception);
                return;
            }

            var list = handler.GetInvocationList();
            foreach (var t in list)
            {
                var action = (Action<Exception>)t;
                try
                {
                    action(exception);
                }
                catch (Exception handlerEx)
                {
                    Console.WriteLine("Fluorine warning: " + exception);
                    Console.WriteLine("Fluorine warning: handler threw: " + handlerEx);
                }
            }
        }
    }
}

using System;

namespace Fluorine
{
    public static class Reactive
    {
        public static event Action<Exception> UnhandledException;

        public static void Shutdown()
        {
            if (Reaction.Current != null)
                throw new ReactiveException("Cannot shut down from inside a reaction.");

            var schedulers = Scheduler.SnapshotAll();
            foreach (var t in schedulers)
                t.RequestShutdown();

            var current = Scheduler.Current;
            current?.DrainAndFinishOnOwnerThread();

            foreach (var t in schedulers)
                t.WaitUntilFinished();
        }

        internal static void OnUnhandledException(Exception exception)
        {
            if (exception == null)
                return;
            try
            {
                var handler = UnhandledException;
                if (handler == null)
                    Console.WriteLine($"Fluorine warning: {exception}");
                else
                    handler(exception);
            }
            catch (Exception handlerEx)
            {
                Console.WriteLine($"Fluorine warning: {exception}");
                Console.WriteLine($"Fluorine warning: handler threw: {handlerEx}");
            }
        }
    }
}
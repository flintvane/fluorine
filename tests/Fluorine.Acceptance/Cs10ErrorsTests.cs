using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs10ErrorsTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs10.1-01")]
        public void cs10_1_01()
        {
            var names = new HashSet<string>();
            foreach (var type in typeof(AcceptanceTest).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var facts = method.GetCustomAttributes(typeof(FactAttribute), false);
                    if (facts.Length == 0)
                        continue;
                    var fact = (FactAttribute)facts[0];
                    names.Add(fact.DisplayName ?? method.Name);
                }
            }

            string[] required =
            {
                "cs2.2-02", "s15.2-01",
                "cs2.3-02",
                "cs2.20-02",
                "cs3.8-06", "cs3.8-07", "cs3.8-08",
                "cs3.11-03",
                "cs3.13-04", "cs3.13-05", "cs10.1-02",
                "cs3.16-01",
                "cs4.2-05", "cs5.2-03",
                "cs6.1-03"
            };
            foreach (var id in required)
                Assert.True(names.Contains(id), "cs10.1 case list missing " + id);
        }

        [Fact(DisplayName = "cs10.1-02")]
        public void cs10_1_02()
        {
            using (var log = new Log())
            {
                Exception caught = null;
                Scheduler forThread = null;
                var s = ThreadScheduler.CreateNew("cs10.1-02", () =>
                {
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    try
                    {
                        WinFormsScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        caught = ex;
                    }
                    forThread = Scheduler.ForThread(Thread.CurrentThread);
                });
                Assert.IsType<ReactiveException>(caught);
                Assert.Equal(0, log.Count);
                Assert.Same(s, forThread);
            }
        }
    }
}

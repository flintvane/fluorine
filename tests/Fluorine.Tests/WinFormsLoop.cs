using System;
using System.Threading;
using System.Windows.Forms;

namespace Fluorine;

sealed class WinFormsLoop : IDisposable
{
    public Thread Thread;

    public Form Form;

    public WinFormsScheduler Scheduler;

    readonly ManualResetEvent _ready = new(false);

    Exception _error;

    public static WinFormsLoop Start()
    {
        var loop = new WinFormsLoop();
        var thread = new Thread(loop.Run)
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Name = "acceptance-winforms";
        loop.Thread = thread;
        thread.Start();
        Harness.Wait(loop._ready, 15000, "winforms loop");
        if (loop._error != null)
            throw new InvalidOperationException("winforms loop failed", loop._error);
        return loop;
    }

    void Run()
    {
        try
        {
            Form = new Form();
            Form.ShowInTaskbar = false;
            Form.FormBorderStyle = FormBorderStyle.None;
            Form.StartPosition = FormStartPosition.Manual;
            Form.Location = new System.Drawing.Point(-20000, -20000);
            Form.Load += delegate
            {
                try
                {
                    Scheduler = WinFormsScheduler.AttachToCurrent();
                }
                catch (Exception ex)
                {
                    _error = ex;
                }
                _ready.Set();
            };
            Application.Run(Form);
        }
        catch (Exception ex)
        {
            _error = ex;
            _ready.Set();
        }
    }

    public void Dispose()
    {
        try
        {
            if (Scheduler != null)
                Scheduler.Shutdown();
        }
        catch (Exception)
        {
        }
        try
        {
            if (Form != null && Form.IsHandleCreated)
                Form.BeginInvoke(new Action(Application.ExitThread));
        }
        catch (Exception)
        {
        }
        if (Thread != null)
            Thread.Join(Harness.TimeoutMs);
    }
}
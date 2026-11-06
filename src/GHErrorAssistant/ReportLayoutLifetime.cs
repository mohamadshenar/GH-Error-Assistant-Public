using System;

namespace GHErrorAssistant
{
    // UI-thread lifetime guard for queued/reentrant layout callbacks.
    internal sealed class ReportLayoutLifetime
    {
        private bool stopped, running;
        internal bool IsStopped => stopped;
        internal void Stop() { stopped = true; }
        internal void Run(Action layout)
        {
            if (stopped || running) return;
            running = true;
            try { layout(); }
            finally { running = false; }
        }
    }
}


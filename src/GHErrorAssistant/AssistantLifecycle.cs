namespace GHErrorAssistant
{
    // Closing the modeless assistant suspends one reusable window, not a burst of native destruction/recreation.
    internal sealed class AssistantLifecycle
    {
        internal bool Active { get; private set; }
        internal bool Terminated { get; private set; }
        internal bool Resume() { if (Active || Terminated) return false; Active = true; return true; }
        internal bool Pause() { bool changed = Active; Active = false; return changed; }
        internal bool Terminate() { if (Terminated) return false; Pause(); Terminated = true; return true; }
    }
}

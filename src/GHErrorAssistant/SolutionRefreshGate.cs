namespace GHErrorAssistant
{
    // Called on the UI timer; never request a new solution or read a different document.
    internal static class SolutionRefreshGate
    {
        internal static bool ShouldRefresh(bool hasSession, bool sameDocument, bool solving, long revision, long lastRefresh) =>
            hasSession && sameDocument && !solving && revision > lastRefresh;
    }
}


using System;

namespace GHErrorAssistant.Diagnostics
{
    // The SDK adapter rechecks identity/existence immediately before focusing, on the UI thread.
    internal static class LocatePolicy
    {
        internal static bool CanLocate(Guid reportDocument, Guid activeDocument, Guid owner,
            bool sameDocumentInstance, bool ownerExists, bool reportingObjectExists, bool ready, bool parameterExists = true) =>
            reportDocument != Guid.Empty && reportDocument == activeDocument && owner != Guid.Empty &&
            sameDocumentInstance && ownerExists && reportingObjectExists && ready && parameterExists;

        internal static bool ConfirmedMissingTarget(Guid reportDocument, Guid activeDocument, Guid owner,
            bool sameDocumentInstance, bool ownerExists) => reportDocument != Guid.Empty &&
            reportDocument == activeDocument && owner != Guid.Empty && sameDocumentInstance && !ownerExists;
    }
}


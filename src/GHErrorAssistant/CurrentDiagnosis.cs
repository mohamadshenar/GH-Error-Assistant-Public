using System;
using System.Collections.Generic;
using System.Linq;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant
{
    // Only live observations may supply current message text. Baseline text is never a fallback.
    internal static class CurrentDiagnosis
    {
        internal static RuntimeObservation Select(Guid owner, IEnumerable<RuntimeObservation> messages) =>
            messages.Where(m => m.OwnerId == owner).OrderByDescending(m => m.Severity == "Error").FirstOrDefault();
        internal static Report Create(Report identity, TutorContext context, string details, IEnumerable<RuntimeObservation> messages)
        {
            var message = Select(identity.OwnerId, messages);
            return new Report { Name = identity.Name, DocumentId = identity.DocumentId, OwnerId = identity.OwnerId,
                FocusInputId = message?.ParameterId ?? identity.FocusInputId, Context = context, DiagnosticText = details,
                HasRuntimeMessage = message != null, Message = message?.Message ?? "", Severity = message?.Severity ?? "Information" };
        }
    }
}

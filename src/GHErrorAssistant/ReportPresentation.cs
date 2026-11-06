using System;
using System.Linq;
using GHErrorAssistant.Core;

namespace GHErrorAssistant
{
    internal sealed class Report
    {
        internal string Name, Severity, Message;
        internal bool HasRuntimeMessage = true;
        internal bool HasCurrentFinding => HasRuntimeMessage || Context?.Graph?.Issues.Any(i =>
            i.Observation.Kind != Diagnostics.ObservationKind.EmptyCache && i.Observation.Kind != Diagnostics.ObservationKind.Other &&
            i.Observation.Kind != Diagnostics.ObservationKind.InspectionNote) == true;
        internal Guid DocumentId = Guid.Empty;
        internal Guid OwnerId = Guid.Empty, FocusInputId = Guid.Empty;
        internal string DiagnosticText = "Dependency inspection pending. No source has been inferred.";
        internal TutorContext Context = null;
        internal string EvidenceText => Context?.Graph == null ? DiagnosticText : Diagnostics.DependencyAnalysis.Format(Context.Graph, Tutor.Guidance.Targets);
        internal TutorExplanation Tutor => ExplanationEngine.ExplainWithContext(Message, Context ?? new TutorContext { Component = Name, InspectionPartial = true });
        internal Explanation Explanation => ExplanationEngine.Explain(Message);
        internal string Observation => "Grasshopper recorded this " + Severity.ToLowerInvariant() +
            " on the named object. No root cause has been independently verified.";
        internal string Causes => string.Join("\n", Explanation.PossibleCauses.Select(x => "• " + x));
        internal string Steps => string.Join("\n", Tutor.Steps.Select((x, i) => (i + 1) + ". " + x));
        internal string Text()
        {
            if (!HasCurrentFinding) return Name + " — current inspection\nNo runtime warning or specific actionable observation was found in this bounded inspection.\n" +
                "This does not prove the intended output is correct.\n\n" + DiagnosticText;
            var tutor = Tutor;
            var guide = tutor.Guidance;
            string primary = guide == null ? "" : "BEGINNER GUIDANCE\nWHAT'S WRONG?\n" + guide.Wrong + "\n\nSTART HERE\n" + guide.StartHere +
                "\n\nWHY THIS MATTERS\n" + guide.Why + "\n\nHOW TO FIX IT\n" + string.Join("\n", guide.Steps.Select((x, i) => (i + 1) + ". " + x)) +
                "\n\nIF THAT DOESN'T WORK\n" + guide.Next + "\n\nTECHNICAL DETAILS\n" + guide.GroupedEvidence + "\n\n";
            return primary + Name + " — " + Severity + "\n\nWHAT'S WRONG?\n" + tutor.WhatsWrong +
                "\n\nWHAT I FOUND\n" + tutor.WhatIFound + "\n\nHOW TO FIX IT\n" + Steps +
                "\n\nIF THAT DOESN'T WORK\n" + tutor.IfNotResolved + "\n\nWHERE TO LOOK\n" + tutor.Where +
                "\n\nORIGINAL GRASSHOPPER MESSAGE\n" + Message + "\n\nCONFIRMED OBSERVATIONS AND UNCERTAINTY\n" + tutor.Evidence +
                "\n\nEXCEPTION OR MESSAGE INTERPRETATION\n" + tutor.Why + "\n\nLIKELY SOURCE OF THE PROBLEM\n" + EvidenceText;
        }

        // Only the compact picker is abbreviated. Report text and clipboard data never use this preview.
        internal static string Preview(string text, int length = 64)
        {
            if (length < 2) throw new ArgumentOutOfRangeException(nameof(length));
            string singleLine = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (singleLine.Length <= length) return singleLine;
            int end = length - 1;
            if (end > 0 && char.IsHighSurrogate(singleLine[end - 1])) end--;
            return singleLine.Substring(0, end) + "…";
        }
    }
}

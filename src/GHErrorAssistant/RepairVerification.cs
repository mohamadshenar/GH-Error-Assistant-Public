using System;
using System.Collections.Generic;
using System.Linq;

namespace GHErrorAssistant.Diagnostics
{
    internal sealed class RuntimeObservation
    {
        internal Guid OwnerId, ParameterId;
        internal string Message = "", Severity = "", Location = "";
        internal string Identity => OwnerId + ":" + ParameterId + ":" + Message;
        internal string Key => Identity + ":" + Severity;
        internal RuntimeObservation Copy() => (RuntimeObservation)MemberwiseClone();
        public override string ToString() => Severity + " · " + Location + ": " + Message;
    }
    internal sealed class InputObservation
    {
        internal Guid OwnerId, ParameterId;
        internal string Location = "", WarningState = "", Tree = "", References = "";
        internal int? Count;
        internal bool HasMissing, HasInvalid, MissingReference, Complete;
        internal bool SampleUsable;
        internal int? MissingReferenceCount = null, ResolvedReferenceCount = null;
        internal bool ReferencesComplete = true;
        internal string Key => OwnerId + ":" + ParameterId;
        internal InputObservation Copy() => (InputObservation)MemberwiseClone();
    }
    internal sealed class VerificationSnapshot
    {
        internal Guid DocumentId, OwnerId;
        internal Guid FocusInputId;
        internal string OriginalSeverity = "";
        internal string DocumentToken = "", OriginalMessage = "";
        internal DateTime CapturedUtc = DateTime.UtcNow;
        internal long SolutionRevision;
        internal bool TargetExists = true, Complete = true, MessagesComplete = true, Ready = true;
        internal bool InvestigationTargetMissing;
        internal readonly List<Guid> Selection = new List<Guid>();
        internal readonly List<string> Messages = new List<string>();
        internal readonly List<RuntimeObservation> RuntimeMessages = new List<RuntimeObservation>();
        internal readonly List<InputObservation> Inputs = new List<InputObservation>();
        internal bool IncludeCurrentMessages(VerificationSnapshot scope, Guid expectedOwner)
        {
            if (scope == null || scope.DocumentId != DocumentId || scope.DocumentToken != DocumentToken || scope.OwnerId != expectedOwner || !scope.Ready)
            { Complete = false; MessagesComplete = false; return false; }
            foreach (var message in scope.RuntimeMessages)
            {
                if (RuntimeMessages.Any(m => m.Key == message.Key)) continue;
                if (RuntimeMessages.Count >= 500) { Complete = false; MessagesComplete = false; break; }
                RuntimeMessages.Add(message.Copy()); Messages.Add(message.Message);
            }
            Complete &= scope.Complete; MessagesComplete &= scope.MessagesComplete && scope.TargetExists;
            return true;
        }
        internal VerificationSnapshot Copy()
        {
            var s = new VerificationSnapshot { DocumentId = DocumentId, OwnerId = OwnerId, DocumentToken = DocumentToken,
                CapturedUtc = CapturedUtc, OriginalMessage = OriginalMessage, SolutionRevision = SolutionRevision,
                TargetExists = TargetExists, Complete = Complete, MessagesComplete = MessagesComplete, Ready = Ready, InvestigationTargetMissing = InvestigationTargetMissing };
            s.FocusInputId = FocusInputId; s.OriginalSeverity = OriginalSeverity;
            s.RuntimeMessages.AddRange(RuntimeMessages.Select(m => m.Copy()));
            s.Selection.AddRange(Selection); s.Messages.AddRange(Messages); s.Inputs.AddRange(Inputs.Select(i => i.Copy())); return s;
        }
    }
    internal sealed class VerificationResult
    {
        internal bool Incomplete, SelectionChanged, MessagePresent, TargetMissing;
        internal bool ComparisonMade, OriginalAbsentConfirmed;
        internal bool InvestigationTargetMissing;
        internal VerificationUnavailableReason UnavailableReason;
        internal readonly List<string> Changes = new List<string>();
        internal readonly List<RuntimeObservation> NewMessages = new List<RuntimeObservation>(), RemainingMessages = new List<RuntimeObservation>();
        internal string Summary => Changes.FirstOrDefault(s => s.StartsWith("The original", StringComparison.Ordinal) || s.StartsWith("Verification incomplete", StringComparison.Ordinal) ||
            s.StartsWith("Active document", StringComparison.Ordinal) || s.Contains("no longer exists") || s.Contains("stale")) ?? Changes.FirstOrDefault() ?? "Not checked.";
        internal string Compact => (Incomplete ? "Partial comparison. " : "") + Summary + (NewMessages.Count > 0 ? " New message: " + GHErrorAssistant.Report.Preview(NewMessages[0].ToString(), 100) + "." : "") +
            (RemainingMessages.Count > 0 ? " Remaining messages: " + RemainingMessages.Count + "." : "") + " Intended output unverified.";
        internal string Text => string.Join("\n", Changes);
    }
    internal enum VerificationUnavailableReason { Unknown, DifferentDocument, ReportingObjectChanged, OriginalMissing, NotReady, BaselineNotReady, Stale, NoCompletedSolution }
    internal static class RepairVerification
    {
        internal static VerificationResult Compare(VerificationSnapshot baseline, VerificationSnapshot current, DateTime now)
        {
            var r = new VerificationResult();
            if (baseline == null || current == null) return Stop(r, "Verification incomplete: capture a baseline and open its document first.");
            if (current.DocumentId != baseline.DocumentId || current.DocumentToken != baseline.DocumentToken)
                return Stop(r, "Active document changed. Return to the baseline's original document; no comparison was made.", VerificationUnavailableReason.DifferentDocument);
            if (current.OwnerId != baseline.OwnerId) return Stop(r, "The reporting object changed. No comparison was made.", VerificationUnavailableReason.ReportingObjectChanged);
            if (!current.TargetExists) { r.TargetMissing = true; return Stop(r, "The original target no longer exists. Deletion is not a verified repair.", VerificationUnavailableReason.OriginalMissing); }
            r.InvestigationTargetMissing = current.InvestigationTargetMissing;
            r.SelectionChanged = !new HashSet<Guid>(baseline.Selection).SetEquals(current.Selection);
            if (r.SelectionChanged) r.Changes.Add("Selection changed. These results refer to the original baseline object, not the newly selected object.");
            if (!current.Ready) return Stop(r, "Verification incomplete: the canvas is unavailable or a solution is still running.", VerificationUnavailableReason.NotReady);
            if (!baseline.Ready) return Stop(r, "Verification incomplete: the baseline was captured before the document was ready. Capture a new baseline.", VerificationUnavailableReason.BaselineNotReady);
            if (now - baseline.CapturedUtc > TimeSpan.FromMinutes(30) || current.CapturedUtc < baseline.CapturedUtc || current.CapturedUtc > now.AddSeconds(5))
                return Stop(r, "Baseline or current data is stale. Capture a new baseline before continuing.", VerificationUnavailableReason.Stale);
            if (current.SolutionRevision <= baseline.SolutionRevision)
                return Stop(r, "Verification incomplete: no subsequent solution request has completed. Recompute Grasshopper yourself, then click Check Again. The assistant never recomputes it.", VerificationUnavailableReason.NoCompletedSolution);
            r.ComparisonMade = true;
            r.MessagePresent = current.RuntimeMessages.Count > 0 ? current.RuntimeMessages.Any(m => m.OwnerId == baseline.OwnerId &&
                m.ParameterId == baseline.FocusInputId && m.Message == baseline.OriginalMessage) : current.Messages.Contains(baseline.OriginalMessage, StringComparer.Ordinal);
            r.OriginalAbsentConfirmed = !r.MessagePresent && current.MessagesComplete;
            r.Changes.Add(r.MessagePresent ? "The original error or warning is still present." : !current.MessagesComplete ?
                "The original message was not seen in this incomplete inspection. Its disappearance is not established." :
                "The original error or warning is no longer reported on the baseline object. This does not prove the definition is fixed.");
            foreach (var message in current.RuntimeMessages.GroupBy(m => m.Key).Select(g => g.First()))
            {
                var old = baseline.RuntimeMessages.FirstOrDefault(m => m.Identity == message.Identity);
                if (old == null) { r.NewMessages.Add(message.Copy()); r.Changes.Add("New message: " + message + " [new to this bounded comparison; it may previously have been outside inspection scope]"); }
                else
                {
                    r.RemainingMessages.Add(message.Copy()); r.Changes.Add("Remaining message: " + message);
                    if (old.Severity != message.Severity) r.Changes.Add("Severity changed at " + message.Location + ": " + old.Severity + " → " + message.Severity + ".");
                }
            }
            // Preserve compatibility for scalar callers supplying message strings only.
            if (current.RuntimeMessages.Count == 0)
            {
                foreach (var m in current.Messages.Except(baseline.Messages, StringComparer.Ordinal)) r.Changes.Add("New message (severity/location unavailable): " + m);
                foreach (var m in current.Messages.Intersect(baseline.Messages, StringComparer.Ordinal)) r.Changes.Add("Remaining message (severity/location unavailable): " + m);
            }
            foreach (var before in baseline.Inputs)
            {
                var after = current.Inputs.FirstOrDefault(i => i.Key == before.Key);
                if (after == null) { r.Incomplete = true; r.Changes.Add(before.Location + ": target/input unavailable in the current inspection."); continue; }
                if (after.Count != before.Count) r.Changes.Add(after.Location + ": available items changed from " + before.Count + " to " + after.Count + ".");
                if (after.Count == 0) r.Changes.Add(after.Location + ": No items were available to inspect. This is not evidence that missing or invalid items were resolved.");
                else if (after.HasMissing) r.Changes.Add(after.Location + ": missing/null items remain in the inspected sample.");
                else if (before.HasMissing && after.Count > 0) r.Changes.Add(after.Location + ": no missing item was found in the new sample; unsampled items may differ.");
                if (after.MissingReference) r.Changes.Add(after.Location + ": a sampled reference is still missing in the active Rhino document.");
                else if (before.MissingReference) r.Changes.Add(after.Location + ": the previous missing reference was not observed in the current sample; this does not establish that the reference resolved. Inspect the intended geometry.");
                if (before.References != after.References) r.Changes.Add(after.Location + ": sampled reference IDs changed. Verify that the current geometry matches your intended reference; replacement is not proof of correctness.");
                if (after.MissingReferenceCount.HasValue) r.Changes.Add(after.Location + ": saved references unresolved: " + after.MissingReferenceCount + "; found in active Rhino document: " + after.ResolvedReferenceCount + ".");
                if (before.MissingReferenceCount > 0 && after.MissingReferenceCount == 0 && before.References == after.References &&
                    after.ReferencesComplete && after.ResolvedReferenceCount > 0)
                    r.Changes.Add(after.Location + ": the previously missing sampled reference IDs now exist in the active Rhino document. Geometry suitability and intended output remain unverified.");
                if (!before.ReferencesComplete || !after.ReferencesComplete) r.Incomplete = true;
                if (after.HasInvalid) r.Changes.Add(after.Location + ": invalid data remains in the inspected sample.");
                if (before.WarningState != after.WarningState) r.Changes.Add(after.Location + ": recorded warning state changed. Review the current messages.");
                if (before.Tree != after.Tree) r.Changes.Add(after.Location + ": observed tree structure changed; compare it with the component's requirements.");
                if (after.Count > 0 && after.SampleUsable && !after.HasInvalid && !after.HasMissing && !after.MissingReference)
                    r.Changes.Add(after.Location + ": usable data was observed in the inspected sample. Type suitability and unsampled data are not independently verified.");
                if (!after.Complete || !before.Complete) r.Incomplete = true;
            }
            r.Incomplete |= !baseline.Complete || !current.Complete;
            if (r.Incomplete) r.Changes.Add("Verification is incomplete: only bounded samples and inspected connections were compared.");
            r.Changes.Add("Intended model output is not independently confirmed. Inspect the expected geometry/result yourself; a missing message or nonempty input is not sufficient.");
            return r;
        }
        private static VerificationResult Stop(VerificationResult r, string text, VerificationUnavailableReason reason = VerificationUnavailableReason.Unknown) { r.Incomplete = true; r.UnavailableReason = reason; r.Changes.Add(text); return r; }
    }
}



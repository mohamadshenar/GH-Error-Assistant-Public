using System;
using System.Collections.Generic;
using System.Linq;

namespace GHErrorAssistant.Diagnostics
{
    internal enum CheckObservation { Present, MissingOrUnusable, Unsure }
    internal sealed class InvestigationRoute
    {
        internal string Key, Name;
        internal readonly List<GuidedCheck> Checks = new List<GuidedCheck>();
    }
    internal sealed class TroubleshootingSession
    {
        internal readonly Report Report;
        internal Report EvidenceReport;
        internal readonly VerificationSnapshot Baseline;
        internal readonly Guid Id = Guid.NewGuid();
        internal readonly string DocumentName;
        internal string CurrentTargetKey;
        internal VerificationResult Verification;
        internal VerificationSnapshot CurrentSnapshot;
        internal bool CurrentUnavailable;
        internal bool ShowingHistoricalDiagnosis;
        internal InvestigationTarget LastKnownTarget { get; private set; }
        internal string LocateFeedback { get; set; }
        internal bool ObserveDocument(bool sameDocument)
        {
            if (!sameDocument && Verification?.UnavailableReason != VerificationUnavailableReason.DifferentDocument)
            {
                CurrentUnavailable = true; LocateFeedback = null;
                Verification = new VerificationResult { Incomplete = true, UnavailableReason = VerificationUnavailableReason.DifferentDocument };
                return true;
            }
            if (sameDocument && Verification?.UnavailableReason == VerificationUnavailableReason.DifferentDocument)
            {
                CurrentUnavailable = true; LocateFeedback = null;
                Verification = new VerificationResult { Incomplete = true };
                return true;
            }
            return false;
        }
        internal const int MaximumContinuations = 8;
        private readonly HashSet<Guid> continuedOwners = new HashSet<Guid>();
        private readonly HashSet<string> continuationEvidence = new HashSet<string>();
        internal int Continuations => continuedOwners.Count;
        internal int UniqueContinuationEvidence => continuationEvidence.Count;
        // Every renderer/navigation redraw consumes this report; Report is historical only.
        internal Report DisplayReport => EvidenceReport;
        internal GuidedCheck ActiveCheck => CurrentUnavailable || !DisplayReport.HasCurrentFinding ? null : Current(DisplayReport.Tutor.Guidance);
        internal RuntimeObservation NextIssue => CurrentUnavailable || CurrentSnapshot == null ? null :
            CurrentSnapshot.RuntimeMessages.Where(m => m.OwnerId != DisplayReport.OwnerId || m.ParameterId != DisplayReport.FocusInputId || m.Message != DisplayReport.Message)
                .Where(m => m.OwnerId != Report.OwnerId || m.ParameterId != Report.FocusInputId || m.Message != Report.Message)
                .OrderByDescending(m => m.Severity == "Error")
                .ThenByDescending(m => Verification?.NewMessages.Any(n => n.Key == m.Key) == true).FirstOrDefault();
        internal readonly Dictionary<string, CheckObservation> Observations = new Dictionary<string, CheckObservation>();
        internal readonly List<string> HistoricalObservations = new List<string>();
        private void ArchiveObservations()
        {
            foreach (var entry in Observations)
            { if (HistoricalObservations.Count >= 256) HistoricalObservations.RemoveAt(0); HistoricalObservations.Add(entry.Key + ": " + entry.Value); }
            Observations.Clear();
        }
        internal TroubleshootingSession(Report report, VerificationSnapshot baseline, string documentName)
        {
            Report = new Report { Name = report.Name, Message = report.Message, Severity = report.Severity, OwnerId = report.OwnerId,
                FocusInputId = report.FocusInputId, DocumentId = report.DocumentId, Context = report.Context, DiagnosticText = report.DiagnosticText, HasRuntimeMessage = report.HasRuntimeMessage };
            Baseline = baseline.Copy(); DocumentName = documentName;
            EvidenceReport = Report;
            CurrentTargetKey = report.Tutor.Guidance.Checks.Select(c => Key(c.Target)).FirstOrDefault();
            LastKnownTarget = Current(report.Tutor.Guidance)?.Target;
        }
        internal static string Key(InvestigationTarget target) => target.OwnerId + ":" + target.ParameterId;
        internal void RememberTarget(InvestigationTarget target)
        { if (target != null && target.OwnerId != Guid.Empty) LastKnownTarget = target; }
        internal void UpdatePlan(Report updated)
        {
            if (updated == null || updated.DocumentId != Report.DocumentId || updated.OwnerId != Report.OwnerId) return;
            EvidenceReport = updated;
            CurrentTargetKey = updated.Tutor.Guidance.Checks.Select(c => Key(c.Target)).FirstOrDefault();
        }
        internal bool RefreshCurrent(Report live, VerificationSnapshot snapshot)
        {
            if (live == null || snapshot == null || Verification?.ComparisonMade != true ||
                snapshot.DocumentId != Baseline.DocumentId || snapshot.DocumentToken != Baseline.DocumentToken ||
                snapshot.OwnerId != Baseline.OwnerId || live.DocumentId != Report.DocumentId ||
                (live.OwnerId != Report.OwnerId && live.OwnerId != EvidenceReport.OwnerId))
            { CurrentUnavailable = true; return false; }
            // An absent message from a partial read cannot drive old repair instructions either.
            CurrentUnavailable = !snapshot.MessagesComplete && !live.HasRuntimeMessage;
            ArchiveObservations();
            CurrentSnapshot = snapshot.Copy(); EvidenceReport = live;
            continuedOwners.Clear(); continuationEvidence.Clear();
            var checks = live.HasCurrentFinding ? live.Tutor.Guidance.Checks : null;
            if (checks != null && checks.Count > 0)
            {
                var selected = checks.FirstOrDefault(c => Key(c.Target) == CurrentTargetKey) ?? checks[0];
                CurrentTargetKey = Key(selected.Target); LastKnownTarget = selected.Target;
            }
            else CurrentTargetKey = null;
            ShowingHistoricalDiagnosis = false;
            return true;
        }
        internal bool FollowIssue(Report live, RuntimeObservation observed)
        {
            if (live == null || observed == null || CurrentSnapshot == null || CurrentUnavailable ||
                live.DocumentId != Report.DocumentId || live.OwnerId != observed.OwnerId || live.FocusInputId != observed.ParameterId ||
                live.Message != observed.Message || !CurrentSnapshot.RuntimeMessages.Any(m => m.Key == observed.Key)) return false;
            ArchiveObservations(); EvidenceReport = live; CurrentTargetKey = live.Tutor.Guidance.Checks.Select(c => Key(c.Target)).FirstOrDefault(); return true;
        }
        internal InspectionFrontier NextFrontier => CurrentUnavailable || Continuations >= MaximumContinuations ? null :
            DisplayReport.Context?.Graph?.FrontierTargets.FirstOrDefault(f => f.CanContinue && !continuedOwners.Contains(f.OwnerId));
        internal bool FollowFrontier(Report live, InspectionFrontier frontier)
        {
            var graph = DisplayReport.Context?.Graph;
            if (CurrentUnavailable || Continuations >= MaximumContinuations || live == null || frontier == null || !frontier.CanContinue ||
                continuedOwners.Contains(frontier.OwnerId) || live.DocumentId != Report.DocumentId ||
                (frontier.Generation != Guid.Empty && graph?.Generation != frontier.Generation) ||
                (graph != null && (DateTime.UtcNow - graph.CapturedUtc).TotalMinutes > 30) ||
                (live.Context?.Graph?.DocumentId != Guid.Empty && live.Context?.Graph?.DocumentId != null &&
                    (live.Context.Graph.DocumentId != Baseline.DocumentId || live.Context.Graph.DocumentToken != Baseline.DocumentToken)) ||
                live.OwnerId != frontier.OwnerId || DisplayReport.Context?.Graph?.FrontierTargets.Any(f => ReferenceEquals(f, frontier)) != true) return false;
            continuedOwners.Add(frontier.OwnerId);
            if (graph != null) foreach (var fact in graph.Observations)
            { if (continuationEvidence.Count >= 512) break; continuationEvidence.Add(fact.OwnerId + ":" + fact.ParameterId + ":" + fact.Kind + ":" + fact.Message); }
            // Only identities are remembered across allocations. Historical facts never become current repair evidence.
            ArchiveObservations(); EvidenceReport = live; CurrentTargetKey = live.HasCurrentFinding ? live.Tutor.Guidance.Checks.Select(c => Key(c.Target)).FirstOrDefault() : null;
            return true;
        }
        internal string UnavailableExplanation => Verification?.TargetMissing == true ?
            "The original component no longer exists in this document. Select another component to start a new investigation; deleting a component does not verify a repair." :
            Verification?.UnavailableReason == VerificationUnavailableReason.DifferentDocument ?
            "Return to the Grasshopper document where this investigation started, then click Check Again." :
            Verification?.InvestigationTargetMissing == true ?
            "The troubleshooting target no longer exists in this document. Recompute Grasshopper, then click Check Again to inspect what remains. Deleting a component does not prove the problem was fixed." :
            Verification?.UnavailableReason == VerificationUnavailableReason.NoCompletedSolution ?
            "No new Grasshopper solution has completed since this investigation started. Recompute Grasshopper, then click Check Again. Saved observations are historical." :
            Verification?.UnavailableReason == VerificationUnavailableReason.Stale || Verification?.UnavailableReason == VerificationUnavailableReason.BaselineNotReady ?
            "This investigation's baseline is stale or was not ready. Select the problem and start a new investigation." :
            "Current inspection is unavailable. Make sure Grasshopper has finished solving, then click Check Again. Saved observations are historical.";
        internal string ComparisonStatus => Verification == null ? "Baseline saved · intended output unverified" :
            Verification.TargetMissing ? "Original component no longer exists. Start a new investigation.\nIntended output unverified." :
            !Verification.ComparisonMade ? "Comparison unavailable · see Advanced details.\nIntended output unverified." :
            (Verification.MessagePresent ? "Original message remains." : Verification.OriginalAbsentConfirmed ? "Original message is no longer reported." : "Original message status is uncertain.") +
            (NextIssue == null ? "" : "\n" + (NextIssue.Severity == "Error" ? "Error" : "Warning") + " at " + GHErrorAssistant.Report.Preview(NextIssue.Location, 70) + ". Investigate next.") +
            "\n" + (Verification.Incomplete ? "Partial inspection · " : "") + "Intended output unverified.";
        internal bool IsOriginal(Report candidate) => candidate != null && candidate.DocumentId == Report.DocumentId &&
            candidate.OwnerId == Report.OwnerId && candidate.FocusInputId == Report.FocusInputId && candidate.Message == Report.Message && candidate.Severity == Report.Severity;
        internal GuidedCheck Current(BeginnerGuidance guide) => guide.Checks.FirstOrDefault(c => Key(c.Target) == CurrentTargetKey) ?? guide.Checks.FirstOrDefault();
        internal string Header => "Troubleshooting: " + Report.Name + " · " + Report.Severity + "\n" + DocumentName;
        internal string Status => Verification?.Summary ?? "Baseline saved. Make your correction and recompute, then Check Again.";
        internal string Text() => Header + "\nDocument ID: " + Baseline.DocumentId + "\nRuntime identity: " + Baseline.DocumentToken +
            "\nReporting object: " + Baseline.OwnerId + "\nOriginal severity: " + Report.Severity + "\nOriginal message (verbatim):\n" + Report.Message +
            "\nCurrent investigation: " + CurrentTargetKey + "\nRecorded observations:\n" + string.Join("\n", Observations.Select(o => o.Key + ": " + o.Value)) +
            "\nHistorical manual observations (not current evidence):\n" + string.Join("\n", HistoricalObservations) +
            "\n\nHISTORICAL BASELINE — NOT CURRENT DIAGNOSIS\n" + Report.Text() +
            (CurrentUnavailable ? "\n\nCurrent inspection unavailable; do not use saved instructions as current evidence." : "\n\nCURRENT DIAGNOSIS\n" + EvidenceReport.Text()) +
            "\n\nVERIFICATION\n" + (Verification?.Text ?? "Not checked.");
        internal string Record(GuidedCheck check, CheckObservation result)
        { CurrentTargetKey = Key(check.Target); RememberTarget(check.Target); Observations[CurrentTargetKey] = result; return Next(EvidenceReport.Tutor.Guidance, check, result).Item1; }
        internal static Tuple<string, GuidedCheck> Next(BeginnerGuidance guide, GuidedCheck current, CheckObservation result)
        {
            if (result == CheckObservation.Unsure)
                return Tuple.Create("Read this port's description/Help and compare the intended result. The evidence does not justify a different next target yet.", (GuidedCheck)null);
            GuidedCheck next = null;
            if (result == CheckObservation.MissingOrUnusable && current.Source != null)
                next = guide.Checks.FirstOrDefault(c => c.Target.OwnerId == current.Source.OwnerId && Key(c.Target) != Key(current.Target));
            if (result == CheckObservation.Present)
            {
                var edge = current.Target.Issue.Path.FirstOrDefault();
                if (edge != null) next = guide.Checks.FirstOrDefault(c => c.Target.OwnerId == edge.TargetId && Key(c.Target) != Key(current.Target));
            }
            return next != null ? Tuple.Create("Next inspect " + next.Target.Location + ". This follows a verified connection, not a proven cause.", next) :
                Tuple.Create(result == CheckObservation.Present ? "Data presence alone does not prove suitability. Compare the named input's requirements, or make your intended correction and use Check Again. No further specific target is justified by this snapshot." :
                current.Source != null ? "Locate source: " + current.Source.Location + ". No further actionable check there was captured; inspect its inputs and Help. Do not guess the next wire." :
                "No earlier source was verified here. Follow this check's assignment/reference instructions if appropriate, then recompute yourself and use Check Again.", (GuidedCheck)null);
        }
    }
    internal static class InvestigationRoutes
    {
        internal static List<InvestigationRoute> Build(BeginnerGuidance guide, GraphEvidence graph)
        {
            var routes = new List<InvestigationRoute>();
            foreach (var check in guide.Checks)
            {
                var issue = check.Target.Issue;
                VerifiedConnection branch = null;
                if (graph != null)
                {
                    var joins = new HashSet<Guid>(graph.Connections.GroupBy(e => e.TargetId)
                        .Where(g => g.Select(e => e.TargetParameterId).Distinct().Count() > 1).Select(g => g.Key));
                    branch = issue.Path.AsEnumerable().Reverse().FirstOrDefault(e => e.TargetId != graph.RootId && joins.Contains(e.TargetId) && graph.ContainsEdge(e)) ??
                        issue.Path.AsEnumerable().Reverse().FirstOrDefault(e => joins.Contains(e.TargetId) && graph.ContainsEdge(e));
                    if (branch == null && joins.Contains(issue.ComponentId))
                        branch = graph.Connections.FirstOrDefault(e => e.TargetId == issue.ComponentId && e.TargetParameterId == issue.ParameterId);
                }
                string key = branch == null ? "reporting" : branch.SourceId + ":" + branch.TargetParameterId;
                var route = routes.FirstOrDefault(r => r.Key == key);
                if (route == null)
                {
                    route = new InvestigationRoute { Key = key, Name = branch == null ? "Reporting object and connected observations" :
                        "Route via " + branch.SourceComponent + " / " + branch.SourcePort + " → " + branch.TargetComponent + " / " + branch.TargetPort };
                    routes.Add(route);
                }
                route.Checks.Add(check);
            }
            return routes;
        }
    }
}




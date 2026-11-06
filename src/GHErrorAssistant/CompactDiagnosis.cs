using System;
using System.Collections.Generic;
using System.Linq;
using GHErrorAssistant.Diagnostics;
using GHErrorAssistant.Core;

namespace GHErrorAssistant
{
    // A compact view of the existing evidence, not another diagnosis or an SDK reader.
    internal sealed class CompactDiagnosis
    {
        internal string Finding, Location, Action, Expected, State, Why;
        internal static CompactDiagnosis Create(GuidedCheck check, Report report)
        {
            var card = RepairCard.Create(check, report);
            var issue = check.Target.Issue;
            var result = new CompactDiagnosis { Finding = card.Found, Location = check.Target.Location,
                Action = FirstSentence(check.Instruction), Expected = FirstSentence(card.Expected),
                State = report.Context?.Graph?.Partial == true ? "Current · partial check" : "Current finding" };
            if (BeginnerDiagnosis.ReferenceProblem(issue))
            {
                string items = BeginnerDiagnosis.DataName(issue.ParameterType);
                bool checkedIds = issue.Observation.Kind == ObservationKind.MissingReferenceInActiveDocument && issue.Observation.Count != 0;
                string noun = string.Equals(issue.ParameterType, "Curve", StringComparison.OrdinalIgnoreCase) ? "curve" :
                    string.Equals(issue.ParameterType, "Point", StringComparison.OrdinalIgnoreCase) ? "point" : "geometry";
                result.Finding = checkedIds ? issue.Observation.Count > 0 ?
                    "The check couldn't find " + issue.Observation.Count + " saved " + noun + (issue.Observation.Count == 1 ? " reference" : " references") + " in the open Rhino file." :
                    "Grasshopper can't find some " + items + " referenced here in the open Rhino file." :
                    "Grasshopper reports a problem with this geometry reference. Check the intended Rhino object.";
                if (checkedIds && !issue.Observation.Complete) result.Finding += " Other references weren't checked.";
                result.Action = issue.Floating && string.Equals(issue.ParameterType, "Curve", StringComparison.OrdinalIgnoreCase) ?
                    "Open the correct Rhino file. If you need to reassign the curves, right-click Curve and choose Set one Curve or Set Multiple Curves." :
                    "Open the correct Rhino file and check the intended geometry. Use this parameter's assignment menu only if you need to replace its reference.";
                result.Why = !checkedIds ? "This warning needs a reference check. It doesn't tell us why other components failed." :
                    issue.ComponentId == report.OwnerId ? "This reference needs attention. Finding the intended object may clear this warning." :
                    (issue.Observation.Count == 1 ? "This missing " + noun + " needs attention. It may be related to the " :
                        "These missing " + items + " need attention. They may be related to the ") + report.Name + " " +
                    (report.Severity == "Error" ? "error" : "warning") + ", but that hasn't been confirmed.";
                result.Expected = "After you recompute, this parameter should contain the intended " + items + " and no missing-reference warning.";
            }
            else if (BeginnerDiagnosis.Unassigned(issue))
            {
                result.Finding = issue.Component + " has no assigned " + BeginnerDiagnosis.DataName(issue.ParameterType) + " and no incoming wire.";
                result.Action = "Supply the intended " + BeginnerDiagnosis.DataName(issue.ParameterType) + " or connect a source that provides them. Check the parameter's Help if needed.";
                result.Expected = "After you recompute, this parameter should contain the data you intended and its missing-data warning should clear.";
            }
            else if (issue.Observation?.Kind == ObservationKind.NullSample)
            {
                result.Finding = "Some items in the checked sample are null (missing values). Other items may differ.";
                result.Action = "Check where the sampled null items first appear, then compare them with this input's Help.";
            }
            else if (issue.Observation?.Kind == ObservationKind.ConversionWarning)
            {
                result.Finding = "Grasshopper couldn't use some data as the required type for this input.";
                var conversion = TutorEngine.ExplainBase(issue.Observation.Message, new TutorContext { Component = issue.Component });
                if (conversion.Kind == "type-mismatch") result.Finding = conversion.WhatsWrong;
                result.Action = "Compare the source data with the original conversion message and this input's Help.";
            }
            else if (GuidedTroubleshooting.TreeProblem(issue.Observation?.Message))
            {
                result.Finding = "Grasshopper reports a problem with the input's data groups.";
                result.Action = "Compare branch paths (group labels) and item counts with the input's Help. Don't Flatten or Graft without checking what's needed.";
            }
            if (string.IsNullOrEmpty(result.Why)) result.Why = issue.ComponentId == report.OwnerId ?
                "This was found here. Check the input data and the result you expected." :
                "A wire path connects this to " + report.Name + ". A connection alone doesn't show what caused its error.";
            return result;
        }
        private static string FirstSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Inspect this location and its input Help.";
            int end = text.IndexOf(". ", StringComparison.Ordinal);
            return end < 0 ? text : text.Substring(0, end + 1);
        }
    }

    internal static class ProblemSelection
    {
        internal static bool Same(Report a, Report b) => a != null && b != null && a.DocumentId == b.DocumentId &&
            a.OwnerId == b.OwnerId && a.FocusInputId == b.FocusInputId && a.Message == b.Message && a.Severity == b.Severity;
        internal static List<Report> Distinct(IEnumerable<Report> reports) => reports
            .GroupBy(r => new { r.OwnerId, r.Severity, r.Message })
            .Select(g => g.OrderByDescending(r => r.FocusInputId != Guid.Empty).First()).ToList();
        internal static string Label(Report report)
        {
            var check = report.Context == null ? null : report.Tutor.Guidance.Checks.FirstOrDefault();
            string hint = check != null && check.Target.OwnerId != report.OwnerId &&
                BeginnerDiagnosis.ReferenceProblem(check.Target.Issue) ? " · connected reference issue" : "";
            return report.Severity + " · " + Report.Preview(report.Name, 24) + hint + " — " + Report.Preview(report.Message, 32);
        }
        internal static string Heading(Report selected, Report active, bool historical, bool unavailable, Report baseline = null)
        {
            var displayed = historical ? baseline ?? active : active;
            return displayed == null ? "Selected problem: " + (selected == null ? "none" : selected.Name + " · " + selected.Severity) :
                "Investigating " + displayed.Name + " · " + displayed.Severity + " · " +
                (historical ? "Historical" : unavailable ? "Refresh required" : "Current inspection");
        }
        internal static string Notice(Report selected, Report active) => selected == null ?
            "No problem selected on the canvas. The active investigation is preserved." : Same(selected, active) ?
            "Selected problem and active investigation match." :
            "Selected problem: " + selected.Name + " · " + selected.Severity + ". Investigating: " + active.Name +
            ". Click Investigate to switch; the previous session will end.";
    }

    internal static class EvidenceTrail
    {
        // Exclude unverifiable routes. Direct observations do not need a connecting edge.
        internal static List<GuidedCheck> Candidates(BeginnerGuidance guide, GraphEvidence graph, int limit = 5)
        {
            if (guide == null || graph == null || limit <= 0) return new List<GuidedCheck>();
            return guide.Checks.Where(c => c?.Target?.Issue != null &&
                ((c.Target.Issue.Depth == 0 && c.Target.OwnerId == graph.RootId) || graph.HasPathToRoot(c.Target.Issue)))
                .Take(Math.Min(limit, 5)).ToList();
        }
    }

    internal static class DiagnosisNavigation
    {
        internal static InvestigationTarget ReportingTarget(Report report) => report == null ? null :
            new InvestigationTarget { Issue = new UpstreamIssue { ComponentId = report.OwnerId,
                Component = report.Name, Observation = new DiagnosticObservation() } };
    }

    internal sealed class CompactEvidence
    {
        internal string Summary, Main, Limits;
        internal List<string> Other = new List<string>();
        internal static CompactEvidence Create(Report report, bool historical, bool unavailable)
        {
            var graph = report?.Context?.Graph;
            var checks = EvidenceTrail.Candidates(report?.Tutor?.Guidance, graph);
            var observations = (graph?.Observations ?? new List<DiagnosticObservation>()).GroupBy(i => Label(i.Kind))
                .OrderByDescending(g => g.Key == "Geometry reference observations")
                .ThenByDescending(g => g.Key == "Missing-data warnings").ToList();
            return new CompactEvidence {
                Summary = historical || unavailable ? "These are saved observations, not verified current findings. Recompute Grasshopper, then use Check Again for a new check." :
                    "The check found connected issues worth looking at. A wire connection does not prove which issue caused the error.",
                Main = checks.Count == 0 ? "No specific connected condition was verified in this inspection." :
                    Report.Preview(checks[0].Target.Location, 65) + ": " + CompactDiagnosis.Create(checks[0], report).Finding,
                Other = observations.Take(4).Select(g => g.Key + " · " + g.Select(i => i.OwnerId).Distinct().Count() + " inspected locations").ToList(),
                Limits = graph == null ? "Upstream evidence is unavailable; intended output has not been verified." :
                    graph.Partial ? "This bounded inspection is incomplete. Other branches or items may remain uninspected; intended output is unverified." :
                    "Only the inspected connections and sampled data are covered. Intended output and causality remain unverified."
            };
        }
        private static string Label(ObservationKind kind)
        {
            switch (kind) {
                case ObservationKind.MissingReferenceInActiveDocument: case ObservationKind.ReferenceWarning: return "Geometry reference observations";
                case ObservationKind.NullSample: return "Sampled null items";
                case ObservationKind.EmptyCache: return "Empty cached data";
                case ObservationKind.MissingDataWarning: return "Missing-data warnings";
                case ObservationKind.ConversionWarning: return "Data-type warnings";
                case ObservationKind.RuntimeWarning: return "Runtime warnings";
                case ObservationKind.ReferenceState: return "Saved geometry references";
                case ObservationKind.TreeStructure: return "Data tree structure";
                case ObservationKind.InspectionNote: return "Inspection notes";
                case ObservationKind.InvalidDataSample: return "Sampled invalid data";
                default: return "Other recorded conditions";
            }
        }
    }
}

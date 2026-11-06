using System;
using System.Collections.Generic;
using System.Linq;
using GHErrorAssistant.Core;

namespace GHErrorAssistant.Diagnostics
{
    // Pure synthesis: consumes scalar snapshots, never Grasshopper SDK objects or rendered report text.
    internal static class DiagnosticSynthesis
    {
        internal static DiagnosticModel BuildModel(string original, TutorContext context)
        {
            var c = context ?? new TutorContext();
            var model = new DiagnosticModel { OriginalRuntimeMessage = original ?? "", ComponentName = c.Component, ComponentType = c.ComponentType,
                ComponentId = c.Graph?.RootId ?? c.OwnerId, ErrorCategory = TechnicalException.Category(original), Graph = c.Graph };
            model.VerifiedObservations.Add(DiagnosticObservation.Warning(original, "runtime message").At(c.Component, c.Parameter));
            if (c.Graph != null)
            {
                model.VerifiedObservations.AddRange(c.Graph.Observations);
                model.CandidateUpstreamIssues.AddRange(c.Graph.RankedUpstream());
            }
            model.Uncertainty.Add("The upstream warning is not proven to cause the selected component's error. The root cause remains unknown.");
            model.Uncertainty.Add("Wire paths and component ownership are verified connections; internal input-to-output dependencies are not verified.");
            if (c.Graph?.Partial == true || c.InspectionPartial)
                model.Uncertainty.Add("Inspection is partial. This is a priority among observed issues, not a claim that every branch or earlier warning was inspected.");
            if (c.Graph?.Cycle == true) model.Uncertainty.Add("A cycle was detected; repeated objects were not traversed again.");
            return model;
        }
        internal static TutorExplanation Explain(string original, TutorContext context)
        {
            var c = context ?? new TutorContext();
            var baseline = TutorEngine.ExplainBase(original, c);
            var model = BuildModel(original, c);
            string wrong = baseline.WhatsWrong, why = baseline.Why, kind = baseline.Kind;
            var steps = baseline.Steps.ToList();
            bool technical = model.ErrorCategory != RuntimeCategory.Unknown;
            if (technical && !(model.ErrorCategory == RuntimeCategory.InvalidCast && baseline.Kind == "type-mismatch"))
            {
                wrong = TechnicalException.Plain(model.ErrorCategory, c.Component);
                why = TechnicalException.Meaning(model.ErrorCategory);
                kind = "exception-" + CategoryKey(model.ErrorCategory);
                steps = new List<string> {
                    "Inspect the data supplied to " + c.Component + ", one input at a time.",
                    model.ErrorCategory == RuntimeCategory.CollectionBounds || model.ErrorCategory == RuntimeCategory.IndexOutOfRange ?
                        "Use Panels or Param Viewers to check list sizes, empty branches and requested item positions; the message does not identify the failing collection." :
                        "Check the input requirements in " + c.Component + "'s help and compare them with the actual supplied items.",
                    "Try a small, valid input set with " + c.Component + ".",
                    "Recompute " + c.Component + " and check the original message again."
                };
            }
            string found;
            var issue = model.CandidateUpstreamIssues.FirstOrDefault();
            if (issue != null)
            {
                if (!technical && baseline.Kind == "unknown")
                {
                    wrong = c.Component + " reported a problem, and a connected upstream warning is worth checking.";
                    why = "The selected message is not recognized, but a verified observation on a connected path gives a specific place to start. It does not establish the cause of this error.";
                }
                found = Describe(issue);
                var feeder = issue.Feeders.FirstOrDefault(f => model.Graph.ContainsEdge(f) && f.TargetId == issue.ComponentId &&
                    (issue.ParameterId == Guid.Empty || f.TargetParameterId == issue.ParameterId));
                if (feeder != null) found += "\n" + feeder.SourceComponent + " / " + feeder.SourcePort + " is wired to " + issue.Location + ".";
                found += "\n\nThis issue is on a connected path to " + c.Component + ", but it is not proven to cause " + c.Component + "'s error.";
                model.RecommendedFirstAction = DependencyAnalysis.FirstAction(issue);
                steps = new List<string> { model.RecommendedFirstAction };
                if (feeder != null)
                    steps.Add("Check the output of " + feeder.SourceComponent + " / " + feeder.SourcePort + " and verify the data arriving at " + issue.Location + ".");
                else steps.Add("Check the inputs and output of " + issue.Component + " before changing downstream components.");
                steps.Add(issue.Observation.Kind == ObservationKind.MissingReferenceInActiveDocument ?
                    "Verify that reference in its intended Rhino document; absence from the active document does not prove deletion." :
                    issue.Observation.Runtime ? "Address that recorded warning and check that " + issue.Component + " produces the intended output." :
                    "Inspect the sampled data issue and correct it only if it is unintended.");
                steps.Add("Recompute " + c.Component + " and check whether the original exception or warning remains.");
            }
            else
            {
                found = technical ? "Grasshopper recorded this exception on " + c.Component + ". " + why : baseline.Why;
                if (c.Graph != null)
                {
                    found += "\nNo actionable upstream warning was found in the inspected paths.";
                    if (c.Graph.Observations.Any(o => o.Kind == ObservationKind.EmptyCache))
                        found += " Some connected outputs have no cached items; emptiness alone is not a confirmed failure.";
                }
                model.RecommendedFirstAction = steps.FirstOrDefault() ?? "Inspect the original runtime message.";
            }
            if (c.Graph?.Partial == true || c.InspectionPartial)
                found += "\nInspection is partial; uninspected branches are unknown.";
            if (c.Graph?.Cycle == true) found += "\nA cycle was detected and inspected without revisiting objects.";
            string next = "If the error remains after checking the observed issues, isolate " + c.Component +
                " in a small definition with known-valid inputs and report the exact original message to that component's author.";
            model.Guidance = BeginnerDiagnosis.Create(model, c, baseline);
            model.CandidateUpstreamIssues.Clear();
            model.CandidateUpstreamIssues.AddRange(model.Guidance.Targets.Where(t => model.Graph?.HasPathToRoot(t.Issue) == true).Select(t => t.Issue));
            if (model.Guidance.Targets.Count > 0 && model.Guidance.Checks.Count > 0)
            {
                steps = model.Guidance.Steps.ToList();
                var firstCheck = model.Guidance.Checks[0];
                found = Describe(firstCheck.Target.Issue) + (firstCheck.Source == null ? "" : "\nConnected from " + firstCheck.Source.Location + ".") +
                    "\nThis observation is not proven to cause the selected error. " + firstCheck.Reason +
                    (c.Graph?.Partial == true || c.InspectionPartial ? " Inspection is partial." : "") +
                    (c.Graph?.Cycle == true ? " A cycle was detected." : "");
            }
            return new TutorExplanation(kind, wrong, why, baseline.Where,
                baseline.Evidence + "\n" + string.Join("\n", model.Uncertainty), steps, found, next, model);
        }
        private static string Describe(UpstreamIssue issue)
        {
            switch (issue.Observation.Kind)
            {
                case ObservationKind.MissingDataWarning:
                    string type = string.IsNullOrWhiteSpace(issue.ParameterType) ? "input" : issue.ParameterType.ToLowerInvariant();
                    return issue.Location + " reports that it cannot collect its " + type + " data.";
                case ObservationKind.ReferenceWarning: return issue.Location + " reports a reference warning that is worth checking.";
                case ObservationKind.ConversionWarning: return issue.Location + " reports a data conversion warning.";
                case ObservationKind.MissingReferenceInActiveDocument: return "A sampled geometry reference at " + issue.Location + " was not found in the active Rhino document; deletion is not confirmed.";
                case ObservationKind.NullSample: return "The inspected data sample at " + issue.Location + " contains a missing (null) item.";
                case ObservationKind.InvalidDataSample: return "An inspected data item at " + issue.Location + " reports that it is invalid; its cause is unknown.";
                default: return issue.Location + " also has a recorded runtime warning or error worth checking.";
            }
        }
        private static string CategoryKey(RuntimeCategory category)
        {
            switch (category)
            {
                case RuntimeCategory.CollectionBounds: return "collection-bounds";
                case RuntimeCategory.IndexOutOfRange: return "index-out-of-range";
                case RuntimeCategory.ArgumentOutOfRange: return "argument-out-of-range";
                case RuntimeCategory.NullReference: return "null-reference";
                case RuntimeCategory.InvalidCast: return "invalid-cast";
                case RuntimeCategory.InvalidOperation: return "invalid-operation";
                default: return "unknown";
            }
        }
    }
}

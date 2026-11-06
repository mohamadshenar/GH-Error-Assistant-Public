using System;
using System.Collections.Generic;
using System.Linq;
using GHErrorAssistant.Core;

namespace GHErrorAssistant.Diagnostics
{
    internal sealed class InvestigationTarget
    {
        internal UpstreamIssue Issue;
        internal readonly List<UpstreamIssue> Evidence = new List<UpstreamIssue>();
        internal Guid OwnerId => Issue.ComponentId;
        internal Guid ParameterId => Issue.ParameterId;
        internal string Location => Issue.Location;
        internal string Summary;
        internal int Score;
        internal string RankingReason;
        internal string Confidence => Issue.Observation?.Kind == ObservationKind.MissingReferenceInActiveDocument ? "Reference IDs checked in active Rhino document; cause unverified." :
            Issue.Observation?.Runtime == true ? "Runtime report; cause unverified." : "Sampled observation; cause unverified.";
        internal string Actionability => BeginnerDiagnosis.ReferenceProblem(Issue) || BeginnerDiagnosis.Unassigned(Issue) ? "Specific assignment/reference check" : "Further inspection needed";
    }
    internal sealed class BeginnerGuidance
    {
        internal string Wrong, StartHere, Why, Next, GroupedEvidence;
        internal readonly List<GuidedCheck> Checks = new List<GuidedCheck>();
        internal readonly List<string> Steps = new List<string>();
        internal readonly List<InvestigationTarget> Targets = new List<InvestigationTarget>();
        internal readonly List<InvestigationTarget> LocateTargets = new List<InvestigationTarget>();
    }
    // Scalar evidence only. Grouping means related places to inspect, never an established cause or the same data item.
    internal static class BeginnerDiagnosis
    {
        private static bool Actionable(UpstreamIssue i) => i.Observation.Kind != ObservationKind.Other &&
            i.Observation.Kind != ObservationKind.InspectionNote && i.Observation.Kind != ObservationKind.EmptyCache &&
            i.Observation.Kind != ObservationKind.TreeStructure && i.Observation.Kind != ObservationKind.ReferenceState;
        private static bool Direct(UpstreamIssue i, DiagnosticModel m) => i.ComponentId == m.ComponentId && i.Depth == 0 &&
            (i.ParameterId != Guid.Empty || !string.IsNullOrWhiteSpace(i.Parameter)) && i.Direction != "output";
        private static bool Related(UpstreamIssue a, UpstreamIssue b) => a.ComponentId == b.ComponentId ||
            a.Path.Any(e => e.SourceId == b.ComponentId || e.TargetId == b.ComponentId) ||
            b.Path.Any(e => e.SourceId == a.ComponentId || e.TargetId == a.ComponentId);
        private static bool MissingSample(UpstreamIssue i) => i.Observation.Kind == ObservationKind.NullSample;
        private static bool MissingWarning(UpstreamIssue i) => i.Observation.Kind == ObservationKind.MissingDataWarning;
        internal static bool Unassigned(UpstreamIssue i) => i.Direction != "output" && i.Floating && i.AssignmentKnown && !i.HasAssignedData &&
            i.ItemCount == 0 && i.SourcesKnown && i.Feeders.Count == 0;
        internal static bool ReferenceProblem(UpstreamIssue i) => Geometry(i.ParameterType) &&
            (i.Observation?.Kind == ObservationKind.MissingReferenceInActiveDocument || i.Observation?.Kind == ObservationKind.ReferenceWarning);
        private static int Score(UpstreamIssue i, DiagnosticModel m) => (ReferenceProblem(i) ? (i.Observation.Kind == ObservationKind.MissingReferenceInActiveDocument ? 3500 : 2500) : 0) +
            (Unassigned(i) ? 2000 : 0) + (Direct(i, m) ? 200 : 0) + i.Priority + (i.SourcesKnown ? 2 : 0) +
            (string.IsNullOrEmpty(i.ParameterType) ? 0 : 1) + GraphIntelligence.Adjustment(i, m.Graph);
        internal static List<InvestigationTarget> Rank(DiagnosticModel model, TutorContext c)
        {
            var g = model.Graph;
            var candidates = g == null ? new List<UpstreamIssue>() : g.Issues.Where(i => Actionable(i) &&
                (Direct(i, model) || g.HasPathToRoot(i))).ToList();
            // Context flags describe the inspected parameter only; source samples must keep their own provenance.
            if (!string.Equals(c.Direction, "output", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.Parameter))
            {
                Action<ObservationKind, string> add = (kind, message) => {
                    if (candidates.Any(i => i.ComponentId == model.ComponentId && i.ParameterId == c.ParameterId && i.Observation.Kind == kind)) return;
                    var i = new UpstreamIssue { ComponentId = model.ComponentId, ParameterId = c.ParameterId, Component = c.Component,
                        Parameter = c.Parameter, ParameterType = c.ParameterType, ItemCount = c.ItemCount, SourcesKnown = c.SourcesKnown,
                        Floating = c.Floating, AssignmentKnown = c.AssignmentKnown, HasAssignedData = c.HasAssignedData,
                        Observation = new DiagnosticObservation { Kind = kind, Message = message }, Depth = 0,
                        Optional = c.Optional, Description = c.Description, TreeSummary = c.TreeSummary };
                    if (g != null) i.Feeders.AddRange(g.Connections.Where(e => e.TargetId == model.ComponentId && e.TargetParameterId == c.ParameterId));
                    if (kind == ObservationKind.MissingReferenceInActiveDocument) i.Observation.Count = c.MissingReferenceCount;
                    candidates.Add(i);
                };
                if (c.HasNullItem) add(ObservationKind.NullSample, "The selected parameter's inspected sample contains a missing item.");
                if (c.HasInvalidItem && !c.ReferenceNotLoaded) add(ObservationKind.InvalidDataSample, "The selected parameter's inspected sample reports invalid data.");
                if (c.MissingReferenceInActiveDocument) add(ObservationKind.MissingReferenceInActiveDocument, "A reference was not found in the active Rhino document.");
            }
            // Identical owner-level echoes of parameter warnings stay in the trace, not in a second target.
            candidates.RemoveAll(i => i.ParameterId == Guid.Empty && candidates.Any(p => p.ComponentId == i.ComponentId &&
                p.ParameterId != Guid.Empty && p.Observation.Runtime && i.Observation.Runtime &&
                p.Observation.Message.Trim() == i.Observation.Message.Trim()));
            var grouped = candidates.GroupBy(i => i.ComponentId + ":" + i.ParameterId)
                .Select(items => { var t = new InvestigationTarget { Issue = items.OrderByDescending(i => Score(i, model)).First() };
                    t.Evidence.AddRange(items.GroupBy(i => i.Observation.Kind + ":" + i.Observation.Message).Select(x => x.First())); return t; })
                .OrderByDescending(t => Score(t.Issue, model)).ThenByDescending(t => t.Issue.Depth)
                .ThenBy(t => t.Location, StringComparer.Ordinal).ThenBy(t => t.OwnerId).ThenBy(t => t.ParameterId).ToList();
            var result = new List<InvestigationTarget>();
            foreach (var t in grouped)
            {
                // Distinct owner/parameter identities remain actionable even along one connected path.
                t.Summary = Describe(t.Issue);
                t.Score = Score(t.Issue, model); t.RankingReason = GraphIntelligence.Rationale(t.Issue, g);
                result.Add(t);
            }
            return result;
        }
        internal static BeginnerGuidance Create(DiagnosticModel model, TutorContext c, TutorExplanation baseline)
        {
            var v = new BeginnerGuidance { Wrong = SimpleError(model, c, baseline),
                Next = "Try " + c.Component + " in a small copy of the definition with data you know works, to see whether it still fails." };
            var ranked = Rank(model, c);
            v.Targets.AddRange(ranked);
            v.GroupedEvidence = string.Join("\n\n", ranked.Select((t, n) => "Investigation group " + (n + 1) + ": " + t.Location + "\n" +
                string.Join("\n", t.Evidence.Select(e => e.Location + " [" + (e.Observation.Runtime ? "recorded " + e.Observation.Severity : "sampled " + e.Observation.Kind) +
                "]: " + e.Observation.Message))));
            v.LocateTargets.AddRange(v.Targets);
            if (v.LocateTargets.Count == 0 && model.ComponentId != Guid.Empty)
                v.LocateTargets.Add(new InvestigationTarget { Issue = new UpstreamIssue { ComponentId = model.ComponentId, Component = c.Component,
                    ParameterId = c.ParameterId, Parameter = c.Parameter, ParameterType = c.ParameterType, ItemCount = c.ItemCount,
                    Floating = c.Floating, AssignmentKnown = c.AssignmentKnown, HasAssignedData = c.HasAssignedData, SourcesKnown = c.SourcesKnown,
                    Description = c.Description, TreeSummary = c.TreeSummary, Optional = c.Optional, Direction = c.Direction } });
            model.RecommendedFirstAction = v.Targets.Count == 0 ? "Inspect " + c.Location + "." : "Inspect " + v.Targets[0].Location + ".";
            if (v.Targets.Count > 0)
            {
                var first = v.Targets[0]; var issue = first.Issue;
                v.StartHere = string.Join("\n\n", v.Targets.Select((t, n) => (n + 1) + ". " + t.Location + " — " + t.Summary +
                    (t.Evidence.Select(e => e.Location).Distinct().Count() > 1 ? " Related observations are grouped in Technical Details." : "")));
                v.Why = Direct(issue, model) ? "This is data arriving at the selected component. A missing or unusable item is worth checking first; it does not prove why the component failed." :
                    "A wire path connects this area to " + c.Component + ". Its warning may matter, but a connection does not prove that it caused this error.";
                v.Steps.Add("Go to " + first.Location + ". " + Inspect(issue));
                var feeder = VerifiedFeeder(issue, model.Graph);
                if (feeder != null) v.Steps.Add("Follow the incoming wire to " + feeder.SourceComponent + " / " + feeder.SourcePort + ". Add a Panel there to see what it is sending; check its warning too.");
                else v.Steps.Add(Provide(issue));
                if (v.Targets.Count > 1)
                {
                    var second = v.Targets[1]; var source = VerifiedFeeder(second.Issue, model.Graph);
                    v.Steps.Add("Next check " + second.Location + ". " + (source == null ? Inspect(second.Issue) :
                        "Inspect " + source.SourceComponent + " / " + source.SourcePort + " on its incoming wire and check that usable " + DataName(second.Issue.ParameterType) + " reaches this input."));
                }
                else if (feeder != null) v.Steps.Add(Provide(issue));
                else v.Steps.Add("Use a Panel to check the result before passing it on. A Panel shows the items a wire carries.");
                v.Steps.Add("After correcting any unintended data issue, recompute " + c.Component + " and check whether its error remains.");
            }
            else
            {
                v.StartHere = c.Location + ". " + (baseline.Kind == "unassigned-parameter" ? "This data holder has no assigned values and no incoming wire." :
                    baseline.Kind == "missing-input" ? "No usable " + DataName(c.ParameterType) + " is available at this input." : "Check the named input and the data supplied to it.");
                v.Why = "Each input needs data that the component can use. Check the actual items and the component's help before changing the definition.";
                if (baseline.Kind == "missing-input" || baseline.Kind == "unassigned-parameter")
                {
                    v.Steps.Add("Go to " + c.Location + ". " + TypeMeaning(c.ParameterType));
                    if (c.Sources.Count > 0)
                    {
                        var s = c.Sources.FirstOrDefault(x => x.ItemCount == 0) ?? c.Sources[0];
                        v.Steps.Add("Follow the existing wire to " + s.Component + " / " + s.Parameter + ". " + (s.ItemCount == 0 ? "It currently sends no items; inspect its data and warnings before adding another wire." : "Inspect what it sends with a Panel."));
                    }
                    else if (c.SourcesKnown) v.Steps.Add(Provide(new UpstreamIssue { Component = c.Component, Parameter = c.Parameter, ParameterType = c.ParameterType,
                        Floating = c.Floating, AssignmentKnown = c.AssignmentKnown, HasAssignedData = c.HasAssignedData, SourcesKnown = true, ItemCount = c.ItemCount }));
                    else v.Steps.Add("Check whether this input has a wire or assigned values; that part of inspection was incomplete.");
                    v.Steps.Add("Use a Panel to confirm that usable " + DataName(c.ParameterType) + " reaches this input.");
                }
                else if (baseline.Kind == "type-mismatch") v.Steps.AddRange(baseline.Steps.Take(3));
                else if (baseline.Kind == "reference-not-found" || baseline.Kind == "reference-warning")
                {
                    v.Steps.Add("Go to " + (string.IsNullOrEmpty(c.ReferenceLocation) ? c.Location : c.ReferenceLocation) + " and check its assigned geometry.");
                    v.Steps.Add("Open the Rhino document that should contain the object; absence from the active document does not prove deletion.");
                    v.Steps.Add("If the reference no longer points to the intended object, reassign it on the parameter that holds that reference.");
                }
                else
                {
                    v.Steps.Add("Go to " + c.Location + " and read its input descriptions.");
                    v.Steps.Add("Connect a Panel to each incoming source, one at a time, to see the items it supplies.");
                    v.Steps.Add("Check any source with a warning first. Keep intentional empty results unless the component requires data there.");
                }
                v.Steps.Add("Recompute " + c.Component + " after the checks and compare the message.");
            }
            if (model.Graph?.Partial == true || c.InspectionPartial) v.Why += " Only part of the definition was checked; other problems may remain.";
            if (model.Graph?.Cycle == true) v.Why += " Some wires loop back; those objects were not checked repeatedly.";
            GuidedTroubleshooting.Populate(v, model, c, baseline);
            return v;
        }
        internal static VerifiedConnection VerifiedFeeder(UpstreamIssue i, GraphEvidence graph) => graph == null ? null : i.Feeders.FirstOrDefault(f =>
            graph.ContainsEdge(f) && f.TargetId == i.ComponentId && (i.ParameterId == Guid.Empty || f.TargetParameterId == i.ParameterId));
        private static string Describe(UpstreamIssue i)
        {
            switch (i.Observation.Kind)
            {
                case ObservationKind.NullSample: return "I found a missing item in the data I checked. Other items may still be usable.";
                case ObservationKind.InvalidDataSample: return "An item I checked is marked unusable. Other items may still be usable.";
                case ObservationKind.MissingReferenceInActiveDocument: return "A linked Rhino object was not found in the active document. It may be in another document." +
                    (i.Observation.Count.HasValue ? " Missing saved references in this sample: " + i.Observation.Count + "." : "");
                case ObservationKind.MissingDataWarning: return "Grasshopper reports that it cannot collect " + DataName(i.ParameterType) + " here.";
                case ObservationKind.ReferenceWarning: return "Grasshopper reports a problem with a linked object; check the assigned geometry.";
                case ObservationKind.ConversionWarning: return "Grasshopper reports that it cannot use some supplied data in the required form.";
                default: return "Grasshopper recorded a warning here; it may be relevant to the selected error.";
            }
        }
        private static string Inspect(UpstreamIssue i) => i.Observation.Kind == ObservationKind.MissingReferenceInActiveDocument || i.Observation.Kind == ObservationKind.ReferenceWarning ?
            "Check the linked geometry in the intended Rhino document; a missing object here does not prove it was deleted." :
            "Connect a Panel to see the supplied items. " + TypeMeaning(i.ParameterType);
        private static string Provide(UpstreamIssue i)
        {
            if (i.Observation?.Kind == ObservationKind.NullSample) return "Find where the missing item first appears. Correct that source if it is unintended; do not remove items blindly.";
            if (i.Observation?.Kind == ObservationKind.MissingReferenceInActiveDocument || i.Observation?.Kind == ObservationKind.ReferenceWarning)
                return "Verify the intended Rhino document and reassign the geometry only if its reference is no longer usable.";
            if (!i.SourcesKnown) return "Check the incoming wires and assigned values first; their inspection was incomplete.";
            if (i.Feeders.Count > 0) return "Check the existing source before adding another wire. Confirm that it supplies usable " + DataName(i.ParameterType) + ".";
            if (i.Floating && i.AssignmentKnown && !i.HasAssignedData && i.ItemCount == 0 && Geometry(i.ParameterType))
                return "Right-click " + i.Location + " and use its geometry assignment command to select the intended Rhino object, or connect a source supplying " + DataName(i.ParameterType) + ".";
            if (i.AssignmentKnown && i.HasAssignedData) return "Inspect the values already assigned here; saved values are not currently arriving as usable data.";
            return "Supply suitable " + DataName(i.ParameterType) + " using an incoming wire or this parameter's supported value controls. Read its input description first.";
        }
        internal static string DataName(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "point": return "points"; case "curve": return "curves"; case "surface": return "surfaces"; case "mesh": return "meshes";
                case "number": return "numbers"; case "integer": return "whole numbers"; case "brep": return "surface or solid geometry";
                case "vector": return "directions"; case "boolean": return "true/false values";
                default: return "data";
            }
        }
        private static bool Geometry(string type) => new[]{"point", "curve", "surface", "mesh", "brep"}.Contains((type ?? "").ToLowerInvariant());
        internal static string TypeMeaning(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "point": return "Points are locations in space.";
                case "curve": return "Curves are lines or curved paths, not single points.";
                case "surface": return "Surfaces are areas of geometry, not just their outline curves.";
                case "mesh": return "A mesh is a shape made of vertices and connected faces.";
                case "number": return "This input expects numeric values. A Number Slider supplies a single value; it does not repair a partly populated list.";
                case "integer": return "This input expects whole numbers; use a whole-number value.";
                case "brep": return "This input expects surface or solid geometry.";
                default: return "Read this input's description to check what data it accepts.";
            }
        }
        private static string SimpleError(DiagnosticModel m, TutorContext c, TutorExplanation b)
        {
            switch (m.ErrorCategory)
            {
                case RuntimeCategory.CollectionBounds: case RuntimeCategory.IndexOutOfRange: return c.Component + " could not finish: an operation used an invalid item position or range. The message does not say which input is responsible or why that range was requested.";
                case RuntimeCategory.NullReference: return c.Component + " could not finish because something it tried to use was unavailable. This message alone does not tell us which input to change.";
                case RuntimeCategory.InvalidCast: return c.Component + " could not use a value in the form it expected. The message alone does not identify the input or both data types.";
                case RuntimeCategory.ArgumentOutOfRange: return c.Component + " could not use a value that was outside the range an operation accepts. The message does not name a responsible input.";
                case RuntimeCategory.InvalidOperation: return c.Component + " could not complete a step with its current data or settings. The cause is still unknown.";
                default: return b.Kind == "unknown" ? c.Component + " reported a problem. The exact cause is still unknown; start with the checks below." : b.WhatsWrong;
            }
        }
    }
}

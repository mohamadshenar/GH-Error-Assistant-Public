using System;
using System.Collections.Generic;
using System.Linq;
using GHErrorAssistant.Core;

namespace GHErrorAssistant.Diagnostics
{
    internal sealed class GuidedCheck
    {
        internal InvestigationTarget Target, Source;
        internal string Instruction, Expected, Next, Reason, Observed;
    }

    // Concise default presentation; the complete legacy explanation/evidence remains available.
    internal sealed class RepairCard
    {
        internal string Found, Why, Expected;
        internal readonly List<string> Steps = new List<string>();
        internal static RepairCard Create(GuidedCheck check, Report report)
        {
            var issue = check.Target.Issue;
            var card = new RepairCard { Found = check.Target.Summary ?? report.Tutor.WhatsWrong,
                Why = issue.ComponentId == report.OwnerId ? "This condition is on the reporting object. Its role in the error is not independently established." :
                    "A verified wire path connects " + issue.Component + " to " + report.Name + ". This is not a proven cause of its error." };
            card.Expected = check.Expected;
            if (BeginnerDiagnosis.ReferenceProblem(issue))
            {
                string type = string.IsNullOrWhiteSpace(issue.ParameterType) ? "geometry" : issue.ParameterType;
                card.Found = issue.Observation.Kind == ObservationKind.MissingReferenceInActiveDocument ?
                    "The " + type + " parameter contains references that cannot currently be found in the active Rhino document." :
                    "Grasshopper reports a reference problem on the " + type + " parameter. Check its intended Rhino geometry; the cause is unverified.";
                if (issue.Observation.Count.HasValue) card.Found += " Missing references checked: " + issue.Observation.Count + ".";
                if (!issue.Observation.Complete) card.Found += " Only part of the saved references was checked; this is not a total count.";
                card.Steps.Add("In Rhino, open the file containing the intended " + BeginnerDiagnosis.DataName(type) + ". Check that those objects still exist.");
                card.Steps.Add(type.Equals("Curve", StringComparison.OrdinalIgnoreCase) && issue.Floating ?
                    "If deliberate reassignment is needed: right-click Curve → Set Multiple Curves (or Set one Curve). Select the intended curves in Rhino and finish selection. This replaces the old references." :
                    "If deliberate reassignment is needed, right-click this parameter and use its geometry assignment menu. Choose the intended objects; use Help if the command is unfamiliar.");
                card.Expected = "This parameter contains the intended usable " + BeginnerDiagnosis.DataName(type) + "; its missing-reference warning is no longer reported. Other errors may remain.";
            }
            else if (BeginnerDiagnosis.Unassigned(issue))
            {
                card.Found = "The " + issue.ParameterType + " parameter has no assigned " + BeginnerDiagnosis.DataName(issue.ParameterType) + " and no incoming wire.";
                card.Steps.Add(check.Instruction);
                card.Steps.Add("Look for: " + check.Expected);
            }
            else
            {
                card.Steps.Add(check.Instruction);
                card.Steps.Add("Look for: " + check.Expected);
            }
            card.Steps.Add("After your correction, recompute Grasshopper yourself, then click Check Again. Verify the intended result separately.");
            return card;
        }
    }

    // All suggestions consume scalar metadata. No document-edit or solve capability exists here.
    internal static class GuidedTroubleshooting
    {
        internal static bool TreeProblem(string message) => DiagnosticObservation.Has(message,
            "data tree paths", "tree structure", "branch mismatch", "paths do not match", "path mismatch");

        internal static void Populate(BeginnerGuidance guide, DiagnosticModel model, TutorContext c, TutorExplanation basis)
        {
            var targets = guide.LocateTargets;
            foreach (var target in targets)
            {
                var i = target.Issue;
                var check = new GuidedCheck { Target = target };
                var wire = BeginnerDiagnosis.VerifiedFeeder(i, model.Graph);
                if (wire != null)
                    check.Source = new InvestigationTarget { Issue = new UpstreamIssue { ComponentId = wire.SourceId,
                        ParameterId = wire.SourceParameterId, Component = wire.SourceComponent, Parameter = wire.SourcePort, Direction = "output",
                        ParameterType = wire.SourceType, ItemCount = wire.SourceCount } };
                else if (i.ComponentId == model.ComponentId && c.Sources.Count > 0)
                {
                    var s = c.Sources[0];
                    check.Source = new InvestigationTarget { Issue = new UpstreamIssue { ComponentId = s.OwnerId,
                        ParameterId = s.ParameterId, Component = s.Component, Parameter = s.Parameter, Direction = "output", ParameterType = s.DeclaredType, ItemCount = s.ItemCount } };
                }
                string type = string.IsNullOrWhiteSpace(i.ParameterType) ? (i.ComponentId == model.ComponentId ? c.ParameterType : "") : i.ParameterType;
                string meaning = BeginnerDiagnosis.TypeMeaning(type);
                string supplied = check.Source == null ? type : check.Source.Issue.ParameterType;
                string description = string.IsNullOrWhiteSpace(i.Description) ? (i.ComponentId == model.ComponentId ? c.Description : "") : i.Description;
                string tree = string.IsNullOrWhiteSpace(i.TreeSummary) ? (i.ComponentId == model.ComponentId ? c.TreeSummary : "") : i.TreeSummary;
                check.Observed = target.Summary ?? "The exact cause is unknown.";
                if (i.ItemCount.HasValue) check.Observed += " Available items: " + i.ItemCount.Value + ".";
                if (!string.IsNullOrWhiteSpace(description)) check.Observed += " Input description: " + description;
                if (string.IsNullOrWhiteSpace(i.Parameter) && c.InputDescriptions.Count > 0)
                    check.Observed += " Available input descriptions: " + string.Join("; ", c.InputDescriptions.Take(8));
                check.Reason = BeginnerDiagnosis.Unassigned(i) ? "This data holder is confirmed unassigned and unwired. Start with this actionable observation before downstream symptoms." :
                    i.ComponentId == model.ComponentId ? "This observation is on the reporting object." :
                    "A verified wire path connects this observation to the reporting object.";
                check.Reason += " It is a place to investigate, not an established cause.";

                var kind = i.Observation?.Kind;
                bool unassigned = BeginnerDiagnosis.Unassigned(i) || (i.ComponentId == model.ComponentId && basis.Kind == "unassigned-parameter");
                if (unassigned)
                {
                    bool geometry = new[]{ "Point", "Curve", "Surface", "Mesh", "Brep" }.Contains(type, StringComparer.OrdinalIgnoreCase);
                    check.Instruction = geometry ? "Right-click " + i.Location + ". Open its data-assignment menu and choose the command for one or more " +
                        BeginnerDiagnosis.DataName(type) + "; select the intended Rhino object(s) and finish the selection. Use the parameter's Help if that command is unavailable. " + meaning :
                        "Right-click " + i.Location + " and read its Help for supported value controls or a source supplying the required data. The assistant does not know this parameter's assignment commands. " + meaning;
                    check.Expected = "The parameter should list the intended " + BeginnerDiagnosis.DataName(type) + " and stop reporting missing data after you recompute. An item count alone does not prove the geometry is correct.";
                    check.Next = geometry ? "If the intended object cannot be selected, check its geometry type and the active Rhino document. You can instead connect an existing output of the required type; do not replace another wire blindly." :
                        "Use the parameter's documented value controls or an existing compatible source. Do not assume an unfamiliar data holder accepts Rhino geometry.";
                    if (string.Equals(type, "Point", StringComparison.OrdinalIgnoreCase))
                    {
                        check.Instruction = "Choose how you intend to supply points. For coordinates: Right-click the Point parameter → Set one Point. At Rhino's Point location (Type=Coordinate) prompt, enter your intended x,y,z and press Enter. For an existing Rhino object of type Point: use Type → Point, then choose the intended point at the Choose Point Object to Reference prompt. Read the displayed prompt; do not substitute coordinates if you need a live reference.";
                        check.Next = "For several points use Set Multiple Points and follow its displayed prompts. If you need an existing Rhino object, confirm it is a point in the intended Rhino document. If the prompt differs, cancel and use parameter Help; the assistant does not change the definition.";
                    }
                }
                else if (kind == ObservationKind.MissingReferenceInActiveDocument || kind == ObservationKind.ReferenceWarning ||
                    (i.ComponentId == model.ComponentId && (basis.Kind == "reference-not-found" || basis.Kind == "reference-warning")))
                {
                    check.Instruction = "At " + (i.ComponentId == model.ComponentId && !string.IsNullOrEmpty(c.ReferenceLocation) ? c.ReferenceLocation : i.Location) + ", inspect the referenced geometry in the intended Rhino document. Absence here does not prove it was deleted. On the parameter holding the reference, use its assignment menu to select the intended replacement only if the old reference is unusable.";
                    check.Expected = "The intended object is present in the correct Rhino document, the reference resolves, and this parameter no longer reports the reference warning after you recompute.";
                    check.Next = "If it is still missing, open the Rhino file containing the intended geometry and inspect the parameter holding the reference before assigning again. Do not assign a different shape just to remove the warning.";
                }
                else if ((i.ComponentId == model.ComponentId && TreeProblem(model.OriginalRuntimeMessage)) || TreeProblem(i.Observation?.Message))
                {
                    check.Instruction = "Inspect the source tree at " + (check.Source?.Location ?? i.Location) + ". Add a Param Viewer from Grasshopper search and connect the source output to it as an additional branch, retaining existing wires. Compare path labels and item counts with this component's input description/Help.";
                    check.Observed += string.IsNullOrEmpty(tree) ? " Branch structure was not available in this inspection." : " " + tree;
                    check.Expected = "Paths are labels for groups of items, not geometry curves. Compare the required groups and items per group; different paths are not automatically wrong. The assistant does not know the required structure unless the component describes it.";
                    check.Next = "If the structure differs from the documented requirement, trace the component that creates those groups. Do not apply Flatten, Graft or Simplify without knowing which grouping the operation needs.";
                }
                else if (basis.Kind == "type-mismatch" && i.ComponentId == model.ComponentId)
                {
                    check.Instruction = "Read the original conversion message, then inspect " + (check.Source?.Location ?? i.Location) + ". " + meaning + " Compare that input's description with the geometry you intended to supply; use its right-click Help for accepted data.";
                    check.Expected = "The supplied data must match the explicitly named destination type. A Panel can show item descriptions; it cannot prove a custom item is valid.";
                    check.Next = "If the supplied type is different, locate an existing output of the destination type. If you need to construct it, consult that construction component's Help and required inputs first; do not choose an operation solely from the error text.";
                }
                else
                {
                    string source = check.Source?.Location;
                    check.Instruction = source != null ? "Inspect " + source + " before adding another wire. Double-click empty canvas, search for Panel, place it, then drag an additional wire from that source's output grip to the Panel's input. Leave the original connection in place." :
                        "At " + i.Location + ", hover the named input to read its description and inspect the incoming wire. For a standalone parameter, inspect its output. Use right-click Help to check accepted data; if there is a source output, connect an additional Panel there without removing its existing wire.";
                    if (kind == ObservationKind.NullSample)
                    {
                        check.Expected = "Look for a missing/empty item among the other items. Other items may still be usable; a text display does not validate custom objects. " +
                            (i.Optional ? "This input is optional; the missing value may be intentional." : "Check whether this operation requires that item.");
                        check.Next = "Compare the next connected source to find where the missing item first appears. Change the source only if the item is unintended; do not remove items blindly or replace a list with one value.";
                    }
                    else if (kind == ObservationKind.MissingDataWarning || basis.Kind == "missing-input")
                    {
                        check.Expected = "Look for available " + BeginnerDiagnosis.DataName(supplied) + ". Zero items means no data is currently available at that output; a wire alone does not guarantee data. " + BeginnerDiagnosis.TypeMeaning(supplied);
                        check.Next = "If no items are present, use Locate source and inspect that component's incoming data and warning. If items exist, compare their type and the input's Help before changing a connection.";
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(i.Parameter))
                            check.Instruction = "Open " + i.Component + "'s right-click Help and read its input descriptions. This message does not identify a responsible input. If an input has its own warning, choose that input-specific message in the assistant before changing any data.";
                        check.Expected = "Compare the displayed items with the input description/Help. " + meaning + " The assistant cannot validate unfamiliar component-specific data or infer the cause of this message.";
                        check.Next = "In a separate copy, record the component/plugin version, exact message, input types and counts, and the smallest steps that reproduce it. Use Copy error report with the component author's help/support; do not change the original to experiment.";
                    }
                }
                if (kind == ObservationKind.InvalidDataSample)
                    check.Expected += " An inspected item reports invalid data; inspect its creator and geometry before replacing it.";
                if (model.Graph?.Partial == true || c.InspectionPartial) check.Reason += " Only part of the definition was checked.";
                if (i.AssignmentKnown && i.HasAssignedData) check.Observed += " Values are already assigned here; inspect them before replacing anything.";
                check.Instruction = "At " + i.Location + ": " + check.Instruction;
                if (i.AssignmentKnown && i.HasAssignedData) check.Instruction += " Inspect the values already assigned here before replacing them.";
                if (check.Source != null)
                {
                    check.Observed += " Source " + check.Source.Location + " is declared to supply " + BeginnerDiagnosis.DataName(supplied) +
                        (string.IsNullOrWhiteSpace(supplied) ? " (type not captured)" : "") + ". Receiver " + i.Location + " expects " + BeginnerDiagnosis.DataName(type) + ".";
                    check.Expected = "Inspect source " + check.Source.Location + ": " + check.Expected + " Receiver " + i.Location + " needs " + BeginnerDiagnosis.DataName(type) +
                        "; these are separate requirements. A connection does not prove compatible data.";
                }
                guide.Checks.Add(check);
            }
            // Retain legacy fields for report consumers, but use exactly the same guided order everywhere.
            if (guide.Checks.Count > 0)
            {
                var first = guide.Checks[0];
                guide.StartHere = "1. " + first.Target.Location + " — " + first.Observed;
                guide.Next = first.Next;
                guide.Steps.Clear();
                guide.Steps.Add(first.Instruction);
                guide.Steps.Add("Look for: " + first.Expected);
                guide.Steps.Add(first.Next);
                guide.Steps.Add("After your correction, recompute Grasshopper yourself, then click Check Again. Inspect the intended model result separately.");
                model.RecommendedFirstAction = "Inspect " + first.Target.Location + ". " + first.Instruction;
            }
        }
    }
}





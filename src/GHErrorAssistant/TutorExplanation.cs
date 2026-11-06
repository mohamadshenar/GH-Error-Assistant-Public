using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant.Core
{
    public sealed class TutorSource
    {
        public Guid OwnerId, ParameterId;
        public string Component = "", Parameter = "", DeclaredType = "";
        public int ItemCount;
        public bool HasNullItem, HasInvalidItem, MissingReferenceInActiveDocument;
        public readonly List<string> SampleTypes = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }
    public sealed class TutorContext
    {
        public string Description = "", TreeSummary = "";
        public string Component = "Selected object", ComponentType = "", Parameter = "", ParameterType = "", Direction = "";
        public int? ItemCount;
        public int? MissingReferenceCount;
        public Guid OwnerId, ParameterId;
        public bool Floating, AssignmentKnown, HasAssignedData, SourcesKnown, Optional;
        public bool HasNullItem, HasInvalidItem, MissingReferenceInActiveDocument, ReferenceNotLoaded, InspectionPartial;
        public string UpstreamFirstAction = "";
        public string ReferenceLocation = "";
        internal GraphEvidence Graph;
        public readonly List<TutorSource> Sources = new List<TutorSource>();
        public readonly List<string> AffectedInputs = new List<string>();
        public readonly List<string> InputDescriptions = new List<string>();
        public string Location => Floating || string.IsNullOrWhiteSpace(Parameter) ? Component : Component + " / " + Parameter;
    }
    public sealed class TutorExplanation
    {
        public string WhatsWrong { get; }
        public string Why { get; }
        public string Where { get; }
        public string Evidence { get; }
        public IReadOnlyList<string> Steps { get; }
        public string Kind { get; }
        public string WhatIFound { get; }
        public string IfNotResolved { get; }
        internal DiagnosticModel Diagnostic { get; }
        internal BeginnerGuidance Guidance => Diagnostic?.Guidance;
        internal TutorExplanation(string kind, string wrong, string why, string where, string evidence, IEnumerable<string> steps,
            string whatFound = null, string ifNotResolved = null, DiagnosticModel diagnostic = null)
        {
            Kind = kind; WhatsWrong = wrong; Why = why; Where = where; Evidence = evidence;
            Steps = Array.AsReadOnly(steps.Take(5).ToArray());
            WhatIFound = whatFound ?? why;
            IfNotResolved = ifNotResolved ?? "Try a small definition with known-valid inputs and consult the component's help.";
            Diagnostic = diagnostic;
        }
    }
    public static class TutorEngine
    {
        private const string Types = "point|curve|surface|number|integer|brep|mesh|vector|text|boolean|plane|line|box|rectangle|circle";
        private static readonly Regex Conversion = new Regex(@"\b(?:data conversion failed|failed to convert|cannot convert)\s+(?:from\s+)?(?<from>" + Types + @")\s+to\s+(?<to>" + Types + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static bool Contains(string text, params string[] patterns) => patterns.Any(p => text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        private static string FriendlyType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "data";
            switch (type.ToLowerInvariant())
            {
                case "generic data": case "generic": return "data";
                case "brep": return "solid or surface geometry";
                case "integer": return "whole-number";
                case "boolean": return "true/false";
                default: return type.ToLowerInvariant();
            }
        }
        public static TutorExplanation Explain(string original, TutorContext context)
            => DiagnosticSynthesis.Explain(original, context);
        internal static TutorExplanation ExplainBase(string original, TutorContext context)
        {
            var c = context ?? new TutorContext();
            string message = original ?? "", type = FriendlyType(c.ParameterType);
            string where = c.Location + (string.IsNullOrEmpty(c.Direction) ? "" : " (" + c.Direction + ")");
            if (c.Sources.Count > 0)
                where += "\nConnected from: " + string.Join("; ", c.Sources.Take(4).Select(s => s.Component + " / " + s.Parameter +
                    (s.SampleTypes.Count == 0 ? "" : " (sample contains " + string.Join(", ", s.SampleTypes) + ")")));
            string facts = "Grasshopper recorded the original message on " + c.Location + ".";
            if (c.ItemCount.HasValue) facts += " Currently available items: " + c.ItemCount.Value + ".";
            if (c.SourcesKnown) facts += " Connected sources: " + c.Sources.Count + ".";
            if (!string.IsNullOrEmpty(c.ComponentType)) facts += " Component type: " + c.ComponentType + ".";
            if (!string.IsNullOrEmpty(c.ParameterType)) facts += " Parameter type: " + c.ParameterType + ".";
            if (c.InspectionPartial) facts += " Inspection is partial; uninspected data is unknown.";
            Func<string, string, string, string[], TutorExplanation> result = (kind, wrong, why, steps) =>
                new TutorExplanation(kind, wrong, why, where, facts + " Suggestions are not a confirmed root cause.", steps);
            string recheck = "Recompute " + c.Component + " and check whether the message disappears.";
            var conversion = Conversion.Match(message);
            if (conversion.Success)
            {
                string from = FriendlyType(conversion.Groups["from"].Value), to = FriendlyType(conversion.Groups["to"].Value);
                return result("type-mismatch", "Grasshopper could not turn " + from + " data into " + to + " data.",
                    "The runtime message explicitly reports this conversion failure. A wire carries data between objects, but it cannot automatically make every kind of geometry or value into another kind.",
                    new[]{"Look at " + c.Location + " and the data connected to it.", "Use a Panel to inspect the supplied " + from + " items.", "Supply " + to + " data, or use a component that deliberately creates it from the data you have.", recheck});
            }
            if (Contains(message, "division by zero", "divide by zero"))
                return result("division-by-zero", "This calculation is trying to divide by zero.",
                    "The number used as the divisor is zero for at least one calculation, according to Grasshopper's message. Dividing by zero has no finite result.",
                    new[]{"Find the divisor on " + c.Component + " — the value you are dividing by.", "Inspect every item in that input with a Panel; a list can contain zero among other numbers.", "Use a nonzero divisor, or handle zero items separately if they are intentional.", recheck});
            if (c.MissingReferenceInActiveDocument)
                return result("reference-not-found", "A referenced object cannot be found in the active Rhino document.",
                    "An inspected geometry reference points to an object ID that is absent from the active Rhino document. The object may have been deleted or the intended geometry may be in another open document; deletion is not confirmed.",
                    new[]{"Inspect " + (string.IsNullOrEmpty(c.ReferenceLocation) ? c.Location : c.ReferenceLocation) + " and switch to the Rhino document that should contain its geometry.", "Check that the intended Rhino object still exists.", "On this connected branch, find the parameter holding the reference. If it is no longer usable, right-click that parameter and select the intended geometry again.", recheck});
            if (DiagnosticObservation.ReferenceMessage(message))
                return result("reference-warning", "Grasshopper could not use a referenced Rhino object.",
                    "The message concerns a reference, but there is not enough evidence to say the object was deleted. Check the intended Rhino document and the geometry assigned to the parameter.",
                    new[]{"Inspect the reference assigned to " + c.Location + ".", "Find the intended geometry in its Rhino document.", "Reassign the reference only if it no longer points to the intended object.", recheck});
            if (c.HasNullItem || Contains(message, "null input", "null value", "null data"))
                return result("null-data", "The inspected data contains a missing item instead of a usable value.",
                    "A null item means there is a place in the data list with no usable value. " + (c.HasNullItem ? "The inspected data sample contains such an item." : "The original message reports such an item; its source is not yet established."),
                    new[]{"Inspect " + c.Location + " with a Panel or Param Viewer.", "Follow the connected wire back to the object that first produces the missing item.", "Correct that source, or remove missing items only if that matches your intended result.", recheck});
            if (c.HasInvalidItem && !c.ReferenceNotLoaded)
                return result("invalid-data", "Some inspected data is marked as unusable.",
                    "An inspected item reports that it is invalid. This does not establish why it became invalid, or prove that every item is affected.",
                    new[]{"Preview the data at " + c.Location + " one item at a time.", "Inspect the connected source for an earlier warning.", "If it is geometry, check its validity in Rhino before rebuilding or replacing it.", recheck});
            bool collection = Contains(message, "failed to collect data", "failed data collection", "insufficient data", "missing data", "no data", "input parameter");
            if (c.Floating && c.ItemCount == 0 && c.AssignmentKnown && !c.HasAssignedData && c.SourcesKnown && c.Sources.Count == 0)
                return result("unassigned-parameter", "This " + type + " parameter has no assigned data and no incoming wire.",
                    "A floating parameter is a holder for data. No saved values or connected source were found here, so it has nothing to pass to the next component.",
                    new[]{"Right-click " + c.Location + ".", "Assign the intended " + type + " data, or connect a component that supplies it.", "For Rhino geometry, select the intended object in the correct Rhino document.", recheck});
            bool empty = c.ItemCount == 0 && !c.Optional && c.Direction != "output" && !string.IsNullOrEmpty(c.Parameter);
            if (empty && collection)
            {
                var source = c.Sources.FirstOrDefault(s => s.ItemCount == 0 || s.Warnings.Count > 0);
                string why = c.Sources.Count == 0 && c.SourcesKnown ?
                    (c.HasAssignedData ? "Saved data was found, but it is not currently reaching this input as usable items. " +
                        (c.ReferenceNotLoaded ? "Some referenced geometry is not loaded; that alone does not prove it was deleted." : "Inspect the assigned data; an empty result does not prove a reference was deleted.") :
                        "No incoming wire was found. There may also be no usable assigned data; an empty result alone does not prove that a geometry reference was deleted.") :
                    source != null ? "The connected source " + source.Component + " / " + source.Parameter +
                        (source.ItemCount == 0 ? " currently provides no items." : " also reports a warning or error.") + " This may be an earlier problem being passed downstream; it is not proof that the source caused this warning." :
                        "The input currently has no usable items even though data may be connected. Check the source data and any conversion warning; the absence of data alone does not identify a type mismatch.";
                return result("missing-input", c.Floating ? "This " + type + " parameter has no usable data yet." :
                    c.Component + " needs " + type + " data at '" + c.Parameter + "', but none is available there.", why,
                    new[]{string.IsNullOrEmpty(c.UpstreamFirstAction) ? "Inspect " + c.Location + " and its assigned data." : c.UpstreamFirstAction,
                        source == null ? "Assign or connect the intended " + type + " data to " + c.Location + "." : "Check the output of " + source.Component + " / " + source.Parameter + " before changing the receiving input.",
                        "Use a Panel or Param Viewer to check that usable items reach " + c.Location + ".", recheck});
            }
            var warningSource = c.Sources.FirstOrDefault(s => s.Warnings.Count > 0);
            if (warningSource != null && collection)
                return result("upstream-warning", "This input may be affected by a warning earlier in the definition.",
                    warningSource.Component + " / " + warningSource.Parameter + " is connected to this input and also reports a warning or error. That connection is verified; whether it caused this warning is still uncertain.",
                    new[]{string.IsNullOrEmpty(c.UpstreamFirstAction) ? "Inspect the warning on " + warningSource.Component + "." : c.UpstreamFirstAction,
                        "Check the output of " + warningSource.Component + " / " + warningSource.Parameter + ".", "Inspect the data arriving at " + c.Location + ".", recheck});
            if (Contains(message, "failed to convert", "cannot convert", "data conversion failed", "invalid cast"))
                return result("conversion-unknown-types", "Grasshopper could not convert some supplied data for this input.",
                    "The original message reports a conversion problem, but it does not establish both data types. " + (type == "data" ? "Inspect the receiving input's requirements." : "The selected parameter is declared to accept " + type + " data; that alone does not identify the failing item's type."),
                    new[]{"Inspect " + c.Location + " and its expected data type.", "Check the actual items on each connected source with a Panel.", "Supply suitable data or an intentional conversion once you have identified the types.", recheck});
            if (Contains(message, "invalid geometry", "geometry is invalid", "invalid curve", "invalid brep"))
                return result("geometry-warning", "Grasshopper reports geometry it cannot use for this operation.",
                    "The geometry may be damaged or may not meet this operation's requirements. The message alone does not prove which requirement failed.",
                    new[]{"Preview the geometry at " + c.Location + ".", "Check its validity in Rhino.", "Read the component's requirements, such as a closed curve or a planar shape, before changing the geometry.", recheck});
            if (Contains(message, "overflow", "out of range", "logarithm", "square root", "not a number") || Regex.IsMatch(message, @"\bnan\b", RegexOptions.IgnoreCase))
                return result("numeric-range", "A value is outside the range this operation can use.",
                    "The message reports a number or range the operation cannot handle. For example, some calculations cannot use negative values, and a list position must exist in the list. The message alone does not identify the offending item.",
                    new[]{"Inspect the values at " + c.Location + " with a Panel.", "Read the operation's allowed input range.", "If you are choosing a list item, check the list length and the requested position.", recheck});
            if (collection && c.AffectedInputs.Count > 0)
                return result("multiple-inputs", "One or more inputs on " + c.Component + " need attention.",
                    "The inspected inputs with warnings or missing data are: " + string.Join(", ", c.AffectedInputs.Take(4)) + ". This component-level message does not identify a single cause.",
                    new[]{"Choose the warning for a specific input in the message picker.", "Inspect " + string.Join(", ", c.AffectedInputs.Take(4)) + " on " + c.Component + ".", "Follow each affected input's actual connected wires upstream.", recheck});
            return result("unknown", c.Component + " reported a problem that this assistant cannot identify yet.",
                "There is not enough verified evidence to give a specific diagnosis. This can happen with third-party components or unusual messages; the original wording is kept in Technical details.",
                new[]{"Read the original message in Technical details.", "Inspect " + c.Location + " and the component's help page.", "Try a small example using data that the component's documentation says it accepts.", "If the problem remains, report the original message and reproduction steps to the component's author."});
        }
    }
}

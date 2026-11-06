using System;
using System.Collections.Generic;
using System.Linq;

namespace GHErrorAssistant.Core
{
    public sealed class Explanation
    {
        public string Title { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Suggestions { get; }
        public IReadOnlyList<string> PossibleCauses { get; }
        public string OriginalMessage { get; }
        public bool IsGeneric { get; }
        public Explanation(string title, string summary, string[] suggestions, bool isGeneric = false,
            string originalMessage = "", string[] possibleCauses = null)
        {
            Title = title; Summary = summary; OriginalMessage = originalMessage;
            Suggestions = Array.AsReadOnly((string[])suggestions.Clone());
            PossibleCauses = Array.AsReadOnly((string[])(possibleCauses ?? new string[0]).Clone());
            IsGeneric = isGeneric;
        }
    }

    public sealed class ExplanationRule
    {
        public string Title { get; }
        public string Summary { get; }
        private readonly string[] patterns, causes, steps;
        public ExplanationRule(string[] patterns, string title, string summary, string[] causes, string[] steps)
        {
            if (patterns == null || patterns.Length == 0 || patterns.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Supply nonempty message patterns.", nameof(patterns));
            this.patterns = (string[])patterns.Clone(); Title = title; Summary = summary;
            this.causes = (string[])causes.Clone(); this.steps = (string[])steps.Clone();
        }
        internal bool Matches(string message) => patterns.Any(p => message.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        internal Explanation Create(string message) => new Explanation(Title, Summary, steps, false, message, causes);
    }

    public static class ExplanationEngine
    {
        public static TutorExplanation ExplainWithContext(string message, TutorContext context) => TutorEngine.Explain(message, context);
        // Order matters: prefer specific observations over broad terms. Custom rules run first.
        private static readonly ExplanationRule[] Rules = {
            new ExplanationRule(new[]{"failed to collect data", "failed data collection"},
                "Required data could not be collected",
                "The parameter could not retrieve usable data. The exact cause needs inspection of its inputs and references.",
                new[]{"The input may be empty.", "Referenced Rhino objects may have been deleted.", "An upstream component may have failed."},
                new[]{"Check whether data is assigned or connected.", "Inspect upstream components for empty outputs.", "If referencing Rhino geometry, verify that the objects still exist."}),
            new ExplanationRule(new[]{"division by zero", "divide by zero"}, "A calculation is dividing by zero",
                "The message reports a zero divisor; this calculation cannot produce a valid finite result.",
                new[]{"A divisor may be zero for one or more list items."},
                new[]{"Inspect the divisor input, including every list item.", "Use a nonzero divisor or handle zero explicitly."}),
            new ExplanationRule(new[]{"failed to convert", "cannot convert", "data conversion failed", "invalid cast"},
                "The supplied data has an incompatible type", "Grasshopper reports that a conversion could not be completed.",
                new[]{"The input may expect a different data type.", "Some list items may not be convertible."},
                new[]{"Check the expected input type.", "Inspect the upstream output type and individual items.", "Add a suitable conversion step if one is possible."}),
            new ExplanationRule(new[]{"invalid reference", "referenced object", "reference is invalid"},
                "A reference could not be used", "The message concerns a reference. Its target needs inspection.",
                new[]{"A Rhino object may have been deleted or replaced.", "Data may belong to a different Rhino document."},
                new[]{"Check the active Rhino document.", "Locate the referenced object.", "Reassign the reference if the original object is unavailable."}),
            new ExplanationRule(new[]{"invalid geometry", "geometry is invalid", "invalid curve", "invalid brep"},
                "Geometry could not be used", "The message reports invalid geometry for this operation.",
                new[]{"Geometry may be degenerate or damaged.", "The operation may require a closed or planar shape."},
                new[]{"Preview each input separately.", "Check geometry validity in Rhino.", "Check the component's closure, planarity and tolerance requirements."}),
            new ExplanationRule(new[]{"null", "object reference not set"}, "An expected value is unavailable",
                "The message reports a missing object or null value; it does not establish where it originated.",
                new[]{"An upstream operation may have returned null.", "An optional value may be used as required data."},
                new[]{"Inspect the inputs with a Panel or Param Viewer.", "Trace null items upstream to their source.", "Filter or replace null items only if appropriate to the intended result."}),
            new ExplanationRule(new[]{"insufficient data", "no data", "empty", "missing data", "input parameter"},
                "Input data needs inspection", "The message concerns an input or unavailable data. Review the original wording for the exact observation.",
                new[]{"An input may be disconnected.", "A list or tree branch may contain no items."},
                new[]{"Check required inputs and assigned values.", "Inspect list counts and tree paths.", "Check whether upstream components produced output."}),
            new ExplanationRule(new[]{"overflow", "not a number", "nan", "out of range", "logarithm", "square root"},
                "A numeric value may be outside the supported range", "The message concerns a numeric operation or range.",
                new[]{"An index may exceed the list length.", "An operation may receive a value outside its mathematical domain."},
                new[]{"Inspect numeric inputs and list bounds.", "Check the valid domain of the operation.", "Test one input value at a time."}),
            new ExplanationRule(new[]{"upstream", "solution exception"}, "An operation reported a failure",
                "The operation did not complete normally. The message alone does not identify the root cause.",
                new[]{"An upstream component may have failed.", "The component may have encountered an unsupported case."},
                new[]{"Inspect upstream warnings and errors first.", "Try a minimal input example.", "Review the component documentation and version."})
        };
        public static Explanation Explain(string message) => Explain(message, null);
        public static Explanation Explain(string message, IEnumerable<ExplanationRule> customRules)
        {
            string original = message ?? string.Empty;
            foreach (var rule in (customRules ?? Enumerable.Empty<ExplanationRule>()).Concat(Rules))
                if (rule != null && rule.Matches(original)) return rule.Create(original);
            return new Explanation("Grasshopper reported a problem",
                "This message is not yet in the offline explanation library. The cause is unknown; review the original message and inspect the component inputs.",
                new[]{"Review the original Grasshopper message.", "Inspect missing or invalid inputs.", "Check upstream warnings and errors.", "Consult the component documentation."},
                true, original, new[]{"The original message requires further investigation."});
        }
    }
}

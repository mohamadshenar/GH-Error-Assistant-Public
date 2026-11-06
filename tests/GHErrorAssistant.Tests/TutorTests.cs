using System;
using System.Linq;
using GHErrorAssistant.Core;
using Xunit;

public class TutorTests
{
    private static TutorContext Input(string type = "Curve") => new TutorContext {
        Component = "Populate 2D (Pop2D)", ComponentType = "Example.Populate2D", Parameter = "Region", ParameterType = type,
        Direction = "input", ItemCount = 0, SourcesKnown = true
    };
    private static void Complete(TutorExplanation e)
    {
        Assert.False(string.IsNullOrWhiteSpace(e.WhatsWrong)); Assert.False(string.IsNullOrWhiteSpace(e.Why));
        Assert.False(string.IsNullOrWhiteSpace(e.Where)); Assert.InRange(e.Steps.Count, 3, 5);
        Assert.All(e.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.DoesNotContain("Required data could not be collected", e.WhatsWrong);
        Assert.Contains("not a confirmed root cause", e.Evidence);
    }
    [Theory][InlineData("Curve", "curve")][InlineData("Point", "point")]
    public void MissingInputUsesActualTypeAndLocation(string type, string expected)
    {
        var e = ExplanationEngine.ExplainWithContext("Input parameter Region failed to collect data", Input(type));
        Assert.Equal("missing-input", e.Kind); Assert.Contains(expected + " data", e.WhatsWrong);
        Assert.Contains("Region", e.WhatsWrong); Assert.Contains("Pop2D", e.Where); Complete(e);
    }
    [Theory][InlineData("Point")][InlineData("Curve")][InlineData("Number")]
    public void KnownUnassignedFloatingParameterGetsAssignmentInstructions(string type)
    {
        var c = Input(type); c.Floating = true; c.Direction = "floating parameter"; c.Component = type + " parameter";
        c.AssignmentKnown = true; c.HasAssignedData = false;
        var e = TutorEngine.Explain("Failed to collect data", c);
        Assert.Equal("unassigned-parameter", e.Kind); Assert.Contains("no assigned data", e.WhatsWrong);
        Assert.Contains(e.Steps, s => s.Contains("Right-click")); Complete(e);
    }
    [Fact] public void UnknownAssignmentDoesNotBecomeUnassignedDiagnosis()
    {
        var c = Input(); c.Floating = true; c.AssignmentKnown = false;
        Assert.NotEqual("unassigned-parameter", TutorEngine.Explain("Failed to collect data", c).Kind);
    }
    [Fact] public void ExistingDataPreventsUnassignedDiagnosis()
    {
        var c = Input(); c.Floating = true; c.AssignmentKnown = true; c.ItemCount = 3;
        Assert.Equal("unknown", TutorEngine.Explain("Vendor problem", c).Kind);
    }
    [Fact] public void PartialSourceInspectionDoesNotClaimNoWire()
    {
        var c = Input(); c.Floating = true; c.AssignmentKnown = true; c.SourcesKnown = false; c.InspectionPartial = true;
        var e = TutorEngine.Explain("Failed to collect data", c);
        Assert.NotEqual("unassigned-parameter", e.Kind); Assert.DoesNotContain("No incoming wire was found", e.Why);
        Assert.Contains("partial", e.Evidence);
    }
    [Fact] public void MissingReferenceInActiveDocumentDoesNotClaimDeletion()
    {
        var c = Input(); c.MissingReferenceInActiveDocument = true;
        var e = TutorEngine.Explain("Failed to collect data", c);
        Assert.Equal("reference-not-found", e.Kind); Assert.Contains("active Rhino document", e.WhatsWrong);
        Assert.Contains("deletion is not confirmed", e.Why); Assert.Contains(e.Steps, s => s.Contains("document")); Complete(e);
    }
    [Fact] public void InvalidReferenceMessageDoesNotInventObservedMissingObject()
    {
        var e = TutorEngine.Explain("Reference is invalid", Input());
        Assert.Equal("reference-warning", e.Kind); Assert.Contains("not enough evidence", e.Why); Complete(e);
    }
    [Fact] public void UpstreamReferenceInstructionsNameTheInspectedSource()
    {
        var c = Input(); c.MissingReferenceInActiveDocument = true; c.ReferenceLocation = "Curve (Crv) / Curve";
        var e = TutorEngine.Explain("Failed to collect data", c);
        Assert.Contains("Curve (Crv)", e.Steps[0]);
        Assert.Contains("parameter holding the reference", e.Steps[2]);
    }
    [Fact] public void UnloadedReferenceIsNotDeclaredInvalidOrDeleted()
    {
        var c = Input(); c.Floating = true; c.AssignmentKnown = true; c.HasAssignedData = true;
        c.ReferenceNotLoaded = true; c.HasInvalidItem = true;
        var e = TutorEngine.Explain("Failed to collect data", c);
        Assert.Equal("missing-input", e.Kind); Assert.Contains("Saved data was found", e.Why);
        Assert.DoesNotContain("reference-not-found", e.Kind); Assert.DoesNotContain("marked as unusable", e.WhatsWrong);
    }
    [Theory]
    [InlineData("Data conversion failed from Point to Surface", "point", "surface")]
    [InlineData("Data conversion failed from Curve to Number", "curve", "number")]
    [InlineData("Cannot convert Point to Surface", "point", "surface")]
    [InlineData("Failed to convert from Curve to Number", "curve", "number")]
    public void ExplicitMessageVerifiesSpecificMismatch(string message, string from, string to)
    {
        var e = TutorEngine.Explain(message, Input(to));
        Assert.Equal("type-mismatch", e.Kind); Assert.Contains(from + " data", e.WhatsWrong);
        Assert.Contains("into " + to + " data", e.WhatsWrong); Assert.Contains("explicitly reports", e.Why); Complete(e);
    }
    [Fact] public void EmptySurfaceDoesNotInventPointMismatch()
    {
        var c = Input("Surface"); var source = new TutorSource { Component = "Pt", Parameter = "Point", DeclaredType = "Point", ItemCount = 10 };
        source.SampleTypes.Add("Point"); c.Sources.Add(source);
        var e = TutorEngine.Explain("Input parameter failed to collect data", c);
        Assert.Equal("missing-input", e.Kind); Assert.DoesNotContain("point data into surface", e.WhatsWrong);
        Assert.Contains("absence of data alone does not identify a type mismatch", e.Why);
    }
    [Fact] public void DeclaredSourceTypeDoesNotVerifyConversionFailure()
    {
        var c = Input("Surface"); c.Sources.Add(new TutorSource { Component = "Pt", Parameter = "Point", DeclaredType = "Point", ItemCount = 1 });
        var e = TutorEngine.Explain("Cannot convert supplied data", c);
        Assert.Equal("conversion-unknown-types", e.Kind); Assert.DoesNotContain("point data into surface", e.WhatsWrong);
        Assert.Contains("does not establish both data types", e.Why); Complete(e);
    }
    [Fact] public void ObservedNullHasSpecificExplanationAndInstructions()
    {
        var c = Input(); c.HasNullItem = true;
        var e = TutorEngine.Explain("Failed to collect data", c);
        Assert.Equal("null-data", e.Kind); Assert.Contains("missing item", e.WhatsWrong);
        Assert.Contains("sample contains", e.Why); Complete(e);
    }
    [Fact] public void ProgrammingExceptionDoesNotProveNullInput()
    {
        var explanation = TutorEngine.Explain("Object reference not set to an instance of an object", Input());
        Assert.Equal("exception-null-reference", explanation.Kind);
        Assert.Contains("does not prove", explanation.Why);
        Assert.NotEqual("null-data", explanation.Kind);
    }
    [Fact] public void DivisionByZeroExplainsDivisorRatherThanMissingCurve()
    {
        var e = TutorEngine.Explain("Division by zero", Input("Number"));
        Assert.Equal("division-by-zero", e.Kind); Assert.Contains("divide by zero", e.WhatsWrong);
        Assert.Contains(e.Steps, s => s.Contains("divisor")); Complete(e);
    }
    [Fact] public void MissingDataUsesActualSourceAndUpstreamFirstAction()
    {
        var c = Input(); c.Sources.Add(new TutorSource { Component = "Cross Reference (CrossRef)", Parameter = "A", ItemCount = 0 });
        c.UpstreamFirstAction = "Inspect Point (Pt) / Point data first.";
        var e = TutorEngine.Explain("Failed to collect data", c);
        Assert.Contains("CrossRef", e.Why); Assert.Contains("not proof", e.Why);
        Assert.Equal(c.UpstreamFirstAction, e.Steps[0]); Assert.Contains("CrossRef", e.Where); Complete(e);
    }
    [Fact] public void ConnectedWarningIsPossibleDownstreamSymptom()
    {
        var c = Input(); c.ItemCount = 2;
        var source = new TutorSource { Component = "Curve", Parameter = "Curve", ItemCount = 2 }; source.Warnings.Add("Invalid geometry"); c.Sources.Add(source);
        var e = TutorEngine.Explain("Failed to collect data", c);
        Assert.Equal("upstream-warning", e.Kind); Assert.Contains("uncertain", e.Why); Complete(e);
    }
    [Fact] public void ComponentWithMultipleInputWarningsDoesNotChooseOneCause()
    {
        var c = new TutorContext { Component = "Pop2D" }; c.AffectedInputs.Add("Region (Curve)"); c.AffectedInputs.Add("Count (Integer)");
        var e = TutorEngine.Explain("Input parameter failed to collect data", c);
        Assert.Equal("multiple-inputs", e.Kind); Assert.Contains("does not identify a single cause", e.Why); Complete(e);
    }
    [Fact] public void OptionalEmptyInputDoesNotBecomeMissingRequiredData()
    {
        var c = Input(); c.Optional = true;
        Assert.NotEqual("missing-input", TutorEngine.Explain("Input parameter warning", c).Kind);
    }
    [Fact] public void EmptyOutputIsNotReportedAsRequiredInput()
    {
        var c = Input(); c.Direction = "output";
        Assert.NotEqual("missing-input", TutorEngine.Explain("Failed to collect data", c).Kind);
    }
    [Theory][InlineData("Vendor XY-42 failed")][InlineData("")][InlineData(null)][InlineData("Financial component error")]
    public void UnknownThirdPartyProblemsStayUnknown(string message)
    {
        var c = new TutorContext { Component = "VendorComponent" };
        var e = TutorEngine.Explain(message, c);
        Assert.Equal("unknown", e.Kind); Assert.Contains("not enough verified evidence", e.Why);
        Assert.Contains("VendorComponent", e.WhatsWrong); Assert.Contains("Technical details", e.Why); Complete(e);
    }
    [Fact] public void NoContextDoesNotInventTypesOrConnections()
    {
        var e = TutorEngine.Explain("Failed to collect data", null);
        Assert.Equal("unknown", e.Kind); Assert.DoesNotContain("curve", e.WhatsWrong);
    }
    [Fact] public void ContextAndOriginalAreNotMutated()
    {
        const string message = "  Failed to collect data\r\n "; var c = Input();
        TutorEngine.Explain(message, c);
        Assert.Equal("  Failed to collect data\r\n ", message); Assert.Equal(0, c.ItemCount);
        Assert.Empty(c.Sources); Assert.Equal("Curve", c.ParameterType);
    }
}

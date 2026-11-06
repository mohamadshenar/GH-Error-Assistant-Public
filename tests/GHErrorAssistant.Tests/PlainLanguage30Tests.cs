using System;
using System.Text.RegularExpressions;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class PlainLanguage30Tests
{
    static GuidedCheck Check(int? count, bool complete = true, bool floating = true) => new GuidedCheck {
        Target = new InvestigationTarget { Summary="Saved reference problem", Issue=new UpstreamIssue {
            ComponentId=Guid.NewGuid(),Component="Curve",ParameterType="Curve",Floating=floating,
            Observation=new DiagnosticObservation {Kind=ObservationKind.MissingReferenceInActiveDocument,Count=count,Complete=complete} } },
        Instruction="Inspect the input.",Expected="Compare the intended output." };
    static Report Report() { var id=Guid.NewGuid();return new Report {OwnerId=id,Name="Solver",Severity="Error",
        Message="Solution exception:Offset and length were out of bounds for the array",
        Context=new TutorContext {Component="Solver",OwnerId=id,Graph=new GraphEvidence {RootId=id}}}; }

    [Theory] [InlineData(1)] [InlineData(2)]
    public void ReferenceCountsDescribeCheckedReferencesNotUniqueGeometryOrProvenCause(int count)
    {
        var card=CompactDiagnosis.Create(Check(count),Report());
        Assert.Contains(count+" saved curve reference",card.Finding);
        Assert.Contains("open Rhino file",card.Finding);Assert.DoesNotContain("deleted",card.Finding,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Solver error",card.Why);Assert.Contains("hasn't been confirmed",card.Why);
        Assert.Contains("Set one Curve",card.Action);Assert.Contains("recompute",card.Expected);
    }
    [Fact] public void UnknownReferenceCountDoesNotInventANumber()
    {
        var card=CompactDiagnosis.Create(Check(null),Report());
        Assert.Empty(Regex.Matches(card.Finding,@"\d+"));Assert.Contains("some curves",card.Finding);
    }
    [Fact] public void PartialReferenceCheckDoesNotClaimAllReferencesWereChecked()
    {
        var card=CompactDiagnosis.Create(Check(2,false),Report());
        Assert.Contains("2 saved curve references",card.Finding);Assert.Contains("Other references weren't checked",card.Finding);
    }
    [Fact] public void ZeroCountCannotBecomeAConfirmedMissingCurveClaim()
    {
        var card=CompactDiagnosis.Create(Check(0),Report());
        Assert.DoesNotContain("0 saved",card.Finding);Assert.DoesNotContain("This missing",card.Why);
        Assert.Contains("reference check",card.Why);
    }
    [Fact] public void EmbeddedInputDoesNotInventFloatingCurveAssignmentCommands()
    {
        Assert.DoesNotContain("Set one Curve",CompactDiagnosis.Create(Check(1,true,false),Report()).Action);
    }
    [Fact] public void PlainCardDoesNotAlterOriginalExceptionOrTechnicalReport()
    {
        var report=Report();var original=report.Message;var card=CompactDiagnosis.Create(Check(1),report);
        Assert.Equal(original,report.Message);Assert.Contains(original,report.Text());
        Assert.DoesNotContain("Offset and length",card.Finding);
    }
    [Fact] public void TreeWarningExplainsGroupsWithoutPrescribingUnverifiedRestructuring()
    {
        var check=Check(null);check.Target.Issue.ParameterType="Generic data";
        check.Target.Issue.Observation=new DiagnosticObservation {Kind=ObservationKind.RuntimeWarning,Message="Data tree paths do not match"};
        var card=CompactDiagnosis.Create(check,Report());
        Assert.Contains("data groups",card.Finding);Assert.Contains("group labels",card.Action);Assert.Contains("Don't Flatten or Graft",card.Action);
    }
    [Fact] public void ExplicitConversionMessageKeepsItsActualTypes()
    {
        var check=Check(null);check.Target.Issue.Observation=new DiagnosticObservation {
            Kind=ObservationKind.ConversionWarning,Message="Data conversion failed from Text to Number"};
        var card=CompactDiagnosis.Create(check,Report());
        Assert.Contains("text data into number data",card.Finding);
        Assert.DoesNotContain("curve",card.Finding,StringComparison.OrdinalIgnoreCase);
    }
}

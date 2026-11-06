using System;
using System.Linq;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class DiagnosticUx29Tests
{
    [Fact] public void HistoricalHeadingUsesBaselineWarningRatherThanRepairedInformationState()
    {
        var baseline=Report(Guid.NewGuid(),"Number");baseline.Severity="Warning";
        var current=Report(baseline.OwnerId,"Number");current.Severity="Information";
        var heading=ProblemSelection.Heading(null,current,true,false,baseline);
        Assert.Contains("Warning",heading);Assert.Contains("Historical",heading);Assert.DoesNotContain("Information",heading);
        Assert.StartsWith("Selected problem:",ProblemSelection.Heading(baseline,null,false,false));
    }
    static Report Report(Guid owner, string name = "Solver") => new Report { OwnerId = owner, DocumentId = Guid.NewGuid(), Name = name,
        Severity = "Error", Message = "Solution exception:Offset and length were out of bounds for the array" };
    static GuidedCheck Check(ObservationKind kind, Guid owner) => new GuidedCheck { Target = new InvestigationTarget {
        Issue = new UpstreamIssue { ComponentId = owner, Component = "Curve", ParameterType = "Curve", Depth = 1,
            Observation = new DiagnosticObservation { Kind = kind, Count = 1, Message = "Observed condition" } } }, Instruction = "Inspect data.", Expected = "Compare the intended output." };

    [Fact] public void ConnectedReferenceIsPrioritizedWithoutClaimingSolverCause()
    {
        var root = Guid.NewGuid(); var source = Guid.NewGuid(); var graph = new GraphEvidence { RootId = root };
        var wire = new VerifiedConnection { SourceId = source, TargetId = root }; graph.Connections.Add(wire);
        var reference = Check(ObservationKind.MissingReferenceInActiveDocument, source).Target.Issue;reference.Path.Add(wire);graph.Issues.Add(reference);
        graph.Issues.Add(new UpstreamIssue { ComponentId = root, Component = "Solver", ParameterId = Guid.NewGuid(), Parameter = "Goals",
            Observation = DiagnosticObservation.Warning("Offset and length were out of bounds", "Error") });
        var ranked = BeginnerDiagnosis.Rank(new DiagnosticModel { ComponentId = root, Graph = graph }, new TutorContext { Component = "Solver" });
        Assert.Equal(source, ranked.First().OwnerId);
        var card = CompactDiagnosis.Create(new GuidedCheck { Target = ranked.First(), Instruction = "Inspect data.", Expected = "Check output." }, Report(root));
        Assert.Contains("needs attention", card.Why); Assert.Contains("hasn't been confirmed", card.Why);
    }
    [Fact] public void UnconnectedReferenceCannotBecomeSolverCauseOrFirstTarget()
    {
        var root = Guid.NewGuid(); var graph = new GraphEvidence { RootId = root };
        graph.Issues.Add(Check(ObservationKind.MissingReferenceInActiveDocument, Guid.NewGuid()).Target.Issue);
        Assert.Empty(BeginnerDiagnosis.Rank(new DiagnosticModel { ComponentId = root, Graph = graph }, new TutorContext { Component = "Solver" }));
    }
    [Fact] public void RuntimeReferenceWarningDoesNotClaimIdsWereChecked()
    {
        var card = CompactDiagnosis.Create(Check(ObservationKind.ReferenceWarning, Guid.NewGuid()), Report(Guid.NewGuid()));
        Assert.Contains("warning needs a reference check", card.Why); Assert.DoesNotContain("needs attention", card.Why);
    }
    [Fact] public void NullAndConversionCardsDescribeEvidenceWithoutInventingRepair()
    {
        var report = Report(Guid.NewGuid());
        Assert.Contains("checked sample are null", CompactDiagnosis.Create(Check(ObservationKind.NullSample, Guid.NewGuid()), report).Finding);
        var mismatch = CompactDiagnosis.Create(Check(ObservationKind.ConversionWarning, Guid.NewGuid()), report);
        Assert.Contains("required type", mismatch.Finding); Assert.Contains("Compare", mismatch.Action);
    }
    [Fact] public void SelectorPreservesIndependentOwnersButDeduplicatesOwnerInputEcho()
    {
        var a = Report(Guid.NewGuid()); var echo = Report(a.OwnerId);echo.FocusInputId = Guid.NewGuid();echo.DocumentId = a.DocumentId;
        var independent = Report(Guid.NewGuid());
        var result = ProblemSelection.Distinct(new[] { a, echo, independent });
        Assert.Equal(2, result.Count); Assert.Contains(echo,result);Assert.Contains(independent,result);
    }
    [Fact] public void SelectorDistinguishesSelectedProblemFromPinnedInvestigationAndHistory()
    {
        var active = Report(Guid.NewGuid());var selected = Report(Guid.NewGuid(),"Curve");selected.Severity="Warning";
        Assert.Contains("Investigating Solver",ProblemSelection.Heading(selected,active,false,false));
        Assert.Contains("Selected problem: Curve",ProblemSelection.Notice(selected,active));
        Assert.Contains("previous session will end",ProblemSelection.Notice(selected,active));
        Assert.Contains("Historical",ProblemSelection.Heading(selected,active,true,false));
        Assert.Contains("Refresh required",ProblemSelection.Heading(selected,active,false,true));
    }
    [Fact] public void ReportingReferenceWarningCannotInventReferenceRepairForDifferentNullSource()
    {
        var root = Guid.NewGuid();var source = Guid.NewGuid();var graph=new GraphEvidence { RootId=root };
        var target=Check(ObservationKind.NullSample,source).Target;var guide=new BeginnerGuidance();guide.LocateTargets.Add(target);
        GuidedTroubleshooting.Populate(guide,new DiagnosticModel {ComponentId=root,Graph=graph,OriginalRuntimeMessage="Referenced curve could not be found"},
            new TutorContext {Component="Curve"},new TutorExplanation("reference-warning","","","","",Array.Empty<string>()));
        Assert.DoesNotContain("replacement",guide.Checks[0].Instruction,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assignment menu",guide.Checks[0].Instruction,StringComparison.OrdinalIgnoreCase);
    }
}

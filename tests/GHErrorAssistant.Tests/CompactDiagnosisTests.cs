using System;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class CompactDiagnosisTests
{
    static GuidedCheck Check(ObservationKind kind, Guid owner, int depth = 0) => new GuidedCheck {
        Target = new InvestigationTarget { Summary = "Observed condition", Issue = new UpstreamIssue {
            ComponentId = owner, Component = "Curve", Parameter = "Input", ParameterType = "Curve", Depth = depth,
            Observation = new DiagnosticObservation { Kind = kind } } },
        Instruction = "Inspect the input. Extra detailed instructions.", Expected = "Check the output.", Reason = "Observed"
    };
    static Report Report(Guid owner) => new Report { OwnerId = owner, Name = "Curve", Message = "Warning",
        Context = new TutorContext { Component = "Curve", Graph = new GraphEvidence { RootId = owner } } };

    [Fact] public void MissingReferenceDoesNotClaimDeletion()
    {
        var id = Guid.NewGuid(); var card = CompactDiagnosis.Create(Check(ObservationKind.MissingReferenceInActiveDocument, id), Report(id));
        Assert.Contains("open Rhino file", card.Finding);
        Assert.DoesNotContain("deleted", card.Finding, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("correct Rhino file", card.Action);
    }
    [Fact] public void NullSamplesAndConversionWarningsDoNotInventRepairs()
    {
        var id = Guid.NewGuid(); var report = Report(id);
        Assert.Contains("sampled null", CompactDiagnosis.Create(Check(ObservationKind.NullSample, id), report).Action);
        var action = CompactDiagnosis.Create(Check(ObservationKind.ConversionWarning, id), report).Action;
        Assert.Contains("Compare", action); Assert.DoesNotContain("convert to", action, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void EmptyCacheIsNotAutomaticallyDeclaredMissingOrInvalid()
    {
        var id = Guid.NewGuid(); var card = CompactDiagnosis.Create(Check(ObservationKind.EmptyCache, id), Report(id));
        Assert.Equal("Inspect the input.", card.Action);
        Assert.DoesNotContain("missing", card.Finding, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid", card.Finding, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void UnknownFindingPreservesSafeFirstInstructionAndPartialState()
    {
        var id = Guid.NewGuid(); var report = Report(id); report.Context.Graph.Partial = true;
        var card = CompactDiagnosis.Create(Check(ObservationKind.Other, id), report);
        Assert.Equal("Inspect the input.", card.Action); Assert.Contains("partial", card.State);
    }
    [Fact] public void TrailShowsOnlyDirectOrVerifiedConnectedCandidates()
    {
        var root = Guid.NewGuid(); var source = Guid.NewGuid(); var unrelated = Guid.NewGuid();
        var graph = new GraphEvidence { RootId = root };
        var wire = new VerifiedConnection { SourceId = source, TargetId = root, SourcePort = "Out", TargetPort = "In" };
        graph.Connections.Add(wire);
        var direct = Check(ObservationKind.MissingDataWarning, root);
        var upstream = Check(ObservationKind.NullSample, source, 1); upstream.Target.Issue.Path.Add(wire);
        var invented = Check(ObservationKind.NullSample, unrelated, 1);
        invented.Target.Issue.Path.Add(new VerifiedConnection { SourceId = unrelated, TargetId = root });
        var guide = new BeginnerGuidance(); guide.Checks.Add(direct); guide.Checks.Add(upstream); guide.Checks.Add(invented);
        var trail = EvidenceTrail.Candidates(guide, graph);
        Assert.Equal(2, trail.Count); Assert.Contains(direct, trail); Assert.Contains(upstream, trail); Assert.DoesNotContain(invented, trail);
    }
}

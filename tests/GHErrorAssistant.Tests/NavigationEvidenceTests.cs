using System;
using System.Linq;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class NavigationEvidenceTests
{
    [Fact] public void ErrorTargetUsesReportingIdentityAndNotInputIdentity()
    {
        var report = new Report { OwnerId = Guid.NewGuid(), FocusInputId = Guid.NewGuid(), Name = "Solver" };
        var target = DiagnosisNavigation.ReportingTarget(report);
        Assert.Equal(report.OwnerId, target.OwnerId);
        Assert.Equal(Guid.Empty, target.ParameterId);
        Assert.Equal("Solver", target.Location);
        Assert.Null(DiagnosisNavigation.ReportingTarget(null));
    }
    [Fact] public void IdenticalNamesCannotSubstituteReportingIdentity()
    {
        var first = new Report { OwnerId = Guid.NewGuid(), Name = "Solver" };
        var second = new Report { OwnerId = Guid.NewGuid(), Name = "Solver" };
        Assert.NotEqual(DiagnosisNavigation.ReportingTarget(first).OwnerId, DiagnosisNavigation.ReportingTarget(second).OwnerId);
    }
    [Fact] public void FifteenRepeatedObservationsBecomeBoundedGroupsWithoutPaths()
    {
        var graph = new GraphEvidence(); var owner = Guid.NewGuid();
        for (int i=0;i<15;i++) graph.Observations.Add(new DiagnosticObservation { Kind = ObservationKind.EmptyCache, OwnerId = owner, Message = "Repeated path and warning" });
        var report = new Report { Name = "Solver", Context = new TutorContext { Graph = graph } };
        var evidence = CompactEvidence.Create(report, false, false);
        Assert.Single(evidence.Other);
        Assert.Contains("1 inspected locations", evidence.Other[0]);
        Assert.DoesNotContain("Repeated path", string.Join("", evidence.Other));
        Assert.Contains("does not prove", evidence.Summary);
    }
    [Theory] [InlineData(true,false)] [InlineData(false,true)]
    public void HistoricalOrUnavailableEvidenceCannotBePresentedAsCurrent(bool historical,bool unavailable)
    {
        var evidence = CompactEvidence.Create(new Report { Name = "Solver" }, historical, unavailable);
        Assert.Contains("saved observations", evidence.Summary);
        Assert.Contains("not verified current", evidence.Summary);
        Assert.Contains("verified", evidence.Limits);
    }
    [Fact] public void ReferenceEvidenceIsPrioritizedAndSharedCategoryLabelsAreMerged()
    {
        var graph = new GraphEvidence();
        foreach (var kind in new[] { ObservationKind.Other, ObservationKind.InspectionNote, ObservationKind.ReferenceWarning, ObservationKind.MissingReferenceInActiveDocument, ObservationKind.TreeStructure, ObservationKind.RuntimeWarning })
            graph.Observations.Add(new DiagnosticObservation { Kind = kind, OwnerId = Guid.NewGuid() });
        var evidence = CompactEvidence.Create(new Report { Context = new TutorContext { Graph = graph } }, false, false);
        Assert.Equal(4, evidence.Other.Count);
        Assert.StartsWith("Geometry reference observations", evidence.Other[0]);
        Assert.Contains("2 inspected locations", evidence.Other[0]);
        Assert.Equal(evidence.Other.Count, System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(evidence.Other, x => x.Split('·')[0])).Count());
    }
}

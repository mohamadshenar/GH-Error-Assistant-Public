using System;
using System.Linq;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class GuidedRepairTests
{
    [Fact] public void UnknownFloatingTypeDoesNotInventRhinoAssignmentCommands()
    {
        var c = new TutorContext { Component = "Vendor", OwnerId = Guid.NewGuid(), Parameter = "Payload", ParameterType = "VendorPayload",
            Floating = true, AssignmentKnown = true, SourcesKnown = true, ItemCount = 0 };
        var step = TutorEngine.Explain("Failed to collect data", c).Guidance.Checks[0];
        Assert.Contains("does not know", step.Instruction); Assert.DoesNotContain("select the intended Rhino object", step.Instruction);
    }
    [Fact] public void AllNineAuditCasesHaveExecutableChecksAndExpectedObservations()
    {
        var fixtures = AuditScenarioFixtures.Cases();
        Assert.Equal(9, fixtures.Count);
        foreach (var f in fixtures)
        {
            var e = TutorEngine.Explain(f.Original, f.Context);
            var first = Assert.IsType<GuidedCheck>(e.Guidance.Checks.First());
            Assert.Contains(first.Target.Location, first.Instruction);
            Assert.True(first.Expected.Length > 30, f.Id);
            Assert.True(first.Next.Length > 30, f.Id);
            Assert.DoesNotContain("automatically fixed", first.Expected);
            Assert.Equal(f.Original, e.Diagnostic.OriginalRuntimeMessage);
            if (e.Guidance.Targets.Count > 0)
            {
                Assert.Contains(e.Guidance.Targets[0].Location, e.Diagnostic.RecommendedFirstAction);
                var text = DependencyAnalysis.Format(f.Context.Graph, e.Guidance.Targets);
                if (f.Context.Graph != null) Assert.Contains("1. " + first.Target.Location, text);
            }
        }
    }
    [Fact] public void S7UnassignedPointComesBeforeDownstreamSymptomsAndRetainsEveryLocation()
    {
        var f = AuditScenarioFixtures.Cases().Single(c => c.Id == "S7");
        var e = TutorEngine.Explain(f.Original, f.Context);
        Assert.Equal("Point (Pt) / Point", e.Guidance.Targets.First().Location);
        Assert.Contains(e.Guidance.Targets, t => t.Location == "Scale / Geometry");
        Assert.Contains(e.Guidance.Targets, t => t.Location == "Mesh FromPoints / Points");
        Assert.Contains("Right-click", e.Guidance.Checks[0].Instruction);
        Assert.Contains("not an established cause", e.Guidance.Checks[0].Reason);
        var report = new Report { Context = f.Context, Message = f.Original, Name = "Scale", Severity = "Warning" };
        Assert.Contains("1. Point (Pt) / Point", report.EvidenceText);
    }
    [Fact] public void SourcesUseExactIdentityAndNoWireIsInferredFromName()
    {
        var f = AuditScenarioFixtures.Cases().Single(c => c.Id == "S7");
        var e = TutorEngine.Explain(f.Original, f.Context);
        var step = e.Guidance.Checks.Single(c => c.Target.Location == "Mesh FromPoints / Points");
        var edge = f.Context.Graph.Connections.Single(c => c.TargetId == step.Target.OwnerId);
        Assert.Equal(edge.SourceId, step.Source.OwnerId); Assert.Equal(edge.SourceParameterId, step.Source.ParameterId);
        Assert.Contains("Leave the original connection in place", step.Instruction);
    }
    [Fact] public void OptionalNullDoesNotSuggestAConstantOrBlindRemoval()
    {
        var f = AuditScenarioFixtures.Cases().Single(c => c.Id == "S5"); f.Context.Optional = true;
        var step = TutorEngine.Explain(f.Original, f.Context).Guidance.Checks[0];
        Assert.Contains("optional", step.Expected); Assert.Contains("intentional", step.Expected);
        Assert.DoesNotContain("Slider", step.Instruction); Assert.Contains("do not remove", step.Next);
    }
    [Fact] public void TreesDescribeOnlyObservedStructureWithoutPrescribingAnOperation()
    {
        var f = AuditScenarioFixtures.Cases().Single(c => c.Id == "S6");
        f.Context.TreeSummary = "Observed groups: 2; items: 6. {0}: 2 items; {1}: 4 items";
        var step = TutorEngine.Explain(f.Original, f.Context).Guidance.Checks[0];
        Assert.Contains("{1}: 4 items", step.Observed); Assert.Contains("Param Viewer", step.Instruction);
        Assert.Contains("does not know the required structure", step.Expected);
        Assert.Contains("Do not apply Flatten", step.Next);
    }
    [Fact] public void UnknownVendorUsesAvailableDescriptionAndUsefulReproductionChecklist()
    {
        var f = AuditScenarioFixtures.Cases().Single(c => c.Id == "S8"); f.Context.Description = "Vendor payload input";
        var step = TutorEngine.Explain(f.Original, f.Context).Guidance.Checks[0];
        Assert.Contains("Vendor payload input", step.Observed); Assert.Contains("cannot validate", step.Expected);
        Assert.Contains("plugin version", step.Next); Assert.Contains("Copy error report", step.Next);
        Assert.Contains("Only part", step.Reason);
    }
    [Theory] [InlineData("Point")] [InlineData("Curve")] [InlineData("Surface")] [InlineData("Mesh")]
    public void SupportedUnassignedGeometryGetsAssignmentAndVerificationInstructions(string type)
    {
        var c = new TutorContext { Component = type, OwnerId = Guid.NewGuid(), Parameter = type, ParameterType = type,
            ItemCount = 0, Floating = true, SourcesKnown = true, AssignmentKnown = true };
        var step = TutorEngine.Explain("Failed to collect data", c).Guidance.Checks[0];
        Assert.Contains("Right-click", step.Instruction); Assert.Contains("Rhino object", step.Instruction);
        Assert.Contains("recompute", step.Expected); Assert.Contains("does not prove", step.Expected);
    }
    [Fact] public void RepeatedGuideGenerationDoesNotModifyScalarDocumentGraph()
    {
        var f = AuditScenarioFixtures.Cases().Single(c => c.Id == "S7");
        var graph = f.Context.Graph; var edges = graph.Connections.ToArray(); var issues = graph.Issues.ToArray();
        TutorEngine.Explain(f.Original, f.Context); TutorEngine.Explain(f.Original, f.Context);
        Assert.Equal(edges, graph.Connections); Assert.Equal(issues, graph.Issues);
    }
}

public class RepairVerificationTests
{
    [Fact] public void InvalidItemCannotBecomeAUsableDataClaim()
    {
        var b = Before(); var a = After(b); a.Inputs[0].HasInvalid = true; a.Inputs[0].SampleUsable = true; a.Inputs[0].Count = 3;
        Assert.DoesNotContain("usable data was observed", RepairVerification.Compare(b, a, DateTime.UtcNow).Text);
    }
    [Fact] public void IncompleteMessageScanCannotProveDisappearance()
    {
        var b = Before(); var a = After(b); a.Messages.Clear(); a.Complete = false; a.MessagesComplete = false;
        var text = RepairVerification.Compare(b, a, DateTime.UtcNow).Text;
        Assert.Contains("disappearance is not established", text); Assert.DoesNotContain("no longer reported", text);
    }
    [Fact] public void PartialGraphDoesNotHideACompleteRuntimeMessageComparison()
    {
        var b = Before(); var a = After(b); a.Messages.Clear(); a.Complete = false;
        var r = RepairVerification.Compare(b, a, DateTime.UtcNow);
        Assert.Contains("no longer reported", r.Text); Assert.True(r.Incomplete);
        Assert.Contains("not independently confirmed", r.Text);
    }
    private static VerificationSnapshot Before()
    {
        var s = new VerificationSnapshot { DocumentId = Guid.NewGuid(), DocumentToken = "runtime-1", OwnerId = Guid.NewGuid(), OriginalMessage = "Exact warning\nline 2", SolutionRevision = 5 };
        s.Messages.Add(s.OriginalMessage); s.Selection.Add(s.OwnerId);
        s.Inputs.Add(new InputObservation { OwnerId = s.OwnerId, ParameterId = Guid.NewGuid(), Location = "Pt / Point", Count = 0, HasMissing = true, MissingReference = true, Complete = true, WarningState = "warning" });
        return s;
    }
    private static VerificationSnapshot After(VerificationSnapshot b)
    { var a = b.Copy(); a.CapturedUtc = DateTime.UtcNow; a.SolutionRevision++; return a; }
    [Fact] public void ExactOriginalMessageRemainsPresent()
    { var b = Before(); Assert.True(RepairVerification.Compare(b, After(b), DateTime.UtcNow).MessagePresent); }
    [Fact] public void DisappearedMessageNeverEstablishesIntendedOutput()
    {
        var b = Before(); var a = After(b); a.Messages.Clear();
        var r = RepairVerification.Compare(b, a, DateTime.UtcNow);
        Assert.Contains("no longer reported", r.Text); Assert.Contains("does not prove", r.Text);
        Assert.Contains("not independently confirmed", r.Text);
    }
    [Fact] public void ChangedSelectionIsReportedWhileComparisonStaysPinned()
    {
        var b = Before(); var a = After(b); a.Selection.Clear(); a.Selection.Add(Guid.NewGuid());
        var r = RepairVerification.Compare(b, a, DateTime.UtcNow);
        Assert.True(r.SelectionChanged); Assert.Contains("original baseline object", r.Text);
    }
    [Fact] public void WrongDocumentStopsComparisonEvenWithSamePersistentId()
    {
        var b = Before(); var a = After(b); a.DocumentToken = "different-live-instance";
        var r = RepairVerification.Compare(b, a, DateTime.UtcNow);
        Assert.True(r.Incomplete); Assert.DoesNotContain("still present", r.Text);
    }
    [Fact] public void ChangedReportingObjectStopsComparison()
    { var b = Before(); var a = After(b); a.OwnerId = Guid.NewGuid(); Assert.Contains("reporting object changed", RepairVerification.Compare(b, a, DateTime.UtcNow).Text); }
    [Fact] public void DeletedOriginalTargetIsNotARepair()
    { var b = Before(); var a = After(b); a.TargetExists = false; var r = RepairVerification.Compare(b, a, DateTime.UtcNow); Assert.True(r.TargetMissing); Assert.Contains("not a verified repair", r.Text); }
    [Fact] public void NoNewSolutionDoesNotAcceptCachedDataAsVerification()
    { var b = Before(); var a = After(b); a.SolutionRevision = b.SolutionRevision; Assert.Contains("no subsequent solution", RepairVerification.Compare(b, a, DateTime.UtcNow).Text); }
    [Fact] public void StaleBaselineRequiresRestart()
    { var b = Before(); b.CapturedUtc = DateTime.UtcNow.AddHours(-1); Assert.Contains("stale", RepairVerification.Compare(b, After(b), DateTime.UtcNow).Text); }
    [Fact] public void OldCurrentSnapshotIsRejected()
    { var b = Before(); var a = After(b); a.CapturedUtc = b.CapturedUtc.AddSeconds(-1); Assert.True(RepairVerification.Compare(b, a, DateTime.UtcNow).Incomplete); }
    [Fact] public void BusyOrUnavailableCanvasCannotVerify()
    { var b = Before(); var a = After(b); a.Ready = false; Assert.Contains("still running", RepairVerification.Compare(b, a, DateTime.UtcNow).Text); }
    [Fact] public void PartialSampleReportsUsableObservationWithoutProvingAllData()
    {
        var b = Before(); var a = After(b); var p = a.Inputs[0];
        p.Count = 3; p.HasMissing = false; p.MissingReference = false; p.WarningState = ""; p.SampleUsable = true; p.Complete = false;
        var r = RepairVerification.Compare(b, a, DateTime.UtcNow);
        Assert.True(r.Incomplete); Assert.Contains("usable data was observed", r.Text); Assert.Contains("unsampled", r.Text);
        Assert.Contains("warning state changed", r.Text); Assert.Contains("0 to 3", r.Text);
    }
    [Fact] public void MissingRelevantInputIsIncomplete()
    { var b = Before(); var a = After(b); a.Inputs.Clear(); Assert.True(RepairVerification.Compare(b, a, DateTime.UtcNow).Incomplete); }
    [Fact] public void SnapshotIsCopiedAndComparisonsDoNotMutateBaseline()
    {
        var b = Before(); var a = After(b); a.Inputs[0].Count = 12; a.Messages.Clear();
        RepairVerification.Compare(b, a, DateTime.UtcNow);
        Assert.Equal(0, b.Inputs[0].Count); Assert.Single(b.Messages); Assert.Equal(5, b.SolutionRevision);
    }
}

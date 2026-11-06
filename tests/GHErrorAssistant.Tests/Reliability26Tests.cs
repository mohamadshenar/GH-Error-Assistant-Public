using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class Reliability26Tests
{
    static readonly Guid Document = Guid.NewGuid(), Owner = Guid.NewGuid(), Port = Guid.NewGuid();
    const string Original = "A referenced Curve could not be found in the Rhino document.";
    static TutorContext Context(bool missing) => new TutorContext { Component = "Curve", Parameter = "Curve", ParameterType = "Curve",
        OwnerId = Owner, ParameterId = Port, Floating = true, AssignmentKnown = true, HasAssignedData = true,
        MissingReferenceInActiveDocument = missing, MissingReferenceCount = missing ? 7 : (int?)null,
        Graph = new GraphEvidence { RootId = Owner, RootName = "Curve" } };
    static Report OriginalReport() => new Report { Name = "Curve", DocumentId = Document, OwnerId = Owner, FocusInputId = Port,
        Severity = "Warning", Message = Original, Context = Context(true) };
    static VerificationSnapshot Snapshot(long revision, bool original)
    {
        var s = new VerificationSnapshot { DocumentId = Document, DocumentToken = "same-live-document", OwnerId = Owner,
            FocusInputId = Port, OriginalMessage = Original, OriginalSeverity = "Warning", SolutionRevision = revision };
        if (original) s.RuntimeMessages.Add(new RuntimeObservation { OwnerId = Owner, ParameterId = Port, Message = Original, Severity = "Warning", Location = "Curve" });
        return s;
    }
    static TroubleshootingSession Session() => new TroubleshootingSession(OriginalReport(), Snapshot(1, true), "Saved test document");
    static Report Healthy() => CurrentDiagnosis.Create(OriginalReport(), Context(false), "Current bounded evidence", Array.Empty<RuntimeObservation>());
    static void Refresh(TroubleshootingSession s, VerificationSnapshot current, Report live = null)
    { s.Verification = RepairVerification.Compare(s.Baseline, current, DateTime.UtcNow); s.RefreshCurrent(live ?? Healthy(), current); }

    [Fact] public void UnavailableVerificationRetainsHistoricalLocationButNotCurrentAdvice()
    {
        var s = Session(); var target = s.LastKnownTarget;
        Assert.NotNull(target); Refresh(s, Snapshot(1, false));
        Assert.True(s.CurrentUnavailable); Assert.Same(target, s.LastKnownTarget);
        Assert.Null(s.ActiveCheck); Assert.Null(CurrentPresentation.Create(s.Report, s).Card);
        Assert.Contains("historical", s.UnavailableExplanation, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void AutomaticRefreshRequiresNewCompletedSolutionInOriginalDocument()
    {
        Assert.True(SolutionRefreshGate.ShouldRefresh(true, true, false, 2, 1));
        Assert.False(SolutionRefreshGate.ShouldRefresh(true, true, false, 1, 1));
        Assert.False(SolutionRefreshGate.ShouldRefresh(true, false, false, 2, 1));
        Assert.False(SolutionRefreshGate.ShouldRefresh(true, true, true, 2, 1));
        Assert.False(SolutionRefreshGate.ShouldRefresh(false, true, false, 2, 1));
    }
    [Fact] public void SuccessfulRefreshExitsHistoryAndKeepsLocationWithoutRevivingOldAdvice()
    {
        var s = Session(); var target = s.LastKnownTarget; s.ShowingHistoricalDiagnosis = true;
        Refresh(s, Snapshot(2, false));
        Assert.False(s.ShowingHistoricalDiagnosis); Assert.Same(target, s.LastKnownTarget);
        Assert.Null(s.ActiveCheck); Assert.Null(CurrentPresentation.Create(s.Report, s).Card);
        Assert.False(s.DisplayReport.HasCurrentFinding);
    }

    [Fact] public void AbsentOriginalMessageCannotBecomeCurrentRepairAdvice()
    {
        var s = Session(); Refresh(s, Snapshot(2, false));
        Assert.True(s.Verification.OriginalAbsentConfirmed); Assert.False(s.DisplayReport.HasCurrentFinding);
        Assert.Equal("", s.DisplayReport.Message); Assert.Null(s.ActiveCheck);
        Assert.DoesNotContain(Original, s.DisplayReport.Text()); Assert.Equal(Original, s.Report.Message);
    }
    [Fact] public void PresentationIgnoresHistoricalReportPassedByNavigation()
    {
        var s = Session(); Refresh(s, Snapshot(2, false));
        // The same presenter feeds real Eto Locate/next controls. A baseline argument cannot resurrect its card.
        var view = CurrentPresentation.Create(s.Report, s);
        Assert.Same(s.DisplayReport, view.Report); Assert.Null(view.Card); Assert.False(view.CanLocate);
    }
    [Fact] public void CurrentRuntimeMessageSuppliesItsOwnTextAndPort()
    {
        var actual = new RuntimeObservation { OwnerId = Owner, ParameterId = Guid.NewGuid(), Message = "Data conversion failed from Point to Curve", Severity = "Error" };
        var r = CurrentDiagnosis.Create(OriginalReport(), Context(false), "Live", new[]{ actual });
        Assert.Equal(actual.Message, r.Message); Assert.Equal(actual.ParameterId, r.FocusInputId); Assert.Equal("Error", r.Severity);
        Assert.DoesNotContain(Original, r.Text());
    }
    [Fact] public void OtherOwnersCannotSupplyCurrentOriginalMessage()
    {
        var r = CurrentDiagnosis.Create(OriginalReport(), Context(false), "Live", new[]{new RuntimeObservation { OwnerId = Guid.NewGuid(), Message = Original, Severity = "Error" }});
        Assert.False(r.HasRuntimeMessage); Assert.False(r.HasCurrentFinding);
    }
    [Fact] public void ReplacementErrorHasExplicitNextActionAndPreservesBaseline()
    {
        var s = Session(); var current = Snapshot(2, false);
        var issue = new RuntimeObservation { OwnerId = Guid.NewGuid(), ParameterId = Guid.Empty, Severity = "Error", Location = "Mesh / Points", Message = "Solution exception: replacement failure" };
        current.RuntimeMessages.Add(issue); Refresh(s, current);
        var view = CurrentPresentation.Create(s.Report, s);
        Assert.Equal("Investigate next error", view.NextAction); Assert.Contains("Mesh / Points", view.Comparison);
        Assert.Contains("Intended output unverified", view.Comparison);
        var live = new Report { Name = "Mesh", OwnerId = issue.OwnerId, DocumentId = Document, FocusInputId = issue.ParameterId,
            Message = issue.Message, Severity = issue.Severity, Context = new TutorContext { Component = "Mesh" } };
        var baseline = s.Baseline; Assert.True(s.FollowIssue(live, issue));
        Assert.Same(live, s.DisplayReport); Assert.Same(baseline, s.Baseline); Assert.Equal(Original, s.Report.Message);
        Assert.Equal(Original, s.Baseline.OriginalMessage); Assert.Null(s.NextIssue);
    }
    [Fact] public void FollowRejectsStaleOrUnobservedMessage()
    {
        var s = Session(); var current = Snapshot(2, false); Refresh(s, current);
        var issue = new RuntimeObservation { OwnerId = Guid.NewGuid(), Message = "Not observed" };
        Assert.False(s.FollowIssue(new Report { OwnerId = issue.OwnerId, DocumentId = Document, Message = issue.Message }, issue));
    }
    [Fact] public void WrongDocumentSuppressesAllSavedRepairActions()
    {
        var s = Session(); var current = Snapshot(2, false); current.DocumentToken = "different-live-instance"; Refresh(s, current);
        var view = CurrentPresentation.Create(s.Report, s);
        Assert.True(view.Unavailable); Assert.Null(view.Card); Assert.False(view.CanLocate); Assert.Null(view.NextAction);
        Assert.Equal(Original, s.Baseline.OriginalMessage);
    }
    [Fact] public void NoCompletedSolutionCannotRefreshOrClaimResolution()
    {
        var s = Session(); Refresh(s, Snapshot(1, false));
        Assert.False(s.Verification.ComparisonMade); Assert.False(s.Verification.OriginalAbsentConfirmed);
        Assert.True(CurrentPresentation.Create(s.Report, s).Unavailable);
    }
    [Fact] public void PartialMessageReadDoesNotTurnAbsenceIntoResolution()
    {
        var s = Session(); var current = Snapshot(2, false); current.MessagesComplete = false; current.Complete = false; Refresh(s, current);
        Assert.False(s.Verification.OriginalAbsentConfirmed); Assert.True(s.CurrentUnavailable);
        Assert.Null(CurrentPresentation.Create(s.Report, s).Card);
    }
    [Fact] public void DeletedOriginalDoesNotLeaveAnEnabledOldRepair()
    {
        var s = Session(); var current = Snapshot(2, false); current.TargetExists = false; Refresh(s, current);
        Assert.True(s.Verification.TargetMissing); Assert.False(CurrentPresentation.Create(s.Report, s).CanLocate);
        Assert.Contains("no longer exists", s.UnavailableExplanation);
        Assert.Contains("Start a new investigation", s.ComparisonStatus);
        Assert.Contains("does not verify a repair", s.UnavailableExplanation);
    }
    [Fact] public void WrongDocumentDoesNotClaimTheOriginalWasDeleted()
    {
        var s = Session(); var current = Snapshot(2, false); current.DocumentToken = "other"; current.TargetExists = false; Refresh(s, current);
        Assert.False(s.Verification.TargetMissing);
        Assert.Contains("document where this investigation started", s.UnavailableExplanation);
        Assert.DoesNotContain("no longer exists", s.UnavailableExplanation);
    }
    [Fact] public void MissingUpstreamTargetWithNoNewSolutionExplainsDeletionWithoutClaimingRepair()
    {
        var s = Session(); var current = Snapshot(1, false); current.InvestigationTargetMissing = true; Refresh(s, current);
        Assert.True(s.CurrentUnavailable); Assert.True(s.Verification.InvestigationTargetMissing);
        Assert.Contains("target no longer exists", s.UnavailableExplanation);
        Assert.Contains("Recompute", s.UnavailableExplanation);
        Assert.Contains("does not prove", s.UnavailableExplanation);
        Assert.DoesNotContain("Return", s.UnavailableExplanation);
    }
    [Fact] public void UnchangedActiveDocumentNeedsRecomputeNotDocumentSwitch()
    {
        var s = Session(); Refresh(s, Snapshot(1, false));
        Assert.Contains("No new Grasshopper solution", s.UnavailableExplanation);
        Assert.DoesNotContain("Return", s.UnavailableExplanation);
    }
    [Fact] public void StaleBaselineCallsForNewInvestigation()
    {
        var s = Session(); var current = Snapshot(2, false); current.CapturedUtc = DateTime.UtcNow.AddHours(-2); Refresh(s, current);
        Assert.Equal(VerificationUnavailableReason.Stale, s.Verification.UnavailableReason);
        Assert.Contains("start a new investigation", s.UnavailableExplanation);
    }
    [Fact] public void SwitchingDocumentsImmediatelyInvalidatesPriorFeedbackAndDoesNotReviveItOnReturn()
    {
        var s = Session(); var current = Snapshot(1, false); current.InvestigationTargetMissing = true; Refresh(s, current);
        s.LocateFeedback = "old target feedback";
        Assert.True(s.ObserveDocument(false));
        Assert.Null(s.LocateFeedback); Assert.True(s.CurrentUnavailable);
        Assert.Contains("document where this investigation started", s.UnavailableExplanation);
        Assert.False(s.ObserveDocument(false));
        Assert.True(s.ObserveDocument(true));
        Assert.True(s.CurrentUnavailable); Assert.DoesNotContain("Return", s.UnavailableExplanation);
        Assert.Contains("click Check Again", s.UnavailableExplanation);
    }
    [Fact] public void MissingUpstreamAfterRecomputeRefreshesEvidenceWithoutDeclaringRepair()
    {
        var s = Session(); var current = Snapshot(2, false); current.InvestigationTargetMissing = true;
        Refresh(s, current);
        Assert.True(s.Verification.ComparisonMade);
        Assert.True(s.Verification.OriginalAbsentConfirmed);
        Assert.False(s.CurrentUnavailable);
        Assert.Contains("Intended output unverified", s.ComparisonStatus);
        Assert.DoesNotContain("fixed", s.ComparisonStatus, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void RepeatedDocumentTransitionsCannotRestoreOldLocateFeedback()
    {
        var s = Session(); s.LocateFeedback = "outdated";
        for (var i = 0; i < 100; i++)
        {
            Assert.True(s.ObserveDocument(false)); Assert.Null(s.LocateFeedback);
            Assert.True(s.ObserveDocument(true)); Assert.True(s.CurrentUnavailable);
            Assert.False(CurrentPresentation.Create(s.Report, s).CanLocate);
        }
        Refresh(s, Snapshot(2, false));
        Assert.False(s.CurrentUnavailable); Assert.Null(s.LocateFeedback);
    }
    [Fact] public void ReferenceCardIncludesExpectedResultAndAtMostThreeSteps()
    {
        var s = Session(); var view = CurrentPresentation.Create(s.Report, s);
        Assert.True(view.CanLocate); Assert.Contains("no longer reported", view.Card.Expected);
        Assert.Contains("Set Multiple Curves", string.Join(" ", view.Card.Steps));
        Assert.InRange(view.Card.Steps.Count, 1, 3);
    }
    [Fact] public void CopyClearlySeparatesBaselineFromCurrentAbsence()
    {
        var s = Session(); Refresh(s, Snapshot(2, false)); var text = s.Text();
        Assert.Contains("HISTORICAL BASELINE", text); Assert.Contains("CURRENT DIAGNOSIS", text);
        Assert.Contains(Original, text); Assert.Contains("No runtime warning", text);
    }
    [Fact] public void OldManualObservationCannotDriveTheRefreshedPlan()
    {
        var s = Session(); s.Record(s.ActiveCheck, CheckObservation.MissingOrUnusable);
        Refresh(s, Snapshot(2, false));
        Assert.Empty(s.Observations); Assert.Single(s.HistoricalObservations); Assert.Null(s.ActiveCheck);
        Assert.Contains("Historical manual observations", s.Text());
    }
    [Fact] public void LongReplacementTextCannotDisplaceLocationOrOutputQualification()
    {
        var s = Session(); var now = Snapshot(2, false);
        now.RuntimeMessages.Add(new RuntimeObservation { OwnerId = Guid.NewGuid(), Severity = "Error", Location = "Replacement component", Message = new string('x', 4000) });
        Refresh(s, now); var view = CurrentPresentation.Create(s.Report, s);
        Assert.Contains("Replacement component", view.Comparison); Assert.Contains("Intended output unverified", view.Comparison);
        Assert.DoesNotContain(new string('x', 100), view.Comparison); Assert.Equal(new string('x', 4000), s.NextIssue.Message);
    }
    [Fact] public void NamedFrontiersDeduplicateSharedObjects()
    {
        var r = new Reader(); var root = new DependencyNode { Id = Guid.NewGuid(), Name = "Cluster", Boundary = true };
        r.Nodes[root.Id] = root;
        var graph = DependencyAnalysis.AnalyzeDetailed(r, root.Id, Guid.Empty).Graph;
        Assert.Single(graph.FrontierTargets); Assert.Equal(root.Id, graph.FrontierTargets[0].OwnerId);
        Assert.Equal("Cluster", graph.FrontierTargets[0].Name); Assert.False(graph.FrontierTargets[0].CanContinue);
    }
    [Fact] public void InvalidRangeDoesNotClaimMissingDataAsCause()
    {
        var r = new Report { Name = "Solver", Severity = "Error", Message = "Solution exception:Offset and length were out of bounds", Context = new TutorContext { Component = "Solver" } };
        Assert.Contains("invalid item position or range", r.Tutor.Guidance.Wrong);
        Assert.DoesNotContain("items that were not there", r.Tutor.Guidance.Wrong);
    }
    [Fact] public void FollowedTargetsMessageScopeIncludesTheNextRecipientWithoutRebaselining()
    {
        var baseline = Snapshot(1, true); var current = Snapshot(3, false); var followed = Guid.NewGuid(); var next = Guid.NewGuid();
        var scope = Snapshot(3, false); scope.OwnerId = followed;
        scope.RuntimeMessages.Add(new RuntimeObservation { OwnerId = next, Message = "New failure two wires after original", Severity = "Error" });
        Assert.True(current.IncludeCurrentMessages(scope, followed)); Assert.Equal(Owner, current.OwnerId);
        var result = RepairVerification.Compare(baseline, current, DateTime.UtcNow);
        Assert.Contains(result.NewMessages, m => m.OwnerId == next);
    }
    [Fact] public void ForeignFollowedScopeCannotLeakMessagesAcrossDocuments()
    {
        var current = Snapshot(3, false); var scope = Snapshot(3, false); scope.DocumentToken = "other";
        scope.RuntimeMessages.Add(new RuntimeObservation { OwnerId = Owner, Message = "Foreign" });
        Assert.False(current.IncludeCurrentMessages(scope, Owner)); Assert.Empty(current.RuntimeMessages); Assert.False(current.MessagesComplete);
    }
    [Fact] public void SavedReferenceBudgetSurvivesNullItemBudgetExhaustion()
    {
        var budget = new InspectionSampling(); for (int i = 0; i < InspectionSampling.Maximum; i++) Assert.True(budget.TakeItem());
        Assert.False(budget.TakeItem()); Assert.Equal(8, budget.ReferenceCapacity);
        budget.ReferencesRead(2, true); Assert.Equal(8, budget.ReferenceCapacity);
        for (int i = 0; i < 23; i++) budget.ReferencesRead(8, true);
        Assert.Equal(6, budget.ReferenceCapacity); budget.ReferencesRead(6, true); Assert.Equal(0, budget.ReferenceCapacity);
    }
    [Fact] public void SharedPortIsSampledOnceAcrossReceiverAndSourceReads()
    {
        var budget = new InspectionSampling(); var port = Guid.NewGuid(); int reads = 0;
        Action<List<DiagnosticObservation>> sample = facts => { reads++; budget.ReferencesRead(7, true); facts.Add(new DiagnosticObservation { Count = 7, Kind = ObservationKind.MissingReferenceInActiveDocument }); };
        var first = budget.ReadOnce(port, sample);
        for (int i = 0; i < 100; i++) Assert.Same(first, budget.ReadOnce(port, sample));
        Assert.Equal(1, reads); Assert.Equal(7, first[0].Count);
    }
    [Fact] public void NamedFrontierContinuationIsExplicitAndIdentityChecked()
    {
        var s = Session(); var f = new InspectionFrontier { OwnerId = Guid.NewGuid(), Name = "Pull Point", CanContinue = true, Depth = 7, Reason = "sample budget" };
        s.DisplayReport.Context.Graph.FrontierTargets.Add(f);
        Assert.Equal("Inspect further upstream", CurrentPresentation.Create(s.Report, s).NextAction);
        var continued = new Report { Name = f.Name, OwnerId = f.OwnerId, DocumentId = Document, HasRuntimeMessage = false, Message = "", Severity = "Information" };
        Assert.True(s.FollowFrontier(continued, f)); Assert.Equal(Owner, s.Baseline.OwnerId); Assert.Same(continued, s.DisplayReport);
        Assert.False(s.FollowFrontier(continued, new InspectionFrontier { OwnerId = f.OwnerId, CanContinue = true }));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void OpaqueOrForeignFrontierCannotBeFollowed(bool opaque)
    {
        var s = Session(); var f = new InspectionFrontier { OwnerId = Guid.NewGuid(), CanContinue = !opaque };
        s.DisplayReport.Context.Graph.FrontierTargets.Add(f);
        Assert.False(s.FollowFrontier(new Report { OwnerId = f.OwnerId, DocumentId = opaque ? Document : Guid.NewGuid() }, f));
    }
    [Fact] public void ReopeningPolicyHasOneActiveMonitorAndIdempotentShutdown()
    {
        var lifecycle = new AssistantLifecycle();
        for (int i = 0; i < 100; i++) { Assert.True(lifecycle.Resume()); Assert.False(lifecycle.Resume()); Assert.True(lifecycle.Pause()); Assert.False(lifecycle.Pause()); }
        Assert.True(lifecycle.Terminate()); Assert.False(lifecycle.Resume()); Assert.False(lifecycle.Terminate()); Assert.False(lifecycle.Active);
    }
    sealed class Reader : IDependencyReader
    {
        internal readonly Dictionary<Guid, DependencyNode> Nodes = new Dictionary<Guid, DependencyNode>();
        public bool Limited { get; set; }
        public DependencyNode Read(Guid id) => Nodes.TryGetValue(id, out var n) ? n : null;
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void CapturedNurbsBranchFindsTwoReferencesEvenWhenVolatileSamplingIsSpent(bool sampleStarved)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v26-native-nurbs.json")));
        var data = json.RootElement; var root = Guid.Parse(data.GetProperty("rootId").GetString());
        var curve = Guid.Parse(data.GetProperty("curveId").GetString()); var reader = new Reader { Limited = sampleStarved };
        foreach (var obj in data.GetProperty("objects").EnumerateArray())
        {
            var node = new DependencyNode { Id = Guid.Parse(obj.GetProperty("id").GetString()), Name = obj.GetProperty("name").GetString() }; reader.Nodes[node.Id] = node;
            foreach (var message in obj.GetProperty("messages").EnumerateArray()) node.Observations.Add(DiagnosticObservation.Warning(message.GetProperty("message").GetString(), message.GetProperty("severity").GetString()));
            foreach (var port in obj.GetProperty("ports").EnumerateArray().Where(p => p.GetProperty("direction").GetString() != "output"))
            {
                node.Floating |= port.GetProperty("direction").GetString() == "floating";
                var input = new DependencyInput { Id = Guid.Parse(port.GetProperty("id").GetString()), Name = port.GetProperty("name").GetString(), TypeName = port.GetProperty("type").GetString(), Count = port.GetProperty("count").GetInt32(), Optional = port.GetProperty("optional").GetBoolean(), AssignmentKnown = port.TryGetProperty("persistentCount", out var count), HasAssignedData = count.ValueKind == JsonValueKind.Number && count.GetInt32() > 0 };
                node.Inputs.Add(input);
                foreach (var message in port.GetProperty("messages").EnumerateArray()) input.Observations.Add(DiagnosticObservation.Warning(message.GetProperty("message").GetString(), message.GetProperty("severity").GetString()));
                if ((!sampleStarved || node.Id == root) && port.GetProperty("sample").EnumerateArray().Any(p => p.GetProperty("null").GetBoolean())) input.Observations.Add(new DiagnosticObservation { Kind = ObservationKind.NullSample, Message = "Captured null items" });
                int missing = port.GetProperty("persistent").EnumerateArray().Count(p => p.TryGetProperty("referenced", out var referenced) && referenced.GetBoolean() && !p.GetProperty("found").GetBoolean());
                if (missing > 0) input.Observations.Add(new DiagnosticObservation { Kind = ObservationKind.MissingReferenceInActiveDocument, Count = missing, Message = "Captured absent saved reference IDs" });
                foreach (var source in port.GetProperty("sources").EnumerateArray()) input.Sources.Add(new DependencyLink { OwnerId = Guid.Parse(source.GetProperty("owner").GetString()), OutputId = Guid.Parse(source.GetProperty("port").GetString()), OwnerName = "Captured source", OutputName = source.GetProperty("name").GetString(), Count = source.GetProperty("count").GetInt32() });
            }
        }
        // This fixture verifies structural tracing, independent of CI machine scheduling.
        var graph = DependencyAnalysis.AnalyzeDetailed(reader, root, Guid.Empty, elapsedMilliseconds: () => 0).Graph;
        var report = new Report { Name = "Nurbs Curve", OwnerId = root, Message = "Insufficient vertices for a curve", Severity = "Error", Context = new TutorContext { Graph = graph, Component = "Nurbs Curve" } };
        var first = report.Tutor.Guidance.Checks.First(); Assert.Equal(curve, first.Target.OwnerId);
        Assert.Equal(8, first.Target.Issue.Depth); Assert.Equal(2, first.Target.Issue.Observation.Count);
        Assert.Contains("not a proven cause", RepairCard.Create(first, report).Why);
        Assert.True(graph.Connections.Count <= DependencyAnalysis.MaxEdges);
    }
}


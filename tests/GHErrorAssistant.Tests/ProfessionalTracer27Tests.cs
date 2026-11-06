using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;
using Xunit.Abstractions;

public class ProfessionalTracer27Tests
{
    readonly ITestOutputHelper output;
    public ProfessionalTracer27Tests(ITestOutputHelper output) { this.output = output; }
    sealed class Reader : IDependencyReader, IDependencySnapshot
    {
        internal readonly Dictionary<Guid, DependencyNode> Nodes = new();
        internal readonly List<Guid> Reads = new();
        public bool Limited { get; set; }
        public Guid DocumentId { get; } = Guid.NewGuid();
        public string DocumentToken => "disposable-scalar";
        public DependencyNode Read(Guid id) { Reads.Add(id); return Nodes.TryGetValue(id, out var n) ? n : null; }
        internal DependencyNode Node(string name) { var n = new DependencyNode { Id = Guid.NewGuid(), Name = name }; Nodes[n.Id] = n; return n; }
    }
    static DependencyInput Wire(DependencyNode receiver, DependencyNode source, int count = 0, Guid output = default)
    {
        var input = new DependencyInput { Id = Guid.NewGuid(), Name = "Input", Count = count };
        input.Sources.Add(new DependencyLink { OwnerId = source.Id, OutputId = output == Guid.Empty ? source.Id : output,
            OwnerName = source.Name, OutputName = "Output", Count = count }); receiver.Inputs.Add(input); return input;
    }
    static void Fault(DependencyNode node, int count = 2)
    {
        var p = new DependencyInput { Id = node.Id, Name = "Curve", TypeName = "Curve", Count = count,
            Tree = new TreeEvidence(), References = new ReferenceEvidence { Available = true, Assigned = true, Complete = true, Sampled = count, Missing = count } };
        p.Observations.Add(new DiagnosticObservation { Kind = ObservationKind.MissingReferenceInActiveDocument, Count = count, Message = "Checked IDs absent" }); node.Inputs.Add(p);
    }
    static GraphEvidence Graph(Reader r, DependencyNode root) => DependencyAnalysis.AnalyzeDetailed(r, root.Id, Guid.Empty).Graph;
    static List<InvestigationTarget> Rank(GraphEvidence g) => BeginnerDiagnosis.Rank(new DiagnosticModel { ComponentId = g.RootId, Graph = g }, new TutorContext());

    [Fact] public void SiblingBranchesAreReadBeforeDeeperDescendants()
    {
        var r = new Reader(); var root = r.Node("Root"); var a = r.Node("A"); var b = r.Node("B"); var deep = r.Node("Deep");
        Wire(root, a); Wire(a, deep); Wire(root, b); Fault(deep); Fault(b);
        var g = Graph(r, root); Assert.True(r.Reads.IndexOf(b.Id) < r.Reads.IndexOf(deep.Id)); Assert.Equal(2, Rank(g).Count);
    }
    [Fact] public void SharedSourceReadOnceRetainsBothRootInputBranches()
    {
        var r = new Reader(); var root = r.Node("Root"); var a = r.Node("A"); var b = r.Node("B"); var shared = r.Node("Curve");
        var pa = Wire(root, a); var pb = Wire(root, b); Wire(a, shared); Wire(b, shared); Fault(shared);
        var g = Graph(r, root); Assert.Equal(1, r.Reads.Count(id => id == shared.Id));
        var target = Assert.Single(Rank(g)); Assert.Equal(new[] { pa.Id, pb.Id }.OrderBy(x => x), target.Issue.Branches.OrderBy(x => x));
        Assert.Contains("shared by 2", target.RankingReason); Assert.True(g.HasPathToRoot(target.Issue));
    }
    [Fact] public void DuplicateNamesUseIdentityAsDeterministicFinalTieBreak()
    {
        var r = new Reader(); var root = r.Node("Root"); var a = r.Node("Curve"); var b = r.Node("Curve");
        Wire(root, a); Wire(root, b); Fault(a); Fault(b); var g = Graph(r, root);
        var first = Rank(g).Select(t => t.OwnerId).ToArray(); g.Issues.Reverse();
        Assert.Equal(first, Rank(g).Select(t => t.OwnerId)); Assert.Equal(2, first.Distinct().Count());
    }
    [Fact] public void UnusedOutputWarningIsRetainedButCannotOutrankUsedOutputFault()
    {
        var r = new Reader(); var root = r.Node("Root"); var source = r.Node("Vendor"); var used = Guid.NewGuid(); var unused = Guid.NewGuid();
        Wire(root, source, 1, used).Sources[0].Observations.Add(new DiagnosticObservation { Kind = ObservationKind.NullSample, Message = "Observed null" });
        var p = new DependencyInput { Id = unused, Name = "Unused", TypeName = "Curve", Direction = "output" };
        p.Observations.Add(new DiagnosticObservation { Kind = ObservationKind.MissingReferenceInActiveDocument, Count = 7, Message = "Other output's missing references" }); source.Inputs.Add(p);
        var targets = Rank(Graph(r, root)); Assert.Equal(used, targets[0].ParameterId);
        Assert.Contains(targets, t => t.ParameterId == unused && t.RankingReason.Contains("relevance is weak"));
    }
    [Fact] public void CrossBranchCycleDetectedEvenWithSharedShortestPaths()
    {
        var r = new Reader(); var root = r.Node("Root"); var a = r.Node("A"); var b = r.Node("B");
        Wire(root, a); Wire(root, b); Wire(a, b); Wire(b, a); var g = Graph(r, root);
        Assert.True(g.Cycle); Assert.Equal(3, r.Reads.Count); Assert.True(g.Connections.Count <= 128);
    }
    [Fact] public void DisconnectedFaultNeverEntersCandidateRanking()
    {
        var r = new Reader(); var root = r.Node("Root"); Fault(r.Node("Unconnected Curve")); Assert.Empty(Rank(Graph(r, root)));
    }
    [Fact] public void OpaqueClusterHasExactNonContinuableBoundary()
    {
        var r = new Reader(); var root = r.Node("Root"); var cluster = r.Node("Cluster"); cluster.Boundary = true;
        Wire(root, cluster); Wire(cluster, r.Node("Hidden")); var g = Graph(r, root);
        Assert.Equal(2, r.Reads.Count); var f = Assert.Single(g.FrontierTargets); Assert.False(f.CanContinue); Assert.Contains("opaque", f.Reason);
    }
    [Fact] public void UnknownVendorWithNoObservedFaultStaysUnknown()
    {
        var r = new Reader(); var root = r.Node("Vendor"); var g = Graph(r, root); Assert.Empty(Rank(g));
        Assert.Contains("cause is still unknown", TutorEngine.Explain("Vendor error 42", new TutorContext { Component = "Vendor", Graph = g }).Guidance.Wrong);
    }
    [Fact] public void SnapshotCarriesDocumentAndGenerationWithoutCausalPromotion()
    {
        var r = new Reader(); var root = r.Node("Root"); var source = r.Node("Curve"); Wire(root, source); Fault(source);
        var g = Graph(r, root); Assert.Equal(r.DocumentId, g.DocumentId); Assert.Equal(r.DocumentToken, g.DocumentToken);
        Assert.NotEqual(Guid.Empty, g.Generation); Assert.All(g.Observations, o => Assert.Equal(EvidenceClass.VerifiedObservation, o.Classification));
        Assert.All(g.Issues, i => Assert.Equal(EvidenceClass.CandidateExplanation, i.Classification));
        Assert.DoesNotContain(g.Issues, i => i.Classification == EvidenceClass.ConfirmedCausalResult);
        Assert.Contains("causation remains unverified", Rank(g)[0].RankingReason);
    }
    [Fact] public void ExactPortIndexesNicknamesAndWireIdentitiesSurvive()
    {
        var r = new Reader(); var root = r.Node("Root"); var source = r.Node("Curve"); var p = Wire(root, source);
        p.Index = 3; p.Nickname = "P"; p.Sources[0].Index = 2; p.Sources[0].Nickname = "C"; Fault(source);
        p.Sources[0].Tree = new TreeEvidence(); p.Sources[0].References = new ReferenceEvidence();
        var edge = Assert.Single(Graph(r, root).Connections); Assert.Equal(3, edge.TargetIndex); Assert.Equal(2, edge.SourceIndex);
        Assert.Equal("P", edge.TargetNickname); Assert.Equal("C", edge.SourceNickname); Assert.Equal(p.Id, edge.TargetParameterId);
    }
    static TreeEvidence Tree(params ItemState[] items)
    {
        var t = new TreeEvidence { Available = true, Complete = true, PathCount = 1, ItemCount = items.Length };
        var b = new BranchEvidence { Path = "{0}", ItemCount = items.Length }; foreach (var item in items) b.Add(item, item == ItemState.Opaque ? "VendorPayload" : "GH_Point");
        t.Branches.Add(b); return t;
    }
    [Theory][InlineData((int)ItemState.Null, (int)ObservationKind.NullSample)][InlineData((int)ItemState.Invalid, (int)ObservationKind.InvalidDataSample)]
    public void ItemFaultsAreSpecificObservedConditions(int state, int kind)
    { var t = Tree((ItemState)state); Assert.Contains(t.Observations(), o => o.Kind == (ObservationKind)kind); Assert.DoesNotContain(t.Observations(), o => o.Kind == ObservationKind.MissingReferenceInActiveDocument); }
    [Fact] public void MixedValidAndNullRetainsBothFacts()
    { var t = Tree(ItemState.Valid, ItemState.Null); Assert.Equal(1, t.Branches[0].Valid); Assert.Equal(1, t.Branches[0].Nulls); Assert.True(t.Complete); Assert.Contains("other items may be usable", t.Observations().Last().Message); }
    [Fact] public void ValidAndOpaqueItemsAreNotPromotedToFaults()
    { var t = Tree(ItemState.Valid, ItemState.Opaque); Assert.Single(t.Observations()); Assert.Equal(1, t.Branches[0].Opaque); Assert.Equal(2, t.Branches[0].Types.Count); }
    [Fact] public void EmptyTreeAndEmptyBranchRemainDistinct()
    {
        var empty = new TreeEvidence { Available = true, Complete = true }; var branch = Tree();
        Assert.Contains("0 branches, 0 items", empty.Observations().Single().Message); Assert.Contains("1 branches, 0 items", branch.Observations().Single().Message);
        Assert.Single(branch.Observations()); Assert.True(branch.Branches[0].Complete);
    }
    [Fact] public void DifferentPathsAreRecordedWithoutInventingMismatchOrRepair()
    {
        var t = Tree(ItemState.Valid); t.PathCount = 2; t.Branches.Add(new BranchEvidence { Path = "{9;2}", ItemCount = 0 });
        var text = t.Observations().Single().Message; Assert.Contains("{9;2}", text); Assert.Contains("no tree repair is inferred", text);
        Assert.DoesNotContain("Flatten", text); Assert.DoesNotContain("Graft", text);
    }
    [Fact] public void PartialTreeSampleCannotEstablishAllItemsValid()
    { var t = Tree(ItemState.Valid); t.Complete = false; t.Branches[0].ItemCount = 90; t.ItemCount = 90; Assert.False(t.Branches[0].Complete); Assert.Contains("unsampled items unknown", t.Observations().Single().Message); }
    [Theory][InlineData(7)][InlineData(2)]
    public void MissingReferenceCountsRemainCurrentCheckedCounts(int count)
    { var r = new ReferenceEvidence { Available = true, Assigned = true, Complete = true, Sampled = count, Missing = count }; Assert.Equal(ReferenceState.Unresolved, r.State); Assert.Contains("document " + count, r.Summary); }
    [Fact] public void ResolvedUnassignedUnavailableAndPartialReferencesAreSeparate()
    {
        var r = new ReferenceEvidence(); Assert.Equal(ReferenceState.Unavailable, r.State);
        r.Available = true; r.Complete = true; Assert.Equal(ReferenceState.Unassigned, r.State);
        r.Assigned = true; r.Sampled = 2; r.Resolved = 2; Assert.Equal(ReferenceState.Resolved, r.State);
        r.Complete = false; r.Missing = 1; Assert.Equal(ReferenceState.Partial, r.State); Assert.Contains("lower bounds", r.Summary);
    }
    [Fact] public void OptionalAndRequiredEmptyInputsAreNotAutomaticFaults()
    {
        var r = new Reader(); var root = r.Node("Root"); root.Inputs.Add(new DependencyInput { Id = Guid.NewGuid(), Name = "Required" });
        root.Inputs.Add(new DependencyInput { Id = Guid.NewGuid(), Name = "Optional", Optional = true });
        var g = Graph(r, root); Assert.Empty(Rank(g)); Assert.Single(g.Observations, o => o.Kind == ObservationKind.EmptyCache);
    }
    [Fact] public void ReferenceQuotaIndependentOfRepeatedNullsAndSharedPortReads()
    {
        var s = new InspectionSampling(); int reads = 0; var id = Guid.NewGuid();
        for (int n = 0; n < 500; n++) { s.TakeItem(); s.ReadOnce(id, facts => { reads++; facts.Add(new DiagnosticObservation()); }); }
        Assert.Equal(1, reads); Assert.Equal(8, s.ReferenceCapacity); s.ReferencesRead(192, true); Assert.Equal(0, s.ReferenceCapacity);
    }
    [Theory][InlineData(80)][InlineData(2000)]
    public void LargeGraphHonorsNodeWireAndWorkBudgets(int size)
    {
        var r = new Reader(); var root = r.Node("Root");
        for (int i = 0; i < size; i++) Wire(root, r.Node("N" + i));
        var before = root.Inputs.Select(p => p.Sources[0].OwnerId).ToArray();
        var watch = Stopwatch.StartNew(); var g = Graph(r, root); watch.Stop();
        Assert.True(g.Partial); Assert.InRange(r.Reads.Count, 1, 64); Assert.InRange(g.Connections.Count, 0, 128);
        Assert.Contains(g.Frontiers, f => f.Contains("limit") || f.Contains("budget"));
        Assert.Equal(before, root.Inputs.Select(p => p.Sources[0].OwnerId));
        output.WriteLine("Synthetic {0} sources: reads={1}, wires={2}, elapsedMs={3:F3}, graphMs={4}", size, r.Reads.Count, g.Connections.Count, watch.Elapsed.TotalMilliseconds, g.ElapsedMilliseconds);
    }
    [Fact] public void CancelledInspectionHasExplicitReasonAndNoUpstreamReads()
    {
        var r = new Reader(); var root = r.Node("Root"); Wire(root, r.Node("Source"));
        var token = new CancellationToken(true); var g = DependencyAnalysis.AnalyzeDetailed(r, root.Id, Guid.Empty, token).Graph;
        Assert.Single(r.Reads); Assert.True(g.Partial); Assert.Contains(g.Frontiers, f => f.Contains("cancelled"));
    }
    [Fact] public void ContinuationRejectsOldGenerationAndStaleSnapshot()
    {
        var doc = Guid.NewGuid(); var owner = Guid.NewGuid(); var graph = new GraphEvidence { RootId = owner };
        var report = new Report { DocumentId = doc, OwnerId = owner, Message = "Warning", Severity = "Warning", Context = new TutorContext { Component = "Root", Graph = graph } };
        var session = new TroubleshootingSession(report, new VerificationSnapshot { DocumentId = doc, OwnerId = owner }, "Disposable");
        var f = new InspectionFrontier { OwnerId = Guid.NewGuid(), CanContinue = true, Generation = Guid.NewGuid() }; graph.FrontierTargets.Add(f);
        var live = new Report { OwnerId = f.OwnerId, DocumentId = doc };
        Assert.False(session.FollowFrontier(live, f)); f.Generation = graph.Generation; graph.CapturedUtc = DateTime.UtcNow.AddHours(-1);
        Assert.False(session.FollowFrontier(live, f)); Assert.Same(report.Context.Graph, session.DisplayReport.Context.Graph);
    }
    [Fact] public void ContinuationCannotLoopAndRetainsOriginalBaseline()
    {
        var doc = Guid.NewGuid(); var owner = Guid.NewGuid(); var graph = new GraphEvidence { RootId = owner };
        var report = new Report { DocumentId = doc, OwnerId = owner, Message = "Warning", Severity = "Warning", Context = new TutorContext { Component = "Root", Graph = graph } };
        var session = new TroubleshootingSession(report, new VerificationSnapshot { DocumentId = doc, OwnerId = owner }, "Disposable"); var baseline = session.Baseline;
        var f = new InspectionFrontier { OwnerId = Guid.NewGuid(), CanContinue = true, Generation = graph.Generation }; graph.FrontierTargets.Add(f);
        graph.Observations.Add(new DiagnosticObservation { OwnerId = owner, Kind = ObservationKind.NullSample });
        var liveGraph = new GraphEvidence { RootId = f.OwnerId }; var again = new InspectionFrontier { OwnerId = f.OwnerId, CanContinue = true, Generation = liveGraph.Generation }; liveGraph.FrontierTargets.Add(again);
        Assert.True(session.FollowFrontier(new Report { OwnerId = f.OwnerId, DocumentId = doc, Context = new TutorContext { Graph = liveGraph } }, f));
        Assert.Null(session.NextFrontier); Assert.False(session.FollowFrontier(new Report { OwnerId = f.OwnerId, DocumentId = doc }, again));
        Assert.Same(baseline, session.Baseline); Assert.Equal(1, session.UniqueContinuationEvidence); Assert.Equal(1, session.Continuations);
    }
    [Fact] public void ContinuationsHaveSessionWideBoundAndFreshAllocationIdentity()
    {
        var doc = Guid.NewGuid(); var owner = Guid.NewGuid();
        var report = new Report { DocumentId = doc, OwnerId = owner, Context = new TutorContext { Graph = new GraphEvidence { RootId = owner } } };
        var session = new TroubleshootingSession(report, new VerificationSnapshot { DocumentId = doc, OwnerId = owner }, "Disposable");
        for (int i = 0; i < TroubleshootingSession.MaximumContinuations; i++)
        {
            var graph = session.DisplayReport.Context.Graph;
            var f = new InspectionFrontier { OwnerId = Guid.NewGuid(), CanContinue = true, Generation = graph.Generation }; graph.FrontierTargets.Add(f);
            var next = new GraphEvidence { RootId = f.OwnerId };
            Assert.NotEqual(graph.Generation, next.Generation);
            Assert.True(session.FollowFrontier(new Report { DocumentId = doc, OwnerId = f.OwnerId, Context = new TutorContext { Graph = next } }, f));
        }
        var final = session.DisplayReport.Context.Graph; final.FrontierTargets.Add(new InspectionFrontier { OwnerId = Guid.NewGuid(), CanContinue = true });
        Assert.Null(session.NextFrontier); Assert.Equal(8, session.Continuations); Assert.Equal(owner, session.Baseline.OwnerId);
    }
    [Fact] public void ContinuationRefusesDifferentLiveDocumentInstance()
    {
        var doc = Guid.NewGuid(); var owner = Guid.NewGuid(); var graph = new GraphEvidence { RootId = owner };
        var report = new Report { DocumentId = doc, OwnerId = owner, Context = new TutorContext { Graph = graph } };
        var session = new TroubleshootingSession(report, new VerificationSnapshot { DocumentId = doc, DocumentToken = "original", OwnerId = owner }, "Disposable");
        var f = new InspectionFrontier { OwnerId = Guid.NewGuid(), CanContinue = true, Generation = graph.Generation }; graph.FrontierTargets.Add(f);
        var other = new GraphEvidence { RootId = f.OwnerId, DocumentId = doc, DocumentToken = "reopened-document" };
        Assert.False(session.FollowFrontier(new Report { DocumentId = doc, OwnerId = f.OwnerId, Context = new TutorContext { Graph = other } }, f));
        Assert.Equal(0, session.Continuations);
    }
    [Fact] public void NodeAndWireLimitsProduceDifferentActualReasons()
    {
        foreach (int size in new[] { 80, 150 })
        {
            var r = new Reader(); var root = r.Node("Root"); for (int i = 0; i < size; i++) Wire(root, r.Node("Source"));
            var g = DependencyAnalysis.AnalyzeDetailed(r, root.Id, Guid.Empty, elapsedMilliseconds: () => 0).Graph;
            Assert.Contains(g.Frontiers, f => f.Contains("unique-node limit"));
            if (size > 128) Assert.Contains(g.Frontiers, f => f.Contains("wire budget"));
        }
    }
    [Fact] public void OrdinaryAndHardDepthStopReasonsAreNotInterchanged()
    {
        foreach (bool promising in new[] { false, true })
        {
            var r = new Reader(); var root = r.Node("Root"); var node = root;
            for (int i = 0; i < 30; i++) { var n = r.Node("N" + i); Wire(node, n, promising ? 0 : 1); node = n; }
            var g = Graph(r, root); Assert.Contains(g.Frontiers, f => f.Contains(promising ? "hard depth" : "ordinary branch depth"));
            var frontier = Assert.Single(g.FrontierTargets); Assert.Equal(promising ? 19 : 7, frontier.Depth); Assert.Equal(g.Generation, frontier.Generation);
        }
    }
    [Fact] public void PartialReferenceEvidenceCarriesCountsWithoutWholeParameterClaim()
    {
        var r = new Reader(); var root = r.Node("Root"); var curve = r.Node("Curve"); Wire(root, curve); Fault(curve, 2);
        curve.Inputs[0].Observations[0].Complete = false; curve.Inputs[0].References.Complete = false;
        var target = Rank(Graph(r, root))[0]; Assert.False(target.Issue.Observation.Complete);
        Assert.Equal(2, target.Issue.Observation.Count); Assert.Contains("partial sample", target.RankingReason);
    }
    [Fact] public void TimeBudgetReportsItsOwnActualStopReason()
    {
        var r = new Reader(); var root = r.Node("Root"); Wire(root, r.Node("Upstream"));
        var g = DependencyAnalysis.AnalyzeDetailed(r, root.Id, Guid.Empty, elapsedMilliseconds: () => 65).Graph;
        Assert.Single(r.Reads); Assert.Equal(65, g.ElapsedMilliseconds);
        Assert.Contains(g.Frontiers, f => f.Contains("graph time limit"));
        Assert.DoesNotContain(g.Frontiers, f => f.Contains("unique-node limit"));
    }
}

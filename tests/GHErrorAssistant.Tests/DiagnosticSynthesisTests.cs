using System;
using System.Collections.Generic;
using System.Linq;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class DiagnosticSynthesisTests
{
    public const string Kangaroo = "Solution exception:Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
    private sealed class Reader : IDependencyReader
    {
        internal readonly Dictionary<Guid, DependencyNode> Nodes = new Dictionary<Guid, DependencyNode>();
        public bool Limited { get; set; }
        internal int Reads;
        public DependencyNode Read(Guid id) { Reads++; return Nodes.TryGetValue(id, out var n) ? n : null; }
        internal DependencyNode Node(string name)
        {
            var n = new DependencyNode { Id = Guid.NewGuid(), Name = name }; Nodes[n.Id] = n; return n;
        }
    }
    private static DependencyInput Port(DependencyNode node, string name, string type = "", int count = 1)
    {
        var p = new DependencyInput { Id = Guid.NewGuid(), Name = name, TypeName = type, Count = count }; node.Inputs.Add(p); return p;
    }
    private static void Wire(DependencyInput input, DependencyNode source, int count = 1, string output = "Output") =>
        input.Sources.Add(new DependencyLink { OwnerId = source.Id, OutputId = Guid.NewGuid(), OwnerName = source.Name, OutputName = output, Count = count });
    private static void Warn(DependencyInput input, string message) => input.Observations.Add(DiagnosticObservation.Warning(message, "Warning"));
    private static TutorExplanation Explain(Reader reader, DependencyNode root, Guid focus = default(Guid), string message = Kangaroo)
    {
        var graph = DependencyAnalysis.AnalyzeDetailed(reader, root.Id, focus).Graph;
        return TutorEngine.Explain(message, new TutorContext { Component = root.Name, ComponentType = "Kangaroo2Component.KangarooGH3", Graph = graph });
    }
    private static Tuple<Reader, DependencyNode, DependencyNode, DependencyInput> Case()
    {
        // Explicit partial SDK fixture; do not rely on the former six-level cutoff.
        var r = new Reader { Limited = true }; var solver = r.Node("Solver"); var n = solver;
        foreach (string name in new[]{"Entwine", "Show", "Weaverbird subdivision", "Mesh WeldVertices", "Mesh Join", "Mesh FromPoints"})
        {
            var source = r.Node(name); Wire(Port(n, "Data"), source, 0); n = source;
        }
        var points = Port(n, "Points", "Point", 0); Warn(points, "Failed to collect data");
        var trim = r.Node("Trim Tree"); Wire(points, trim, 0, "Tree");
        r.Node("Unrelated point parameter").Evidence.Add("Error: unrelated invalid reference");
        return Tuple.Create(r, solver, n, points);
    }
    [Fact] public void ExactKangarooMessageUsesStructuredPointWarningAndTrimTreeConnection()
    {
        var fixture = Case(); var e = Explain(fixture.Item1, fixture.Item2);
        Assert.Equal("exception-collection-bounds", e.Kind);
        Assert.Contains("range of items", e.WhatsWrong); Assert.Contains("beyond", e.WhatsWrong);
        Assert.Contains("Mesh FromPoints / Points", e.WhatIFound); Assert.Contains("point data", e.WhatIFound);
        Assert.Contains("Trim Tree / Tree", e.WhatIFound);
        Assert.Contains("Mesh FromPoints / Points", e.Steps[0]); Assert.Contains("Trim Tree / Tree", e.Steps[1]);
        Assert.Contains("not proven to cause", e.WhatIFound); Assert.Contains("partial", e.WhatIFound);
        Assert.DoesNotContain("Unrelated point parameter", e.WhatIFound);
        Assert.DoesNotContain("goal count", e.WhatsWrong + e.WhatIFound + string.Join(" ", e.Steps));
        Assert.Equal(Kangaroo, e.Diagnostic.OriginalRuntimeMessage);
        Assert.Equal("Kangaroo2Component.KangarooGH3", e.Diagnostic.ComponentType);
        Assert.Equal(fixture.Item2.Id, e.Diagnostic.ComponentId);
        Assert.Equal(RuntimeCategory.CollectionBounds, e.Diagnostic.ErrorCategory);
        Assert.Equal("Mesh FromPoints", e.Diagnostic.CandidateUpstreamIssues[0].Component);
        Assert.Equal("Points", e.Diagnostic.CandidateUpstreamIssues[0].Parameter);
        Assert.Equal(6, e.Diagnostic.CandidateUpstreamIssues[0].Path.Count);
        Assert.True(e.Diagnostic.Graph.Partial);
        Assert.InRange(e.Steps.Count, 3, 5);
    }
    [Fact] public void CopyReportKeepsExactOriginalAndPrimaryEvidence()
    {
        var f = Case(); var context = new TutorContext { Component = "Solver", Graph = DependencyAnalysis.AnalyzeDetailed(f.Item1, f.Item2.Id, Guid.Empty).Graph };
        var report = new Report { Name = "Solver", Severity = "Error", Message = Kangaroo, Context = context };
        Assert.Contains("ORIGINAL GRASSHOPPER MESSAGE\n" + Kangaroo + "\n\nCONFIRMED", report.Text());
        Assert.Contains("WHAT I FOUND\nMesh FromPoints / Points", report.Text());
        Assert.Contains("IF THAT DOESN'T WORK", report.Text()); Assert.Equal(Kangaroo, report.Message);
    }
    [Theory]
    [InlineData("System.IndexOutOfRangeException: Index was outside the bounds of the array.", "exception-index-out-of-range")]
    [InlineData("ArgumentOutOfRangeException: argument must be non-negative", "exception-argument-out-of-range")]
    [InlineData("Specified argument was out of the range of valid values.", "exception-argument-out-of-range")]
    [InlineData("NullReferenceException: Object reference not set to an instance of an object.", "exception-null-reference")]
    [InlineData("InvalidCastException: Specified cast is not valid.", "exception-invalid-cast")]
    [InlineData("InvalidOperationException: Operation is not valid due to the current state of the object.", "exception-invalid-operation")]
    [InlineData("Solution exception:Sequence contains no elements", "exception-invalid-operation")]
    public void RecognizedExceptionsAreExplainedWithoutInventingAnInputCause(string message, string kind)
    {
        var e = TutorEngine.Explain(message, new TutorContext { Component = "Solver" });
        Assert.Equal(kind, e.Kind); Assert.Contains("Solver", e.WhatsWrong);
        Assert.DoesNotContain("missing geometry caused", e.WhatIFound);
        Assert.Contains("does not", e.Why); Assert.InRange(e.Steps.Count, 3, 5);
        Assert.Contains("root cause remains unknown", e.Evidence);
    }
    [Fact] public void SpecificCollectionWarningBeatsDeeperGenericExceptionAndEmptyCaches()
    {
        var r = new Reader(); var root = r.Node("Solver"); var mesh = r.Node("Mesh FromPoints"); var trim = r.Node("Trim Tree"); var generic = r.Node("Deep generic component");
        Wire(Port(root, "Goals"), mesh, 0);
        var points = Port(mesh, "Points", "Point", 0); Warn(points, "Failed to collect data"); Wire(points, trim, 0);
        Wire(Port(trim, "Data"), generic, 0); generic.Observations.Add(DiagnosticObservation.Warning("Solution exception:Index was outside the bounds of the array", "Error"));
        var e = Explain(r, root);
        Assert.Equal("Mesh FromPoints", e.Diagnostic.CandidateUpstreamIssues[0].Component);
        Assert.Contains("Mesh FromPoints / Points", e.Steps[0]); Assert.DoesNotContain("Deep generic component", e.Steps[0]);
    }
    [Fact] public void EquallySpecificWarningsPreferEarlierObservedIssueOnThePath()
    {
        var r = new Reader(); var root = r.Node("Solver"); var downstream = r.Node("Downstream"); var earlier = r.Node("Earlier");
        Wire(Port(root, "Goals"), downstream); var p = Port(downstream, "Points"); Warn(p, "Failed to collect data"); Wire(p, earlier);
        Warn(Port(earlier, "Points"), "Failed to collect data");
        Assert.Equal("Earlier", Explain(r, root).Diagnostic.CandidateUpstreamIssues[0].Component);
    }
    [Fact] public void ReferenceWarningBeatsGenericDownstreamSymptom()
    {
        var r = new Reader(); var root = r.Node("Solver"); var near = r.Node("Mesh Join"); var curve = r.Node("Curve parameter");
        Wire(Port(root, "Goals"), near); near.Observations.Add(DiagnosticObservation.Warning("Solution exception:out of bounds", "Error"));
        Wire(Port(near, "Geometry"), curve); Warn(Port(curve, "Curve", "Curve"), "Reference is invalid");
        var e = Explain(r, root); Assert.Contains("Curve parameter / Curve", e.Steps[0]);
        Assert.Contains("reference warning", e.WhatIFound); Assert.DoesNotContain("was deleted", e.WhatsWrong + e.WhatIFound);
    }
    [Fact] public void InputFocusExcludesWarningOnAnotherRootBranch()
    {
        var r = new Reader(); var root = r.Node("Solver"); var mesh = r.Node("Relevant mesh"); var other = r.Node("Other branch");
        var focus = Port(root, "A"); Wire(focus, mesh); Wire(Port(root, "B"), other);
        Warn(Port(mesh, "Points", "Point"), "Failed to collect data"); Warn(Port(other, "Reference"), "Reference is invalid");
        var e = Explain(r, root, focus.Id);
        Assert.Contains("Relevant mesh", e.WhatIFound); Assert.DoesNotContain("Other branch", e.WhatIFound + string.Join(" ", e.Steps));
    }
    [Fact] public void PartialInspectionNeverClaimsGlobalEarliestCause()
    {
        var f = Case(); f.Item1.Limited = true; var e = Explain(f.Item1, f.Item2);
        Assert.Contains("partial", e.WhatIFound); Assert.Contains(e.Diagnostic.Uncertainty, s => s.Contains("every branch"));
        Assert.DoesNotContain("confirmed cause", e.WhatsWrong + e.WhatIFound);
    }
    [Fact] public void UnknownThirdPartyExceptionStillUsesActionableUpstreamEvidence()
    {
        var f = Case(); var e = Explain(f.Item1, f.Item2, message: "Solution exception:Vendor XY-42 failed");
        Assert.Equal(RuntimeCategory.Unknown, e.Diagnostic.ErrorCategory);
        Assert.Contains("upstream warning is worth checking", e.WhatsWrong);
        Assert.Contains("Mesh FromPoints / Points", e.WhatIFound); Assert.Contains("not proven", e.WhatIFound);
    }
    [Fact] public void UnknownWithoutEvidenceRemainsUnknown()
    {
        var e = TutorEngine.Explain("Solution exception:Vendor XY-42 failed", new TutorContext { Component = "Vendor" });
        Assert.Equal("unknown", e.Kind); Assert.Empty(e.Diagnostic.CandidateUpstreamIssues);
        Assert.Contains("not enough verified evidence", e.Why);
    }
    [Fact] public void EmptyNonFailingSourceIsNotPromotedToAnUpstreamDiagnosis()
    {
        var r = new Reader(); var root = r.Node("Solver"); var source = r.Node("Empty but valid source"); Wire(Port(root, "Goals"), source, 0);
        var e = Explain(r, root);
        Assert.Empty(e.Diagnostic.CandidateUpstreamIssues); Assert.Contains("emptiness alone", e.WhatIFound);
        Assert.DoesNotContain("Empty but valid source", string.Join(" ", e.Steps));
    }
    [Fact] public void CycleCannotTurnTheSelectedExceptionIntoItsOwnUpstreamCause()
    {
        var r = new Reader(); var root = r.Node("Solver"); var upstream = r.Node("Upstream");
        Wire(Port(root, "Goals"), upstream); var back = Port(upstream, "Feedback"); Wire(back, root);
        back.Sources[0].Observations.Add(DiagnosticObservation.Warning(Kangaroo, "Error"));
        var e = Explain(r, root); Assert.True(e.Diagnostic.Graph.Cycle);
        Assert.DoesNotContain(e.Diagnostic.CandidateUpstreamIssues, i => i.ComponentId == root.Id);
        Assert.True(r.Reads <= 2); Assert.Contains("cycle", e.WhatIFound);
    }
    [Fact] public void ForgedCandidateWithoutVerifiedPathIsIgnored()
    {
        var r = new Reader(); var root = r.Node("Solver"); var graph = DependencyAnalysis.AnalyzeDetailed(r, root.Id, Guid.Empty).Graph;
        var fake = new UpstreamIssue { ComponentId = Guid.NewGuid(), Component = "Unconnected", Parameter = "Points", Depth = 1,
            Observation = DiagnosticObservation.Warning("Failed to collect data", "Warning") };
        fake.Path.Add(new VerifiedConnection { SourceId = fake.ComponentId, TargetId = root.Id, SourceComponent = "Unconnected", SourcePort = "Out", TargetComponent = root.Name, TargetPort = "Goals" });
        graph.Issues.Add(fake);
        var e = TutorEngine.Explain(Kangaroo, new TutorContext { Component = "Solver", Graph = graph });
        Assert.Empty(e.Diagnostic.CandidateUpstreamIssues); Assert.DoesNotContain("Unconnected", e.WhatIFound);
    }
    [Fact] public void UnverifiedFeederNameIsNeverShownAsAConnection()
    {
        var f = Case(); var graph = DependencyAnalysis.AnalyzeDetailed(f.Item1, f.Item2.Id, Guid.Empty).Graph;
        var primary = graph.RankedUpstream()[0]; primary.Feeders.Clear();
        primary.Feeders.Add(new VerifiedConnection { SourceId = Guid.NewGuid(), TargetId = primary.ComponentId, SourceComponent = "Invented feeder", SourcePort = "Out", TargetComponent = primary.Component, TargetPort = primary.Parameter });
        var e = TutorEngine.Explain(Kangaroo, new TutorContext { Component = "Solver", Graph = graph });
        Assert.DoesNotContain("Invented feeder", e.WhatIFound + string.Join(" ", e.Steps));
    }
    [Fact] public void ObservationAndCandidateLimitsAreExplicit()
    {
        var r = new Reader(); var root = r.Node("Solver"); var source = r.Node("Many warnings"); Wire(Port(root, "Goals"), source);
        for (int i = 0; i < 700; i++) source.Observations.Add(DiagnosticObservation.Warning("Failed to collect data " + i, "Warning"));
        var e = Explain(r, root);
        Assert.True(e.Diagnostic.Graph.Partial); Assert.True(e.Diagnostic.Graph.Observations.Count <= 512); Assert.True(e.Diagnostic.Graph.Issues.Count <= 128);
    }
}


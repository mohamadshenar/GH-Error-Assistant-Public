using System;
using System.Collections.Generic;
using System.Linq;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class BeginnerDiagnosisTests
{
    private const string Kangaroo = "Solution exception:Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
    private sealed class Reader : IDependencyReader
    {
        internal readonly Dictionary<Guid, DependencyNode> Nodes = new Dictionary<Guid, DependencyNode>();
        public bool Limited { get; set; }
        public DependencyNode Read(Guid id) => Nodes.TryGetValue(id, out var n) ? n : null;
        internal DependencyNode Add(string name) { var n = new DependencyNode { Id = Guid.NewGuid(), Name = name }; Nodes.Add(n.Id, n); return n; }
    }
    private static DependencyInput Input(DependencyNode n, string name, string type = "", int count = 1)
    { var p = new DependencyInput { Id = Guid.NewGuid(), Name = name, TypeName = type, Count = count }; n.Inputs.Add(p); return p; }
    private static DependencyLink Wire(DependencyInput input, DependencyNode source, string output = "Result", int count = 1)
    { var link = new DependencyLink { OwnerId = source.Id, OwnerName = source.Name, OutputId = Guid.NewGuid(), OutputName = output, Count = count }; input.Sources.Add(link); return link; }
    private static void Warn(DependencyInput input) => input.Observations.Add(DiagnosticObservation.Warning("Failed to collect data", "Warning"));
    private static DiagnosticObservation Null() => new DiagnosticObservation { Kind = ObservationKind.NullSample, Message = "Observed null sample" };
    private static TutorExplanation Explain(Reader r, DependencyNode root, string message = Kangaroo, Guid focus = default(Guid)) =>
        TutorEngine.Explain(message, new TutorContext { Component = root.Name, OwnerId = root.Id, Graph = DependencyAnalysis.AnalyzeDetailed(r, root.Id, focus).Graph });
    private static BeginnerGuidance Guide(Reader r, DependencyNode root) => Explain(r, root).Guidance;
    private static Tuple<Reader, DependencyNode> RealCase()
    {
        // Explicit partial SDK fixture; do not rely on the former six-level cutoff.
        var r = new Reader { Limited = true }; var solver = r.Add("Solver"); var goal = Input(solver, "GoalObjects"); goal.Observations.Add(Null());
        var entwine = r.Add("Entwine"); Wire(goal, entwine).Observations.Add(Null());
        var show = r.Add("Show"); Wire(Input(entwine, "Data"), show, "G").Observations.Add(Null());
        var n = show;
        foreach (var name in new[]{"Weaverbird", "Mesh WeldVertices", "Mesh Join", "Mesh FromPoints"})
        {
            var source = r.Add(name); var p = Input(n, "Geometry"); Warn(p); Wire(p, source, count:0); n = source;
        }
        var points = Input(n, "Points", "Point", 0); Warn(points); Wire(points, r.Add("Trim Tree"), "Tree", 0);
        var naked = r.Add("NakedVertices"); var edges = r.Add("EdgeLengths");
        var edgeInput = Input(edges, "Mesh", "Mesh", 0); Warn(edgeInput); Wire(edgeInput, naked);
        var nakedInput = Input(naked, "Mesh", "Mesh", 0); Warn(nakedInput);
        Wire(Input(solver, "OtherGoalData"), edges);
        return Tuple.Create(r, solver);
    }
    [Fact] public void KangarooStartsWithGoalDataAndRetainsDistinctMeshAndSampleTargets()
    {
        var f = RealCase(); var e = Explain(f.Item1, f.Item2); var g = e.Guidance;
        Assert.Equal("Solver / GoalObjects", g.Targets[0].Location);
        Assert.Equal("Mesh FromPoints / Points", g.Targets[1].Location);
        Assert.Contains(g.Targets, t => t.Location == "Entwine / Result");
        Assert.Contains(g.Targets, t => t.Location == "Show / G");
        Assert.Contains(g.Targets, t => t.Location == "Mesh Join / Geometry");
        Assert.Contains("sampled NullSample", g.GroupedEvidence);
        Assert.Contains("recorded Warning", g.GroupedEvidence);
        Assert.Contains("missing item", g.StartHere); Assert.Contains("Trim Tree / Tree", g.Checks.Single(x => x.Target.Location == "Mesh FromPoints / Points").Instruction);
        Assert.Contains("Entwine / Result", string.Join(" ", g.Steps));
        Assert.Contains("does not prove", g.Why); Assert.Contains("Only part", g.Why);
        Assert.DoesNotContain("because Mesh", g.Wrong); Assert.InRange(g.Steps.Count, 3, 5);
        Assert.Equal(Kangaroo, e.Diagnostic.OriginalRuntimeMessage);
        string main = g.Wrong + g.StartHere + g.Why + string.Join(" ", g.Steps) + g.Next;
        foreach (var jargon in new[]{"array", "null", "collection bounds", "cached", "exception", "goal count"}) Assert.DoesNotContain(jargon, main, StringComparison.OrdinalIgnoreCase);
        Assert.True(g.Targets.Count > 2);
    }
    [Fact] public void DirectMissingItemBeatsDistantRuntimeWarning()
    {
        var r = new Reader(); var root = r.Add("Root"); var p = Input(root, "Values"); p.Observations.Add(Null());
        var upstream = r.Add("Upstream"); Wire(p, upstream); Warn(Input(upstream, "Points", "Point", 0));
        Assert.Equal(root.Id, Guide(r, root).Targets[0].OwnerId);
    }
    [Fact] public void OutputMissingItemIsObservationNotClaimThatAllOutputFailed()
    {
        var r = new Reader(); var root = r.Add("Root"); var source = r.Add("Source"); Wire(Input(root, "A"), source).Observations.Add(Null());
        var g = Guide(r, root); Assert.Contains("Other items may still be usable", g.StartHere); Assert.DoesNotContain("no usable data", g.StartHere);
    }
    [Fact] public void NoWarningAndEmptyOutputDoesNotCreateTarget()
    {
        var r = new Reader(); var root = r.Add("Root"); Wire(Input(root, "A"), r.Add("Intentionally empty"), count:0);
        Assert.Empty(Guide(r, root).Targets);
        Assert.Equal(root.Id, Guide(r, root).LocateTargets.Single().OwnerId);
    }
    [Fact] public void DuplicateOwnerAndParameterWarningsProduceOneTargetWithFullTrace()
    {
        var r = new Reader(); var root = r.Add("Root"); var upstream = r.Add("Mesh"); Wire(Input(root, "A"), upstream);
        var p = Input(upstream, "Points", "Point", 0); Warn(p); Warn(p); upstream.Observations.Add(DiagnosticObservation.Warning("Failed to collect data", "Warning"));
        var e = Explain(r, root); Assert.Single(e.Guidance.Targets); Assert.Equal("Mesh / Points", e.Guidance.Targets[0].Location);
        Assert.True(e.Diagnostic.Graph.Observations.Count >= 3);
    }
    [Fact] public void ConnectedWarningsOnSamePathRetainDistinctActionableTargets()
    {
        var r = new Reader(); var root = r.Add("Root"); var near = r.Add("Near"); var far = r.Add("Far");
        Wire(Input(root, "A"), near); var a = Input(near, "In", count:0); Warn(a); Wire(a, far); Warn(Input(far, "In", count:0));
        var g = Guide(r, root); Assert.Equal(2, g.Targets.Count); Assert.Equal("Far / In", g.Targets[0].Location);
        Assert.Equal("Near / In", g.Targets[1].Location); Assert.Single(g.Targets[0].Evidence);
    }
    [Fact] public void FocusedBranchExcludesUnrelatedWarning()
    {
        var r = new Reader(); var root = r.Add("Root"); var a = Input(root, "A"); Wire(a, r.Add("Good"));
        var bad = r.Add("Unrelated"); Wire(Input(root, "B"), bad); Warn(Input(bad, "Points", "Point", 0));
        Assert.DoesNotContain("Unrelated", Explain(r, root, focus:a.Id).Guidance.StartHere);
    }
    [Fact] public void CyclesDoNotMakeRootAnUpstreamCause()
    {
        var r = new Reader(); var root = r.Add("Root"); var a = r.Add("A"); Wire(Input(root, "Input"), a); Wire(Input(a, "Input"), root);
        var g = Guide(r, root); Assert.Contains("loop back", g.Why); Assert.Empty(g.Targets);
    }
    [Fact] public void UnknownThirdPartyMessageKeepsUncertainty()
    {
        var r = new Reader(); var root = r.Add("ThirdParty"); var g = Explain(r, root, "Unexpected gizmo state 42").Guidance;
        Assert.Contains("cause is still unknown", g.Wrong); Assert.InRange(g.Steps.Count, 3, 5);
    }
    [Fact] public void ConnectedEmptyInputPointsToExistingSourceNotAnotherWire()
    {
        var c = new TutorContext { Component = "Receiver", Parameter = "Points", ParameterType = "Point", Direction = "input", ItemCount = 0, SourcesKnown = true };
        c.Sources.Add(new TutorSource { Component = "Point source", Parameter = "Points", ItemCount = 0 });
        var g = TutorEngine.Explain("Failed to collect data", c).Guidance;
        Assert.Contains("Point source / Points", g.Steps[1]); Assert.Contains("before adding another wire", g.Steps[1]);
        Assert.Contains("Points are locations", g.Steps[0]);
    }
    [Fact] public void MissingUnwiredInputSuggestsSuitableData()
    {
        var c = new TutorContext { Component = "Receiver", Parameter = "Value", ParameterType = "Number", Direction = "input", ItemCount = 0, SourcesKnown = true };
        var g = TutorEngine.Explain("Failed to collect data", c).Guidance;
        Assert.Contains("Number Slider", g.Steps[0]); Assert.Contains("numbers", g.Steps[1]); Assert.DoesNotContain("Rhino object", string.Join(" ", g.Steps));
    }
    [Fact] public void UnassignedFloatingPointCanAssignRhinoGeometry()
    {
        var c = new TutorContext { Component = "Pt", Parameter = "Point", ParameterType = "Point", Floating = true, ItemCount = 0,
            AssignmentKnown = true, HasAssignedData = false, SourcesKnown = true };
        var g = TutorEngine.Explain("Failed to collect data", c).Guidance;
        Assert.Contains("Right-click", g.Steps[1]); Assert.Contains("Rhino object", g.Steps[1]);
    }
    [Fact] public void AssignedFloatingParameterDoesNotGetUnassignedInstructions()
    {
        var r = new Reader(); var root = r.Add("Root"); var pt = r.Add("Pt"); pt.Floating = true; Wire(Input(root, "A"), pt);
        var p = Input(pt, "Point", "Point", 0); p.AssignmentKnown = true; p.HasAssignedData = true; Warn(p);
        var g = Guide(r, root); Assert.Contains("already assigned", string.Join(" ", g.Steps)); Assert.DoesNotContain("select the intended Rhino object", string.Join(" ", g.Steps));
    }
    [Fact] public void InvalidReferenceDoesNotClaimDeletion()
    {
        var r = new Reader(); var root = r.Add("Root"); var p = Input(root, "Curve", "Curve");
        p.Observations.Add(new DiagnosticObservation { Kind = ObservationKind.MissingReferenceInActiveDocument, Message = "Reference not found" });
        var g = Guide(r, root); Assert.Contains("another document", g.StartHere); Assert.Contains("does not prove it was deleted", g.Steps[0]);
    }
    [Fact] public void VerifiedConversionKeepsActualTypesWithoutInventingThem()
    {
        var c = new TutorContext { Component = "Receiver", Parameter = "Surface", ParameterType = "Surface", Direction = "input" };
        var g = TutorEngine.Explain("Data conversion failed from Point to Surface", c).Guidance;
        Assert.Contains("point data into surface data", g.Wrong); Assert.Contains("surface data", string.Join(" ", g.Steps));
        var unknown = TutorEngine.Explain("Data conversion failed", c).Guidance;
        Assert.DoesNotContain("point", unknown.Wrong, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void SourceSamplesDoNotBecomeConfirmedReceivingInputObservations()
    {
        var c = new TutorContext { Component = "Root", Parameter = "A", Direction = "input", ItemCount = 1 };
        c.Sources.Add(new TutorSource { Component = "Source", HasNullItem = true });
        var g = TutorEngine.Explain(Kangaroo, c).Guidance; Assert.Empty(g.Targets);
    }
    [Fact] public void ContextDirectSampleIsUsedEvenWhenGraphReadWasPartial()
    {
        var root = Guid.NewGuid(); var parameter = Guid.NewGuid();
        var c = new TutorContext { Component = "Root", OwnerId = root, ParameterId = parameter, Parameter = "A", Direction = "input",
            HasNullItem = true, Graph = new GraphEvidence { RootId = root, Partial = true } };
        var g = TutorEngine.Explain(Kangaroo, c).Guidance;
        Assert.Equal("Root / A", g.Targets[0].Location); Assert.Contains("Only part", g.Why);
        c.Graph = null; c.OwnerId = Guid.Empty; c.ParameterId = Guid.Empty;
        Assert.DoesNotContain("wire path", TutorEngine.Explain(Kangaroo, c).Guidance.Why, StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("Point", "Points are locations")]
    [InlineData("Curve", "Curves are lines")]
    [InlineData("Surface", "Surfaces are areas")]
    [InlineData("Mesh", "vertices and connected faces")]
    [InlineData("Number", "Number Slider")]
    [InlineData("Custom third-party", "input's description")]
    public void RecognizableDataTypesGetSafeMeanings(string type, string expected) => Assert.Contains(expected, BeginnerDiagnosis.TypeMeaning(type));
    [Fact] public void CopyHasBeginnerGuidanceThenVerbatimOriginal()
    {
        var report = new Report { Name = "Solver", Severity = "Error", Message = Kangaroo };
        Assert.StartsWith("BEGINNER GUIDANCE", report.Text()); Assert.Contains("START HERE", report.Text());
        Assert.Contains("ORIGINAL GRASSHOPPER MESSAGE\n" + Kangaroo + "\n\nCONFIRMED", report.Text());
    }
    [Fact] public void LocateOnlyAcceptsSameLiveDocumentAndExistingObjects()
    {
        var doc = Guid.NewGuid(); var owner = Guid.NewGuid();
        Assert.True(LocatePolicy.CanLocate(doc, doc, owner, true, true, true, true));
        Assert.False(LocatePolicy.CanLocate(doc, doc, owner, true, true, true, true, false));
        Assert.False(LocatePolicy.CanLocate(doc, Guid.NewGuid(), owner, true, true, true, true));
        Assert.False(LocatePolicy.CanLocate(doc, doc, owner, false, true, true, true));
        Assert.False(LocatePolicy.CanLocate(doc, doc, owner, true, false, true, true));
        Assert.False(LocatePolicy.CanLocate(doc, doc, owner, true, true, false, true));
        Assert.False(LocatePolicy.CanLocate(doc, doc, owner, true, true, true, false));
        Assert.False(LocatePolicy.CanLocate(doc, doc, Guid.Empty, true, true, true, true));
        Assert.False(LocatePolicy.CanLocate(Guid.Empty, Guid.Empty, owner, true, true, true, true));
        Assert.True(LocatePolicy.ConfirmedMissingTarget(doc, doc, owner, true, false));
        Assert.False(LocatePolicy.ConfirmedMissingTarget(doc, Guid.NewGuid(), owner, true, false));
        Assert.False(LocatePolicy.ConfirmedMissingTarget(doc, doc, owner, false, false));
        Assert.False(LocatePolicy.ConfirmedMissingTarget(doc, doc, owner, true, true));
    }
}



using System;
using System.Collections.Generic;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class DependencyTests
{
    private sealed class Reader : IDependencyReader
    {
        internal readonly Dictionary<Guid, DependencyNode> Nodes = new Dictionary<Guid, DependencyNode>();
        internal int Reads;
        public bool Limited { get; set; }
        public DependencyNode Read(Guid id) { Reads++; return Nodes.TryGetValue(id, out var n) ? n : null; }
        internal DependencyNode Add(string name)
        {
            var n = new DependencyNode { Id = Guid.NewGuid(), Name = name }; Nodes[n.Id] = n; return n;
        }
    }
    private static DependencyInput Input(DependencyNode node, string name, int count = 0, bool optional = false)
    {
        var input = new DependencyInput { Id = Guid.NewGuid(), Name = name, Count = count, Optional = optional };
        node.Inputs.Add(input); return input;
    }
    private static void Wire(DependencyInput target, DependencyNode source, int count = 0) =>
        target.Sources.Add(new DependencyLink { OwnerId = source.Id, OwnerName = source.Name, OutputName = "Output", Count = count });
    [Fact] public void RealWireChainPrioritizesUpstreamAndOmitsUnconnectedObjects()
    {
        var r = new Reader(); var pop = r.Add("Pop2D"); var cross = r.Add("CrossRef"); var pt = r.Add("Pt"); var crv = r.Add("Crv"); r.Add("Unconnected");
        Wire(Input(pop, "Region"), cross); Wire(Input(cross, "A"), pt); Wire(Input(cross, "B"), crv);
        Input(pt, "Point data").Observations.Add(DiagnosticObservation.Warning("Failed to collect data", "Warning"));
        Input(crv, "Curve data").Observations.Add(new DiagnosticObservation { Kind = ObservationKind.MissingReferenceInActiveDocument, Message = "A geometry reference ID was not found in the active Rhino document; deletion is not confirmed." });
        var text = DependencyAnalysis.Analyze(r, pop.Id, Guid.Empty);
        Assert.Contains("Pt / Output → CrossRef / A", text);
        Assert.Contains("CrossRef / Output → Pop2D / Region", text);
        Assert.Contains("geometry reference", text);
        Assert.DoesNotContain("Unconnected", text);
        Assert.Contains("not a confirmed root cause", text);
        var actions = text.Substring(text.IndexOf("Recommended first action", StringComparison.Ordinal));
        Assert.Contains("Pt / Point data", actions);
        Assert.Contains("Inspect Pt / Point data.", actions);
        Assert.DoesNotContain("confirmed cause", actions);
    }
    [Fact] public void FocusedInputDoesNotAccuseOtherBranches()
    {
        var r = new Reader(); var root = r.Add("Root"); var left = r.Add("Left"); var other = r.Add("Unrelated branch");
        var input = Input(root, "A"); Wire(input, left); Wire(Input(root, "B"), other);
        other.Evidence.Add("A warning on another branch");
        var text = DependencyAnalysis.Analyze(r, root.Id, input.Id);
        Assert.Contains("Left", text); Assert.DoesNotContain("Unrelated branch", text);
    }
    [Fact] public void CyclesTerminate()
    {
        var r = new Reader(); var a = r.Add("A"); var b = r.Add("B"); Wire(Input(a, "in"), b); Wire(Input(b, "in"), a);
        Assert.Contains("cyclic", DependencyAnalysis.Analyze(r, a.Id, Guid.Empty)); Assert.True(r.Reads <= 2);
    }
    [Fact] public void DepthLimitIsExplicit()
    {
        var r = new Reader(); var root = r.Add("Root"); var n = root;
        for (int i = 0; i < 20; i++) { var source = r.Add("Node" + i); Wire(Input(n, "in"), source); n = source; }
        Assert.Contains("Inspection is partial", DependencyAnalysis.Analyze(r, root.Id, Guid.Empty));
        Assert.True(r.Reads <= DependencyAnalysis.MaxDepth + 1);
    }
    [Fact] public void NodeAndEdgeWorkIsBounded()
    {
        var r = new Reader(); var root = r.Add("Root"); var input = Input(root, "in");
        for (int i = 0; i < 300; i++) Wire(input, r.Add("Source" + i));
        Assert.Contains("Inspection is partial", DependencyAnalysis.Analyze(r, root.Id, Guid.Empty));
        Assert.True(r.Reads <= DependencyAnalysis.MaxNodes);
    }
    [Fact] public void OptionalEmptyInputIsNotDiagnosedAsMissingRequiredData()
    {
        var r = new Reader(); var root = r.Add("Root"); Input(root, "Optional", 0, true);
        var text = DependencyAnalysis.Analyze(r, root.Id, Guid.Empty);
        Assert.DoesNotContain("no connected sources and zero cached items", text);
        Assert.Contains("no specific upstream fault", text);
    }
    [Fact] public void ClusterBoundaryIsOpaque()
    {
        var r = new Reader(); var root = r.Add("Cluster"); root.Boundary = true;
        Wire(Input(root, "in"), r.Add("Hidden internals"));
        var text = DependencyAnalysis.Analyze(r, root.Id, Guid.Empty);
        Assert.DoesNotContain("Hidden internals", text); Assert.Contains("Inspection is partial", text);
    }
    [Fact] public void DeletedRootIsHandled() => Assert.Contains("no longer", DependencyAnalysis.Analyze(new Reader(), Guid.NewGuid(), Guid.Empty));
    [Fact] public void PartialSourceInspectionIsNotReportedAsDisconnected()
    {
        var r = new Reader { Limited = true }; var root = r.Add("Root"); Input(root, "in").SourcesInspected = false;
        var text = DependencyAnalysis.Analyze(r, root.Id, Guid.Empty);
        Assert.DoesNotContain("no connected sources", text); Assert.Contains("Inspection is partial", text);
    }
    [Fact] public void HealthyCachedSourceHasNoInventedFailure()
    {
        var r = new Reader(); var root = r.Add("Root"); var source = r.Add("Healthy source"); Wire(Input(root, "in", 5), source, 5);
        var text = DependencyAnalysis.Analyze(r, root.Id, Guid.Empty);
        Assert.DoesNotContain("zero cached", text); Assert.Contains("no specific upstream fault", text);
    }
    [Fact] public void ComponentWarningIncludesVerifiedObjectPath()
    {
        var r = new Reader(); var root = r.Add("Pop2D"); var cross = r.Add("CrossRef"); var pt = r.Add("Pt");
        Wire(Input(root, "in"), cross); Wire(Input(cross, "in"), pt); pt.Evidence.Add("Warning: unavailable data");
        Assert.Contains("Pt → CrossRef → Pop2D", DependencyAnalysis.Analyze(r, root.Id, Guid.Empty));
    }
}

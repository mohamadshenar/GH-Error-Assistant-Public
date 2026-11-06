using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;

internal static class AuditScenarioFixtures
{
    private static int serial;
    private static Guid Id() { var b = new byte[16]; BitConverter.GetBytes(++serial).CopyTo(b, 0); return new Guid(b); }
    private sealed class Reader : IDependencyReader
    {
        internal readonly Dictionary<Guid, DependencyNode> Nodes = new Dictionary<Guid, DependencyNode>();
        public bool Limited { get; set; }
        public DependencyNode Read(Guid id) => Nodes.TryGetValue(id, out var n) ? n : null;
        internal DependencyNode Add(string name) { var n = new DependencyNode { Id = Id(), Name = name }; Nodes.Add(n.Id, n); return n; }
    }
    private static DependencyInput Port(DependencyNode n, string name, string type = "", int count = 1)
    { var p = new DependencyInput { Id = Id(), Name = name, TypeName = type, Count = count }; n.Inputs.Add(p); return p; }
    private static DependencyLink Wire(DependencyInput p, DependencyNode source, string output, int count = 1)
    { var l = new DependencyLink { OwnerId = source.Id, OwnerName = source.Name, OutputId = Id(), OutputName = output, Count = count }; p.Sources.Add(l); return l; }
    private static void Warn(DependencyInput p) => p.Observations.Add(DiagnosticObservation.Warning("Input parameter " + p.Name + " failed to collect data", "Warning"));
    private static DiagnosticObservation Missing() => new DiagnosticObservation { Kind = ObservationKind.NullSample, Message = "A sampled cached/persistent data item is null." };
    private static TutorContext Context(string component, string parameter, string type, int count = 0) => new TutorContext { Component = component,
        OwnerId = Id(), ParameterId = Id(), Parameter = parameter, ParameterType = type, Direction = "input", ItemCount = count, SourcesKnown = true };
    internal sealed record Case(string Id, string Setup, string Original, TutorContext Context);
    internal static List<Case> Cases()
    {
        var cases = new List<Case>();
        var r = new Reader(); var solver = r.Add("Solver"); var goal = Port(solver, "GoalObjects"); goal.Observations.Add(Missing());
        var entwine = r.Add("Entwine"); Wire(goal, entwine, "Result").Observations.Add(Missing());
        var show = r.Add("Show"); Wire(Port(entwine, "Data"), show, "G").Observations.Add(Missing());
        var n = show;
        foreach (var name in new[]{"Weaverbird", "Mesh WeldVertices", "Mesh Join", "Mesh FromPoints"})
        { var source = r.Add(name); var p = Port(n, "Geometry"); Warn(p); Wire(p, source, "Result", 0); n = source; }
        var points = Port(n, "Points", "Point", 0); Warn(points); Wire(points, r.Add("Trim Tree"), "Tree", 0);
        var naked = r.Add("NakedVertices"); var edges = r.Add("EdgeLengths"); var ep = Port(edges, "Mesh", "Mesh", 0); Warn(ep); Wire(ep, naked, "NakedPts", 0); Warn(Port(naked, "Mesh", "Mesh", 0)); Wire(Port(solver, "OtherGoalData"), edges, "EL", 0);
        cases.Add(new Case("K0", "Synthetic scalar fixture based on the existing Kangaroo regression. Root missing sample, Entwine/Show samples, connected mesh warnings; depth-limited. No Rhino execution or causation proof.",
            "Solution exception:Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.",
            new TutorContext { Component = "Solver", OwnerId = solver.Id, ComponentType = "Kangaroo2Component.KangarooGH3", Graph = DependencyAnalysis.AnalyzeDetailed(r, solver.Id, Guid.Empty).Graph }));
        var c = Context("Point (Pt)", "Point", "Point"); c.Floating = true; c.Direction = "floating parameter"; c.AssignmentKnown = true;
        cases.Add(new Case("S1", "Unassigned floating Point; zero available/saved items; source inspection complete with no wires.", "Failed to collect data", c));
        c = Context("Curve (Crv)", "Curve", "Curve"); c.Floating = true; c.AssignmentKnown = true; c.HasAssignedData = true; c.MissingReferenceInActiveDocument = true; c.ReferenceLocation = "Curve (Crv) / Curve";
        cases.Add(new Case("S2", "Scenario assumes a deleted reference, but evidence only proves the sampled object ID is absent from the active Rhino document; deletion remains unverified to the assistant.", "Reference is invalid", c));
        c = Context("Surface consumer", "Surface", "Surface"); c.Sources.Add(new TutorSource { Component = "Point (Pt)", Parameter = "Point", DeclaredType = "Point", ItemCount = 3 });
        cases.Add(new Case("S3", "Explicit runtime conversion failure; incoming Point-typed data, receiving Surface input.", "Data conversion failed from Point to Surface", c));
        c = Context("Mesh FromPoints", "Points", "Point"); c.Sources.Add(new TutorSource { Component = "Trim Tree", Parameter = "Tree", ItemCount = 0 });
        cases.Add(new Case("S4", "Required Points input has a recorded warning and existing source wire; source sends zero available items.", "Input parameter Points failed to collect data", c));
        c = Context("Consumer", "Values", "Number", 4); c.HasNullItem = true;
        cases.Add(new Case("S5", "One missing item observed among four available input items. Actual origin is not established.", "Null input value", c));
        c = Context("Tree consumer", "Curves", "Curve", 6);
        cases.Add(new Case("S6", "Six available curves; runtime reports mismatched data-tree paths. No branch paths/shape supplied because the current context does not capture them.", "Data tree paths do not match the expected structure.", c));
        r = new Reader(); var root = r.Add("Scale"); var rp = Port(root, "Geometry", "Geometry", 0); Warn(rp);
        var build = r.Add("Mesh FromPoints"); Wire(rp, build, "Mesh", 0); var bp = Port(build, "Points", "Point", 0); Warn(bp);
        var pt = r.Add("Point (Pt)"); pt.Floating = true; Wire(bp, pt, "Point", 0); var pp = Port(pt, "Point", "Point", 0); pp.AssignmentKnown = true; pp.HasAssignedData = false; Warn(pp);
        cases.Add(new Case("S7", "Verified chain Point (known unassigned) -> Mesh FromPoints / Points -> Scale / Geometry; warnings at all receiving locations. Wire connections do not independently prove internal causation.", "Input parameter Geometry failed to collect data",
            new TutorContext { Component = root.Name, OwnerId = root.Id, ParameterId = rp.Id, Parameter = rp.Name, ParameterType = rp.TypeName, Direction = "input", ItemCount = 0, SourcesKnown = true, Graph = DependencyAnalysis.AnalyzeDetailed(r, root.Id, rp.Id).Graph }));
        c = Context("Vendor Tool", "Data", "VendorData", 3); c.InspectionPartial = true;
        cases.Add(new Case("S8", "Unrecognized third-party message; no independently observed data/reference problem; incomplete context.", "Unexpected gizmo state 42", c));
        return cases;
    }

}

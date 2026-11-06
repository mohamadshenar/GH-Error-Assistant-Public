using System;
using System.Collections.Generic;
using System.Linq;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class Reliability24Tests
{
    private sealed class Reader : IDependencyReader
    {
        internal readonly Dictionary<Guid, DependencyNode> Nodes = new Dictionary<Guid, DependencyNode>();
        public bool Limited { get; set; }
        public DependencyNode Read(Guid id) => Nodes.TryGetValue(id, out var n) ? n : null;
        internal DependencyNode Add(string name, bool floating = false)
        { var n = new DependencyNode { Id = Guid.NewGuid(), Name = name, Floating = floating }; Nodes[n.Id] = n; return n; }
    }
    [Fact] public void RealFloatingSharedIdentityKeepsOwnRoleAndAssignmentFacts()
    {
        var reader = new Reader(); var mesh = reader.Add("Mesh"); var pt = reader.Add("Point", true);
        var point = new DependencyInput { Id = pt.Id, Name = "Point", TypeName = "Point", AssignmentKnown = true, SourcesInspected = true, Count = 0 };
        point.Observations.Add(DiagnosticObservation.Warning("Floating parameter Pt failed to collect data", "Warning")); pt.Inputs.Add(point);
        var input = new DependencyInput { Id = mesh.Id, Name = "Mesh", TypeName = "Mesh", Count = 0 };
        input.Observations.Add(DiagnosticObservation.Warning("Failed to collect data", "Warning")); mesh.Inputs.Add(input);
        var source = new DependencyLink { OwnerId = pt.Id, OutputId = pt.Id, OwnerName = "Point", OutputName = "Point", TypeName = "Point", Count = 0 }; source.Observations.Add(DiagnosticObservation.Warning("Floating parameter Pt failed to collect data", "Warning")); input.Sources.Add(source);
        var graph = DependencyAnalysis.AnalyzeDetailed(reader, mesh.Id, Guid.Empty).Graph;
        var e = TutorEngine.Explain("Failed to collect data", new TutorContext { Component = "Mesh", OwnerId = mesh.Id, Graph = graph });
        var first = e.Guidance.Targets[0];
        Assert.Equal(pt.Id, first.OwnerId); Assert.Equal(pt.Id, first.ParameterId);
        Assert.Equal("floating parameter", first.Issue.Direction); Assert.True(BeginnerDiagnosis.Unassigned(first.Issue));
        Assert.Contains(graph.Issues, i => i.ComponentId == pt.Id && i.Direction == "output");
        Assert.Contains(e.Guidance.Targets, t => t.OwnerId == mesh.Id);
        var receiver = e.Guidance.Checks.Single(c => c.Target.OwnerId == mesh.Id);
        Assert.Contains("Look for available points", receiver.Expected);
        Assert.DoesNotContain("Look for available meshes", receiver.Expected);
        Assert.Contains("Receiver Mesh / Mesh needs meshes", receiver.Expected);
        Assert.Contains("not an established cause", receiver.Reason);
    }
    [Fact] public void CapturedRhino835SharedIdentityReplaysWithoutSyntheticPortIds()
    {
        var row = System.IO.File.ReadAllLines(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "v24-native-shared-identity.tsv"))[1].Split('\t');
        var reader = new Reader(); var mesh = reader.Add("Mesh", true); var point = reader.Add("Point", true);
        reader.Nodes.Clear(); mesh.Id = Guid.Parse(row[0]); point.Id = Guid.Parse(row[2]); reader.Nodes[mesh.Id] = mesh; reader.Nodes[point.Id] = point;
        var own = new DependencyInput { Id = Guid.Parse(row[4]), Name = "Point", TypeName = row[5], Count = int.Parse(row[7]),
            AssignmentKnown = bool.Parse(row[8]), HasAssignedData = bool.Parse(row[9]), SourcesInspected = bool.Parse(row[10]) };
        var warning = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(row[11]));
        own.Observations.Add(DiagnosticObservation.Warning(warning, "Warning")); point.Inputs.Add(own);
        var input = new DependencyInput { Id = Guid.Parse(row[1]), Name = "Mesh", TypeName = row[6], Count = 0 };
        input.Observations.Add(DiagnosticObservation.Warning("Floating parameter Mesh failed to collect data", "Warning")); mesh.Inputs.Add(input);
        var wire = new DependencyLink { OwnerId = point.Id, OutputId = Guid.Parse(row[3]), OwnerName = "Point", OutputName = "Point", TypeName = own.TypeName, Count = own.Count };
        wire.Observations.Add(DiagnosticObservation.Warning(warning, "Warning")); input.Sources.Add(wire);
        var graph = DependencyAnalysis.AnalyzeDetailed(reader, mesh.Id, Guid.Empty).Graph;
        var guide = TutorEngine.Explain("Floating parameter Mesh failed to collect data", new TutorContext { Component = "Mesh", OwnerId = mesh.Id, Graph = graph }).Guidance;
        Assert.Equal(point.Id, own.Id); Assert.Equal(point.Id, wire.OutputId);
        Assert.Equal(point.Id, guide.Targets[0].OwnerId); Assert.True(BeginnerDiagnosis.Unassigned(guide.Targets[0].Issue));
        Assert.Contains("available points", guide.Checks.Single(c => c.Target.OwnerId == mesh.Id).Expected);
    }
    [Fact] public void UnknownSourceTypeDoesNotBorrowReceiverType()
    {
        var c = new TutorContext { Component = "Mesh", OwnerId = Guid.NewGuid(), Parameter = "Mesh", ParameterType = "Mesh", ItemCount = 0, SourcesKnown = true };
        c.Sources.Add(new TutorSource { Component = "Custom", Parameter = "Out", ItemCount = 0 });
        var s = TutorEngine.Explain("Failed to collect data", c).Guidance.Checks.First();
        Assert.Contains("Look for available data", s.Expected); Assert.DoesNotContain("Look for available meshes", s.Expected);
        Assert.Contains("type not captured", s.Observed);
    }
    private static Report PointReport()
    {
        var owner = Guid.NewGuid(); return new Report { Name = "Point", OwnerId = owner, DocumentId = Guid.NewGuid(), Severity = "Warning", Message = "Failed to collect data",
            Context = new TutorContext { Component = "Point", OwnerId = owner, ParameterId = owner, Parameter = "Point", ParameterType = "Point", Floating = true, SourcesKnown = true, AssignmentKnown = true, ItemCount = 0 } };
    }
    private static TroubleshootingSession Session(Report r) => new TroubleshootingSession(r, new VerificationSnapshot {
        OwnerId = r.OwnerId, DocumentId = r.DocumentId, DocumentToken = "live", OriginalMessage = r.Message, OriginalSeverity = r.Severity }, "Disposable.gh");
    [Fact] public void LocateOrSelectionCannotReplaceSessionProblemOrBaseline()
    {
        var original = PointReport(); var s = Session(original); var candidate = PointReport(); candidate.Name = "Mesh";
        Assert.False(s.IsOriginal(candidate)); Assert.Equal(original.OwnerId, s.Report.OwnerId);
        Assert.Equal(original.DocumentId, s.Baseline.DocumentId); Assert.Contains("Point", s.Header);
        var first = s.Current(s.Report.Tutor.Guidance); s.Record(first, CheckObservation.Unsure);
        Assert.Equal(TroubleshootingSession.Key(first.Target), s.CurrentTargetKey); Assert.Single(s.Observations);
        Assert.Equal(original.Message, s.Baseline.OriginalMessage);
    }
    [Fact] public void ExplicitNewSessionHasNewIdentityAndDoesNotMutateOldSession()
    { var a = Session(PointReport()); var b = Session(PointReport()); Assert.NotEqual(a.Id, b.Id); Assert.NotEqual(a.Report.OwnerId, b.Report.OwnerId); }
    [Fact] public void SessionCopiesReportIdentityAndBaseline()
    {
        var r = PointReport(); var s = Session(r); var id = s.Report.OwnerId; r.OwnerId = Guid.NewGuid(); r.Message = "Changed";
        Assert.Equal(id, s.Report.OwnerId); Assert.Equal("Failed to collect data", s.Report.Message);
        s.Verification = new VerificationResult(); s.Verification.Changes.Add("Not confirmed");
        Assert.Contains("Original severity: Warning", s.Text()); Assert.Contains("Failed to collect data", s.Text());
    }
    [Fact] public void RoutesPreserveEveryKangarooObservationWithoutCallingThemCauses()
    {
        var f = AuditScenarioFixtures.Cases().Single(c => c.Id == "K0"); var guide = TutorEngine.Explain(f.Original, f.Context).Guidance;
        var routes = InvestigationRoutes.Build(guide, f.Context.Graph);
        Assert.Equal(guide.Checks.Count, routes.Sum(r => r.Checks.Count));
        Assert.True(routes.Count < guide.Checks.Count); Assert.True(routes.Count >= 2);
        foreach (var r in routes) Assert.DoesNotContain("cause", r.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(guide.Checks.Select(c => TroubleshootingSession.Key(c.Target)).OrderBy(x => x), routes.SelectMany(r => r.Checks).Select(c => TroubleshootingSession.Key(c.Target)).OrderBy(x => x));
    }
    [Fact] public void MissingObservationChoosesOnlyVerifiedSourceOwnerAndUnsureDoesNotAdvance()
    {
        var f = AuditScenarioFixtures.Cases().Single(c => c.Id == "S7"); var guide = TutorEngine.Explain(f.Original, f.Context).Guidance;
        var receiver = guide.Checks.Single(c => c.Target.Location == "Scale / Geometry");
        var next = TroubleshootingSession.Next(guide, receiver, CheckObservation.MissingOrUnusable);
        Assert.NotNull(next.Item2); Assert.Equal(receiver.Source.OwnerId, next.Item2.Target.OwnerId);
        Assert.Null(TroubleshootingSession.Next(guide, receiver, CheckObservation.Unsure).Item2);
    }
    [Fact] public void NoKnownSourceExplainsAbsenceInsteadOfInventingNextComponent()
    { var s = Session(PointReport()); var next = TroubleshootingSession.Next(s.Report.Tutor.Guidance, s.Current(s.Report.Tutor.Guidance), CheckObservation.MissingOrUnusable); Assert.Null(next.Item2); Assert.Contains("No earlier source", next.Item1); }
    [Fact] public void PointGuideExplainsCoordinatesAndReferenceChoice()
    {
        var text = PointReport().Tutor.Guidance.Checks.First().Instruction;
        Assert.Contains("Set one Point", text); Assert.Contains("Type=Coordinate", text); Assert.Contains("x,y,z", text); Assert.Contains("reference", text);
    }
    private static Tuple<VerificationSnapshot, VerificationSnapshot> Pair()
    {
        var b = new VerificationSnapshot { DocumentId = Guid.NewGuid(), DocumentToken = "live", OwnerId = Guid.NewGuid(), OriginalMessage = "Old", OriginalSeverity = "Warning", SolutionRevision = 1 };
        b.RuntimeMessages.Add(new RuntimeObservation { OwnerId = b.OwnerId, ParameterId = Guid.Empty, Message = "Old", Severity = "Warning", Location = "Point" });
        b.Messages.Add("Old"); var a = b.Copy(); a.SolutionRevision = 2; return Tuple.Create(b, a);
    }
    [Fact] public void ReplacementErrorIsExplicitWithSeverityAndLocation()
    {
        var p = Pair(); p.Item2.RuntimeMessages.Clear(); p.Item2.Messages.Clear();
        p.Item2.RuntimeMessages.Add(new RuntimeObservation { OwnerId = p.Item2.OwnerId, Message = "Cannot convert Point to Mesh", Severity = "Error", Location = "Mesh" });
        var r = RepairVerification.Compare(p.Item1, p.Item2, DateTime.UtcNow);
        Assert.False(r.MessagePresent); Assert.Single(r.NewMessages); Assert.Contains("New message: Error · Mesh", r.Text); Assert.Contains("no longer reported", r.Text);
        Assert.Contains("not independently confirmed", r.Text);
    }
    [Fact] public void AddedErrorAndRemainingErrorAreReportedSeparately()
    {
        var p = Pair(); p.Item2.RuntimeMessages.Add(new RuntimeObservation { OwnerId = p.Item2.OwnerId, Message = "New", Severity = "Error", Location = "Point" });
        var r = RepairVerification.Compare(p.Item1, p.Item2, DateTime.UtcNow);
        Assert.Single(r.NewMessages); Assert.Single(r.RemainingMessages); Assert.True(r.MessagePresent);
    }
    [Fact] public void SameTextAtAnotherObjectDoesNotKeepOriginalMessagePresent()
    { var p = Pair(); p.Item2.RuntimeMessages[0].OwnerId = Guid.NewGuid(); Assert.False(RepairVerification.Compare(p.Item1, p.Item2, DateTime.UtcNow).MessagePresent); }
    [Fact] public void SeverityChangesAreReportedWithoutCallingTheMessageGone()
    {
        var p = Pair(); p.Item2.RuntimeMessages[0].Severity = "Error";
        var r = RepairVerification.Compare(p.Item1, p.Item2, DateTime.UtcNow);
        Assert.True(r.MessagePresent); Assert.Contains("Warning → Error", r.Text);
    }
    [Fact] public void EmptyAfterMissingSampleCannotReadAsNullRecovery()
    {
        var p = Pair(); var input = new InputObservation { OwnerId = p.Item1.OwnerId, ParameterId = Guid.NewGuid(), Location = "Mesh", Count = 3, HasMissing = true, Complete = true };
        p.Item1.Inputs.Add(input); var current = input.Copy(); current.Count = 0; current.HasMissing = false; p.Item2.Inputs.Add(current);
        var text = RepairVerification.Compare(p.Item1, p.Item2, DateTime.UtcNow).Text;
        Assert.Contains("No items were available to inspect", text); Assert.DoesNotContain("no missing item was found", text);
    }
    [Fact] public void ChangedReferencesAreReportedWithoutCertifyingReplacement()
    {
        var p = Pair(); var b = new InputObservation { OwnerId = p.Item1.OwnerId, ParameterId = Guid.NewGuid(), Count = 1, References = "old", Complete = true };
        p.Item1.Inputs.Add(b); var a = b.Copy(); a.References = "new"; a.HasInvalid = true; p.Item2.Inputs.Add(a);
        var text = RepairVerification.Compare(p.Item1, p.Item2, DateTime.UtcNow).Text;
        Assert.Contains("reference IDs changed", text); Assert.Contains("replacement is not proof", text); Assert.Contains("invalid data remains", text);
    }
    [Fact] public void PartialWrongDocumentStillRejectsComparison()
    { var p = Pair(); p.Item2.DocumentToken = "other"; p.Item2.Complete = false; var r = RepairVerification.Compare(p.Item1, p.Item2, DateTime.UtcNow); Assert.True(r.Incomplete); Assert.Empty(r.NewMessages); }
}




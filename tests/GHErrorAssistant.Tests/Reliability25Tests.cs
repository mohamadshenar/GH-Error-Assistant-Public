using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GHErrorAssistant;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class Reliability25Tests
{
 [Fact] public void HealthyBranchesDoNotExpandPastBaseDepth()
 {
  var r=new Reader();DependencyNode previous=null;Guid root=Guid.Empty;
  for(int i=0;i<12;i++){var n=new DependencyNode{Id=Guid.NewGuid(),Name="Healthy"+i};r.Nodes[n.Id]=n;if(previous==null)root=n.Id;else Connect(previous,n,1);previous=n;}
  var graph=DependencyAnalysis.AnalyzeDetailed(r,root,Guid.Empty).Graph;
  Assert.True(graph.Partial);Assert.Contains(graph.Frontiers,s=>s.Contains("distance 7"));Assert.DoesNotContain(graph.Connections,e=>e.SourceComponent=="Healthy8");
 }
 static void Connect(DependencyNode receiver,DependencyNode source,int count=0)
 {var p=new DependencyInput{Id=Guid.NewGuid(),Name="Input",Count=count};p.Sources.Add(new DependencyLink{OwnerId=source.Id,OutputId=source.Id,OwnerName=source.Name,OutputName="Output",Count=count});receiver.Inputs.Add(p);}
 [Fact] public void ShorterRouteUpdatesIssueWithoutDuplicatingObservation()
 {
  var r=new Reader();var root=new DependencyNode{Id=Guid.NewGuid(),Name="Root"};var a=new DependencyNode{Id=Guid.NewGuid(),Name="A"};var b=new DependencyNode{Id=Guid.NewGuid(),Name="B"};var source=new DependencyNode{Id=Guid.NewGuid(),Name="Curve"};
  foreach(var n in new[]{root,a,b,source})r.Nodes[n.Id]=n;
  Connect(root,a);Connect(a,b);Connect(b,source);Connect(root,source);
  source.Observations.Add(DiagnosticObservation.Warning("A referenced Curve could not be found in the Rhino document.","Warning"));
  var graph=DependencyAnalysis.AnalyzeDetailed(r,root.Id,Guid.Empty).Graph;
  Assert.Single(graph.Issues,i=>i.ComponentId==source.Id);Assert.Equal(1,graph.Issues.Single(i=>i.ComponentId==source.Id).Depth);Assert.Single(graph.Observations,o=>o.OwnerId==source.Id&&o.Kind==ObservationKind.ReferenceWarning);
 }
 [Fact] public void SameNamedSourcesRetainDistinctIdentityBearingEvidence()
 {
  var r=new Reader();var root=new DependencyNode{Id=Guid.NewGuid(),Name="Root"};r.Nodes[root.Id]=root;
  for(int i=0;i<2;i++){var n=new DependencyNode{Id=Guid.NewGuid(),Name="Curve"};n.Observations.Add(DiagnosticObservation.Warning("A referenced Curve could not be found in the Rhino document.","Warning"));r.Nodes[n.Id]=n;Connect(root,n);}
  var graph=DependencyAnalysis.AnalyzeDetailed(r,root.Id,Guid.Empty).Graph;
  Assert.Equal(2,graph.Observations.Count(o=>o.Kind==ObservationKind.ReferenceWarning));Assert.Equal(2,graph.Issues.Select(i=>i.ComponentId).Distinct().Count());
 }
 sealed class Reader : IDependencyReader
 {
  internal Dictionary<Guid,DependencyNode> Nodes=new Dictionary<Guid,DependencyNode>();
  public bool Limited {get;set;}
  public DependencyNode Read(Guid id)=>Nodes.TryGetValue(id,out var n)?n:null;
 }
 static (Reader reader,Guid root,Guid curve) Native()
 {
  var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","v25-native-nine-hop.json"))).RootElement;
  var r=new Reader();
  foreach(var o in data.GetProperty("objects").EnumerateArray())
  {
   var n=new DependencyNode{Id=Guid.Parse(o.GetProperty("id").GetString()),Name=o.GetProperty("name").GetString(),Floating=o.GetProperty("type").GetString().Contains("Param_Curve")};r.Nodes[n.Id]=n;
   foreach(var message in o.GetProperty("messages").EnumerateArray())n.Observations.Add(DiagnosticObservation.Warning(message.GetProperty("message").GetString(),message.GetProperty("severity").GetString()));
   foreach(var p in o.GetProperty("inputs").EnumerateArray())
   {
    var input=new DependencyInput{Id=Guid.Parse(p.GetProperty("id").GetString()),Name=p.GetProperty("name").GetString(),TypeName=p.GetProperty("type").GetString(),Count=p.GetProperty("count").GetInt32(),AssignmentKnown=p.GetProperty("persistentCount").ValueKind!=JsonValueKind.Null,HasAssignedData=p.GetProperty("persistentCount").ValueKind!=JsonValueKind.Null&&p.GetProperty("persistentCount").GetInt32()>0};n.Inputs.Add(input);
    foreach(var message in p.GetProperty("messages").EnumerateArray())input.Observations.Add(DiagnosticObservation.Warning(message.GetProperty("message").GetString(),message.GetProperty("severity").GetString()));
    if(p.GetProperty("nullCount").GetInt32()>0)input.Observations.Add(new DiagnosticObservation{Kind=ObservationKind.NullSample,Message="Captured null volatile entries"});
    int missing=p.GetProperty("missingSavedReferences").GetInt32();if(missing>0)input.Observations.Add(new DiagnosticObservation{Kind=ObservationKind.MissingReferenceInActiveDocument,Count=missing,Message=missing+" saved reference IDs absent in captured active Rhino document"});
    foreach(var source in p.GetProperty("sources").EnumerateArray())input.Sources.Add(new DependencyLink{OwnerId=Guid.Parse(source.GetProperty("owner").GetString()),OutputId=Guid.Parse(source.GetProperty("port").GetString()),OutputName=source.GetProperty("name").GetString(),OwnerName="Captured source",Count=0});
   }
  }
  return(r,Guid.Parse(data.GetProperty("rootId").GetString()),Guid.Parse(data.GetProperty("curveId").GetString()));
 }
 static Report Explain(Reader r,Guid root)=>new Report{Name=r.Nodes[root].Name,OwnerId=root,DocumentId=Guid.NewGuid(),Message="Solution exception:Offset and length were out of bounds",Severity="Error",Context=new TutorContext{Component=r.Nodes[root].Name,OwnerId=root,Graph=DependencyAnalysis.AnalyzeDetailed(r,root,Guid.Empty).Graph}};
 [Fact] public void CapturedNineHopCurveWithSevenMissingReferencesIsFirst()
 {
  var f=Native();var report=Explain(f.reader,f.root);var first=report.Tutor.Guidance.Checks.First();
  Assert.Equal(f.curve,first.Target.OwnerId);Assert.Equal(9,first.Target.Issue.Depth);Assert.Equal(7,first.Target.Issue.Observation.Count);
  var card=RepairCard.Create(first,report);Assert.Contains("Curve parameter contains references",card.Found);Assert.Contains("7",card.Found);Assert.Contains("not a proven cause",card.Why);Assert.InRange(card.Steps.Count,1,3);Assert.Contains(card.Steps,s=>s.Contains("Set Multiple Curves"));
 }
 [Fact] public void CapturedBrokenCurveSelectedDirectlyIsReferenceNotNullAdvice()
 {var f=Native();var report=Explain(f.reader,f.curve);var first=report.Tutor.Guidance.Checks.First();Assert.Equal(ObservationKind.MissingReferenceInActiveDocument,first.Target.Issue.Observation.Kind);Assert.Contains("cannot currently be found",RepairCard.Create(first,report).Found);Assert.False(BeginnerDiagnosis.Unassigned(first.Target.Issue));}
 [Theory][InlineData("Point")][InlineData("Curve")][InlineData("Surface")][InlineData("Brep")][InlineData("Mesh")]
 public void NativeReferenceMessagesForGeometryTypesAreRecognized(string type)
 {var m="A referenced "+type+" could not be found in the Rhino document.";var fact=DiagnosticObservation.Warning(m,"Warning");Assert.Equal(ObservationKind.ReferenceWarning,fact.Kind);Assert.Equal(m,fact.Message);Assert.Equal("reference-warning",TutorEngine.Explain(m,new TutorContext{Component=type,Parameter=type,ParameterType=type}).Kind);}
 [Fact] public void ConnectedUnassignedGeometryStaysBehindVerifiedMissingReferences()
 {var f=Native();var p=new DependencyNode{Id=Guid.NewGuid(),Name="Point",Floating=true};f.reader.Nodes[p.Id]=p;var pi=new DependencyInput{Id=p.Id,Name="Point",TypeName="Point",AssignmentKnown=true,Count=0};pi.Observations.Add(DiagnosticObservation.Warning("Failed to collect data","Warning"));p.Inputs.Add(pi);f.reader.Nodes[f.root].Inputs.Add(new DependencyInput{Id=Guid.NewGuid(),Name="Other"});f.reader.Nodes[f.root].Inputs.Last().Sources.Add(new DependencyLink{OwnerId=p.Id,OutputId=p.Id,OwnerName="Point",OutputName="Point"});var guide=Explain(f.reader,f.root).Tutor.Guidance;Assert.Equal(f.curve,guide.Targets.First().OwnerId);Assert.Contains(guide.Targets,t=>t.OwnerId==p.Id);Assert.NotEqual(guide.Targets.First().Confidence,guide.Targets.First().Actionability);}
 [Fact] public void DisconnectedReferenceWarningIsNotRanked()
 {var f=Native();var orphan=new DependencyNode{Id=Guid.NewGuid(),Name="Unrelated Curve",Floating=true};orphan.Observations.Add(DiagnosticObservation.Warning("A referenced Curve could not be found in the Rhino document.","Warning"));f.reader.Nodes[orphan.Id]=orphan;Assert.DoesNotContain(Explain(f.reader,f.root).Tutor.Guidance.Targets,t=>t.OwnerId==orphan.Id);}
 [Fact] public void NativeGraphAnalysisIsReadOnlyAndCyclesTerminate()
 {var f=Native();var before=f.reader.Nodes.Sum(n=>n.Value.Inputs.Sum(i=>i.Sources.Count));var p=f.reader.Nodes[f.curve].Inputs.First();p.Sources.Add(new DependencyLink{OwnerId=f.root,OutputId=Guid.NewGuid(),OwnerName="Solver",OutputName="Cycle"});var g=DependencyAnalysis.AnalyzeDetailed(f.reader,f.root,Guid.Empty).Graph;Assert.True(g.Cycle);Assert.Equal(before+1,f.reader.Nodes.Sum(n=>n.Value.Inputs.Sum(i=>i.Sources.Count)));Assert.True(g.Connections.Count<=DependencyAnalysis.MaxEdges);}
 [Fact] public void HardLimitReportsExactFrontier()
 {var r=new Reader();DependencyNode prev=null;Guid root=Guid.Empty;for(int i=0;i<40;i++){var n=new DependencyNode{Id=Guid.NewGuid(),Name="N"+i};r.Nodes[n.Id]=n;if(prev==null)root=n.Id;else{var input=new DependencyInput{Id=Guid.NewGuid(),Name="Input"};input.Sources.Add(new DependencyLink{OwnerId=n.Id,OutputId=Guid.NewGuid(),OwnerName=n.Name,OutputName="Out",Count=0});prev.Inputs.Add(input);}prev=n;}var g=DependencyAnalysis.AnalyzeDetailed(r,root,Guid.Empty).Graph;Assert.True(g.Partial);Assert.Contains(g.Frontiers,s=>s.Contains("distance 19"));}
 [Fact] public void CompleteReferenceResolutionIsDistinctFromReplacement()
 {
  var b=new VerificationSnapshot{DocumentId=Guid.NewGuid(),DocumentToken="live",OwnerId=Guid.NewGuid(),SolutionRevision=1};
  var input=new InputObservation{OwnerId=b.OwnerId,ParameterId=Guid.NewGuid(),Location="Curve",References="same-seven-ids",MissingReference=true,MissingReferenceCount=7,ResolvedReferenceCount=0,Count=7,Complete=true};b.Inputs.Add(input);var a=b.Copy();a.SolutionRevision=2;a.Inputs[0].MissingReference=false;a.Inputs[0].MissingReferenceCount=0;a.Inputs[0].ResolvedReferenceCount=7;
  var result=RepairVerification.Compare(b,a,DateTime.UtcNow);Assert.Contains("previously missing sampled reference IDs now exist",result.Text);Assert.Contains("Intended model output is not independently confirmed",result.Text);
  a.Inputs[0].References="replacement";Assert.DoesNotContain("previously missing sampled reference IDs now exist",RepairVerification.Compare(b,a,DateTime.UtcNow).Text);
 }
 [Fact] public void PartialReferenceReadCannotCertifyResolution()
 {var b=new VerificationSnapshot{DocumentId=Guid.NewGuid(),DocumentToken="live",OwnerId=Guid.NewGuid(),SolutionRevision=1};b.Inputs.Add(new InputObservation{OwnerId=b.OwnerId,ParameterId=Guid.NewGuid(),MissingReferenceCount=7,References="ids",Complete=true});var a=b.Copy();a.SolutionRevision++;a.Inputs[0].MissingReferenceCount=0;a.Inputs[0].ResolvedReferenceCount=7;a.Inputs[0].ReferencesComplete=false;var v=RepairVerification.Compare(b,a,DateTime.UtcNow);Assert.True(v.Incomplete);Assert.StartsWith("Partial comparison",v.Compact);Assert.DoesNotContain("previously missing sampled reference IDs now exist",v.Text);}
 [Fact] public void UpdatedPlanPreservesOriginalProblemBaselineAndRejectsWrongDocument()
 {var f=Native();var r=Explain(f.reader,f.root);var s=new TroubleshootingSession(r,new VerificationSnapshot{DocumentId=r.DocumentId,OwnerId=r.OwnerId,OriginalMessage=r.Message},"original");var updated=Explain(f.reader,f.root);updated.DocumentId=r.DocumentId;s.UpdatePlan(updated);Assert.Same(updated,s.EvidenceReport);Assert.Equal(r.Message,s.Baseline.OriginalMessage);var wrong=Explain(f.reader,f.root);s.UpdatePlan(wrong);Assert.Same(updated,s.EvidenceReport);Assert.Equal(r.OwnerId,s.Report.OwnerId);}
}


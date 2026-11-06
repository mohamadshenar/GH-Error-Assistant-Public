using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GHErrorAssistant.Diagnostics
{
    internal sealed class DependencyNode
    {
        internal Guid Id;
        internal string Name;
        internal bool Boundary, Floating;
        internal readonly List<string> Evidence = new List<string>();
        internal readonly List<DiagnosticObservation> Observations = new List<DiagnosticObservation>();
        internal readonly List<DependencyInput> Inputs = new List<DependencyInput>();
    }
    internal sealed class DependencyInput
    {
        internal Guid Id;
        internal string Name, TypeName = "", Description = "", TreeSummary = "", Direction = "input";
        internal int Count;
        internal bool Optional, AssignmentKnown, HasAssignedData;
        internal bool SourcesInspected = true;
        internal int Index = -1;
        internal string Nickname = "";
        internal TreeEvidence Tree;
        internal ReferenceEvidence References;
        internal readonly List<string> Evidence = new List<string>();
        internal readonly List<DiagnosticObservation> Observations = new List<DiagnosticObservation>();
        internal readonly List<DependencyLink> Sources = new List<DependencyLink>();
    }
    internal sealed class DependencyLink
    {
        internal Guid OwnerId, OutputId = Guid.Empty;
        internal string OwnerName, OutputName, TypeName = "";
        internal int Count;
        internal int Index = -1;
        internal string Nickname = "";
        internal TreeEvidence Tree;
        internal ReferenceEvidence References;
        internal readonly List<string> Evidence = new List<string>();
        internal readonly List<DiagnosticObservation> Observations = new List<DiagnosticObservation>();
    }
    internal interface IDependencyReader
    {
        DependencyNode Read(Guid id);
        bool Limited { get; }
    }
    internal interface IDependencySnapshot
    {
        Guid DocumentId { get; }
        string DocumentToken { get; }
    }
    internal interface IDependencyLimits { string LimitReason { get; } }
    internal sealed class DependencyAdvice
    {
        internal string Details, FirstAction;
        internal bool HasUpstreamFinding;
        internal GraphEvidence Graph;
    }
    internal static class DependencyAnalysis
    {
        internal static string Format(GraphEvidence graph, IEnumerable<InvestigationTarget> order)
        {
            if (graph == null) return "Dependency inspection unavailable. No source has been inferred.";
            var targets = order.ToList();
            return "Affected object: " + graph.RootName + "\nShared investigation order (not established causes)\n" +
                string.Join("\n", targets.Select((t, n) => (n + 1) + ". " + t.Location + " — " + t.Summary)) +
                (targets.Count == 0 ? "\nThis bounded inspection found no specific upstream fault." : "") +
                "\n" + string.Join("\n", targets.Where(t => t.Issue.Path.Count > 0).Select(t => "Observed object path: " + string.Join(" → ", t.Issue.Path.Select(e => e.SourceComponent).Concat(new[]{graph.RootName})))) +
                "\n\nRecommended first action\n" + (targets.Count == 0 ? "Inspect the reporting object's input description and original message." : "Inspect " + targets[0].Location + ".") +
                "\n\nConfirmed runtime observations and sampled-data facts\n" +
                string.Join("\n", graph.Observations.Select(o => o.Component + " / " + o.Parameter + " [" + o.Classification + ", " + o.Kind + "]: " + o.Message)) +
                "\n\nCandidate ranking (not causal confirmation)\n" + string.Join("\n", targets.Select(t => t.Location + " [" + t.OwnerId + " / " + t.ParameterId + "] score " + t.Score + ": " + t.RankingReason)) +
                "\nSnapshot " + graph.Generation + " captured " + graph.CapturedUtc.ToString("o") + "; document " + graph.DocumentId + " / " + graph.DocumentToken +
                "; nodes " + graph.NodesRead + "; wires " + graph.Connections.Count + "; graph ms " + graph.ElapsedMilliseconds + "." +
                "\n\nVerified wire connections (source output → receiving input)\n" + string.Join("\n", graph.Connections.Select(e => e.Description).Distinct()) +
                "\n\nConnections do not establish internal input-to-output dependencies or causation; this is not a confirmed root cause. Empty output alone is not a confirmed failure." +
                (graph.Partial ? " Inspection is partial; uninspected branches are unknown.\n" + string.Join("\n", graph.Frontiers) : "") + (graph.Cycle ? " A cyclic dependency was detected; repeated nodes were not traversed again." : "");
        }
        internal const int BaseDepth = 6, MaxDepth = 18, MaxNodes = 64, MaxEdges = 128;
        private static IEnumerable<DiagnosticObservation> All(IEnumerable<string> legacy, IEnumerable<DiagnosticObservation> typed)
            => typed.Concat(legacy.Select(DiagnosticObservation.Legacy));
        private static VerifiedConnection Edge(DependencyNode target, DependencyInput input, DependencyLink source) => new VerifiedConnection {
            SourceId = source.OwnerId, SourceParameterId = source.OutputId, SourceComponent = source.OwnerName, SourcePort = source.OutputName,
            SourceType = source.TypeName, SourceCount = source.Count,
            TargetId = target.Id, TargetParameterId = input.Id, TargetComponent = target.Name, TargetPort = input.Name,
            SourceIndex = source.Index, TargetIndex = input.Index, SourceNickname = source.Nickname, TargetNickname = input.Nickname
        };
        internal static string FirstAction(UpstreamIssue issue)
        {
            string type = string.Equals(issue.ParameterType, "Point", StringComparison.OrdinalIgnoreCase) ? "the point data" :
                string.Equals(issue.ParameterType, "Curve", StringComparison.OrdinalIgnoreCase) ? "the curve data" : "the data";
            return "Inspect " + type + " at " + issue.Location + (issue.Observation.Runtime ? " and its recorded warning." : " and the observed data issue.");
        }
        internal static string Analyze(IDependencyReader reader, Guid rootId, Guid focusInput)
            => AnalyzeDetailed(reader, rootId, focusInput).Details;
        internal static DependencyAdvice AnalyzeDetailed(IDependencyReader reader, Guid rootId, Guid focusInput, System.Threading.CancellationToken cancellation = default(System.Threading.CancellationToken), Func<long> elapsedMilliseconds = null)
        {
            var graph = new GraphEvidence { RootId = rootId };
            if (reader is IDependencySnapshot snapshot) { graph.DocumentId = snapshot.DocumentId; graph.DocumentToken = snapshot.DocumentToken; }
            var root = reader.Read(rootId);
            if (root == null) { graph.Partial = true; return new DependencyAdvice { Graph = graph,
                Details = "Context unavailable: the reporting object is no longer in the active document. No source has been inferred.",
                FirstAction = "Inspect the reporting object in the active document." }; }
            graph.RootName = root.Name;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // Tests isolate structural budgets with a controlled clock; native callers always use Stopwatch.
            var elapsed = elapsedMilliseconds ?? (() => clock.ElapsedMilliseconds);
            Action<Guid, int, string> frontier = (id, depth, reason) => {
                graph.Partial = true;
                if (graph.FrontierTargets.Any(f => f.OwnerId == id) || graph.Frontiers.Count >= 32) return;
                string name = graph.Connections.FirstOrDefault(e => e.SourceId == id)?.SourceComponent ?? (id == rootId ? root.Name : "Upstream object");
                graph.Frontiers.Add(name + " [" + id + "] at distance " + depth + ": " + reason);
                graph.FrontierTargets.Add(new InspectionFrontier { OwnerId = id, Name = name, Depth = depth, Reason = reason,
                    Generation = graph.Generation, CanContinue = !reason.Contains("opaque") && !reason.Contains("unavailable") });
            };
            var visited = new HashSet<Guid>();
            var queue = new Queue<Tuple<Guid, int, bool, List<VerifiedConnection>>>();
            var reads = new Dictionary<Guid, DependencyNode>();
            var bestDepth = new Dictionary<Guid, int>();
            var expanded = new HashSet<Guid>();
            var trail = new List<VerifiedConnection>();
            Action<DependencyNode, DependencyInput, DiagnosticObservation, int, IEnumerable<VerifiedConnection>> record = (node, input, observation, depth, path) => {
                var fact = observation.At(node.Name, input?.Name ?? "");
                fact.OwnerId = node.Id; fact.ParameterId = input?.Id ?? Guid.Empty;
                if (graph.Observations.Count >= 512) { frontier(node.Id, depth, "observation limit reached"); return; }
                if (!graph.Observations.Any(o => o.OwnerId == fact.OwnerId && o.ParameterId == fact.ParameterId && o.Kind == fact.Kind && o.Message == fact.Message)) graph.Observations.Add(fact);
                if (fact.Kind == ObservationKind.EmptyCache || fact.Kind == ObservationKind.InspectionNote || fact.Kind == ObservationKind.Other || fact.Kind == ObservationKind.TreeStructure || fact.Kind == ObservationKind.ReferenceState) return;
                if (graph.Issues.Count >= 128) { frontier(node.Id, depth, "candidate limit reached"); return; }
                var issue = new UpstreamIssue { ComponentId = node.Id, Component = node.Name, ParameterId = input?.Id ?? Guid.Empty,
                    Parameter = input?.Name ?? "", ParameterType = input?.TypeName ?? "", Observation = fact, Depth = depth,
                    Direction = input?.Direction == "output" ? "output" : node.Floating ? "floating parameter" : "input",
                    ItemCount = input?.Count, SourcesKnown = input?.SourcesInspected == true, Floating = node.Floating,
                    AssignmentKnown = input?.AssignmentKnown == true, HasAssignedData = input?.HasAssignedData == true,
                    Optional = input?.Optional == true, Description = input?.Description ?? "", TreeSummary = input?.TreeSummary ?? "",
                    PortIndex = input?.Index ?? -1, Nickname = input?.Nickname ?? "", Tree = input?.Tree, References = input?.References };
                var previous = graph.Issues.FirstOrDefault(i => i.ComponentId == issue.ComponentId && i.ParameterId == issue.ParameterId &&
                    i.Direction == issue.Direction && i.Observation.Kind == fact.Kind && i.Observation.Message == fact.Message);
                if (previous != null && previous.Depth <= depth) return;
                if (previous != null) graph.Issues.Remove(previous);
                issue.Path.AddRange(path.Reverse());
                foreach (var p in input == null ? node.Inputs.AsEnumerable() : new[]{input})
                    issue.Feeders.AddRange(p.Sources.Take(4).Select(s => Edge(node, p, s)));
                graph.Issues.Add(issue);
            };
            queue.Enqueue(Tuple.Create(rootId, 0, false, new List<VerifiedConnection>()));
            while (queue.Count > 0)
            {
                var work = queue.Dequeue(); var id = work.Item1; var depth = work.Item2; var promising = work.Item3;
                trail = work.Item4;
                if (bestDepth.TryGetValue(id, out var seenDepth) && seenDepth <= depth && (!promising || expanded.Contains(id))) continue;
                string limit = cancellation.IsCancellationRequested ? "inspection cancelled" : depth > MaxDepth ? "hard depth limit reached" : depth > BaseDepth && !promising ? "ordinary branch depth reached; sampled evidence did not justify automatic expansion" :
                    visited.Count >= MaxNodes && !visited.Contains(id) ? "unique-node limit reached" : elapsed() >= 65 ? "graph time limit reached" : null;
                if (limit != null) { frontier(id, depth, limit + "; upstream data remains unknown"); continue; }
                if (!reads.TryGetValue(id, out var node)) { node = id == rootId ? root : reader.Read(id); reads[id] = node; }
                if (node == null) { frontier(id, depth, "object unavailable or SDK inspection budget reached"); continue; }
                visited.Add(id); bestDepth[id] = depth;
                if (promising) expanded.Add(id);
                foreach (var fact in All(node.Evidence, node.Observations)) record(node, null, fact, depth, trail);
                if (node.Boundary) { frontier(id, depth, "opaque cluster or unreadable third-party boundary"); continue; }
                bool focused = depth == 0 && node.Inputs.Any(p => p.Id == focusInput);
                foreach (var input in node.Inputs.Where(p => !focused || p.Id == focusInput))
                {
                    foreach (var fact in All(input.Evidence, input.Observations)) record(node, input, fact, depth, trail);
                    if (input.Count == 0 && !input.Optional && input.SourcesInspected && input.Sources.Count == 0)
                        record(node, input, new DiagnosticObservation { Kind = ObservationKind.EmptyCache,
                            Message = "No connected sources and zero cached items. Emptiness alone does not establish a fault." }, depth, trail);
                    foreach (var source in input.Sources.OrderByDescending(s => s.Observations.Any(o => o.Kind == ObservationKind.ReferenceWarning || o.Kind == ObservationKind.MissingReferenceInActiveDocument)).ThenByDescending(s => s.Observations.Count > 0 || s.Count == 0))
                    {
                        if (graph.Connections.Count >= MaxEdges) { frontier(source.OwnerId, depth + 1, "wire budget reached"); break; }
                        var edge = Edge(node, input, source); if (!graph.ContainsEdge(edge)) graph.Connections.Add(edge); trail.Add(edge);
                        var sourceNode = new DependencyNode { Id = source.OwnerId, Name = source.OwnerName };
                        var output = new DependencyInput { Id = source.OutputId, Name = source.OutputName, TypeName = source.TypeName, Count = source.Count, SourcesInspected = false, Direction = "output",
                            Index = source.Index, Nickname = source.Nickname, Tree = source.Tree, References = source.References };
                        foreach (var fact in All(source.Evidence, source.Observations)) record(sourceNode, output, fact, depth + 1, trail);
                        if (source.Count == 0) record(sourceNode, output, new DiagnosticObservation { Kind = ObservationKind.EmptyCache,
                            Message = "Source has zero cached output items. Empty output alone is not a confirmed failure." }, depth + 1, trail);
                        // Retain the reason for investigating this branch if later sampling is exhausted.
                        // This is reachability priority, never a claim of causation.
                        bool relevant = promising || source.Count == 0 || source.Observations.Any(o => o.Runtime || o.Kind == ObservationKind.NullSample || o.Kind == ObservationKind.MissingReferenceInActiveDocument) || input.Observations.Any(o => o.Runtime || o.Kind == ObservationKind.NullSample);
                        if (source.OwnerId == rootId || trail.Take(trail.Count - 1).Any(e => e.TargetId == source.OwnerId || e.SourceId == source.OwnerId)) graph.Cycle = true;
                        else if (queue.Count < MaxEdges) queue.Enqueue(Tuple.Create(source.OwnerId, depth + 1, relevant, trail.ToList()));
                        else frontier(source.OwnerId, depth + 1, "pending-work limit reached");
                        trail.RemoveAt(trail.Count - 1);
                    }
                }
            }
            graph.NodesRead = visited.Count;
            GraphIntelligence.Annotate(graph);
            graph.ElapsedMilliseconds = elapsed();
            graph.Partial |= reader.Limited;
            if (reader.Limited && graph.Frontiers.Count < 32) graph.Frontiers.Add(
                reader is IDependencyLimits limits ? limits.LimitReason : "SDK adapter reports incomplete inspection; exact stopping reason unavailable.");
            foreach (var issue in graph.Issues)
                issue.Feeders.RemoveAll(f => !graph.Connections.Any(e => e.SourceId == f.SourceId && e.TargetId == f.TargetId &&
                    e.SourceParameterId == f.SourceParameterId && e.TargetParameterId == f.TargetParameterId));
            var shared = BeginnerDiagnosis.Rank(new DiagnosticModel { ComponentId = rootId, Graph = graph }, new GHErrorAssistant.Core.TutorContext());
            return new DependencyAdvice { Graph = graph, Details = Format(graph, shared),
                FirstAction = shared.Count == 0 ? "Inspect the reporting input and its requirements." : "Inspect " + shared[0].Location + ".",
                HasUpstreamFinding = shared.Any(t => t.Issue.Depth > 0) };
        }
    }
}




using System;
using System.Collections.Generic;
using System.Linq;

namespace GHErrorAssistant.Diagnostics
{
    internal static class GraphIntelligence
    {
        // Reachability on the already bounded snapshot; never invoke SDK getters or expand the graph here.
        internal static void Annotate(GraphEvidence graph)
        {
            var incoming = graph.Connections.GroupBy(e => e.TargetId).ToDictionary(g => g.Key, g => g.ToList());
            var active = new HashSet<Guid>(); var done = new HashSet<Guid>();
            Action<Guid> cycle = null;
            cycle = id => {
                if (active.Contains(id)) { graph.Cycle = true; return; }
                if (!done.Add(id)) return;
                active.Add(id);
                if (incoming.TryGetValue(id, out var edges)) foreach (var edge in edges) cycle(edge.SourceId);
                active.Remove(id);
            };
            cycle(graph.RootId);
            foreach (var branch in graph.Connections.Where(e => e.TargetId == graph.RootId).GroupBy(e => e.TargetParameterId))
            {
                var queue = new Queue<Guid>(branch.Select(e => e.SourceId));
                var seen = new HashSet<Guid> { graph.RootId };
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue(); if (!seen.Add(id)) continue;
                    foreach (var issue in graph.Issues.Where(i => i.ComponentId == id)) issue.Branches.Add(branch.Key);
                    if (incoming.TryGetValue(id, out var edges)) foreach (var edge in edges) queue.Enqueue(edge.SourceId);
                }
            }
        }
        internal static bool UnusedOutput(UpstreamIssue issue, GraphEvidence graph) => issue.Direction == "output" &&
            issue.ParameterId != Guid.Empty && graph != null && graph.Connections.Any(e => e.SourceId == issue.ComponentId) &&
            !graph.Connections.Any(e => e.SourceId == issue.ComponentId && e.SourceParameterId == issue.ParameterId);
        internal static int Adjustment(UpstreamIssue issue, GraphEvidence graph) =>
            (UnusedOutput(issue, graph) ? -4000 : 0) + (issue.Optional ? -20 : 0) +
            (issue.Observation.Complete ? 3 : 0) + Math.Min(3, issue.Branches.Count) * 4;
        internal static string Rationale(UpstreamIssue issue, GraphEvidence graph) =>
            (issue.Observation.Kind == ObservationKind.MissingReferenceInActiveDocument ? "Checked saved reference IDs; " :
             issue.Observation.Runtime ? "Exact runtime report; " : "Direct sampled data observation; ") +
            (UnusedOutput(issue, graph) ? "this output is not used by the inspected route, so relevance is weak; " :
             issue.Depth == 0 ? "on the reporting object; " : issue.Depth + " verified wire connections upstream; internal port dependencies unknown; ") +
            (issue.Branches.Count > 1 ? "shared by " + issue.Branches.Count + " reporting-input branches; " : "") +
            (issue.Optional ? "optional input; " : "") + (!issue.Observation.Complete ? "partial sample; " : "") +
            "investigation priority, causation remains unverified.";
    }
}

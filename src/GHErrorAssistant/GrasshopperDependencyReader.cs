using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant
{
    // Short-lived, read-only SDK adapter. Never solve, load referenced geometry, or retain GH objects after analysis.
    internal sealed class GrasshopperDependencyReader : IDependencyReader, IDependencySnapshot, IDependencyLimits
    {
        private readonly Dictionary<Guid, IGH_DocumentObject> objects = new Dictionary<Guid, IGH_DocumentObject>();
        private readonly Dictionary<Guid, DependencyNode> cache = new Dictionary<Guid, DependencyNode>();
        private readonly InspectionSampling sampling = new InspectionSampling();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly GH_Document document;
        private int inputs, links;
        private readonly Dictionary<Guid, TreeEvidence> trees = new Dictionary<Guid, TreeEvidence>();
        private readonly Dictionary<Guid, ReferenceEvidence> referencesByPort = new Dictionary<Guid, ReferenceEvidence>();
        public Guid DocumentId { get; }
        public string DocumentToken { get; }
        public bool Limited { get; private set; }
        private readonly HashSet<string> limits = new HashSet<string>();
        public string LimitReason => string.Join("; ", limits.Concat(sampling.Limits).Distinct().OrderBy(s => s)) + "; uninspected metadata remains unknown.";
        private void Limit(string reason) { Limited = true; limits.Add(reason); }
        internal GrasshopperDependencyReader(IGH_DocumentObject root) { objects[root.InstanceGuid] = root;
            document = root.OnPingDocument(); DocumentId = document?.DocumentID ?? Guid.Empty; DocumentToken = document?.RuntimeID.ToString() ?? ""; }
        private bool OutOfTime()
        {
            if (clock.ElapsedMilliseconds < 45) return false;
            Limit("SDK time limit reached"); return true;
        }
        private static string Label(IGH_DocumentObject obj) => obj.Name +
            (string.IsNullOrEmpty(obj.NickName) || obj.NickName == obj.Name ? "" : " (" + obj.NickName + ")");
        private IEnumerable<DiagnosticObservation> Warnings(IGH_ActiveObject obj)
        {
            foreach (var level in new[]{ GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning })
            {
                int count = 0;
                foreach (var message in obj.RuntimeMessages(level))
                { if (++count > 2) { Limit("per-object runtime-message sample limit reached"); break; }
                    yield return DiagnosticObservation.Warning(message, level.ToString()); }
            }
        }
        public DependencyNode Read(Guid id)
        {
            if (cache.TryGetValue(id, out var cached)) return cached;
            if (OutOfTime() || cache.Count >= DependencyAnalysis.MaxNodes || !objects.TryGetValue(id, out var obj))
            { Limit(cache.Count >= DependencyAnalysis.MaxNodes ? "SDK unique-node limit reached" : "object unavailable or SDK time limit reached"); return null; }
            var node = new DependencyNode { Id = id, Name = Label(obj), Floating = obj is IGH_Param };
            cache[id] = node;
            try
            {
                if (obj is IGH_ActiveObject active) node.Observations.AddRange(Warnings(active));
                if (obj is GH_Cluster)
                {
                    node.Boundary = true;
                    return node;
                }
                var component = obj as IGH_Component;
                IEnumerable<IGH_Param> parameters = component != null ? component.Params.Input :
                    obj is IGH_Param parameter ? new[]{ parameter } : Enumerable.Empty<IGH_Param>();
                foreach (var p in parameters)
                {
                    if (++inputs > 192 || OutOfTime()) { Limit(inputs > 192 ? "input-port limit reached" : "SDK time limit reached"); break; }
                    var input = new DependencyInput { Id = p.InstanceGuid, Name = p.Name, TypeName = ParameterTypeMetadata.Describe(() => p.Type), Count = p.VolatileDataCount, Optional = p.Optional, SourcesInspected = false, Index = component == null ? -1 : component.Params.Input.IndexOf(p), Nickname = p.NickName };
                    input.Description = p.Description; input.TreeSummary = GrasshopperTutorContext.TreeSummary(p);
                    input.Direction = node.Floating ? "floating parameter" : "input";
                    var persistent = GrasshopperTutorContext.Persistent(p);
                    if (persistent != null) { input.AssignmentKnown = true; input.HasAssignedData = persistent.DataCount > 0; }
                    node.Inputs.Add(input);
                    input.Observations.AddRange(Warnings(p));
                    InspectData(p, input.Observations);
                    trees.TryGetValue(p.InstanceGuid, out input.Tree); referencesByPort.TryGetValue(p.InstanceGuid, out input.References);
                    bool sourcesComplete = true;
                    foreach (var source in p.Sources)
                    {
                        if (++links > DependencyAnalysis.MaxEdges || OutOfTime()) { Limit(links > DependencyAnalysis.MaxEdges ? "SDK wire limit reached" : "SDK time limit reached"); sourcesComplete = false; break; }
                        var owner = source.Attributes?.GetTopLevel?.DocObject;
                        // Missing ownership is unknown; never guess an owner by name, screen position or type.
                        if (owner == null) { Limited = true; sourcesComplete = false; input.Observations.Add(new DiagnosticObservation { Kind = ObservationKind.InspectionNote, Message = "A connected source's owning object could not be inspected." }); continue; }
                        if (document != null && !ReferenceEquals(owner.OnPingDocument(), document))
                        {
                            Limit("connected source belongs to a different or unavailable document"); sourcesComplete = false;
                            input.Observations.Add(new DiagnosticObservation { Kind = ObservationKind.InspectionNote,
                                Message = "A source outside this live document was not inspected; no cross-document connection is inferred." });
                            continue;
                        }
                        objects[owner.InstanceGuid] = owner;
                        var link = new DependencyLink { OwnerId = owner.InstanceGuid, OwnerName = Label(owner),
                            OutputId = source.InstanceGuid, OutputName = source.Name, TypeName = ParameterTypeMetadata.Describe(() => source.Type), Count = source.VolatileDataCount,
                            Index = owner is IGH_Component sourceComponent ? sourceComponent.Params.Output.IndexOf(source) : -1, Nickname = source.NickName };
                        link.Observations.AddRange(Warnings(source));
                        InspectData(source, link.Observations);
                        trees.TryGetValue(source.InstanceGuid, out link.Tree); referencesByPort.TryGetValue(source.InstanceGuid, out link.References);
                        input.Sources.Add(link);
                    }
                    input.SourcesInspected = sourcesComplete;
                }
            }
            catch (Exception)
            {
                Limit("SDK object inspection threw; boundary is opaque");
                node.Boundary = true;
            }
            return node;
        }
        private void InspectData(IGH_Param p, List<DiagnosticObservation> evidence)
        {
            evidence.AddRange(sampling.ReadOnce(p.InstanceGuid, facts => InspectUniquePort(p, facts)));
            Limited |= sampling.Limited;
        }
        private void InspectUniquePort(IGH_Param p, List<DiagnosticObservation> evidence)
        {
            if (OutOfTime()) return;
            // A null-rich branch cannot spend the saved-reference budget. Repeated wires reuse this read.
            var references = GrasshopperReferences.Inspect(p, clock, 45, sampling.ReferenceCapacity);
            sampling.ReferencesRead(references.Sampled, references.Complete);
            Limited |= sampling.Limited;
            var provenance = new ReferenceEvidence { Available = GrasshopperTutorContext.Persistent(p) != null,
                Assigned = references.Ids.Count > 0, Complete = references.Complete, Sampled = references.Sampled,
                Missing = references.Missing, Resolved = references.Resolved };
            provenance.Ids.AddRange(references.Ids); referencesByPort[p.InstanceGuid] = provenance;
            evidence.Add(new DiagnosticObservation { Kind = ObservationKind.ReferenceState, Complete = provenance.Complete, Message = provenance.Summary });
            if (references.Missing > 0) evidence.Add(new DiagnosticObservation {
                Kind = ObservationKind.MissingReferenceInActiveDocument, Count = references.Missing, Complete = references.Complete,
                Message = provenance.Summary });
            var tree = GrasshopperTreeInspection.Capture(p, sampling, clock); trees[p.InstanceGuid] = tree;
            evidence.AddRange(tree.Observations());
            if (!tree.Complete) Limit("tree branch/item sample incomplete (maximum eight branches and eight items per port)");
        }
    }
}




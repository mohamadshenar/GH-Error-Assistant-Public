using System;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant
{
    internal static class GrasshopperVerification
    {
        // User-invoked, bounded read-only snapshot. No solve, expiry, reference loading or data mutation.
        internal static VerificationSnapshot Capture(GH_Document doc, Report report, long revision, VerificationSnapshot baseline = null)
        {
            var s = new VerificationSnapshot { DocumentId = doc.DocumentID, DocumentToken = doc.RuntimeID.ToString(),
                OwnerId = report.OwnerId, OriginalMessage = report.Message, SolutionRevision = revision,
                OriginalSeverity = report.Severity, FocusInputId = report.FocusInputId,
                Ready = doc.Enabled && GH_Document.EnableSolutions && doc.SolutionState != GH_ProcessStep.Process };
            s.Selection.AddRange(doc.SelectedObjects().Take(201).Select(o => o.InstanceGuid));
            if (s.Selection.Count > 200) s.Complete = false;
            var root = doc.FindObject(s.OwnerId, false);
            s.TargetExists = root != null;
            if (!s.Ready || root == null) { s.Complete = false; return s; }
            var timer = Stopwatch.StartNew();
            Action<IGH_ActiveObject, Guid, string> messages = (o, ownerId, ownerName) => {
                foreach (var level in new[]{ GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning })
                {
                    var recorded = o.RuntimeMessages(level);
                    int remaining = Math.Max(0, 500 - s.Messages.Count);
                    s.Messages.AddRange(recorded.Take(remaining));
                    foreach (var text in recorded.Take(remaining)) s.RuntimeMessages.Add(new RuntimeObservation {
                        OwnerId = ownerId, ParameterId = o is IGH_Param port ? port.InstanceGuid : Guid.Empty, Severity = level.ToString(), Message = text,
                        Location = ownerName + (o.InstanceGuid == ownerId ? "" : " / " + o.Name) });
                    if (recorded.Count > remaining) { s.Complete = false; s.MessagesComplete = false; }
                }
            };
            if (root is IGH_ActiveObject active) messages(active, root.InstanceGuid, root.Name);
            if (root is IGH_ActiveObject rootActive && rootActive.Phase == GH_SolutionPhase.Blank) { s.Complete = false; s.MessagesComplete = false; }
            if (root is IGH_Component component)
                foreach (var p in component.Params.Input.Concat(component.Params.Output).Take(256))
                {
                    if (timer.ElapsedMilliseconds >= 75) { s.Complete = false; s.MessagesComplete = false; break; }
                    if (p.Phase == GH_SolutionPhase.Blank) s.MessagesComplete = false;
                    messages(p, root.InstanceGuid, root.Name);
                }
            if (root is IGH_Component allPorts && allPorts.Params.Input.Count + allPorts.Params.Output.Count > 256)
            { s.Complete = false; s.MessagesComplete = false; }
            var targets = baseline == null ? report.Tutor.Guidance.LocateTargets.Select(t => new InputObservation {
                OwnerId = t.OwnerId, ParameterId = t.ParameterId, Location = t.Location }).ToList() : baseline.Inputs;
            if (baseline == null)
                targets = targets.SelectMany(t => t.ParameterId == Guid.Empty && doc.FindObject(t.OwnerId, false) is IGH_Component c ?
                    c.Params.Input.Take(13).Select(p => new InputObservation { OwnerId = t.OwnerId, ParameterId = p.InstanceGuid, Location = c.Name + " / " + p.Name }) :
                    new[]{ t }.AsEnumerable()).GroupBy(t => t.Key).Select(g => g.First()).ToList();
            // Compare retained upstream owners and current direct recipients of the original object.
            // This catches a new downstream error after repairing the original input, without scanning the document.
            var outputPorts = root is IGH_Component outputOwner ? outputOwner.Params.Output.AsEnumerable() : root is IGH_Param outputPort ? new[]{ outputPort } : Enumerable.Empty<IGH_Param>();
            var boundedOutputs = outputPorts.Take(33).ToList();
            if (boundedOutputs.Count > 32 || boundedOutputs.Take(32).Any(p => p.Recipients.Count > 16)) { s.Complete = false; s.MessagesComplete = false; }
            var recipients = boundedOutputs.Take(32).SelectMany(p => p.Recipients.Take(16)).Select(p => p.Attributes?.GetTopLevel?.DocObject?.InstanceGuid ?? Guid.Empty);
            var related = targets.Select(t => t.OwnerId).Concat(recipients).Where(id => id != root.InstanceGuid && id != Guid.Empty).Distinct().Take(13).ToList();
            if (related.Count > 12) { s.Complete = false; s.MessagesComplete = false; }
            foreach (var id in related.Take(12))
            {
                if (timer.ElapsedMilliseconds >= 75) { s.Complete = false; s.MessagesComplete = false; break; }
                var obj = doc.FindObject(id, false);
                if (obj is IGH_ActiveObject relatedActive) { if (relatedActive.Phase == GH_SolutionPhase.Blank) { s.Complete = false; s.MessagesComplete = false; } messages(relatedActive, id, obj.Name); }
                if (obj is IGH_Component relatedComponent)
                {
                    if (relatedComponent.Params.Input.Count + relatedComponent.Params.Output.Count > 32) { s.Complete = false; s.MessagesComplete = false; }
                    foreach (var port in relatedComponent.Params.Input.Concat(relatedComponent.Params.Output).Take(32))
                    { if (timer.ElapsedMilliseconds >= 75) { s.Complete = false; s.MessagesComplete = false; break; } messages(port, id, obj.Name); }
                }
            }
            if (targets.Count > 12) s.Complete = false;
            foreach (var target in targets.Take(12))
            {
                if (timer.ElapsedMilliseconds >= 75) { s.Complete = false; break; }
                var owner = doc.FindObject(target.OwnerId, false);
                IGH_Param parameter = owner as IGH_Param;
                if (owner is IGH_Component c)
                    parameter = c.Params.Input.Concat(c.Params.Output).FirstOrDefault(p => p.InstanceGuid == target.ParameterId);
                if (parameter == null) { s.Complete = false; continue; }
                var observation = new InputObservation { OwnerId = target.OwnerId, ParameterId = parameter.InstanceGuid,
                    Location = target.Location, Count = parameter.VolatileDataCount,
                    Tree = GrasshopperTutorContext.TreeSummary(parameter), Complete = parameter.Phase != GH_SolutionPhase.Blank };
                observation.WarningState = string.Join("\n", new[]{ GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning }
                    .SelectMany(level => parameter.RuntimeMessages(level).Take(8)));
                if (owner is IGH_ActiveObject ownerActive && !ReferenceEquals(owner, parameter))
                    observation.WarningState += "\n" + string.Join("\n", new[]{ GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning }
                        .SelectMany(level => ownerActive.RuntimeMessages(level).Take(8)));
                int count = 0;
                var referenceInspection = GrasshopperReferences.Inspect(parameter, timer, 75);
                observation.ReferencesComplete = referenceInspection.Complete;
                observation.MissingReferenceCount = referenceInspection.Missing;
                observation.ResolvedReferenceCount = referenceInspection.Resolved;
                observation.MissingReference = referenceInspection.Missing > 0;
                var references = new System.Collections.Generic.List<Guid>(referenceInspection.Ids);
                var data = parameter.VolatileData.AllData(false);
                if (parameter.VolatileDataCount == 0) data = GrasshopperTutorContext.Persistent(parameter)?.AllData(false) ?? data;
                foreach (var item in data)
                {
                    if (++count > 8 || timer.ElapsedMilliseconds >= 75) { observation.Complete = false; break; }
                    if (item == null) { observation.HasMissing = true; continue; }
                    var geometry = item as Grasshopper.Kernel.Types.IGH_GeometricGoo;
                    bool unloaded = geometry != null && geometry.IsReferencedGeometry && !geometry.IsGeometryLoaded;
                    if (geometry != null && geometry.IsReferencedGeometry && geometry.ReferenceID != Guid.Empty)
                    {
                        references.Add(geometry.ReferenceID);
                        var rhino = Rhino.RhinoDoc.ActiveDoc;
                        if (rhino == null) observation.Complete = false;
                        else observation.MissingReference |= rhino.Objects.FindId(geometry.ReferenceID) == null;
                    }
                    observation.HasInvalid |= !unloaded && !item.IsValid;
                    observation.SampleUsable |= !unloaded && item.IsValid;
                    if (unloaded) observation.Complete = false;
                }
                observation.References = string.Join(",", references.Distinct().OrderBy(id => id));
                s.Inputs.Add(observation);
            }
            s.Complete &= baseline == null ? report.Context?.Graph?.Partial != true && report.Context?.InspectionPartial != true : s.Inputs.Count == baseline.Inputs.Count;
            return s;
        }
    }
}



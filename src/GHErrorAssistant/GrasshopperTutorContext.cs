using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GHErrorAssistant.Core;

namespace GHErrorAssistant
{
    internal static class GrasshopperTutorContext
    {
        // Capture only type/count/reference facts; never ask Grasshopper to solve or load geometry.
        internal static TutorContext Capture(IGH_DocumentObject root, Guid focus)
        {
            var c = new TutorContext { Component = root.Name + (root.NickName == root.Name ? "" : " (" + root.NickName + ")"), OwnerId = root.InstanceGuid, ComponentType = root.GetType().FullName, Description = root.Description };
            var clock = Stopwatch.StartNew();
            try
            {
                var component = root as IGH_Component;
                IGH_Param parameter = root as IGH_Param;
                if (component != null)
                {
                    var inputs = component.Params.Input.Take(32).ToList();
                    c.InputDescriptions.AddRange(inputs.Take(8).Select(p => p.Name + " (" + ParameterTypeMetadata.Describe(() => p.Type) + "): " + p.Description));
                    if (component.Params.Input.Count > 32) c.InspectionPartial = true;
                    var affected = inputs.Where(p => p.RuntimeMessageLevel == GH_RuntimeMessageLevel.Warning || p.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error ||
                        (!p.Optional && p.VolatileDataCount == 0)).ToList();
                    foreach (var p in affected.Take(4)) c.AffectedInputs.Add(p.Name + " (" + ParameterTypeMetadata.Describe(() => p.Type) + ")");
                    if (focus != Guid.Empty)
                        parameter = inputs.Concat(component.Params.Output.Take(32)).FirstOrDefault(p => p.InstanceGuid == focus);
                    else
                    {
                        var warningInputs = affected.Where(p => p.RuntimeMessageLevel == GH_RuntimeMessageLevel.Warning || p.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error).ToList();
                        // Do not invent a single failing input when several candidates exist.
                        if (warningInputs.Count == 1) parameter = warningInputs[0];
                        else if (warningInputs.Count == 0 && affected.Count == 1) parameter = affected[0];
                    }
                    if (parameter != null) c.Direction = component.Params.Input.Contains(parameter) ? "input" : "output";
                }
                else if (parameter != null) { c.Floating = true; c.Direction = "floating parameter"; }
                if (parameter == null) return c;
                c.ParameterId = parameter.InstanceGuid; c.Parameter = parameter.Name; c.ParameterType = ParameterTypeMetadata.Describe(() => parameter.Type);
                c.Description = parameter.Description; c.TreeSummary = TreeSummary(parameter);
                c.Optional = parameter.Optional; c.ItemCount = parameter.VolatileDataCount;
                IGH_Structure persistent = Persistent(parameter);
                if (persistent != null) { c.AssignmentKnown = true; c.HasAssignedData = persistent.DataCount > 0; }
                IEnumerable<IGH_Goo> data = parameter.VolatileData.AllData(false);
                if (parameter.VolatileDataCount == 0 && persistent != null) data = persistent.AllData(false);
                Sample(data, c, null, clock);
                var references = GrasshopperReferences.Inspect(parameter, clock, 40);
                c.InspectionPartial |= !references.Complete;
                c.MissingReferenceInActiveDocument |= references.Missing > 0;
                c.MissingReferenceCount = references.Missing > 0 ? (int?)references.Missing : null;
                if (references.Missing > 0) c.ReferenceLocation = c.Location;
                int seen = 0;
                foreach (var source in parameter.Sources)
                {
                    if (++seen > 8 || clock.ElapsedMilliseconds >= 40) { c.InspectionPartial = true; break; }
                    var owner = source.Attributes?.GetTopLevel?.DocObject;
                    var s = new TutorSource { Component = owner?.Name ?? "Connected source (owner unavailable)", Parameter = source.Name,
                        DeclaredType = ParameterTypeMetadata.Describe(() => source.Type), ItemCount = source.VolatileDataCount, OwnerId = owner?.InstanceGuid ?? Guid.Empty, ParameterId = source.InstanceGuid };
                    foreach (var level in new[]{GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning})
                    {
                        s.Warnings.AddRange(source.RuntimeMessages(level).Take(2));
                        if (owner is IGH_ActiveObject active && !ReferenceEquals(active, source)) s.Warnings.AddRange(active.RuntimeMessages(level).Take(2));
                    }
                    Sample(source.VolatileData.AllData(false), c, s, clock);
                    c.Sources.Add(s);
                }
                c.SourcesKnown = seen == parameter.Sources.Count && !c.InspectionPartial;
            }
            catch (Exception) { c.InspectionPartial = true; c.SourcesKnown = false; }
            return c;
        }
        internal static IGH_Structure Persistent(IGH_Param p)
        {
            if (p is GH_PersistentParam<GH_Point> point) return point.PersistentData;
            if (p is GH_PersistentParam<GH_Curve> curve) return curve.PersistentData;
            if (p is GH_PersistentParam<GH_Surface> surface) return surface.PersistentData;
            if (p is GH_PersistentParam<GH_Brep> brep) return brep.PersistentData;
            if (p is GH_PersistentParam<GH_Number> number) return number.PersistentData;
            if (p is GH_PersistentParam<GH_Integer> integer) return integer.PersistentData;
            if (p is GH_PersistentParam<GH_Mesh> mesh) return mesh.PersistentData;
            if (p is GH_PersistentParam<GH_String> text) return text.PersistentData;
            if (p is GH_PersistentParam<GH_Vector> vector) return vector.PersistentData;
            if (p is GH_PersistentParam<GH_Plane> plane) return plane.PersistentData;
            if (p is GH_PersistentParam<GH_Boolean> boolean) return boolean.PersistentData;
            return null;
        }
        internal static string TreeSummary(IGH_Param p)
        {
            var tree = p.VolatileData;
            return "Observed groups: " + tree.PathCount + "; items: " + tree.DataCount + ". " +
                string.Join("; ", Enumerable.Range(0, Math.Min(4, tree.PathCount)).Select(n => tree.get_Path(n) + ": " + tree.get_Branch(n).Count + " items")) +
                (tree.PathCount > 4 ? "; remaining groups not listed." : "");
        }
        private static void Sample(IEnumerable<IGH_Goo> data, TutorContext c, TutorSource source, Stopwatch clock)
        {
            int count = 0;
            foreach (var item in data)
            {
                if (++count > 8 || clock.ElapsedMilliseconds >= 40) { c.InspectionPartial = true; break; }
                if (item == null) { if (source == null) c.HasNullItem = true; else source.HasNullItem = true; continue; }
                if (source != null && !source.SampleTypes.Contains(item.TypeName)) source.SampleTypes.Add(item.TypeName);
                var geometry = item as IGH_GeometricGoo;
                bool unloaded = geometry != null && geometry.IsReferencedGeometry && !geometry.IsGeometryLoaded;
                if (source == null) c.ReferenceNotLoaded |= unloaded;
                if (!unloaded && !item.IsValid) { if (source == null) c.HasInvalidItem = true; else source.HasInvalidItem = true; }
                if (geometry != null && geometry.IsReferencedGeometry && geometry.ReferenceID != Guid.Empty)
                {
                    var rhino = Rhino.RhinoDoc.ActiveDoc;
                    if (rhino != null && rhino.Objects.FindId(geometry.ReferenceID) == null)
                    {
                        if (source == null) c.MissingReferenceInActiveDocument = true;
                        else source.MissingReferenceInActiveDocument = true;
                        if (source == null) c.ReferenceLocation = c.Location;
                    }
                }
            }
        }
    }
}



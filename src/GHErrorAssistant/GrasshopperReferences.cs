using System;
using System.Collections.Generic;
using System.Diagnostics;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace GHErrorAssistant
{
    // Read saved reference metadata separately from volatile data. Never LoadGeometry/solve/replace.
    internal sealed class ReferenceInspection
    {
        internal int Sampled, Missing, Resolved;
        internal bool Complete = true;
        internal readonly List<Guid> Ids = new List<Guid>();
    }
    internal static class GrasshopperReferences
    {
        internal static ReferenceInspection Inspect(IGH_Param parameter, Stopwatch clock, int milliseconds, int maximum = 8)
        {
            var result = new ReferenceInspection();
            var persistent = GrasshopperTutorContext.Persistent(parameter);
            if (persistent == null) return result;
            foreach (var item in persistent.AllData(false))
            {
                if (result.Sampled >= maximum || clock.ElapsedMilliseconds >= milliseconds) { result.Complete = false; break; }
                result.Sampled++;
                if (!(item is IGH_GeometricGoo geometry) || !geometry.IsReferencedGeometry || geometry.ReferenceID == Guid.Empty) continue;
                result.Ids.Add(geometry.ReferenceID);
                var rhino = Rhino.RhinoDoc.ActiveDoc;
                if (rhino == null) { result.Complete = false; continue; }
                if (rhino.Objects.FindId(geometry.ReferenceID) == null) result.Missing++;
                else result.Resolved++;
            }
            return result;
        }
    }
}

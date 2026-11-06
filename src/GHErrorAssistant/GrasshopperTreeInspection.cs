using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant
{
    internal static class GrasshopperTreeInspection
    {
        internal static TreeEvidence Capture(IGH_Param parameter, InspectionSampling budget, Stopwatch clock)
        {
            var result = new TreeEvidence();
            try
            {
                var tree = parameter.VolatileData;
                result.Available = true; result.PathCount = tree.PathCount; result.ItemCount = tree.DataCount;
                var lists = new List<IList>();
                for (int i = 0; i < Math.Min(TreeEvidence.MaximumBranches, tree.PathCount); i++)
                {
                    if (clock.ElapsedMilliseconds >= 45) break;
                    var list = tree.get_Branch(i); lists.Add(list);
                    result.Branches.Add(new BranchEvidence { Path = tree.get_Path(i).ToString(), ItemCount = list.Count });
                }
                // Round-robin within the bounded branch sample avoids spending all eight slots on branch zero.
                int sampled = 0;
                for (int offset = 0; offset < 8 && sampled < 8; offset++)
                    for (int branch = 0; branch < lists.Count && sampled < 8; branch++)
                    {
                        if (offset >= lists[branch].Count) continue;
                        if (clock.ElapsedMilliseconds >= 45 || !budget.TakeItem()) return result;
                        var value = lists[branch][offset]; sampled++;
                        var goo = value as IGH_Goo;
                        var state = value == null ? ItemState.Null : ItemState.Opaque;
                        string type = value?.GetType().Name ?? "";
                        // Vendor implementations are opaque. Do not call arbitrary vendor validity/geometry getters.
                        if (goo != null && goo.GetType().Assembly == typeof(GH_Point).Assembly)
                        {
                            var geometry = goo as IGH_GeometricGoo;
                            bool unloaded = geometry != null && geometry.IsReferencedGeometry && !geometry.IsGeometryLoaded;
                            if (!unloaded) state = goo.IsValid ? ItemState.Valid : ItemState.Invalid;
                        }
                        result.Branches[branch].Add(state, type);
                    }
                result.Complete = result.Branches.Count == result.PathCount;
                foreach (var branch in result.Branches) result.Complete &= branch.Complete;
            }
            catch (Exception) { result.Complete = false; }
            return result;
        }
    }
}

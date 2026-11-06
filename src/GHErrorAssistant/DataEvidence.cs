using System;
using System.Collections.Generic;
using System.Linq;

namespace GHErrorAssistant.Diagnostics
{
    internal enum EvidenceClass { VerifiedObservation, CandidateExplanation, ConfirmedCausalResult }
    internal enum ItemState { Null, Valid, Invalid, Opaque }
    internal enum ReferenceState { Unavailable, Unassigned, Resolved, Unresolved, Partial }
    internal sealed class BranchEvidence
    {
        internal string Path = "";
        internal int ItemCount, Sampled, Nulls, Valid, Invalid, Opaque;
        internal readonly List<string> Types = new List<string>();
        internal bool Complete => Sampled == ItemCount;
        internal void Add(ItemState state, string type)
        {
            Sampled++;
            switch (state) { case ItemState.Null: Nulls++; break; case ItemState.Valid: Valid++; break;
                case ItemState.Invalid: Invalid++; break; default: Opaque++; break; }
            if (!string.IsNullOrEmpty(type) && Types.Count < 8 && !Types.Contains(type)) Types.Add(type);
        }
    }
    // Scalar snapshot only. Different paths/counts are facts, never proof of a tree mismatch.
    internal sealed class TreeEvidence
    {
        internal const int MaximumBranches = 8;
        internal int PathCount, ItemCount;
        internal bool Available, Complete;
        internal readonly List<BranchEvidence> Branches = new List<BranchEvidence>();
        internal IEnumerable<DiagnosticObservation> Observations()
        {
            if (!Available) yield break;
            yield return new DiagnosticObservation { Kind = ObservationKind.TreeStructure,
                Complete = Complete, Message = "Observed tree: " + PathCount + " branches, " + ItemCount + " items. " +
                string.Join("; ", Branches.Select(b => b.Path + ": " + b.ItemCount + " items, sampled " + b.Sampled +
                    " (null " + b.Nulls + ", valid " + b.Valid + ", invalid " + b.Invalid + ", opaque " + b.Opaque + ")")) +
                (Complete ? " Inspection complete." : " Inspection partial; unsampled items unknown.") +
                " Required paths and item counts are not established; no tree repair is inferred." };
            if (Branches.Any(b => b.Nulls > 0)) yield return new DiagnosticObservation { Kind = ObservationKind.NullSample,
                Complete = Complete, Message = "The inspected volatile tree contains null items; other items may be usable. This does not establish missing Rhino references." };
            if (Branches.Any(b => b.Invalid > 0)) yield return new DiagnosticObservation { Kind = ObservationKind.InvalidDataSample,
                Complete = Complete, Message = "An inspected volatile item reports IsValid=false. The cause is unknown." };
        }
    }
    internal sealed class ReferenceEvidence
    {
        internal bool Available, Assigned, Complete;
        internal int Sampled, Missing, Resolved;
        internal readonly List<Guid> Ids = new List<Guid>();
        internal ReferenceState State => !Available ? ReferenceState.Unavailable : !Complete ? ReferenceState.Partial :
            !Assigned ? ReferenceState.Unassigned : Missing > 0 ? ReferenceState.Unresolved :
            Resolved > 0 ? ReferenceState.Resolved : ReferenceState.Unavailable;
        internal string Summary => "Saved reference state: " + State + "; checked " + Sampled +
            "; absent in active Rhino document " + Missing + "; resolved " + Resolved +
            (Complete ? "." : ". Partial inspection; counts are lower bounds, not totals.") +
            " Absence here does not establish deletion or downstream causation.";
    }
}

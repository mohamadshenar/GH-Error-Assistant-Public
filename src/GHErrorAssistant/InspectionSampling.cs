using System;
using System.Collections.Generic;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant
{
    internal sealed class InspectionSampling
    {
        internal const int Maximum = 192;
        private int items, references;
        private readonly Dictionary<Guid, List<DiagnosticObservation>> ports = new Dictionary<Guid, List<DiagnosticObservation>>();
        internal bool Limited { get; private set; }
        internal readonly List<string> Limits = new List<string>();
        internal int ItemsRead => items;
        internal int ReferenceEntriesRead => references;
        private void Limit(string reason) { Limited = true; if (!Limits.Contains(reason)) Limits.Add(reason); }
        internal int ReferenceCapacity => Math.Min(8, Math.Max(0, Maximum - references));
        internal void ReferencesRead(int count, bool complete)
        { references += count; if (!complete) Limit(references >= Maximum ? "saved-reference quota reached" : "saved-reference inspection incomplete"); }
        internal bool TakeItem()
        { if (items >= Maximum) { Limit("ordinary-item quota reached"); return false; } items++; return true; }
        internal List<DiagnosticObservation> ReadOnce(Guid id, Action<List<DiagnosticObservation>> read)
        {
            if (!ports.TryGetValue(id, out var facts)) { facts = new List<DiagnosticObservation>(); ports[id] = facts; read(facts); }
            return facts;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace GHErrorAssistant.Diagnostics
{
    internal enum RuntimeCategory { Unknown, CollectionBounds, IndexOutOfRange, ArgumentOutOfRange, NullReference, InvalidCast, InvalidOperation }
    internal enum ObservationKind { Other, RuntimeWarning, MissingDataWarning, ReferenceWarning, ConversionWarning, InvalidDataSample, NullSample, MissingReferenceInActiveDocument, EmptyCache, InspectionNote, TreeStructure, ReferenceState }
    internal sealed class DiagnosticObservation
    {
        internal ObservationKind Kind;
        internal string Message = "", Severity = "";
        internal string Component = "", Parameter = "";
        internal bool Runtime;
        internal int? Count;
        internal Guid OwnerId, ParameterId;
        internal EvidenceClass Classification => EvidenceClass.VerifiedObservation;
        internal bool Complete = true;
        internal static bool ReferenceMessage(string message) => Has(message, "invalid reference", "reference is invalid", "referenced object") ||
            (Has(message, "referenced ") && Has(message, "could not be found", "cannot be found", "not found in the Rhino document"));
        internal static DiagnosticObservation Warning(string message, string severity)
        {
            ObservationKind kind = ObservationKind.RuntimeWarning;
            if (Has(message, "failed to collect data", "failed data collection", "insufficient data", "missing data", "no data supplied")) kind = ObservationKind.MissingDataWarning;
            else if (ReferenceMessage(message)) kind = ObservationKind.ReferenceWarning;
            else if (Has(message, "failed to convert", "cannot convert", "data conversion failed", "invalid cast")) kind = ObservationKind.ConversionWarning;
            return new DiagnosticObservation { Kind = kind, Message = message ?? "", Severity = severity, Runtime = true };
        }
        internal static bool Has(string message, params string[] values) => values.Any(s => (message ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        internal static DiagnosticObservation Legacy(string message)
        {
            if (Has(message, "Warning:", "Error:")) return Warning(message, "Runtime message");
            return new DiagnosticObservation { Message = message ?? "", Kind = ObservationKind.Other };
        }
        internal DiagnosticObservation At(string component, string parameter) => new DiagnosticObservation {
            Kind = Kind, Message = Message, Severity = Severity, Runtime = Runtime, Component = component, Parameter = parameter, Count = Count, OwnerId = OwnerId, ParameterId = ParameterId, Complete = Complete
        };
    }
    internal sealed class VerifiedConnection
    {
        internal Guid SourceId, TargetId;
        internal Guid SourceParameterId, TargetParameterId;
        internal string SourceComponent, SourcePort, TargetComponent, TargetPort;
        internal string SourceType = "";
        internal int? SourceCount;
        internal int SourceIndex = -1, TargetIndex = -1;
        internal string SourceNickname = "", TargetNickname = "";
        internal string Description => SourceComponent + " / " + SourcePort + " → " + TargetComponent + " / " + TargetPort;
    }
    internal sealed class UpstreamIssue
    {
        internal Guid ComponentId, ParameterId;
        internal string Component, Parameter = "", ParameterType = "", Description = "", TreeSummary = "";
        internal DiagnosticObservation Observation;
        internal int Depth;
        internal string Direction = "input";
        internal int? ItemCount;
        internal bool SourcesKnown, Floating, AssignmentKnown, HasAssignedData, Optional;
        internal EvidenceClass Classification => EvidenceClass.CandidateExplanation;
        internal int PortIndex = -1;
        internal string Nickname = "";
        internal TreeEvidence Tree;
        internal ReferenceEvidence References;
        internal readonly HashSet<Guid> Branches = new HashSet<Guid>();
        internal readonly List<VerifiedConnection> Path = new List<VerifiedConnection>();
        internal readonly List<VerifiedConnection> Feeders = new List<VerifiedConnection>();
        internal string Location => string.IsNullOrEmpty(Parameter) ? Component : Component + " / " + Parameter;
        internal int Priority => (Observation.Kind == ObservationKind.ReferenceWarning || Observation.Kind == ObservationKind.MissingDataWarning ? 100 :
            Observation.Kind == ObservationKind.ConversionWarning ? 90 : Observation.Kind == ObservationKind.MissingReferenceInActiveDocument ? 85 :
            Observation.Kind == ObservationKind.NullSample || Observation.Kind == ObservationKind.InvalidDataSample ? 70 :
            Observation.Runtime && TechnicalException.Category(Observation.Message) == RuntimeCategory.Unknown ? 60 : Observation.Runtime ? 30 : 10) +
            (string.IsNullOrEmpty(Parameter) ? 0 : 5);
    }
    internal sealed class InspectionFrontier
    {
        internal Guid OwnerId;
        internal string Name, Reason;
        internal int Depth;
        internal bool CanContinue;
        internal Guid Generation;
    }
    internal sealed class GraphEvidence
    {
        internal Guid RootId;
        internal Guid DocumentId, Generation = Guid.NewGuid();
        internal string DocumentToken = "";
        internal DateTime CapturedUtc = DateTime.UtcNow;
        internal int NodesRead;
        internal long ElapsedMilliseconds;
        internal string RootName = "";
        internal bool Partial, Cycle;
        internal readonly List<string> Frontiers = new List<string>();
        internal readonly List<InspectionFrontier> FrontierTargets = new List<InspectionFrontier>();
        internal readonly List<DiagnosticObservation> Observations = new List<DiagnosticObservation>();
        internal readonly List<VerifiedConnection> Connections = new List<VerifiedConnection>();
        internal readonly List<UpstreamIssue> Issues = new List<UpstreamIssue>();
        internal bool ContainsEdge(VerifiedConnection edge) => Connections.Any(c => c.SourceId == edge.SourceId && c.TargetId == edge.TargetId &&
            c.SourceParameterId == edge.SourceParameterId && c.TargetParameterId == edge.TargetParameterId && c.SourcePort == edge.SourcePort && c.TargetPort == edge.TargetPort);
        internal bool HasPathToRoot(UpstreamIssue issue)
        {
            if (issue.Depth <= 0 || issue.ComponentId == RootId || issue.Path.Count == 0 || issue.Path[0].SourceId != issue.ComponentId) return false;
            var seen = new HashSet<Guid> { RootId };
            for (int i = 0; i < issue.Path.Count; i++)
            {
                var edge = issue.Path[i];
                if (!seen.Add(edge.SourceId)) return false;
                if (!ContainsEdge(edge)) return false;
                if (i > 0 && issue.Path[i - 1].TargetId != edge.SourceId) return false;
            }
            return issue.Path[issue.Path.Count - 1].TargetId == RootId;
        }
        internal List<UpstreamIssue> RankedUpstream() => BeginnerDiagnosis.Rank(new DiagnosticModel { ComponentId = RootId, Graph = this },
            new GHErrorAssistant.Core.TutorContext()).Select(t => t.Issue).Where(HasPathToRoot).ToList();
    }
    internal sealed class DiagnosticModel
    {
        internal string OriginalRuntimeMessage, ComponentName, ComponentType;
        internal Guid ComponentId;
        internal RuntimeCategory ErrorCategory;
        internal GraphEvidence Graph;
        internal readonly List<DiagnosticObservation> VerifiedObservations = new List<DiagnosticObservation>();
        internal readonly List<UpstreamIssue> CandidateUpstreamIssues = new List<UpstreamIssue>();
        internal readonly List<string> Uncertainty = new List<string>();
        internal string RecommendedFirstAction;
        internal BeginnerGuidance Guidance;
    }
    internal static class TechnicalException
    {
        internal static RuntimeCategory Category(string message)
        {
            if (DiagnosticObservation.Has(message, "Offset and length were out of bounds", "count is greater than the number of elements from index", "array bounds", "collection bounds")) return RuntimeCategory.CollectionBounds;
            if (DiagnosticObservation.Has(message, "IndexOutOfRangeException", "index was outside the bounds", "index out of range", "index is out of range")) return RuntimeCategory.IndexOutOfRange;
            if (DiagnosticObservation.Has(message, "ArgumentOutOfRangeException", "argument out of range", "Specified argument was out of the range", "non-negative and less than the size of the collection")) return RuntimeCategory.ArgumentOutOfRange;
            if (DiagnosticObservation.Has(message, "NullReferenceException", "Object reference not set to an instance")) return RuntimeCategory.NullReference;
            if (DiagnosticObservation.Has(message, "InvalidCastException", "Specified cast is not valid", "Unable to cast object", "invalid cast")) return RuntimeCategory.InvalidCast;
            if (DiagnosticObservation.Has(message, "InvalidOperationException", "Operation is not valid due to the current state", "Collection was modified; enumeration operation", "Sequence contains no elements", "Sequence contains more than one")) return RuntimeCategory.InvalidOperation;
            return RuntimeCategory.Unknown;
        }
        internal static string Plain(RuntimeCategory category, string name)
        {
            switch (category)
            {
                case RuntimeCategory.CollectionBounds: return name + " tried to use a range of items that extends beyond the available collection.";
                case RuntimeCategory.IndexOutOfRange: return name + " tried to access an item at a position that does not exist in a list or array.";
                case RuntimeCategory.ArgumentOutOfRange: return name + " passed an operation a value outside the range it accepts.";
                case RuntimeCategory.NullReference: return name + " tried to use an object that was not available.";
                case RuntimeCategory.InvalidCast: return name + " tried to use a value as a different type that the operation could not accept.";
                case RuntimeCategory.InvalidOperation: return name + " tried to perform an operation that was not allowed in its current state.";
                default: return "";
            }
        }
        internal static string Meaning(RuntimeCategory category)
        {
            switch (category)
            {
                case RuntimeCategory.CollectionBounds: return "An operation requested a starting position and number of items that do not fit inside a collection. The message does not identify which collection, or why that range was requested.";
                case RuntimeCategory.IndexOutOfRange: return "An operation used an item position outside the available list or array. The message does not identify the offending list or prove that an upstream input caused it.";
                case RuntimeCategory.ArgumentOutOfRange: return "A value supplied to an internal operation was outside its accepted range. This does not identify a particular Grasshopper input.";
                case RuntimeCategory.NullReference: return "An internal operation expected an object but had no object to use. This exception alone does not prove that a Grasshopper input contains null data.";
                case RuntimeCategory.InvalidCast: return "An internal operation could not use a value as the requested type. This message alone does not establish both Grasshopper data types or a particular input mismatch.";
                case RuntimeCategory.InvalidOperation: return "An operation could not run in the state it encountered. The message does not establish whether its cause is input data, component state, or the component's implementation.";
                default: return "The exact meaning of this message is unknown to the offline library.";
            }
        }
    }
}

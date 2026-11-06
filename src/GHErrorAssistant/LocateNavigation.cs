using System;

namespace GHErrorAssistant.Diagnostics
{
    internal sealed class LocateRequest
    {
        internal readonly Guid DocumentId, OwnerId, ParameterId, ReportingOwnerId;
        internal LocateRequest(Guid document, Guid owner, Guid parameter, Guid reportingOwner)
        { DocumentId = document; OwnerId = owner; ParameterId = parameter; ReportingOwnerId = reportingOwner; }
    }
    internal struct LocateBounds
    {
        internal double X, Y, Width, Height;
        internal bool Valid => Finite(X) && Finite(Y) && Finite(Width) && Finite(Height) && Width > 0 && Height > 0;
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }
    // Navigation exposes selection/view operations only. No solve, undo, wire, value or geometry API is available here.
    internal interface ILocateSurface
    {
        Guid DocumentId { get; }
        bool SameDocumentInstance { get; }
        bool Ready { get; }
        int Width { get; }
        int Height { get; }
        bool ContainsOwner(Guid id);
        bool ContainsParameter(Guid owner, Guid parameter);
        bool IsTopLevel(Guid owner);
        LocateBounds Bounds(Guid owner);
        void SelectAndFrame(Guid owner, LocateBounds bounds, float zoom);
    }
    internal static class LocateNavigation
    {
        internal static bool TryNavigate(LocateRequest request, ILocateSurface surface)
        {
            if (request == null || surface == null || request.DocumentId == Guid.Empty || request.OwnerId == Guid.Empty ||
                request.ReportingOwnerId == Guid.Empty || request.DocumentId != surface.DocumentId || !surface.SameDocumentInstance || !surface.Ready) return false;
            if (!surface.ContainsOwner(request.OwnerId) || !surface.ContainsOwner(request.ReportingOwnerId) ||
                !surface.ContainsParameter(request.OwnerId, request.ParameterId) || !surface.IsTopLevel(request.OwnerId)) return false;
            var bounds = surface.Bounds(request.OwnerId);
            float zoom = ZoomToFit(bounds, surface.Width, surface.Height);
            if (zoom <= 0) return false;
            surface.SelectAndFrame(request.OwnerId, bounds, zoom);
            return true;
        }
        internal static float ZoomToFit(LocateBounds bounds, int width, int height)
        {
            if (!bounds.Valid || width < 100 || height < 100) return 0;
            double zoom = Math.Min(1.5, Math.Min(width * 0.8 / bounds.Width, height * 0.8 / bounds.Height));
            return zoom < 0.01 || double.IsNaN(zoom) ? 0 : (float)zoom;
        }
    }
}

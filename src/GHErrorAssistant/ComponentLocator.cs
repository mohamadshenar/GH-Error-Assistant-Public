using System;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.GUI.Canvas;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant
{
    internal static class ComponentLocator
    {
        internal static string Locate(Report report, InvestigationTarget target, WeakReference<GH_Document> sourceDocument)
        {
            string result = "Unable to locate this object now. Return to the same document, let it finish calculating and select the reporting object again.";
            // Resolve live objects only inside the UI-thread operation.
            try
            {
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                try
                {
                    var canvas = Instances.ActiveCanvas;
                    GH_Document original = null;
                    sourceDocument?.TryGetTarget(out original);
                    var surface = new Surface(canvas, original);
                    var request = new LocateRequest(report.DocumentId, target.OwnerId, target.ParameterId, report.OwnerId);
                    if (LocateNavigation.TryNavigate(request, surface))
                        result = "Located and selected " + target.Location + ". Other selections are preserved; monitoring may include this target.";
                    else if (LocatePolicy.ConfirmedMissingTarget(report.DocumentId, surface.DocumentId, target.OwnerId,
                        surface.SameDocumentInstance, surface.ContainsOwner(target.OwnerId)))
                        result = "This troubleshooting target no longer exists in the original document. Click Check Again to inspect the current problem; deletion does not verify a repair.";
                }
                catch (Exception) { result = "Unable to locate this object. Its document, parameter or canvas may be unavailable."; }
                }));
            }
            catch (Exception) { result = "Unable to access the Rhino UI thread. Try again after Rhino is ready."; }
            return result;
        }
        private sealed class Surface : ILocateSurface
        {
            private readonly GH_Canvas canvas;
            private readonly GH_Document doc, expected;
            internal Surface(GH_Canvas canvas, GH_Document expected) { this.canvas = canvas; doc = canvas?.Document; this.expected = expected; }
            public Guid DocumentId => doc?.DocumentID ?? Guid.Empty;
            public bool SameDocumentInstance => doc != null && ReferenceEquals(doc, expected);
            public bool Ready => doc != null && ReferenceEquals(canvas.Document, doc) && Instances.DocumentEditor?.Visible == true &&
                canvas.Visible && !canvas.IsDisposed && !canvas.IsActiveInteraction && !canvas.Painting && doc.SolutionState != GH_ProcessStep.Process;
            public int Width => canvas?.ClientSize.Width ?? 0;
            public int Height => canvas?.ClientSize.Height ?? 0;
            private IGH_DocumentObject Object(Guid id) => doc?.FindObject(id, false);
            public bool ContainsOwner(Guid id) => id != Guid.Empty && Object(id) != null;
            public bool ContainsParameter(Guid owner, Guid parameter)
            {
                var obj = Object(owner);
                return parameter == Guid.Empty || obj?.InstanceGuid == parameter ||
                    (obj is IGH_Component c && c.Params.Input.Concat(c.Params.Output).Any(p => p.InstanceGuid == parameter));
            }
            public bool IsTopLevel(Guid owner) => Object(owner)?.Attributes?.IsTopLevel == true;
            public LocateBounds Bounds(Guid owner)
            {
                var b = Object(owner).Attributes.Bounds;
                return new LocateBounds { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height };
            }
            public void SelectAndFrame(Guid owner, LocateBounds bounds, float zoom)
            {
                var obj = Object(owner);
                // Add to selection; never clear other objects or enter a cluster document.
                obj.Attributes.Selected = true;
                canvas.Viewport.Zoom = zoom;
                canvas.Viewport.MidPoint = new System.Drawing.PointF((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
                canvas.OnViewportChanged();
                canvas.Refresh();
            }
        }
    }
}


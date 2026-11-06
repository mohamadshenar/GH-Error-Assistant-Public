using System;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace GHErrorAssistant
{
    // Grasshopper instantiates this public, parameterless loader without placing any component.
    public sealed class AssistantLoader : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            try { Rhino.RhinoApp.InvokeOnUiThread((Action)GlobalAssistantMenu.Start); }
            catch (Exception ex) { Rhino.RhinoApp.WriteLine("GH Error Assistant menu initialization: " + ex.Message); }
            return GH_LoadingInstruction.Proceed;
        }
    }
    internal static class GlobalAssistantMenu
    {
        private const string MenuId = "GHErrorAssistant.GlobalMenu";
        private static Timer retry;
        private static MenuStrip owner;
        private static ToolStripMenuItem item;
        private static bool started;
        private static int attempts;
        internal static void Start()
        {
            if (started) return;
            started = true;
            Instances.CanvasCreated += CanvasCreated;
            Instances.CanvasDestroyed += CanvasDestroyed;
            Rhino.RhinoApp.Closing += Closing;
            retry = new Timer { Interval = 500 };
            retry.Tick += TryInstall;
            Schedule();
        }
        private static void CanvasCreated(GH_Canvas canvas) => Schedule();
        private static void CanvasDestroyed(GH_Canvas canvas) { retry?.Stop(); Detach(); }
        private static void Schedule() { attempts = 0; retry?.Start(); }
        private static void TryInstall(object sender, EventArgs e)
        {
            // CanvasCreated can precede editor construction. Retry on the UI thread, with no busy loop.
            if (++attempts >= 120) retry.Stop();
            try
            {
                var editor = Instances.DocumentEditor;
                if (editor == null || editor.IsDisposed || !editor.IsHandleCreated) return;
                var menu = editor.MainMenuStrip;
                if (menu == null || menu.IsDisposed) return;
                if (ReferenceEquals(menu, owner) && item != null && !item.IsDisposed) { retry.Stop(); return; }
                Detach();
                // Stable Name identifies our entry; do not depend on menu text or child-control indices.
                if (menu.Items.ContainsKey(MenuId)) { retry.Stop(); return; }
                owner = menu;
                item = new ToolStripMenuItem("Error Assistant") { Name = MenuId };
                var open = new ToolStripMenuItem("Open assistant") { Name = MenuId + ".Open" };
                open.Click += Open;
                item.DropDownItems.Add(open);
                menu.Items.Add(item);
                menu.Disposed += MenuDisposed;
                retry.Stop();
            }
            catch (Exception ex)
            {
                retry.Stop(); Detach();
                Rhino.RhinoApp.WriteLine("GH Error Assistant menu could not be registered: " + ex.Message);
            }
        }
        private static void Open(object sender, EventArgs e) => AssistantWindow.Open();
        private static void MenuDisposed(object sender, EventArgs e) { Detach(); Schedule(); }
        private static void Detach()
        {
            var previous = owner; owner = null;
            if (previous != null)
            {
                previous.Disposed -= MenuDisposed;
                if (!previous.IsDisposed && item != null) previous.Items.Remove(item);
            }
            item?.Dispose(); item = null;
        }
        private static void Closing(object sender, EventArgs e)
        {
            if (!started) return;
            started = false;
            Instances.CanvasCreated -= CanvasCreated;
            Instances.CanvasDestroyed -= CanvasDestroyed;
            Rhino.RhinoApp.Closing -= Closing;
            retry?.Stop();
            if (retry != null) { retry.Tick -= TryInstall; retry.Dispose(); retry = null; }
            Detach(); AssistantWindow.CloseAssistant();
        }
    }
}

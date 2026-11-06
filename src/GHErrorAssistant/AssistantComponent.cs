using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.Kernel;

namespace GHErrorAssistant
{
    public sealed class AssemblyInfo : GH_AssemblyInfo
    {
        public override string Name => "GH Error Assistant";
        public override Bitmap Icon => BrandAssets.CreateIcon();
        public override string Description => "Offline explanations for Grasshopper warnings and errors.";
        public override Guid Id => new Guid("6a4e5e21-6507-4b82-b44f-a2f167899319");
        public override string AuthorName => "GH Error Assistant contributors";
        public override string AuthorContact => "https://github.com/mohamadshenar/GH-Error-Assistant-Public/issues";
    }
    public sealed class AssistantComponent : GH_Component
    {
        public AssistantComponent() : base("GH Error Assistant", "Error Help",
            "Right-click and choose Open assistant. Then select components to inspect their runtime messages.",
            "Params", "Util") { }
        public override Guid ComponentGuid => new Guid("86b55443-4890-4590-89aa-0131b1537af5");
        protected override Bitmap Icon => BrandAssets.CreateIcon();
        protected override void RegisterInputParams(GH_InputParamManager p) { }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { }
        protected override void SolveInstance(IGH_DataAccess da) { Message = "Right-click → Open assistant"; }
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Open assistant", (s, e) => AssistantWindow.Open());
        }
    }
}

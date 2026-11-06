using System;
using Eto.Drawing;
using Eto.Forms;

namespace GHErrorAssistant
{
    // Shared by the production form and the standalone Eto visual review.
    // Uses host-native surfaces/controls rather than forcing light backgrounds or custom chrome.
    internal static class AssistantSurface
    {
        internal static Color StatusColor(bool error)
        {
            var text = SystemColors.ControlText;
            bool darkSurface = text.R + text.G + text.B > 1.5f;
            return error ? darkSurface ? new Color(1f, .62f, .62f) : new Color(.64f, .16f, .20f) :
                darkSurface ? new Color(1f, .80f, .43f) : new Color(.48f, .31f, .06f);
        }
        internal static Control Create(Control heading, Control brand, Control context, DropDown messages,
            Control notice, Control report, Control feedback, Button investigate, Button back,
            Button locateError, Button locateSource, Button checkAgain, Control next,
            Control technical, Button end, Button copy)
        {
            foreach (var button in new[] { investigate, back, locateError, locateSource, checkAgain, end, copy })
                button.Height = UiLayoutPolicy.ButtonHeight;
            var title = new DynamicLayout { Spacing = new Size(UiLayoutPolicy.Gap, 0) };
            title.AddRow(brand, heading, null);
            var header = new DynamicLayout { Spacing = new Size(0, 6) };
            header.AddRow(title); header.AddRow(context);
            header.AddRow(new MessagePicker(messages)); header.AddRow(notice); header.AddRow(investigate);

            var navigation = new TableLayout { Spacing = new Size(UiLayoutPolicy.Gap, UiLayoutPolicy.Gap), Rows = {
                new TableRow(new TableCell(locateError, true), new TableCell(locateSource, true)),
                new TableRow(new TableCell(checkAgain, true), new TableCell(back, true)) } };
            var utilities = new DynamicLayout { Spacing = new Size(UiLayoutPolicy.Gap, 0) };
            utilities.AddRow(technical, null, copy);
            var footer = new DynamicLayout { Spacing = new Size(0, 6) };
            footer.AddRow(feedback); footer.AddRow(next); footer.AddRow(navigation);
            footer.AddRow(utilities); footer.AddRow(end);
            var surface = new DynamicLayout { Spacing = new Size(0, UiLayoutPolicy.Gap) };
            surface.AddRow(header); surface.Add(report, xscale: true, yscale: true); surface.AddRow(footer);
            return surface;
        }

        private sealed class MessagePicker : PixelLayout
        {
            private readonly DropDown picker;
            private readonly ReportLayoutLifetime lifetime = new ReportLayoutLifetime();
            internal MessagePicker(DropDown value)
            {
                picker = value; Height = 30; Add(picker, 0, 0); SizeChanged += Fit;
            }
            private void Fit(object sender, EventArgs args) => lifetime.Run(() => {
                picker.Size = new Size(Math.Max(24, Width - 2), 28);
            });
            protected override void Dispose(bool disposing)
            {
                if (disposing) { lifetime.Stop(); SizeChanged -= Fit; }
                base.Dispose(disposing);
            }
        }
    }
}

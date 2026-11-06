using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Eto.Drawing;
using Eto.Forms;

namespace GHErrorAssistant
{
    internal sealed class ReportView : Scrollable
    {
        private readonly PixelLayout page = new PixelLayout();
        private readonly List<Tuple<TextControl, int>> rows = new List<Tuple<TextControl, int>>();
        private readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        private Font TextFont(float size, bool bold = false)
        {
            string key = size + ":" + bold;
            if (!fonts.TryGetValue(key, out var font)) fonts[key] = font = new Font(bold ? SystemFont.Bold : SystemFont.Default, size);
            return font;
        }
        internal bool ShowTechnical;
        internal string VerificationText = "";
        internal GHErrorAssistant.Diagnostics.TroubleshootingSession Session;
        internal Action<GHErrorAssistant.Diagnostics.InvestigationTarget> LocateRequested;
        internal Action CopyTechnicalRequested;
        private readonly ReportLayoutLifetime layoutLifetime = new ReportLayoutLifetime();
        private bool showVerification, showTechnicalEvidence, showTrail;
        private int checkIndex;
        private string identity = "";
        internal ReportView()
        {
            Border = BorderType.None; ExpandContentWidth = false; ExpandContentHeight = false;
            Content = page; SizeChanged += OnSizeChanged;
        }
        private void OnSizeChanged(object sender, EventArgs e) => Arrange();
        internal void Display(Report report, string status)
        {
            if (layoutLifetime.IsStopped) return;
            report = Session?.DisplayReport ?? report;
            string key = (Session?.Id.ToString() ?? "preview") + (report == null ? "empty" : report.DocumentId + ":" + report.OwnerId + ":" + report.FocusInputId + ":" + report.Message);
            bool same = identity == key;
            var position = ScrollPosition;
            if (!same) { checkIndex = 0; showVerification = false; identity = key; }
            ReleaseRows();
            if (Session?.ShowingHistoricalDiagnosis == true)
            {
                Add("Earlier diagnosis · historical", 11, true, 8);
                Add("This is the saved warning, not a current finding. Use Check Again after Grasshopper finishes a new solution.", 10, false, 8);
                Section("What went wrong?", Session.Report.Message);
                Section("Where was it?", Session.LastKnownTarget?.Location ?? Session.Report.Name);
                Add("Locate is available only while the original document and target still exist.", 9, false, 6);
                if (ShowTechnical) Section("Current verification", Session.ComparisonStatus);
                Arrange(); ScrollPosition = same ? position : Point.Empty; return;
            }
            if (report != null && (Session?.CurrentUnavailable == true || !report.HasCurrentFinding))
            {
                Add(Session?.CurrentUnavailable == true ? "Current inspection unavailable" : "No current warning found here", 12, true, 6);
                Section("Where", report.Name);
                Add(Session?.CurrentUnavailable == true ? Session.UnavailableExplanation :
                    "No current runtime warning or specific actionable observation was found in this bounded inspection. This does not verify the intended output.", 10, false, 8);
                if (Session?.NextIssue != null)
                {
                    Section("Another problem needs attention", Session.NextIssue.Location);
                    Section("What to do", "Use Investigate next error below to inspect that current message. Your original baseline will be kept.");
                }
                if (ShowTechnical)
                {
                    Button(showTechnicalEvidence ? "Hide evidence and verification" : "Show evidence and verification", () => { showTechnicalEvidence = !showTechnicalEvidence; Display(report, status); });
                    if (showTechnicalEvidence)
                    {
                        if (Session != null) { Add("Historical baseline · not current evidence", 10, true, 4); Area(Session.Report.Message); }
                        EvidenceSummary(report);
                    }
                }
                Arrange(); ScrollPosition = same ? position : Point.Empty; return;
            }
            if (report == null)
            {
                Add("Start with a warning", 14, true, 10);
                Section("Select → Investigate", "Select a component or parameter with a warning or error, then choose Investigate above.");
                Add(status, 10, false, 8);
            }
            else
            {
                // Use the native theme foreground. Do not depend on Label background painting for contrast.
                var severity = Add((report.Severity == "Error" ? "ERROR" : report.HasRuntimeMessage ? "WARNING" : "OBSERVATION") + " · " + report.Name, 9, true, 10);
                if (report.HasRuntimeMessage) severity.TextColor = AssistantSurface.StatusColor(report.Severity == "Error");
                if (report.Context == null && report.DiagnosticText.StartsWith("Dependency inspection pending"))
                    Add("Upstream inspection pending · no source has been verified yet.", 9, false, 8);
                var tutor = report.Tutor;
                var guide = tutor.Guidance;
                if (guide != null && guide.Checks.Count > 0)
                {
                    checkIndex = Math.Max(0, Math.Min(checkIndex, guide.Checks.Count - 1));
                    var check = Session?.Current(guide) ?? guide.Checks[checkIndex];
                    var card = CompactDiagnosis.Create(check, report);
                    Add(card.State, 9, false, 6);
                    Add(card.Finding, 13, true, 12);
                    Section("What we know", card.Why);
                    Section("Where to look", card.Location + (check.Target.Issue.Depth > 0 ? " · " + check.Target.Issue.Depth + " connections upstream" : ""));
                    Section("Try this", card.Action);
                    Section("After your repair", card.Expected + " Check the result you intended, too.");
                    if (report.Context?.Graph?.Partial == true) Add("Inspection is partial. Other problems may remain; see Advanced details for limits.", 9, false, 5);
                    if (ShowTechnical) Button(showTrail ? "Hide upstream trail" : "Show upstream trail", () => { showTrail = !showTrail; Display(report, status); });
                    if (ShowTechnical && showTrail)
                    {
                        Add("Possible sources follow verified connections. Investigation order does not prove a cause.", 9, false, 6);
                        var graph = report.Context?.Graph;
                        var candidates = EvidenceTrail.Candidates(guide, graph);
                        if (candidates.Count == 0) Add("No connected alternative was verified in this inspection.", 9, false, 6);
                        foreach (var candidate in candidates)
                        {
                            var selected = candidate;
                            Add(candidate.Target.Location + " · " + candidate.Target.Issue.Depth + " verified connections", 9, true, 3);
                            Add("Possible source · connected route verified; cause unverified.", 9, false, 3);
                            Button("Inspect " + Report.Preview(candidate.Target.Location, 36), () => {
                                checkIndex = guide.Checks.IndexOf(selected);
                                if (Session != null) { Session.CurrentTargetKey = GHErrorAssistant.Diagnostics.TroubleshootingSession.Key(selected.Target); Session.RememberTarget(selected.Target); }
                                Display(report, status); ScrollPosition = Point.Empty;
                            });
                        }
                        if (graph?.Partial == true) Add("Inspection is partial; other connections may remain uninspected. Use Inspect further upstream when offered.", 9, false, 6);
                        if (guide.Checks.Count > candidates.Count) Add("Only verified routes are listed here; other observations remain available in detailed investigation.", 9, false, 6);
                    }
                }
                else
                {
                    // An inspection with no actionable target still retains the existing engine's
                    // explanation. No new source/cause is manufactured for an empty candidate list.
                    Add(tutor.WhatsWrong, 13, true, 12);
                    Section("What we know", CompactEvidence.Create(report, false, false).Summary);
                    Section("Where to look", tutor.Where);
                    Section("Try this", string.Join("\n", tutor.Steps));
                    Add("Check the intended output after your repair; warning disappearance alone is not verification.", 9, false, 8);
                }

                if (ShowTechnical && (Session?.Verification != null || !string.IsNullOrEmpty(VerificationText)))
                {
                    Add(Session?.Verification?.Compact ?? "Verification incomplete. Open details.", 9, true, 5);
                    Button(showVerification ? "Hide verification details" : "Show verification details", () => { showVerification = !showVerification; Display(report, status); });
                    if (showVerification) { Section("Verification", Session?.Verification?.Compact ?? "Comparison incomplete; intended output unverified."); Button("Copy full technical report", () => CopyTechnicalRequested?.Invoke()); }
                }
                if (ShowTechnical)
                {
                    Button(showTechnicalEvidence ? "Hide evidence and message" : "Show evidence and message", () => { showTechnicalEvidence = !showTechnicalEvidence; Display(report, status); });
                    if (showTechnicalEvidence)
                    {
                    EvidenceSummary(report);
                    }
                }
                if (ShowTechnical) Section("Inspection status", status);
            }
            Add("Offline · Repairs and recomputation stay under your control.", 9, false, 6);
            Arrange();
            ScrollPosition = same ? position : Point.Empty;
        }
        private void EvidenceSummary(Report report)
        {
            var evidence = CompactEvidence.Create(report, Session?.ShowingHistoricalDiagnosis == true, Session?.CurrentUnavailable == true);
            Section("Summary", evidence.Summary);
            Section("Main finding · observation, cause unverified", evidence.Main);
            if (evidence.Other.Count > 0) Section("Other findings", string.Join("\n", evidence.Other));
            Section("Inspection limits", evidence.Limits);
            Section("Original runtime message · verbatim", report.Message);
            Button("Copy full technical report", () => CopyTechnicalRequested?.Invoke());
        }
        private void Area(string text)
        {
            var area = new TextArea { ReadOnly = true, Wrap = true, Text = text ?? "", Font = TextFont(10) };
            rows.Add(Tuple.Create<TextControl, int>(area, 10)); page.Add(area, 0, 0);
        }
        private void Button(string text, Action action)
        {
            var button = new Button { Text = text, ToolTip = text, Font = TextFont(9) };
            button.Click += (s, e) => action();
            rows.Add(Tuple.Create<TextControl, int>(button, 8)); page.Add(button, 0, 0);
        }
        private void Section(string title, string text) { Add(title, 9, true, 4); Add(text, 10, false, 12); }
        private Label Add(string text, float size, bool bold, int spaceAfter)
        {
            // Word boundaries remain natural. Long unbroken identifiers gain display-only soft break opportunities.
            string display = UiLayoutPolicy.WrapTokens(text);
            var label = new Label { Text = display, Wrap = WrapMode.Word,
                TextColor = SystemColors.ControlText, Font = TextFont(size, bold) };
            rows.Add(Tuple.Create<TextControl, int>(label, spaceAfter)); page.Add(label, 0, 0); return label;
        }
        private void Arrange()
        {
            layoutLifetime.Run(() =>
            {
                if (Width < 40) return;
                int width = UiLayoutPolicy.BodyWidth(Width), y = 8;
                foreach (var row in rows)
                {
                    var control = row.Item1;
                    control.Size = new Size(width, -1);
                    var preferred = control is TextArea ? new SizeF(width, 150) : control.GetPreferredSize(new SizeF(width, float.PositiveInfinity));
                    int height = Math.Max(control is Button ? 30 : 16, (int)Math.Ceiling(preferred.Height) + 3);
                    control.Size = new Size(width, height); page.Move(control, UiLayoutPolicy.Inset, y); y += height + row.Item2;
                }
                page.Size = new Size(width + UiLayoutPolicy.Inset * 2, y);
            });
        }
        internal void StopLayout()
        {
            layoutLifetime.Stop();
            SizeChanged -= OnSizeChanged;
        }
        internal void ReleaseRows()
        {
            if (rows.Count == 0) return;
            page.RemoveAll();
            foreach (var row in rows) row.Item1.Dispose();
            rows.Clear();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { StopLayout(); ReleaseRows(); foreach (var font in fonts.Values) font.Dispose(); fonts.Clear(); }
            base.Dispose(disposing);
        }
    }
}





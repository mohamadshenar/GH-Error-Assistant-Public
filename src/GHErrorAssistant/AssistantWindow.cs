using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eto.Forms;
using Eto.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using GHErrorAssistant.Core;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant
{
    internal sealed class AssistantWindow : Form
    {
        private static AssistantWindow current;
        private Bitmap brandImage;
        private readonly UITimer timer = new UITimer { Interval = 0.4 };
        private readonly DropDown messages = new DropDown { ToolTip = "Choose a message. Full text appears below." };
        private readonly ReportView detail = new ReportView();
        private string clipboardText = "";
        private string status = "Select a component in Grasshopper.";
        private readonly Button copy = new Button { Text = "Copy error report", Enabled = false };
        private readonly CheckBox technical = new CheckBox { Text = "Advanced details" };
        private readonly Button begin = new Button { Text = "Start troubleshooting" };
        private readonly Button checkAgain = new Button { Text = "Check Again", Enabled = false };
        private readonly Button locate = new Button { Text = "Locate Source", Enabled = false };
        private readonly Button locateError = new Button { Text = "Locate Error", Enabled = false };
        private readonly Button nextProblem = new Button { Text = "Investigate next error", Visible = false };
        private TroubleshootingSession session;
        private VerificationSnapshot baseline => session?.Baseline;
        private Report baselineReport => session?.Report;
        private readonly Label sessionHeader = new Label { Text = "Select a warning to begin", Wrap = WrapMode.Word, Font = new Font(SystemFont.Default, 9) };
        private readonly Label selectionNotice = new Label { Wrap = WrapMode.Word, Font = new Font(SystemFont.Default, 9) };
        private readonly Button endSession = new Button { Text = "End session", Enabled = false };
        private readonly Button continueSession = new Button { Text = "Continue session", Enabled = false };
        private readonly Label verificationStatus = new Label { Wrap = WrapMode.Word, Font = new Font(SystemFont.Default, 9) };
        private GH_Document watchedDocument;
        private WeakReference<GH_Document> baselineDocument;
        private long solutionRevision;
        private long lastRefreshRevision;
        private List<Report> reports = new List<Report>();
        private string signature = "";
        private bool updating;
        private bool cleanedUp;
        private bool shuttingDown;
        private readonly AssistantLifecycle lifetime = new AssistantLifecycle();
        private DateTime lastAnalysis = DateTime.MinValue;
        private string analysisKey = "", analysisText = "";
        private TutorContext analysisContext;
        private WeakReference<GH_Document> reportDocument;
        internal static void CloseAssistant() => current?.SuspendAssistant();
        internal static void Open()
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                if (Rhino.RhinoApp.IsClosing || Rhino.RhinoApp.IsExiting) return;
                if (current != null && !current.IsDisposed)
                { if (!current.Visible) current.Show(); current.ResumeMonitoring(); current.BringToFront(); return; }
                try
                {
                    current = new AssistantWindow();
                    current.Owner = Rhino.UI.RhinoEtoApp.MainWindow;
                    current.Show();
                    current.ResumeMonitoring();
                }
                catch (Exception ex)
                {
                    current?.Dispose(); current = null;
                    Rhino.RhinoApp.WriteLine("GH Error Assistant could not open: " + ex.Message);
                }
            }));
        }
        private AssistantWindow()
        {
            Title = "GH Error Assistant · 3.1 RC1";
            ClientSize = new Size(UiLayoutPolicy.DefaultWidth, UiLayoutPolicy.DefaultHeight);
            MinimumSize = new Size(UiLayoutPolicy.MinimumWidth, UiLayoutPolicy.MinimumHeight);
            Padding = new Padding(UiLayoutPolicy.Inset);
            var heading = new Label { Text = "GH Error Assistant", Font = new Font(SystemFont.Bold, 12), Wrap = WrapMode.Word };
            begin.Text = "Investigate";
            brandImage = BrandAssets.CreateHeaderImage();
            var brand = new ImageView { Image = brandImage, Size = new Size(24, 24) };
            Content = AssistantSurface.Create(heading, brand, sessionHeader, messages, selectionNotice, detail,
                verificationStatus, begin, continueSession, locateError, locate, checkAgain, nextProblem, technical, endSession, copy);
            detail.CopyTechnicalRequested = () => {
                try { using (var cb = new Clipboard()) cb.Text = session?.Text() ?? (SelectedReport?.Text() ?? clipboardText); }
                catch (Exception ex) { status = "Copy failed: " + ex.Message; ShowReport(); }
            };
            detail.Display(null, status);
            detail.LocateRequested = target => {
                int i = messages.SelectedIndex;
                var report = session?.ShowingHistoricalDiagnosis == true || session?.CurrentUnavailable == true ? session.Report :
                    session?.DisplayReport ?? (i >= 0 && i < reports.Count ? reports[i] : null);
                if (report == null) return;
                string result = ComponentLocator.Locate(report, target, session == null ? reportDocument : baselineDocument);
                status = result;
                if (session != null) { session.RememberTarget(target); session.LocateFeedback = result; }
                ShowReport();
            };
            locateError.Click += (s, e) => {
                var reporting = session?.Report ?? SelectedReport;
                if (reporting == null) return;
                var result = ComponentLocator.Locate(reporting, DiagnosisNavigation.ReportingTarget(reporting), session == null ? reportDocument : baselineDocument);
                if (session != null) session.LocateFeedback = result;
                status = result; ShowReport();
            };
            locate.Click += (s, e) => {
                var report = session?.DisplayReport ?? SelectedReport;
                var target = session != null && (session.CurrentUnavailable || session.ShowingHistoricalDiagnosis || !session.DisplayReport.HasCurrentFinding)
                    ? session.LastKnownTarget : session?.ActiveCheck?.Target ?? (report?.HasCurrentFinding == true ? report.Tutor.Guidance.Checks.FirstOrDefault()?.Target : null);
                if (report != null && target != null && (session?.CurrentUnavailable != true || IsLiveHistoricalTarget(target))) detail.LocateRequested?.Invoke(target);
            };
            nextProblem.Click += (s, e) => AdvanceInvestigation();
            messages.SelectedIndexChanged += (s, e) => {
                if (updating) return;
                InspectSelectedProblem(); ShowReport();
            };
            technical.CheckedChanged += (s, e) => { detail.ShowTechnical = technical.Checked == true; ShowReport(); };
            copy.Click += (s, e) => {
                try { using (var cb = new Clipboard()) cb.Text = session?.Text() ?? clipboardText; }
                catch (Exception ex) { status = "Copy failed: " + ex.Message; detail.Display(null, status); }
            };
            begin.Click += (s, e) => BeginTroubleshooting();
            checkAgain.Click += (s, e) => CheckRepair();
            continueSession.Click += (s, e) => { if (session != null) session.ShowingHistoricalDiagnosis = !session.ShowingHistoricalDiagnosis; ShowReport(); detail.ScrollPosition = Point.Empty; };
            endSession.Click += (s, e) => {
                if (watchedDocument != null) watchedDocument.SolutionEnd -= SolutionEnded;
                watchedDocument = null; baselineDocument = null; session = null; detail.Session = null; detail.VerificationText = "";
                checkAgain.Enabled = false; ShowReport();
            };
            timer.Elapsed += Tick;
            Shown += (s, e) => ResumeMonitoring();
            Closing += (s, e) => { if (!shuttingDown && !Rhino.RhinoApp.IsClosing && !Rhino.RhinoApp.IsExiting) { e.Cancel = true; SuspendAssistant(); } };
            Rhino.RhinoApp.Closing += RhinoClosing;
            Closed += (s, e) => Cleanup();
        }
        private void ResumeMonitoring()
        {
            if (!lifetime.Resume()) return;
            signature = ""; lastAnalysis = DateTime.MinValue;
            timer.Start(); Tick(this, EventArgs.Empty);
        }
        private void SuspendAssistant()
        {
            if (cleanedUp) return;
            lifetime.Pause(); timer.Stop();
            if (watchedDocument != null) watchedDocument.SolutionEnd -= SolutionEnded;
            watchedDocument = null; baselineDocument = null; reportDocument = null;
            session = null; detail.Session = null; detail.VerificationText = "";
            reports.Clear(); analysisContext = null; analysisKey = ""; signature = ""; clipboardText = "";
            Visible = false;
        }
        private void RhinoClosing(object sender, EventArgs e) { shuttingDown = true; Cleanup(); }
        private void Cleanup()
        {
            if (cleanedUp) return;
            cleanedUp = true;
            lifetime.Terminate();
            Rhino.RhinoApp.Closing -= RhinoClosing;
            timer.Stop(); timer.Elapsed -= Tick; timer.Dispose();
            if (watchedDocument != null) watchedDocument.SolutionEnd -= SolutionEnded;
            watchedDocument = null; baselineDocument = null; session = null;
            detail.StopLayout(); detail.ReleaseRows(); reports.Clear(); clipboardText = ""; reportDocument = null;
            if (ReferenceEquals(current, this)) current = null;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Cleanup();
            base.Dispose(disposing);
            if (disposing) { brandImage?.Dispose(); brandImage = null; }
        }
        private void ShowReport()
        {
            if (updating) return;
            int i = messages.SelectedIndex;
            Report candidate = i >= 0 && i < reports.Count ? reports[i] : null;
            Report report = session?.DisplayReport ?? candidate;
            var presentation = CurrentPresentation.Create(candidate, session);
            if (report != null && report.DiagnosticText.StartsWith("Dependency inspection pending")) lastAnalysis = DateTime.MinValue;
            clipboardText = session?.Text() ?? (report?.Text() ?? "") +
                (string.IsNullOrEmpty(detail.VerificationText) ? "" : "\n\nREPAIR VERIFICATION\n" + detail.VerificationText);
            copy.Enabled = report != null || baselineReport != null;
            begin.Enabled = candidate != null && candidate.Context != null && (session == null || !session.IsOriginal(candidate));
            sessionHeader.Text = ProblemSelection.Heading(candidate, session == null ? null : report,
                session?.ShowingHistoricalDiagnosis == true, session?.CurrentUnavailable == true, session?.Report);
            selectionNotice.Text = session == null ? "Previewing canvas selection." : session.IsOriginal(candidate) ?
                "Session pinned. Locate and selection do not change the baseline." :
                "Canvas selection differs. Continue this session or explicitly investigate the selected problem.";
            if (session != null && candidate == null) selectionNotice.Text = session.CurrentUnavailable ?
                "The original warning is saved. Follow the guidance below to refresh the inspection." :
                "Current evidence is displayed. The original warning remains available under Back to Diagnosis.";
            if (session != null && candidate != null) selectionNotice.Text = ProblemSelection.Notice(candidate, report);
            if (session?.ShowingHistoricalDiagnosis == true) selectionNotice.Text = "Historical diagnosis is displayed. Back to Results returns to the current inspection.";
            selectionNotice.Visible = session == null || candidate == null || !session.IsOriginal(candidate) || session.ShowingHistoricalDiagnosis;
            continueSession.Enabled = session != null; endSession.Enabled = session != null;
            continueSession.Visible = session != null;
            continueSession.Text = session?.ShowingHistoricalDiagnosis == true ? "Back to Results" : "Back to Diagnosis";
            endSession.Visible = technical.Checked == true && session != null;
            begin.Visible = session == null || begin.Enabled;
            detail.Session = session;
            verificationStatus.Text = session?.LocateFeedback ?? presentation.Comparison;
            verificationStatus.Visible = !string.IsNullOrWhiteSpace(verificationStatus.Text);
            bool historicalTarget = session != null && (session.CurrentUnavailable || session.ShowingHistoricalDiagnosis || !session.DisplayReport.HasCurrentFinding);
            locate.Enabled = historicalTarget ? IsLiveHistoricalTarget(session.LastKnownTarget) : presentation.CanLocate;
            var reporting = session?.Report ?? candidate;
            locateError.Enabled = IsLiveNavigationTarget(reporting, DiagnosisNavigation.ReportingTarget(reporting));
            nextProblem.Visible = presentation.NextAction != null;
            nextProblem.Text = presentation.NextAction ?? "Investigate next error";
            detail.Display(report, status);
        }
        private Report SelectedReport => messages.SelectedIndex >= 0 && messages.SelectedIndex < reports.Count ? reports[messages.SelectedIndex] : null;
        private void InspectSelectedProblem()
        {
            var report = SelectedReport; var doc = Instances.ActiveCanvas?.Document;
            if (report == null || doc == null || doc.SolutionState == GH_ProcessStep.Process || reportDocument == null ||
                !reportDocument.TryGetTarget(out var expected) || !ReferenceEquals(expected, doc) || doc.DocumentID != report.DocumentId) return;
            var root = doc.FindObject(report.OwnerId, false);
            if (root == null) return;
            try {
                var advice = DependencyAnalysis.AnalyzeDetailed(new GrasshopperDependencyReader(root), root.InstanceGuid, report.FocusInputId);
                report.Context = GrasshopperTutorContext.Capture(root, report.FocusInputId);
                report.Context.Graph = advice.Graph; report.DiagnosticText = advice.Details;
                signature = "";
            }
            catch (Exception ex) {
                report.Context = null; report.DiagnosticText = "Selected problem inspection unavailable. No current source has been verified.";
                status = "Selected problem inspection incomplete: " + ex.Message;
            }
        }
        private bool IsLiveHistoricalTarget(InvestigationTarget target)
            => IsLiveNavigationTarget(session?.Report, target);
        private bool IsLiveNavigationTarget(Report reporting, InvestigationTarget target)
        {
            var canvas = Instances.ActiveCanvas; var doc = canvas?.Document;
            var reference = session == null ? reportDocument : baselineDocument;
            if (reporting == null || target == null || doc == null || reference == null ||
                !reference.TryGetTarget(out var expected) || !ReferenceEquals(doc, expected) ||
                doc.DocumentID != reporting.DocumentId || doc.SolutionState == GH_ProcessStep.Process ||
                Instances.DocumentEditor?.Visible != true || canvas.IsDisposed || doc.FindObject(reporting.OwnerId, false) == null) return false;
            var owner = doc.FindObject(target.OwnerId, false);
            return owner != null && (target.ParameterId == Guid.Empty || owner.InstanceGuid == target.ParameterId ||
                owner is IGH_Component component && component.Params.Input.Concat(component.Params.Output).Any(p => p.InstanceGuid == target.ParameterId));
        }
        private void AdvanceInvestigation()
        {
            if (session != null) session.LocateFeedback = null;
            try
            {
                var doc = Instances.ActiveCanvas?.Document;
                GH_Document expected;
                if (session == null || doc == null || baselineDocument == null || !baselineDocument.TryGetTarget(out expected) ||
                    !ReferenceEquals(doc, expected) || doc.DocumentID != session.Report.DocumentId || doc.SolutionState == GH_ProcessStep.Process) return;
                var issue = session.NextIssue;
                if (issue != null)
                {
                    var live = ReadCurrent(doc, issue.OwnerId, issue);
                    if (!session.FollowIssue(live, issue)) { status = "That message changed or is unavailable. Click Check Again."; ShowReport(); return; }
                }
                else
                {
                    var frontier = session.NextFrontier;
                    if (frontier == null || !session.FollowFrontier(ReadCurrent(doc, frontier.OwnerId), frontier))
                    { status = "Upstream target unavailable. Click Check Again."; ShowReport(); return; }
                    status = "Continued from a verified upstream frontier. This refresh has its own bounded inspection; causation remains unknown.";
                }
                ShowReport(); detail.ScrollPosition = Point.Empty;
            }
            catch (Exception ex) { VerificationFailure("Current investigation unavailable: " + ex.Message); }
        }
        private void SolutionEnded(object sender, GH_SolutionEventArgs args) => System.Threading.Interlocked.Increment(ref solutionRevision);
        private void BeginTroubleshooting()
        {
            try
            {
                var doc = Instances.ActiveCanvas?.Document;
                int index = messages.SelectedIndex;
                if (doc == null || index < 0 || index >= reports.Count || doc.SolutionState == GH_ProcessStep.Process) return;
                var report = reports[index];
                GH_Document expected;
                if (reportDocument == null || !reportDocument.TryGetTarget(out expected) || !ReferenceEquals(doc, expected) || report.DocumentId != doc.DocumentID) return;
                InspectSelectedProblem();
                if (watchedDocument != null) watchedDocument.SolutionEnd -= SolutionEnded;
                watchedDocument = doc; solutionRevision = 0; watchedDocument.SolutionEnd += SolutionEnded;
                baselineDocument = new WeakReference<GH_Document>(doc);
                lastRefreshRevision = solutionRevision;
                var captured = GrasshopperVerification.Capture(doc, report, solutionRevision);
                session = new TroubleshootingSession(report, captured, doc.DisplayName);
                checkAgain.Enabled = true;
                detail.VerificationText = "";
                ShowReport();
            }
            catch (Exception ex) { detail.VerificationText = "Baseline could not be captured: " + ex.Message; ShowReport(); }
        }
        private void CheckRepair()
        {
            if (session != null) { session.LocateFeedback = null; session.ShowingHistoricalDiagnosis = false; }
            lastRefreshRevision = System.Threading.Interlocked.Read(ref solutionRevision);
            try
            {
                var canvas = Instances.ActiveCanvas;
                var doc = canvas?.Document;
                if (baseline == null || doc == null || Instances.DocumentEditor?.Visible != true || canvas.IsDisposed)
                { VerificationFailure("Verification incomplete: open the original Grasshopper document."); return; }
                VerificationSnapshot snapshot;
                GH_Document original = null;
                if (baselineDocument == null || !baselineDocument.TryGetTarget(out original) || !ReferenceEquals(doc, original))
                    snapshot = new VerificationSnapshot { DocumentId = doc.DocumentID, DocumentToken = doc.RuntimeID.ToString() };
                else
                {
                    snapshot = GrasshopperVerification.Capture(doc, baselineReport, System.Threading.Interlocked.Read(ref solutionRevision), baseline);
                    var target = session.ActiveCheck?.Target ?? session.LastKnownTarget;
                    if (target != null && target.OwnerId != Guid.Empty && target.OwnerId != baseline.OwnerId)
                        snapshot.InvestigationTargetMissing = doc.FindObject(target.OwnerId, false) == null;
                }
                // Explicitly followed targets get one additional bounded message scope, including their direct recipients.
                // Baseline identity/data comparisons remain pinned to the original object.
                if (original != null && ReferenceEquals(doc, original) && session.DisplayReport.OwnerId != baseline.OwnerId)
                    snapshot.IncludeCurrentMessages(GrasshopperVerification.Capture(doc, session.DisplayReport,
                        System.Threading.Interlocked.Read(ref solutionRevision)), session.DisplayReport.OwnerId);
                session.Verification = RepairVerification.Compare(baseline, snapshot, DateTime.UtcNow);
                if (original != null && ReferenceEquals(doc, original) && snapshot.Ready && snapshot.TargetExists && session.Verification.ComparisonMade)
                {
                    session.RefreshCurrent(ReadCurrent(doc, session.DisplayReport.OwnerId), snapshot);
                }
                else session.CurrentUnavailable = true;
                detail.VerificationText = session.Verification.Text;
                lastAnalysis = DateTime.MinValue;
                Tick(this, EventArgs.Empty); ShowReport(); detail.ScrollPosition = Point.Empty;
                clipboardText = session.Text();
                copy.Enabled = baselineReport != null;
            }
            catch (Exception ex) { VerificationFailure("Verification incomplete: " + ex.Message); }
        }
        private static Report ReadCurrent(GH_Document doc, Guid ownerId, RuntimeObservation preferred = null)
        {
            var root = doc.FindObject(ownerId, false);
            if (root == null) return null;
            var messages = new List<RuntimeObservation>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Action<IGH_ActiveObject, Guid> read = (obj, portId) => {
                foreach (var level in new[]{ GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning })
                    foreach (var text in obj.RuntimeMessages(level).Take(500 - messages.Count))
                        messages.Add(new RuntimeObservation { OwnerId = ownerId, ParameterId = portId, Message = text,
                            Severity = level.ToString(), Location = root.Name + (portId == Guid.Empty ? "" : " / " + obj.Name) });
            };
            if (root is IGH_ActiveObject active) read(active, root is IGH_Param ? ownerId : Guid.Empty);
            if (root is IGH_Component component)
            {
                var ports = component.Params.Input.Concat(component.Params.Output).ToList();
                if (ports.Count > 256) return null;
                foreach (var port in ports)
                { if (clock.ElapsedMilliseconds >= 25 || messages.Count >= 500) return null; read(port, port.InstanceGuid); }
            }
            var current = preferred == null ? CurrentDiagnosis.Select(ownerId, messages) : messages.FirstOrDefault(m => m.Key == preferred.Key);
            if (preferred != null && current == null) return null;
            Guid focus = current?.ParameterId ?? (root is IGH_Param ? ownerId : Guid.Empty);
            var advice = DependencyAnalysis.AnalyzeDetailed(new GrasshopperDependencyReader(root), ownerId, focus);
            var context = GrasshopperTutorContext.Capture(root, focus); context.Graph = advice.Graph;
            return CurrentDiagnosis.Create(new Report { Name = root.Name, OwnerId = ownerId, DocumentId = doc.DocumentID,
                FocusInputId = focus }, context, advice.Details, current == null ? Enumerable.Empty<RuntimeObservation>() : new[]{ current });
        }
        private void VerificationFailure(string text)
        {
            if (session != null) session.CurrentUnavailable = true;
            if (session != null) { session.Verification = new VerificationResult { Incomplete = true }; session.Verification.Changes.Add(text); }
            detail.VerificationText = text; ShowReport();
        }
        // Poll only while the window is open. No document/object event subscriptions or retained GH objects.
        // Eto's timer executes on the UI thread; skipping active solutions avoids reading partially solved data.
        private void Tick(object sender, EventArgs args)
        {
            if (!lifetime.Active || cleanedUp) return;
            try
            {
                var canvas = Instances.ActiveCanvas;
                var doc = canvas?.Document;
                GH_Document sessionDocument;
                if (session != null && session.ObserveDocument(doc != null && baselineDocument != null &&
                    baselineDocument.TryGetTarget(out sessionDocument) && ReferenceEquals(doc, sessionDocument)))
                    ShowReport();
                if (watchedDocument != null && !ReferenceEquals(watchedDocument, doc))
                { watchedDocument.SolutionEnd -= SolutionEnded; watchedDocument = null; }
                GH_Document baselineDoc;
                if (doc != null && watchedDocument == null && baselineDocument != null && baselineDocument.TryGetTarget(out baselineDoc) && ReferenceEquals(doc, baselineDoc))
                { watchedDocument = doc; watchedDocument.SolutionEnd += SolutionEnded; }
                if (Instances.DocumentEditor == null || !Instances.DocumentEditor.Visible || doc == null)
                { Empty("Open a Grasshopper document to inspect messages."); return; }
                if (doc.SolutionState == GH_ProcessStep.Process) return;
                if (SolutionRefreshGate.ShouldRefresh(session != null,
                    baselineDocument != null && baselineDocument.TryGetTarget(out sessionDocument) && ReferenceEquals(doc, sessionDocument),
                    doc.SolutionState == GH_ProcessStep.Process, System.Threading.Interlocked.Read(ref solutionRevision), lastRefreshRevision))
                { CheckRepair(); return; }
                var selected = doc.SelectedObjects();
                if (selected.Count == 0) { Empty("Nothing selected. Select a component or parameter."); return; }
                var next = new List<Report>(); bool truncated = false;
                var scan = System.Diagnostics.Stopwatch.StartNew(); int ports = 0;
                foreach (var obj in selected.Take(200))
                {
                    if (scan.ElapsedMilliseconds >= 25) { truncated = true; break; }
                    var active = obj as IGH_ActiveObject;
                    if (active != null) Collect(active, obj.Name, next, obj.InstanceGuid, obj is IGH_Param ? obj.InstanceGuid : Guid.Empty);
                    var component = obj as IGH_Component;
                    if (component != null)
                        foreach (var p in component.Params.Input.Concat(component.Params.Output))
                        {
                            if (++ports > 256 || scan.ElapsedMilliseconds >= 25) { truncated = true; break; }
                            Collect(p, obj.Name + " / " + p.Name, next, obj.InstanceGuid, p.InstanceGuid);
                        }
                    if (truncated) break;
                    if (next.Count >= 500) { truncated = true; break; }
                }
                next = ProblemSelection.Distinct(next);
                string state = selected.Count + " selected • " + next.Count + (next.Count == 1 ? " message" : " messages") +
                    (truncated || selected.Count > 200 ? " • Selection scan limited; narrow the selection to inspect the remaining objects." : "");
                if (next.Count == 0) { Empty(state + "\nNo runtime warnings or errors in the inspected selection."); return; }
                var previous = messages.SelectedIndex >= 0 && messages.SelectedIndex < reports.Count ? reports[messages.SelectedIndex] : null;
                int oldIndex = previous == null ? 0 : next.FindIndex(r => r.OwnerId == previous.OwnerId && r.FocusInputId == previous.FocusInputId && r.Severity == previous.Severity && r.Message == previous.Message);
                oldIndex = Math.Max(0, oldIndex);
                var inspectedReport = next[oldIndex];
                string targetKey = doc.DocumentID + ":" + doc.RuntimeID + ":" + inspectedReport.OwnerId + ":" + inspectedReport.FocusInputId + ":" + inspectedReport.Severity + ":" + inspectedReport.Message;
                if (targetKey != analysisKey || (DateTime.UtcNow - lastAnalysis).TotalSeconds >= 1.2)
                {
                    var root = doc.FindObject(inspectedReport.OwnerId, false);
                    if (root == null)
                    {
                        analysisText = "The reporting object is no longer available. No source has been inferred.";
                        analysisContext = new TutorContext { Component = inspectedReport.Name, InspectionPartial = true };
                    }
                    else
                    {
                        var advice = DependencyAnalysis.AnalyzeDetailed(new GrasshopperDependencyReader(root), root.InstanceGuid, inspectedReport.FocusInputId);
                        analysisText = advice.Details;
                        analysisContext = GrasshopperTutorContext.Capture(root, inspectedReport.FocusInputId);
                        analysisContext.Graph = advice.Graph;
                        if (advice.HasUpstreamFinding) analysisContext.UpstreamFirstAction = advice.FirstAction;
                    }
                    analysisKey = targetKey; lastAnalysis = DateTime.UtcNow;
                }
                inspectedReport.DiagnosticText = analysisText;
                inspectedReport.Context = analysisContext;
                var key = new StringBuilder(state);
                foreach (var r in next) key.Append(r.Name.Length).Append(':').Append(r.Name).Append(r.Severity).Append(r.Message.Length).Append(':').Append(r.Message);
                key.Append(targetKey).Append(inspectedReport.Text());
                if (key.ToString() == signature) return;
                signature = key.ToString(); status = state;
                updating = true; reports = next;
                reportDocument = new WeakReference<GH_Document>(doc);
                foreach (var r in reports) r.DocumentId = doc.DocumentID;
                messages.Items.Clear();
                foreach (var r in reports) messages.Items.Add(new ListItem { Text = ProblemSelection.Label(r) });
                messages.SelectedIndex = Math.Max(0, Math.Min(oldIndex, reports.Count - 1));
                updating = false; ShowReport();
            }
            catch (Exception ex)
            {
                updating = false;
                Empty("Unable to read the selection. Try selecting again.\n" + ex.Message);
            }
        }
        private static void Collect(IGH_ActiveObject obj, string name, List<Report> target, Guid ownerId, Guid focusId)
        {
            foreach (var level in new[]{GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning})
                foreach (var message in obj.RuntimeMessages(level))
                {
                    if (target.Count >= 500) return;
                    target.Add(new Report { Name = name, Severity = level.ToString(), Message = message, OwnerId = ownerId, FocusInputId = focusId });
                }
        }
        private void Empty(string text)
        {
            if (signature == text) return;
            signature = text; updating = true; reports.Clear(); messages.Items.Clear(); updating = false;
            status = text; clipboardText = ""; ShowReport();
            begin.Enabled = false;
            clipboardText = session?.Text() ?? "";
            copy.Enabled = baselineReport != null;
        }
    }
}





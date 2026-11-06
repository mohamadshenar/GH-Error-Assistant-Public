using System.Linq;
using GHErrorAssistant.Diagnostics;

namespace GHErrorAssistant
{
    // Scalar presentation contract used by the real Eto controls and regression tests.
    internal sealed class CurrentPresentation
    {
        internal Report Report;
        internal GuidedCheck Check;
        internal RepairCard Card;
        internal bool Unavailable, CanLocate;
        internal string NextAction, Comparison;
        internal static CurrentPresentation Create(Report candidate, TroubleshootingSession session)
        {
            var report = session?.DisplayReport ?? candidate;
            var view = new CurrentPresentation { Report = report, Unavailable = session?.CurrentUnavailable == true,
                Comparison = session?.ComparisonStatus ?? "",
                NextAction = session?.NextIssue != null ? session.NextIssue.Severity == "Error" ? "Investigate next error" : "Investigate next warning" : session?.NextFrontier != null ? "Inspect further upstream" : null };
            if (!view.Unavailable && report?.HasCurrentFinding == true)
            {
                view.Check = session?.ActiveCheck ?? report.Tutor.Guidance.Checks.FirstOrDefault();
                if (view.Check != null) { view.Card = RepairCard.Create(view.Check, report); view.CanLocate = view.Check.Target.OwnerId != System.Guid.Empty; }
            }
            return view;
        }
    }
}

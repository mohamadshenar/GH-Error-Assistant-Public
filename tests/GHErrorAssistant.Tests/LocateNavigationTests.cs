using System;
using System.Collections.Generic;
using GHErrorAssistant.Diagnostics;
using Xunit;

public class LocateNavigationTests
{
    private sealed class Canvas : ILocateSurface
    {
        public Guid DocumentId { get; set; } = Guid.NewGuid();
        public bool SameDocumentInstance { get; set; } = true;
        public bool Ready { get; set; } = true;
        public int Width { get; set; } = 1000;
        public int Height { get; set; } = 700;
        internal Guid Target = Guid.NewGuid(), Reporting = Guid.NewGuid(), Parameter = Guid.NewGuid();
        internal readonly HashSet<Guid> Selection = new HashSet<Guid>();
        internal bool TargetExists = true, ReportingExists = true, ParameterExists = true, TopLevel = true;
        internal int Navigations;
        internal Guid NavigatedId;
        internal float Zoom;
        internal LocateBounds Frame = new LocateBounds { X = 4000, Y = -2000, Width = 200, Height = 100 };
        public bool ContainsOwner(Guid id) => id == Target ? TargetExists : id == Reporting && ReportingExists;
        public bool ContainsParameter(Guid owner, Guid parameter) => owner == Target && (parameter == Guid.Empty || parameter == Parameter && ParameterExists);
        public bool IsTopLevel(Guid owner) => owner == Target && TopLevel;
        public LocateBounds Bounds(Guid owner) { Assert.Equal(Target, owner); return Frame; }
        public void SelectAndFrame(Guid owner, LocateBounds frame, float zoom)
        { Navigations++; NavigatedId = owner; Selection.Add(owner); Zoom = zoom; Assert.Equal(Frame.X, frame.X); }
        internal LocateRequest Request() => new LocateRequest(DocumentId, Target, Parameter, Reporting);
    }
    [Fact] public void SelectsExactOwnerAndKeepsExistingSelections()
    {
        var c = new Canvas(); c.Selection.Add(c.Reporting); var another = Guid.NewGuid(); c.Selection.Add(another);
        Assert.True(LocateNavigation.TryNavigate(c.Request(), c));
        Assert.Equal(c.Target, c.NavigatedId); Assert.Contains(c.Target, c.Selection); Assert.Contains(c.Reporting, c.Selection);
        Assert.Contains(another, c.Selection); Assert.Equal(3, c.Selection.Count); Assert.Equal(1, c.Navigations);
        Assert.Equal(1.5f, c.Zoom);
    }
    [Fact] public void AlreadySelectedTargetStaysSelectedWithoutDuplicates()
    {
        var c = new Canvas(); c.Selection.Add(c.Target); Assert.True(LocateNavigation.TryNavigate(c.Request(), c)); Assert.Single(c.Selection);
    }
    [Fact] public void SameNameOrOtherIdCannotSubstituteForDeletedTarget()
    {
        var c = new Canvas(); var r = new LocateRequest(c.DocumentId, Guid.NewGuid(), c.Parameter, c.Reporting);
        Assert.False(LocateNavigation.TryNavigate(r, c)); Assert.Equal(0, c.Navigations);
    }
    [Fact] public void WrongDocumentDoesNotNavigate()
    {
        var c = new Canvas(); var r = c.Request(); c.DocumentId = Guid.NewGuid();
        Assert.False(LocateNavigation.TryNavigate(r, c)); Assert.Empty(c.Selection); Assert.Equal(0, c.Navigations);
    }
    [Fact] public void ReopenedDocumentWithSameIdIsRejected()
    { var c = new Canvas { SameDocumentInstance = false }; Assert.False(LocateNavigation.TryNavigate(c.Request(), c)); Assert.Equal(0, c.Navigations); }
    [Theory]
    [InlineData("target")][InlineData("reporting")][InlineData("input")][InlineData("canvas")][InlineData("cluster")]
    public void InvalidStateHasNoSelectionOrViewSideEffects(string missing)
    {
        var c = new Canvas(); c.Selection.Add(c.Reporting);
        if (missing == "target") c.TargetExists = false;
        if (missing == "reporting") c.ReportingExists = false;
        if (missing == "input") c.ParameterExists = false;
        if (missing == "canvas") c.Ready = false;
        if (missing == "cluster") c.TopLevel = false;
        Assert.False(LocateNavigation.TryNavigate(c.Request(), c)); Assert.Equal(0, c.Navigations);
        Assert.Single(c.Selection); Assert.Contains(c.Reporting, c.Selection);
    }
    [Fact] public void FloatingParameterLocatesItselfAndComponentInputLocatesItsOwner()
    {
        var c = new Canvas(); Assert.True(LocateNavigation.TryNavigate(c.Request(), c)); Assert.Equal(c.Target, c.NavigatedId);
        c = new Canvas(); c.Parameter = c.Target; Assert.True(LocateNavigation.TryNavigate(c.Request(), c)); Assert.Equal(c.Target, c.NavigatedId);
    }
    [Fact] public void ZoomFitsOversizedComponentWithMargin()
    {
        var b = new LocateBounds { X = 100, Y = 200, Width = 1600, Height = 800 };
        float zoom = LocateNavigation.ZoomToFit(b, 1000, 700);
        Assert.Equal(0.5f, zoom); Assert.True(b.Width * zoom <= 1000 * .8); Assert.True(b.Height * zoom <= 700 * .8);
    }
    [Theory]
    [InlineData(0, 100)][InlineData(-1, 100)][InlineData(double.NaN, 100)][InlineData(100, double.PositiveInfinity)]
    public void InvalidBoundsDoNotNavigate(double width, double height)
    { var c = new Canvas(); c.Frame = new LocateBounds { Width = width, Height = height }; Assert.False(LocateNavigation.TryNavigate(c.Request(), c)); Assert.Equal(0, c.Navigations); }
    [Fact] public void MissingCanvasAndTinyViewportAreRejected()
    { var c = new Canvas { Width = 20 }; Assert.False(LocateNavigation.TryNavigate(c.Request(), c)); Assert.False(LocateNavigation.TryNavigate(c.Request(), null)); }
}

using System;
using GHErrorAssistant;
using Xunit;

public class ReportLayoutLifetimeTests
{
    [Fact]
    public void QueuedLayoutDoesNotTouchControlsAfterShutdown()
    {
        var lifetime = new ReportLayoutLifetime();
        var calls = 0;
        Action callback = () => lifetime.Run(() => calls++);
        callback();
        lifetime.Stop();
        lifetime.Stop();
        callback();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ReentrantLayoutIsIgnoredAndFailureDoesNotPermanentlyBlockLayout()
    {
        var lifetime = new ReportLayoutLifetime();
        var calls = 0;
        lifetime.Run(() => { calls++; lifetime.Run(() => calls++); });
        Assert.Throws<InvalidOperationException>(() => lifetime.Run(() => throw new InvalidOperationException()));
        lifetime.Run(() => calls++);
        Assert.Equal(2, calls);
    }
}


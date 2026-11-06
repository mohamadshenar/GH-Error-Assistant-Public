using System;
using System.Linq;
using GHErrorAssistant.Core;
using Xunit;
public class EngineTests
{
    [Theory]
    [InlineData("Failed to collect data")]
    [InlineData("DIVISION BY ZERO")]
    [InlineData("Cannot convert text to curve")]
    [InlineData("Data conversion failed from Text to Number")]
    [InlineData("Invalid geometry")]
    [InlineData("Null value")]
    [InlineData("Invalid reference")]
    [InlineData("Insufficient data")]
    [InlineData("Number overflow")]
    [InlineData("Upstream failure")]
    public void KnownMessagesPreserveExactOriginal(string message)
    {
        var e = ExplanationEngine.Explain(message);
        Assert.False(e.IsGeneric); Assert.Equal(message, e.OriginalMessage);
        Assert.NotEmpty(e.Suggestions); Assert.NotEmpty(e.PossibleCauses);
    }
    [Theory][InlineData(null)][InlineData("")][InlineData("🐛 third party custom failure\r\n  42")]
    public void UnknownIsHonestAndPreserved(string message)
    {
        var e = ExplanationEngine.Explain(message);
        Assert.True(e.IsGeneric); Assert.Equal(message ?? "", e.OriginalMessage);
        Assert.Contains("unknown", e.Summary);
    }
    [Fact] public void SpecificRuleWinsOverNullKeyword() => Assert.Contains("collected", ExplanationEngine.Explain("Failed to collect data: null").Title);
    [Fact] public void CustomRuleCanExtendLibrary()
    {
        var rule = new ExplanationRule(new[]{"vendor failure"}, "Vendor", "Inspect vendor inputs", new[]{"Possible unsupported input"}, new[]{"Read vendor help"});
        Assert.Equal("Vendor", ExplanationEngine.Explain("Vendor failure", new[]{rule}).Title);
    }
    [Fact] public void RulesDefensivelyCopyInput()
    {
        var patterns = new[]{"custom"}; var steps = new[]{"Inspect"};
        var rule = new ExplanationRule(patterns, "Title", "Summary", new[]{"Possible"}, steps);
        patterns[0] = "changed"; steps[0] = "changed";
        Assert.Equal("Inspect", ExplanationEngine.Explain("custom", new[]{rule}).Suggestions.Single());
    }
    [Fact] public void EmptyPatternsRejected() => Assert.Throws<ArgumentException>(() => new ExplanationRule(new[]{""}, "", "", new string[0], new string[0]));
}

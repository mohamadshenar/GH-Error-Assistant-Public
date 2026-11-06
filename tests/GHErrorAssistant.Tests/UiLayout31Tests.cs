using System;
using System.Globalization;
using System.Linq;
using GHErrorAssistant;
using Xunit;

public class UiLayout31Tests
{
    [Theory]
    [InlineData(140)]
    [InlineData(320)]
    [InlineData(360)]
    [InlineData(440)]
    [InlineData(900)]
    public void WrappedBodyLeavesScrollbarSpaceAndInsets(int viewport)
    {
        int occupied = UiLayoutPolicy.BodyWidth(viewport) + 2 * UiLayoutPolicy.Inset;
        Assert.InRange(occupied, 1, viewport - 12);
    }

    [Theory]
    [InlineData("Exact warning\r\n  line two <original> & geometry")]
    [InlineData("small words stay unchanged")]
    public void OrdinaryTextKeepsExactCharactersAndLineBreaks(string text)
        => Assert.Equal(text, UiLayoutPolicy.WrapTokens(text));

    [Fact] public void LongIdentifierIsOnlyChangedForDisplay()
    {
        string original = new string('x', 40000);
        string display = UiLayoutPolicy.WrapTokens(original);
        Assert.Contains("\u200B", display);
        Assert.Equal(original, display.Replace("\u200B", ""));
        var report = new Report { Name = "Disposable", Severity = "Warning", Message = original };
        Assert.Contains(original, report.Text());
        Assert.Equal(original, report.Message);
    }

    [Theory]
    [InlineData("🐛")]
    [InlineData("e\u0301")]
    [InlineData("界")]
    public void BreakOpportunitiesDoNotSplitTextElements(string unit)
    {
        string original = string.Concat(Enumerable.Repeat(unit, 80));
        string display = UiLayoutPolicy.WrapTokens(original);
        foreach (var chunk in display.Split('\u200B'))
        {
            Assert.InRange(new StringInfo(chunk).LengthInTextElements, 1, 24);
            Assert.False(char.IsHighSurrogate(chunk[chunk.Length - 1]));
            Assert.False(char.IsLowSurrogate(chunk[0]));
            Assert.NotEqual(UnicodeCategory.NonSpacingMark, char.GetUnicodeCategory(chunk[0]));
        }
        Assert.Equal(original, display.Replace("\u200B", ""));
    }

    [Fact] public void NullDisplayTextIsSafe() => Assert.Equal("", UiLayoutPolicy.WrapTokens(null));
}

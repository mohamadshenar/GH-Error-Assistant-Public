using System;
using GHErrorAssistant;
using Xunit;

public class PresentationTests
{
    [Theory]
    [InlineData("  Failed to collect data\r\nPath: C:\\a_&_b\\file.3dm\t \n")]
    [InlineData("未知の警告 🐛\r\n<original> & data")]
    public void ReportPreservesOriginalMessageVerbatim(string original)
    {
        var r = new Report { Name = "Point", Severity = "Warning", Message = original };
        Assert.Contains("ORIGINAL GRASSHOPPER MESSAGE\n" + original + "\n\nCONFIRMED OBSERVATIONS AND UNCERTAINTY", r.Text());
        Assert.Equal(original, r.Message);
        Assert.Contains("WHAT'S WRONG?", r.Text());
        Assert.Contains("WHAT I FOUND", r.Text());
        Assert.Contains("IF THAT DOESN'T WORK", r.Text());
        Assert.Contains("WHERE TO LOOK", r.Text());
        Assert.Contains("HOW TO FIX IT\n1.", r.Text());
        Assert.Contains("LIKELY SOURCE OF THE PROBLEM", r.Text());
    }
    [Fact] public void VeryLongMessageIsNeverTruncatedInReport()
    {
        string original = new string('X', 40000);
        var r = new Report { Name = new string('N', 1000), Severity = "Error", Message = original };
        Assert.Contains(original, r.Text());
        Assert.Contains(r.Name, r.Text());
        Assert.True(Report.Preview(original).Length <= 64);
    }
    [Fact] public void PickerPreviewDoesNotSplitUnicodeSurrogatePair()
    {
        Assert.Equal("abc…", Report.Preview("abc🐛longer", 5));
        Assert.Equal("one  two three", Report.Preview("one\r\ntwo\tthree"));
        Assert.Equal("", Report.Preview(null));
    }
    [Fact] public void PreviewDoesNotMutateReportOrCrossContaminateMessages()
    {
        var a = new Report { Name = "A", Severity = "Warning", Message = "first\r\nmessage" };
        var b = new Report { Name = "B", Severity = "Error", Message = "second message" };
        Report.Preview(a.Message, 5);
        Assert.Contains("first\r\nmessage", a.Text());
        Assert.DoesNotContain("second message", a.Text());
        Assert.Contains("second message", b.Text());
        Assert.Contains("error", b.Observation);
    }
    [Fact] public void PreviewRejectsInvalidLimit() => Assert.Throws<ArgumentOutOfRangeException>(() => Report.Preview("test", 0));
}

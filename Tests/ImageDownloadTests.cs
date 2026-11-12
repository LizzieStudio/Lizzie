using Xunit;

namespace Lizzie.Tests;

public class ImageDownloadTests
{
    [Theory]
    [InlineData("https://drive.google.com/file/d/1h0Ko53Pxhb/view?usp=sharing")]
    [InlineData("https://drive.google.com/open?id=1h0Ko53Pxhb")]
    [InlineData("https://drive.google.com/uc?id=1h0Ko53Pxhb&export=download")]
    [InlineData("https://drive.google.com/file/u/0/d/1h0Ko53Pxhb/view")]
    [InlineData("https://docs.google.com/uc?export=download&id=1h0Ko53Pxhb")]
    public void DriveLinksDownloadTheFile(string link) =>
        Check(
            "https://drive.usercontent.google.com/download?id=1h0Ko53Pxhb&export=download&confirm=t",
            link
        );

    [Theory]
    [InlineData(
        "https://www.dropbox.com/scl/fi/abc/sheet.png?rlkey=xyz&dl=0",
        "https://www.dropbox.com/scl/fi/abc/sheet.png?rlkey=xyz&dl=1"
    )]
    [InlineData(
        "https://www.dropbox.com/s/abc/sheet.png",
        "https://www.dropbox.com/s/abc/sheet.png?dl=1"
    )]
    [InlineData(
        "https://www.dropbox.com/s/abc/Card%20%231%E2%9C%93.png?dl=0",
        "https://www.dropbox.com/s/abc/Card%20%231%E2%9C%93.png?dl=1"
    )]
    public void DropboxLinksDownloadTheFile(string link, string expected) => Check(expected, link);

    [Theory]
    [InlineData("https://example.com/sheet.png?v=2")]
    [InlineData("https://drive.google.com/drive/folders")]
    [InlineData("not a link")]
    public void OtherLinksAreUsedAsTheyAre(string link) => Check(link, link);

    private static void Check(string expected, string link)
    {
        var actual = ImageDownload.DirectLink(link);
        Assert.True(actual == expected, $"{link} became {actual}, expected {expected}.");
    }
}

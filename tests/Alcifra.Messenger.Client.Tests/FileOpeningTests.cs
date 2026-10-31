using Alcifra.Messenger.Client.Display;

namespace Alcifra.Messenger.Client.Tests;

// What opening a sent file does (#27): in the app wherever the app can show it; handed on only where the tenant lets files
// leave the app, and then only from the menu; refused, saying so, where it keeps them in the app.
public sealed class FileOpeningTests
{
    private static readonly byte[] Pdf = "%PDF-1.7\n"u8.ToArray(), Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A], Jpeg = [0xFF, 0xD8, 0xFF, 0xE0],
        Zip = [0x50, 0x4B, 0x03, 0x04], Text = "Hello"u8.ToArray();

    [Theory]
    [InlineData("photo.jpg", "image/jpeg", FileShownIn.Photo)]
    [InlineData("scan.heic", "image/heic", FileShownIn.Photo)]
    [InlineData("route.pdf", "application/pdf", FileShownIn.Preview)]
    [InlineData("notes.txt", "text/plain; charset=utf-8", FileShownIn.Preview)]
    [InlineData("table.csv", "text/csv", FileShownIn.Preview)]
    [InlineData("readme.md", "text/markdown", FileShownIn.Preview)]
    [InlineData("clip.mp4", "video/mp4", FileShownIn.Player)]
    [InlineData("clip.mov", "video/quicktime", FileShownIn.Player)]
    [InlineData("voice.m4a", "audio/mp4", FileShownIn.Player)]
    [InlineData("song.mp3", "audio/mpeg", FileShownIn.Player)]
    [InlineData("report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileShownIn.OtherApp)]
    [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FileShownIn.OtherApp)]
    [InlineData("page.html", "text/html", FileShownIn.OtherApp)]            // markup would show as its source
    [InlineData("scan.tiff", "image/tiff", FileShownIn.OtherApp)]           // the image views draw no TIFF
    [InlineData("clip.webm", "video/webm", FileShownIn.OtherApp)]           // one platform's player only
    [InlineData("archive.zip", "application/zip", FileShownIn.OtherApp)]
    public void A_file_opens_in_the_app_wherever_the_app_can_show_it(string name, string type, FileShownIn expected)
    {
        var opening = FileOpening.Of(Zip, name, type, filesStayInApp: false);

        Assert.Equal(expected, opening.ShownIn);
        Assert.Equal(expected is not FileShownIn.OtherApp, opening.InTheApp);
        Assert.True(opening.MayLeaveTheApp);   // and the menu offers Open with and Share, for what it shows too
    }

    [Fact]
    public void The_bytes_speak_before_the_sent_type_and_the_name_after_it()
    {
        // A PDF sent as octet-stream by a picker, and a photo named and typed as something else, are what they are.
        Assert.Equal((FileShownIn.Preview, "application/pdf"), Shown(FileOpening.Of(Pdf, "download", "application/octet-stream", false)));
        Assert.Equal((FileShownIn.Photo, "image/png"), Shown(FileOpening.Of(Png, "picture.bin", "application/x-whatever", false)));
        Assert.Equal((FileShownIn.Photo, "image/jpeg"), Shown(FileOpening.Of(Jpeg, "photo", string.Empty, false)));

        // Bytes that prove nothing: the sent type, and where it says nothing, the file name.
        Assert.Equal((FileShownIn.Preview, "text/plain"), Shown(FileOpening.Of(Text, "notes.txt", "application/octet-stream", false)));
        Assert.Equal((FileShownIn.Player, "audio/mpeg"), Shown(FileOpening.Of(Zip, "song.MP3", string.Empty, false)));
        Assert.Equal((FileShownIn.Photo, "image/jpeg"), Shown(FileOpening.Of(Zip, "x", "image/jpg", false)));   // a usual misspelling

        // A sent type the app cannot show yields to a name it can; an unknown name keeps the sent type for the other app.
        Assert.Equal((FileShownIn.Preview, "text/plain"), Shown(FileOpening.Of(Text, "server.log", "text/x-log", false)));
        Assert.Equal((FileShownIn.OtherApp, "application/vnd.ms-excel"), Shown(FileOpening.Of(Zip, "old.xls", "application/vnd.ms-excel", false)));
        Assert.Equal((FileShownIn.OtherApp, "application/octet-stream"), Shown(FileOpening.Of(Zip, "blob", "application/octet-stream", false)));
    }

    [Fact]
    public void Where_the_tenant_keeps_files_in_the_app_nothing_leaves_it_and_what_it_cannot_show_says_so()
    {
        var office = FileOpening.Of(Zip, "report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", filesStayInApp: true);
        Assert.Equal(FileShownIn.Nowhere, office.ShownIn);
        Assert.False(office.MayLeaveTheApp);

        // What the app shows, it still shows; only the menu loses Open with and Share.
        var pdf = FileOpening.Of(Pdf, "route.pdf", "application/pdf", filesStayInApp: true);
        Assert.Equal((FileShownIn.Preview, false), (pdf.ShownIn, pdf.MayLeaveTheApp));
        var video = FileOpening.Of(Zip, "clip.mp4", "video/mp4", filesStayInApp: true);
        Assert.Equal((FileShownIn.Player, false), (video.ShownIn, video.MayLeaveTheApp));
    }

    private static (FileShownIn, string) Shown(FileOpening opening) => (opening.ShownIn, opening.ContentType);
}

namespace ServiceLib.Tests.V2pn;

/// <summary>The installed V2pn puts its folder on the user's PATH once, so v2pnctl works after an update too.</summary>
public class V2pnUserPathTests
{
    private const string Folder = @"C:\Users\me\AppData\Local\Programs\V2pn";

    private static string Expand(string entry) => entry.Replace("%LOCALAPPDATA%", @"C:\Users\me\AppData\Local", StringComparison.OrdinalIgnoreCase);

    [Test]
    public async Task TheFolderGoesAtTheEnd_TheRestAsStored()
    {
        await V2pnUserPath.WithFolder(@"%USERPROFILE%\bin;C:\Tools", Folder, Expand).Should()
            .BeEqualTo(@"%USERPROFILE%\bin;C:\Tools;" + Folder);
        await V2pnUserPath.WithFolder(@"C:\Tools;", Folder, Expand).Should().BeEqualTo(@"C:\Tools;" + Folder);
        await V2pnUserPath.WithFolder(null, Folder, Expand).Should().BeEqualTo(Folder);
        await V2pnUserPath.WithFolder("", Folder, Expand).Should().BeEqualTo(Folder);
    }

    [Test]
    public async Task AFolderAlreadyThere_IsNotAddedTwice()
    {
        await (V2pnUserPath.WithFolder(@"C:\Tools;c:\users\ME\appdata\local\programs\v2pn\", Folder, Expand) == null).Should().BeTrue();
        await (V2pnUserPath.WithFolder(@"%LOCALAPPDATA%\Programs\V2pn;C:\Tools", Folder, Expand) == null).Should().BeTrue();
        await (V2pnUserPath.WithFolder("\"" + Folder + "\"", Folder, Expand) == null).Should().BeTrue();
        // A folder under it is another folder.
        await (V2pnUserPath.WithFolder(Folder + @"\bin", Folder, Expand) == null).Should().BeFalse();
    }
}

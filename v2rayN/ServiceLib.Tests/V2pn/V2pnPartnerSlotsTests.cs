namespace ServiceLib.Tests.V2pn;

public class V2pnPartnerSlotsTests
{
    // Just the header bytes the size is read from.
    private static byte[] Png(int width, int height)
    {
        var b = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        b[16] = (byte)((width >> 24) & 0xFF); b[17] = (byte)((width >> 16) & 0xFF); b[18] = (byte)((width >> 8) & 0xFF); b[19] = (byte)(width & 0xFF);
        b[20] = (byte)((height >> 24) & 0xFF); b[21] = (byte)((height >> 16) & 0xFF); b[22] = (byte)((height >> 8) & 0xFF); b[23] = (byte)(height & 0xFF);
        return b;
    }

    private static byte[] WebpX(int width, int height)
    {
        var b = new byte[30];
        "RIFF"u8.ToArray().CopyTo(b, 0);
        "WEBPVP8X"u8.ToArray().CopyTo(b, 8);
        var w = width - 1;
        var h = height - 1;
        b[24] = (byte)(w & 0xFF); b[25] = (byte)((w >> 8) & 0xFF); b[26] = (byte)((w >> 16) & 0xFF);
        b[27] = (byte)(h & 0xFF); b[28] = (byte)((h >> 8) & 0xFF); b[29] = (byte)((h >> 16) & 0xFF);
        return b;
    }

    private static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, // APP0, length 4
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)((height >> 8) & 0xFF), (byte)(height & 0xFF), (byte)((width >> 8) & 0xFF), (byte)(width & 0xFF), 0x03, 0, 0, 0, 0, 0, 0,
    ];

    [Test]
    public async Task ImageSize_ReadsPngJpegAndWebpHeaders()
    {
        await V2pnPartnerSlots.ImageSize(Png(337, 96)).Should().BeEqualTo((337, 96));
        await V2pnPartnerSlots.ImageSize(Jpeg(640, 480)).Should().BeEqualTo((640, 480));
        await V2pnPartnerSlots.ImageSize(WebpX(1200, 900)).Should().BeEqualTo((1200, 900));
        await (V2pnPartnerSlots.ImageSize([1, 2, 3]) == null).Should().BeTrue();
    }

    [Test]
    public async Task Fit_KeepsWhatFits_AndSaysWhyTheRestIsNotShown()
    {
        var cert = TestPartner.Listing();
        var json = TestPartner.Pack();
        var read = V2pnPartnerTrust.ReadPack(json, cert, out _)!;
        var pack = read with
        {
            Logo = "logo.png",
            LogoOnLight = "tall.png",
            Mark = "mark.png",
            Theme = read.Theme! with { Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Image, [], "lamp.webp") },
        };
        var files = new Dictionary<string, byte[]>
        {
            ["logo.png"] = Png(337, 96),
            ["tall.png"] = Png(96, 337),
            ["mark.png"] = Png(128, 128),
            ["lamp.webp"] = WebpX(4000, 4000),
        };

        var fitted = V2pnPartnerSlots.Fit(pack, name => files.GetValueOrDefault(name), out var problems);

        await fitted.Logo.Should().BeEqualTo("logo.png");
        await fitted.Mark.Should().BeEqualTo("mark.png");
        await (fitted.LogoOnLight == null).Should().BeTrue();
        await fitted.Theme!.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        await problems.Count.Should().BeEqualTo(2);
        await problems.Any(p => p.Contains("96×337")).Should().BeTrue();
    }
}

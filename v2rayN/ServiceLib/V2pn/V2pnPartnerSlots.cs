namespace ServiceLib;

/// <summary>
/// The picture slots of a partner pack and what fits each, like the fixed image slots of a wallet pass: a picture
/// that does not fit its slot is not shown there (V2pn falls back to the partner's name, or to nothing) and
/// tools/partner-check.cs says why. The pack itself stays valid, so one wrong picture never takes the look away.
/// </summary>
public static class V2pnPartnerSlots
{
    public sealed record Slot(string Name, string Description, Func<int, int, bool> Fits);

    /// <summary>The wide logo of the title bar, for dark and light grounds: 2:1 to 8:1, 32 to 256 px tall.</summary>
    public static readonly Slot Logo = new("logo", "a wide logo, 2:1 to 8:1, 32-256 px tall",
        (w, h) => h is >= 32 and <= 256 && w >= h * 2 && w <= h * 8);

    /// <summary>The square mark of the subscription card: square within 5 %, 64 to 512 px.</summary>
    public static readonly Slot Mark = new("mark", "a square mark, 64-512 px",
        (w, h) => w is >= 64 and <= 512 && Math.Abs(w - h) <= w / 20);

    /// <summary>The picture behind the Scene (theme.background.art): 640 to 3200 px a side, at most 3:1 either way.</summary>
    public static readonly Slot Backdrop = new("backdrop", "a background picture, 640-3200 px a side, at most 3:1",
        (w, h) => w is >= 640 and <= 3200 && h is >= 640 and <= 3200 && w <= h * 3 && h <= w * 3);

    /// <summary>Every slot a pack has, by the name partner-check uses.</summary>
    public static readonly IReadOnlyList<Slot> All = [Logo, Mark, Backdrop];

    public static Slot? ByName(string name) => All.FirstOrDefault(s => s.Name == name);

    /// <summary>Why a picture cannot go into a slot (the theme builder says it at once, before anything is saved).</summary>
    public enum Problem
    {
        None,

        /// <summary>Larger than V2pn takes for one picture (1 MB).</summary>
        TooLarge,

        /// <summary>Not a PNG, JPEG or WebP whose size V2pn can read.</summary>
        NotPicture,

        /// <summary>Its width and height do not fit the slot.</summary>
        WrongSize,
    }

    /// <summary>Whether <paramref name="content"/> may go into <paramref name="slot"/>, with its size when V2pn could read it.</summary>
    public static (Problem Problem, int Width, int Height) Check(Slot slot, byte[] content)
    {
        var size = ImageSize(content);
        var (width, height) = size ?? (0, 0);
        if (content.LongLength > V2pnPartnerTrust.MaxAssetBytes)
        {
            return (Problem.TooLarge, width, height);
        }
        if (size == null || width <= 0 || height <= 0)
        {
            return (Problem.NotPicture, 0, 0);
        }
        return (slot.Fits(width, height) ? Problem.None : Problem.WrongSize, width, height);
    }

    /// <summary>
    /// The pack with every picture that does not fit its slot taken out of that slot, and why, one line each.
    /// A picture whose size cannot be read counts as not fitting.
    /// </summary>
    public static V2pnPartnerPack Fit(V2pnPartnerPack pack, Func<string, byte[]?> read, out IReadOnlyList<string> problems)
    {
        var notes = new List<string>();
        string? Check(string? file, Slot slot, string role)
        {
            if (file == null)
            {
                return null;
            }
            if (read(file) is not { } bytes || ImageSize(bytes) is not { } size)
            {
                notes.Add($"{role} {file}: not a PNG, JPEG or WebP V2pn can read the size of");
                return null;
            }
            if (!slot.Fits(size.Width, size.Height))
            {
                notes.Add($"{role} {file} is {size.Width}×{size.Height}; the slot takes {slot.Description}");
                return null;
            }
            return file;
        }

        var theme = pack.Theme;
        if (theme != null)
        {
            var background = theme.Background;
            if (background.Kind == V2pnThemeBackgroundKinds.Image && Check(background.Art, Backdrop, "theme.background.art") == null)
            {
                // No picture that fits: the brand colour, as a theme without a background.
                background = V2pnThemeBackground.Solid;
            }
            theme = theme with { Background = background };
        }
        problems = notes;
        return pack with
        {
            Logo = Check(pack.Logo, Logo, "brand.logo"),
            LogoOnLight = Check(pack.LogoOnLight, Logo, "brand.logoOnLight"),
            Mark = Check(pack.Mark, Mark, "brand.mark"),
            Theme = theme,
        };
    }

    /// <summary>Width and height from a PNG, JPEG or WebP header; null for anything else.</summary>
    public static (int Width, int Height)? ImageSize(byte[] b)
    {
        try
        {
            // PNG: the IHDR chunk follows the 8-byte signature.
            if (b.Length >= 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G')
            {
                return (BigEndian(b, 16), BigEndian(b, 20));
            }
            // JPEG: walk the markers to the first start-of-frame.
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
            {
                var i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF)
                    {
                        i++;
                        continue;
                    }
                    var marker = b[i + 1];
                    var length = (b[i + 2] << 8) | b[i + 3];
                    if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                    {
                        return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                    }
                    i += 2 + length;
                }
                return null;
            }
            // WebP: RIFF....WEBP, then VP8 (lossy), VP8L (lossless) or VP8X (extended).
            if (b.Length >= 30 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
            {
                var chunk = System.Text.Encoding.ASCII.GetString(b, 12, 4);
                return chunk switch
                {
                    "VP8 " => (((b[27] << 8) | b[26]) & 0x3FFF, ((b[29] << 8) | b[28]) & 0x3FFF),
                    "VP8L" => (1 + (((b[22] & 0x3F) << 8) | b[21]), 1 + (((b[24] & 0x0F) << 10) | (b[23] << 2) | ((b[22] & 0xC0) >> 6))),
                    "VP8X" => (1 + (b[24] | (b[25] << 8) | (b[26] << 16)), 1 + (b[27] | (b[28] << 8) | (b[29] << 16))),
                    _ => null,
                };
            }
        }
        catch (IndexOutOfRangeException)
        {
        }
        return null;
    }

    private static int BigEndian(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
}

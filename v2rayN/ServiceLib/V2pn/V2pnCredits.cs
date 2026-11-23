namespace ServiceLib;

/// <summary>
/// V2pn: the text under the version in the Mac's About panel, as RTF: plain centred lines and links. RTF is ASCII,
/// so every other character is written as its UTF-16 code.
/// </summary>
public static class V2pnCredits
{
    public sealed record Link(string Text, string Url);

    /// <summary>Each element is a line: a string, a <see cref="Link"/>, or several of them side by side.</summary>
    public static string Rtf(IEnumerable<object[]> lines)
    {
        var sb = new StringBuilder(@"{\rtf1\ansi\uc0\pard\qc ");
        var first = true;
        foreach (var line in lines)
        {
            if (!first)
            {
                sb.Append(@"\line ");
            }
            first = false;
            foreach (var part in line)
            {
                if (part is Link link)
                {
                    sb.Append(@"{\field{\*\fldinst{HYPERLINK """).Append(Escape(link.Url)).Append(@"""}}{\fldrslt ").Append(Escape(link.Text)).Append("}}");
                }
                else
                {
                    sb.Append(Escape(part?.ToString() ?? string.Empty));
                }
            }
        }
        return sb.Append('}').ToString();
    }

    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '{' or '}')
            {
                sb.Append('\\').Append(c);
            }
            else if (c is >= ' ' and < (char)127)
            {
                sb.Append(c);
            }
            else if (c == '\n')
            {
                sb.Append(@"\line ");
            }
            else
            {
                // \uN takes a signed 16-bit number; the space ends it and is not shown.
                sb.Append(@"\u").Append(unchecked((short)c)).Append(' ');
            }
        }
        return sb.ToString();
    }
}

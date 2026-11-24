using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace ServiceLib;

/// <summary>
/// V2pn: limits for the YAML of a Clash / mihomo config that came with a subscription. Aliases written out whole and
/// merges of merges grow exponentially ("a billion laughs"): a few hundred bytes could take all memory.
/// </summary>
public static class V2pnYamlLimits
{
    public const int MaxEvents = 2_000_000;
    public const int MaxChars = 32 * 1024 * 1024;

    /// <summary>Stops a parse that goes past <see cref="MaxEvents"/> events.</summary>
    public sealed class CountingParser(IParser inner, int max = MaxEvents) : IParser
    {
        private int _count;

        public ParsingEvent? Current => inner.Current;

        public bool MoveNext()
        {
            if (++_count > max)
            {
                throw new InvalidDataException("The YAML expands past what V2pn reads.");
            }
            return inner.MoveNext();
        }
    }

    /// <summary>A StringWriter that stops past <see cref="MaxChars"/> characters.</summary>
    public sealed class LimitedWriter(int max = MaxChars) : StringWriter
    {
        private void Check(int more)
        {
            if (GetStringBuilder().Length + more > max)
            {
                throw new InvalidDataException("The YAML expands past what V2pn writes.");
            }
        }

        public override void Write(char value)
        {
            Check(1);
            base.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Check(count);
            base.Write(buffer, index, count);
        }

        public override void Write(string? value)
        {
            Check(value?.Length ?? 0);
            base.Write(value);
        }
    }
}

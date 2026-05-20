using System.Text;

namespace DiagKit.Dbc;

internal static class DbcQuotedText
{
    public static bool IsEscapedQuote(string text, int quoteIndex)
    {
        var backslashCount = 0;
        for (var i = quoteIndex - 1; i >= 0 && text[i] == '\\'; i--)
        {
            backslashCount++;
        }

        return backslashCount % 2 == 1;
    }

    public static string Unescape(string text)
    {
        var slashIndex = text.IndexOf('\\', StringComparison.Ordinal);
        if (slashIndex < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        builder.Append(text.AsSpan(0, slashIndex));
        for (var i = slashIndex; i < text.Length; i++)
        {
            if (text[i] == '\\' &&
                i + 1 < text.Length &&
                (text[i + 1] == '"' || text[i + 1] == '\\'))
            {
                builder.Append(text[i + 1]);
                i++;
                continue;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }
}

using System.Text;

namespace CodeIndex.Core;

internal static class EcmaScriptScanner
{
    public static string[] Mask(string source)
    {
        var characters = source.ToCharArray();
        var state = ScanState.Code;
        var templateExpressionDepth = 0;
        var previousSignificant = '\0';

        for (var index = 0; index < characters.Length; index++)
        {
            var current = characters[index];
            var next = index + 1 < characters.Length ? characters[index + 1] : '\0';

            if (state == ScanState.LineComment)
            {
                if (current == '\n')
                {
                    state = ScanState.Code;
                }
                else
                {
                    characters[index] = ' ';
                }

                continue;
            }

            if (state == ScanState.BlockComment)
            {
                characters[index] = current == '\n' ? '\n' : ' ';
                if (current == '*' && next == '/')
                {
                    characters[++index] = ' ';
                    state = ScanState.Code;
                }

                continue;
            }

            if (state is ScanState.SingleQuote or ScanState.DoubleQuote or ScanState.Regex)
            {
                var delimiter = state switch
                {
                    ScanState.SingleQuote => '\'',
                    ScanState.DoubleQuote => '"',
                    _ => '/'
                };

                characters[index] = current == '\n' ? '\n' : ' ';
                if (current == '\\' && next != '\0')
                {
                    characters[++index] = next == '\n' ? '\n' : ' ';
                    continue;
                }

                if (current == delimiter)
                {
                    state = ScanState.Code;
                }

                continue;
            }

            if (state == ScanState.Template)
            {
                characters[index] = current == '\n' ? '\n' : ' ';
                if (current == '\\' && next != '\0')
                {
                    characters[++index] = next == '\n' ? '\n' : ' ';
                    continue;
                }

                if (current == '`')
                {
                    state = ScanState.Code;
                    continue;
                }

                if (current == '$' && next == '{')
                {
                    characters[index] = ' ';
                    characters[++index] = '{';
                    templateExpressionDepth = 1;
                    state = ScanState.Code;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                characters[index] = characters[++index] = ' ';
                state = ScanState.LineComment;
                continue;
            }

            if (current == '/' && next == '*')
            {
                characters[index] = characters[++index] = ' ';
                state = ScanState.BlockComment;
                continue;
            }

            if (current == '\'')
            {
                characters[index] = ' ';
                state = ScanState.SingleQuote;
                continue;
            }

            if (current == '"')
            {
                characters[index] = ' ';
                state = ScanState.DoubleQuote;
                continue;
            }

            if (current == '`')
            {
                characters[index] = ' ';
                state = ScanState.Template;
                continue;
            }

            if (current == '/' && IsRegexStart(previousSignificant))
            {
                characters[index] = ' ';
                state = ScanState.Regex;
                continue;
            }

            if (templateExpressionDepth > 0)
            {
                if (current == '{')
                {
                    templateExpressionDepth++;
                }
                else if (current == '}' && --templateExpressionDepth == 0)
                {
                    state = ScanState.Template;
                }
            }

            if (!char.IsWhiteSpace(current))
            {
                previousSignificant = current;
            }
        }

        return MultiLanguageSymbolParser.SplitLines(new string(characters));
    }

    public static bool TryJoinDeclaration(IReadOnlyList<string> maskedLines, int startIndex, out string declaration, out int endIndex)
    {
        declaration = string.Empty;
        endIndex = startIndex;
        if (startIndex < 0 || startIndex >= maskedLines.Count)
        {
            return false;
        }

        var builder = new StringBuilder();
        var parenthesisDepth = 0;
        var seenParenthesis = false;
        for (var index = startIndex; index < maskedLines.Count; index++)
        {
            var line = maskedLines[index];
            builder.Append(index == startIndex ? line : " " + line.Trim());
            parenthesisDepth += MultiLanguageSymbolParser.CountOccurrences(line, '(') - MultiLanguageSymbolParser.CountOccurrences(line, ')');
            seenParenthesis |= line.Contains('(');

            if ((!seenParenthesis || parenthesisDepth <= 0) &&
                (line.Contains('{') || line.Contains(';') || line.Contains("=>", StringComparison.Ordinal)))
            {
                declaration = builder.ToString();
                endIndex = index;
                return true;
            }
        }

        declaration = builder.ToString();
        endIndex = maskedLines.Count - 1;
        return !string.IsNullOrWhiteSpace(declaration);
    }

    private static bool IsRegexStart(char previous) =>
        previous == '\0' || previous is '(' or '[' or '{' or '=' or ':' or ',' or ';' or '!' or '&' or '|' or '?' or '+' or '-' or '*' or '%' or '~' or '^' or '<' or '>';

    private enum ScanState
    {
        Code,
        LineComment,
        BlockComment,
        SingleQuote,
        DoubleQuote,
        Template,
        Regex
    }
}

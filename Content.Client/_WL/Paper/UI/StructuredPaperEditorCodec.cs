using System.Text;
using System.Linq;
using Content.Shared._WL.Paper;

namespace Content.Client._WL.Paper.UI;

/// <summary>
/// Converts structured paper elements to the compact source shown by the full paper editor.
/// Existing elements are reconciled by type and value when that match is unambiguous. New or changed elements
/// deliberately receive new stable IDs from the server when the full structure is replaced.
/// </summary>
public sealed class StructuredPaperEditorCodec
{
    private readonly List<StructuredPaperElement> _originalElements;

    private StructuredPaperEditorCodec(IReadOnlyList<StructuredPaperElement> elements)
    {
        _originalElements = elements.Select(element => element.Copy()).ToList();
    }

    public static StructuredPaperEditorCodec Create(
        IReadOnlyList<StructuredPaperElement> elements,
        bool appendOnly,
        out string source)
    {
        var codec = new StructuredPaperEditorCodec(elements);
        if (appendOnly)
        {
            source = string.Empty;
            return codec;
        }

        var builder = new StringBuilder();
        foreach (var element in elements)
        {
            switch (element.Type)
            {
                case StructuredPaperElementType.StaticText:
                    builder.Append(EscapeRaw(element.Text));
                    break;
                case StructuredPaperElementType.HandwrittenText:
                    AppendTag(builder, "w", element);
                    break;
                case StructuredPaperElementType.SingleLineField:
                case StructuredPaperElementType.SignatureField:
                    AppendTag(builder, "f", element);
                    break;
                case StructuredPaperElementType.MultilineField:
                    AppendTag(builder, "lf", element);
                    break;
                case StructuredPaperElementType.Signature:
                    AppendTag(builder, "sign", element);
                    break;
            }

            if (element.NewLineAfter)
                builder.Append('\n');
        }

        source = builder.ToString();
        return codec;
    }

    public bool TryParse(
        string source,
        bool appendOnly,
        PaperHandwritingStyle handwritingStyle,
        out List<StructuredPaperElement> elements)
    {
        elements = new List<StructuredPaperElement>();
        var rawStart = 0;
        var index = 0;

        while (index < source.Length)
        {
            if (source[index] != '[')
            {
                index++;
                continue;
            }

            if (IsEscaped(source, index))
            {
                index++;
                continue;
            }

            if (IsClosingEditorTag(source, index))
                return false;

            if (!TryReadOpeningTag(source, index, out var tag, out var openingEnd))
            {
                if (LooksLikeAliasedEditorTag(source, index))
                    return false;
                index++;
                continue;
            }

            AddRaw(elements, source[rawStart..index], appendOnly, handwritingStyle);

            var closingTag = $"[/{tag.Name}]";
            var closingStart = FindUnescaped(source, closingTag, openingEnd);
            var hasClosingTag = closingStart >= 0;
            if (!hasClosingTag && (tag.Name == "w" || !appendOnly))
                return false;

            var value = hasClosingTag
                ? UnescapeValue(source[openingEnd..closingStart], closingTag)
                : string.Empty;
            var nextIndex = hasClosingTag
                ? closingStart + closingTag.Length
                : openingEnd;

            if (!TryCreateElement(tag, value, handwritingStyle, out var element))
                return false;

            elements.Add(element);
            index = nextIndex;
            rawStart = nextIndex;
        }

        AddRaw(elements, source[rawStart..], appendOnly, handwritingStyle);
        if (!appendOnly)
            ReconcileOriginalElements(elements);
        return true;
    }

    private static void AppendTag(StringBuilder builder, string tag, StructuredPaperElement element)
    {
        var closingTag = $"[/{tag}]";
        builder.Append('[').Append(tag).Append(']');
        builder.Append(EscapeValue(element.Text, closingTag));
        builder.Append(closingTag);
    }

    private bool TryCreateElement(
        EditorTag tag,
        string value,
        PaperHandwritingStyle handwritingStyle,
        out StructuredPaperElement element)
    {
        var type = tag.Name switch
        {
            "f" => StructuredPaperElementType.SingleLineField,
            "lf" => StructuredPaperElementType.MultilineField,
            "sign" => StructuredPaperElementType.Signature,
            "w" => StructuredPaperElementType.HandwrittenText,
            _ => throw new InvalidOperationException(),
        };

        if (type is (StructuredPaperElementType.SingleLineField or StructuredPaperElementType.Signature) &&
            (value.Contains('\n') || value.Contains('\r')))
        {
            element = default!;
            return false;
        }

        element = new StructuredPaperElement(string.Empty, type, value, newLineAfter: false)
        {
            HandwritingStyle = handwritingStyle,
        };
        return true;
    }

    private void ReconcileOriginalElements(List<StructuredPaperElement> parsed)
    {
        var originalGroups = _originalElements
            .Select((element, index) => (Identity: GetIdentity(element), Index: index))
            .GroupBy(entry => entry.Identity)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Index).ToList());
        var parsedGroups = parsed
            .Select((element, index) => (Identity: GetIdentity(element), Index: index))
            .GroupBy(entry => entry.Identity);

        foreach (var group in parsedGroups)
        {
            if (group.Count() != 1 ||
                !originalGroups.TryGetValue(group.Key, out var originalMatches) ||
                originalMatches.Count != 1)
            {
                continue;
            }

            var parsedIndex = group.Single().Index;
            var parsedElement = parsed[parsedIndex];
            var reconciled = _originalElements[originalMatches[0]].Copy();
            reconciled.Type = parsedElement.Type;
            reconciled.Text = parsedElement.Text;
            reconciled.NewLineAfter = parsedElement.NewLineAfter;
            reconciled.LocId = null;
            parsed[parsedIndex] = reconciled;
        }
    }

    private static EditorIdentity GetIdentity(StructuredPaperElement element)
    {
        var type = element.Type == StructuredPaperElementType.SignatureField
            ? StructuredPaperElementType.SingleLineField
            : element.Type;

        return new EditorIdentity(type, element.Text);
    }

    private static void AddRaw(
        List<StructuredPaperElement> elements,
        string text,
        bool appendOnly,
        PaperHandwritingStyle handwritingStyle)
    {
        if (text.Length == 0)
            return;

        if (appendOnly)
        {
            AddNormalizedAppendRaw(elements, UnescapeRaw(text), handwritingStyle);
            return;
        }

        var type = appendOnly
            ? StructuredPaperElementType.HandwrittenText
            : StructuredPaperElementType.StaticText;
        var unescaped = UnescapeRaw(text);
        if (elements.Count > 0 && elements[^1].Type == type && string.IsNullOrEmpty(elements[^1].Id))
        {
            elements[^1].Text += unescaped;
            return;
        }

        elements.Add(new StructuredPaperElement(string.Empty, type, unescaped, newLineAfter: false)
        {
            HandwritingStyle = type == StructuredPaperElementType.HandwrittenText
                ? handwritingStyle
                : PaperHandwritingStyle.Default,
        });
    }

    private static void AddNormalizedAppendRaw(
        List<StructuredPaperElement> elements,
        string text,
        PaperHandwritingStyle handwritingStyle)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!string.IsNullOrWhiteSpace(line))
            {
                elements.Add(new StructuredPaperElement(
                    string.Empty,
                    StructuredPaperElementType.HandwrittenText,
                    line,
                    newLineAfter: i < lines.Length - 1)
                {
                    HandwritingStyle = handwritingStyle,
                });
            }
            else if (i > 0 && elements.Count > 0)
            {
                elements[^1].NewLineAfter = true;
            }
        }
    }

    private static bool TryReadOpeningTag(string source, int start, out EditorTag tag, out int end)
    {
        tag = default;
        end = start;
        var bracket = source.IndexOf(']', start + 1);
        if (bracket < 0)
            return false;

        var name = source[(start + 1)..bracket];
        if (name is not ("f" or "lf" or "sign" or "w"))
            return false;

        tag = new EditorTag(name);
        end = bracket + 1;
        return true;
    }

    private static bool IsClosingEditorTag(string source, int index)
    {
        return source.AsSpan(index).StartsWith("[/f]") ||
            source.AsSpan(index).StartsWith("[/lf]") ||
            source.AsSpan(index).StartsWith("[/sign]") ||
            source.AsSpan(index).StartsWith("[/w]");
    }

    private static bool LooksLikeAliasedEditorTag(string source, int index)
    {
        var remaining = source.AsSpan(index);
        return remaining.StartsWith("[f:") ||
            remaining.StartsWith("[lf:") ||
            remaining.StartsWith("[sign:") ||
            remaining.StartsWith("[w:");
    }

    private static int FindUnescaped(string source, string value, int start)
    {
        var index = source.IndexOf(value, start, StringComparison.Ordinal);
        while (index >= 0)
        {
            if (!IsEscaped(source, index))
                return index;

            index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return -1;
    }

    private static bool IsEscaped(string source, int index)
    {
        var slashes = 0;
        for (var cursor = index - 1; cursor >= 0 && source[cursor] == '\\'; cursor--)
            slashes++;

        return slashes % 2 != 0;
    }

    private static string EscapeRaw(string value)
    {
        var escaped = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\')
            {
                escaped.Append("\\\\");
                continue;
            }

            if (character == '[' &&
                (IsClosingEditorTag(value, index) || TryReadOpeningTag(value, index, out _, out _)))
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }

    private static string UnescapeRaw(string value)
    {
        var unescaped = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index + 1 >= value.Length ||
                value[index + 1] is not ('\\' or '['))
            {
                unescaped.Append(value[index]);
                continue;
            }

            unescaped.Append(value[++index]);
        }

        return unescaped.ToString();
    }

    private static string EscapeValue(string value, string closingTag)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(closingTag, $"\\{closingTag}", StringComparison.Ordinal);
    }

    private static string UnescapeValue(string value, string closingTag)
    {
        return value.Replace($"\\{closingTag}", closingTag, StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private readonly record struct EditorTag(string Name);
    private readonly record struct EditorIdentity(StructuredPaperElementType Type, string Text);
}

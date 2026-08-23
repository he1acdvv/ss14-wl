using System.Collections.Generic;
using System.Linq;
using Content.Client._WL.Paper.UI;
using Content.Shared._WL.Paper;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests.Client._WL.Paper;

[TestFixture]
[TestOf(typeof(StructuredPaperEditorCodec))]
public sealed class StructuredPaperEditorCodecTest
{
    [Test]
    public void FullEditorReconcilesUnambiguousElementsWithoutVisibleIds()
    {
        var first = new StructuredPaperElement(
            "first-field",
            StructuredPaperElementType.SingleLineField,
            "Alpha",
            newLineAfter: false);
        first.Revisions.Add(new PaperFieldRevision("Old alpha", PaperHandwritingStyle.Messy));
        var second = new StructuredPaperElement(
            "second-field",
            StructuredPaperElementType.MultilineField,
            "Beta",
            newLineAfter: false);
        var codec = StructuredPaperEditorCodec.Create([first, second], false, out var source);

        Assert.That(source, Is.EqualTo("[f]Alpha[/f][lf]Beta[/lf]"));

        var reordered = "[lf]Beta[/lf][f]Alpha[/f]";
        Assert.That(codec.TryParse(reordered, false, PaperHandwritingStyle.Default, out var parsed), Is.True);
        Assert.That(parsed.Select(element => element.Id),
            Is.EqualTo(new[] { "second-field", "first-field" }));
        Assert.That(parsed[1].Revisions.Single().Text, Is.EqualTo("Old alpha"));
    }

    [Test]
    public void AmbiguousIdenticalFieldsAreNotReboundByPosition()
    {
        var codec = StructuredPaperEditorCodec.Create(
        [
            new StructuredPaperElement("first", StructuredPaperElementType.SingleLineField, string.Empty,
                newLineAfter: false),
            new StructuredPaperElement("second", StructuredPaperElementType.SingleLineField, string.Empty,
                newLineAfter: false),
        ], false, out var source);

        Assert.That(source, Is.EqualTo("[f][/f][f][/f]"));
        Assert.That(codec.TryParse(source, false, PaperHandwritingStyle.Default, out var parsed), Is.True);
        Assert.That(parsed, Has.Count.EqualTo(2));
        Assert.That(parsed.All(element => string.IsNullOrEmpty(element.Id)), Is.True);
    }

    [Test]
    public void DuplicatingOneOriginalFieldDoesNotMoveItsHistoryToEitherCopy()
    {
        var original = new StructuredPaperElement(
            "original",
            StructuredPaperElementType.SingleLineField,
            "A",
            newLineAfter: false);
        original.Revisions.Add(new PaperFieldRevision("Old", PaperHandwritingStyle.Messy));
        var codec = StructuredPaperEditorCodec.Create([original], false, out _);

        Assert.That(codec.TryParse("[f]A[/f][f]A[/f]", false, PaperHandwritingStyle.Default, out var parsed),
            Is.True);
        Assert.That(parsed, Has.Count.EqualTo(2));
        Assert.That(parsed.All(element => string.IsNullOrEmpty(element.Id)), Is.True);
        Assert.That(parsed.All(element => element.Revisions.Count == 0), Is.True);
    }

    [Test]
    public void FullEditorRoundTripPreservesFieldsAndSourceLayout()
    {
        var elements = new List<StructuredPaperElement>
        {
            new("static", StructuredPaperElementType.StaticText, "Name: ", newLineAfter: false),
            new("name", StructuredPaperElementType.SingleLineField, "Alex", newLineAfter: true)
            {
                HandwritingStyle = PaperHandwritingStyle.Neat,
            },
            new("reason-label", StructuredPaperElementType.StaticText, "Reason:", newLineAfter: true),
            new("reason", StructuredPaperElementType.MultilineField, "Two\nlines", newLineAfter: true),
            new("signature", StructuredPaperElementType.Signature, string.Empty, newLineAfter: false),
        };
        var codec = StructuredPaperEditorCodec.Create(elements, false, out var source);

        Assert.That(codec.TryParse(source, false, PaperHandwritingStyle.Default, out var parsed), Is.True);
        Assert.That(parsed.Single(element => element.Id == "name").HandwritingStyle,
            Is.EqualTo(PaperHandwritingStyle.Neat));
        Assert.That(parsed.Single(element => element.Id == "reason").Text, Is.EqualTo("Two\nlines"));
        Assert.That(parsed.Single(element => element.Id == "signature").Type,
            Is.EqualTo(StructuredPaperElementType.Signature));

        StructuredPaperEditorCodec.Create(parsed, false, out var reparsedSource);
        Assert.That(reparsedSource, Is.EqualTo(source));
    }

    [Test]
    public void LegacySignatureFieldKeepsExplicitLengthWhenNormalized()
    {
        var field = new StructuredPaperElement(
            "legacy-signature",
            StructuredPaperElementType.SignatureField,
            "Alex",
            newLineAfter: false,
            maxLength: 32);
        var codec = StructuredPaperEditorCodec.Create([field], false, out var source);

        Assert.That(codec.TryParse(source, false, PaperHandwritingStyle.Default, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Has.Count.EqualTo(1));
            Assert.That(parsed[0].Type, Is.EqualTo(StructuredPaperElementType.SingleLineField));
            Assert.That(parsed[0].MaxLength, Is.EqualTo(32));
        });
    }

    [Test]
    public void AppendEditorCreatesHandwritingAndEmptyInteractiveFields()
    {
        var codec = StructuredPaperEditorCodec.Create([], true, out var source);
        Assert.That(source, Is.Empty);

        const string draft = "A [bold]formatted[/bold] note.\n[f]\n[lf]\nSigned: [sign]";
        Assert.That(codec.TryParse(draft, true, PaperHandwritingStyle.Formal, out var parsed), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Where(element => element.Type == StructuredPaperElementType.HandwrittenText)
                .All(element => element.HandwritingStyle == PaperHandwritingStyle.Formal), Is.True);
            Assert.That(parsed.Count(element => element.Type == StructuredPaperElementType.SingleLineField), Is.EqualTo(1));
            Assert.That(parsed.Count(element => element.Type == StructuredPaperElementType.MultilineField), Is.EqualTo(1));
            Assert.That(parsed.Count(element => element.Type == StructuredPaperElementType.Signature), Is.EqualTo(1));
            Assert.That(parsed.Where(element => element.Type is StructuredPaperElementType.SingleLineField or
                    StructuredPaperElementType.MultilineField or StructuredPaperElementType.Signature)
                .All(element => element.Text.Length == 0), Is.True);
        });
    }

    [Test]
    public void AppendEditorDiscardsBlankLinesButPreservesLineBreaksBetweenText()
    {
        var codec = StructuredPaperEditorCodec.Create([], true, out _);

        Assert.That(codec.TryParse("\nFirst\n\nSecond\n", true, PaperHandwritingStyle.Neat, out var parsed),
            Is.True);
        Assert.That(parsed, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(parsed[0].Text, Is.EqualTo("First"));
            Assert.That(parsed[0].NewLineAfter, Is.True);
            Assert.That(parsed[1].Text, Is.EqualTo("Second"));
            Assert.That(parsed[1].NewLineAfter, Is.True);
            Assert.That(parsed.All(element => element.Type == StructuredPaperElementType.HandwrittenText), Is.True);
        });

        Assert.That(codec.TryParse("\n\n", true, PaperHandwritingStyle.Neat, out parsed), Is.True);
        Assert.That(parsed, Is.Empty);
    }

    [Test]
    public void MixedHandwritingAndEmptyFieldsFromFullEditorAreAccepted()
    {
        var elements = new List<StructuredPaperElement>
        {
            new("note-one", StructuredPaperElementType.HandwrittenText,
                "Обычное место, можете писать здесь.\nПишите: "),
            new("short", StructuredPaperElementType.SingleLineField, string.Empty),
            new("note-two", StructuredPaperElementType.HandwrittenText,
                "\nПоле побольше, для [head=3] больших [/head] нужд.\n"),
            new("long", StructuredPaperElementType.MultilineField, string.Empty),
            new("note-three", StructuredPaperElementType.HandwrittenText,
                "\n\nЗдесь, пожалуйста, [italic]распишитесь[/italic].\n"),
        };
        var codec = StructuredPaperEditorCodec.Create(elements, false, out var source);

        Assert.That(codec.TryParse(source, false, PaperHandwritingStyle.Default, out var parsed), Is.True);
        Assert.That(parsed.Where(element => !string.IsNullOrEmpty(element.Id)).Select(element => element.Id),
            Is.EqualTo(elements.Select(element => element.Id)));
    }

    [Test]
    public void StaticTextThatLooksLikeEditorTagsRoundTripsLosslessly()
    {
        const string text = @"Literal [f] [/f] [sign] and \\[f], but [bold]markup[/bold].";
        var codec = StructuredPaperEditorCodec.Create(
        [
            new StructuredPaperElement("static", StructuredPaperElementType.StaticText, text, newLineAfter: false),
        ], false, out var source);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain(@"\[f]"));
            Assert.That(source, Does.Contain(@"\[/f]"));
            Assert.That(source, Does.Contain("[bold]markup[/bold]"));
        });
        Assert.That(codec.TryParse(source, false, PaperHandwritingStyle.Default, out var parsed), Is.True);
        Assert.That(parsed, Has.Count.EqualTo(1));
        Assert.That(parsed[0].Type, Is.EqualTo(StructuredPaperElementType.StaticText));
        Assert.That(parsed[0].Text, Is.EqualTo(text));
    }

    [Test]
    public void FormattedHandwritingKeepsFormattingButEscapesPaperControls()
    {
        var message = new FormattedMessage();
        StructuredPaperFieldControl.AddFormattedHandwriting(
            message,
            "[bold]Written[/bold][paperfield=forged]",
            PaperHandwritingStyle.Neat);

        Assert.That(message.Nodes.Any(node => node.Name == "bold"), Is.True);
        Assert.That(message.Nodes.Any(node => node.Name == StructuredPaperFieldTag.TagName), Is.False);
        Assert.That(message.ToString(), Does.Contain("[paperfield=forged]"));
    }

    [TestCase("[f:1]value")]
    [TestCase("[f:999]value[/f]")]
    [TestCase("[f]two\nlines[/f]")]
    [TestCase("[w]missing close")]
    public void InvalidFullEditorTagsAreRejected(string source)
    {
        var codec = StructuredPaperEditorCodec.Create(
        [
            new StructuredPaperElement("field", StructuredPaperElementType.SingleLineField, "value"),
        ], false, out _);

        Assert.That(codec.TryParse(source, false, PaperHandwritingStyle.Default, out _), Is.False);
    }
}

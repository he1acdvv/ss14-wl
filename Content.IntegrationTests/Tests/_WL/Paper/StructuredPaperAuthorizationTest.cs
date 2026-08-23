using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WL.Paper;
using Content.Shared.Paper;
using static Content.Shared.Paper.PaperComponent;

namespace Content.IntegrationTests.Tests._WL.Paper;

[TestFixture]
public sealed class StructuredPaperAuthorizationTest : InteractionTest
{
    [Test]
    public async Task FullEditorCannotForgeElementIdentityOrRevisionHistory()
    {
        await SpawnTarget("Paper");
        var paper = STarget!.Value;
        var structured = SEntMan.EnsureComponent<StructuredPaperComponent>(paper);
        var original = new StructuredPaperElement(
            "original-field",
            StructuredPaperElementType.SingleLineField,
            "A",
            newLineAfter: false);
        original.Revisions.Add(new PaperFieldRevision("Authoritative", PaperHandwritingStyle.Default));
        var duplicateTarget = new StructuredPaperElement(
            "duplicate-target",
            StructuredPaperElementType.SingleLineField,
            "D",
            newLineAfter: false);
        duplicateTarget.Revisions.Add(new PaperFieldRevision("Other authoritative", PaperHandwritingStyle.Default));
        structured.Elements = [original, duplicateTarget];

        var forgedRevision = new PaperFieldRevision("Forged", PaperHandwritingStyle.Default);
        await InteractUsing("PenCentcom");
        await SendBui(PaperUiKey.Key, new PaperInputStructureMessage(
        [
            new StructuredPaperElement(
                "original-field",
                StructuredPaperElementType.SingleLineField,
                "A",
                newLineAfter: false)
            {
                Revisions = [forgedRevision],
            },
            new StructuredPaperElement(
                "duplicate-target",
                StructuredPaperElementType.SingleLineField,
                "B",
                newLineAfter: false)
            {
                Revisions = [forgedRevision],
            },
            new StructuredPaperElement(
                "duplicate-target",
                StructuredPaperElementType.SingleLineField,
                "D",
                newLineAfter: false)
            {
                Revisions = [forgedRevision],
            },
            new StructuredPaperElement(
                "attacker-selected-id",
                StructuredPaperElementType.SingleLineField,
                "C",
                newLineAfter: false)
            {
                Revisions = [forgedRevision],
            },
        ]));

        Assert.That(structured.Elements, Has.Count.EqualTo(4));
        Assert.That(structured.Elements[0].Id, Is.EqualTo("original-field"));
        Assert.That(structured.Elements[0].Revisions.Select(revision => revision.Text),
            Is.EqualTo(new[] { "Authoritative" }));
        Assert.That(structured.Elements.Skip(1).All(element => element.Id != "duplicate-target"), Is.True);
        Assert.That(structured.Elements.Select(element => element.Id), Does.Not.Contain("attacker-selected-id"));
        Assert.That(structured.Elements.Skip(1).All(element => element.Revisions.Count == 0), Is.True);
    }
}

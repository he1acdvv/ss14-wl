using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Corvax.Documents;
using Content.Shared._WL.Paper;
using Content.Shared.Paper;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WL.Paper;

[TestFixture]
public sealed class StructuredPaperPrinterTest : InteractionTest
{
    [Test]
    public async Task PrinterFormattingTransformsStructuredElements()
    {
        await SpawnTarget("Paper");
        var paper = STarget!.Value;
        var paperComponent = SEntMan.GetComponent<PaperComponent>(paper);
        var structured = SEntMan.EnsureComponent<StructuredPaperComponent>(paper);
        var localization = Server.ResolveDependency<ILocalizationManager>();
        var stationToken = localization.GetString("doc-var-station");
        var dateToken = localization.GetString("doc-var-date");
        var nameToken = localization.GetString("doc-var-name");
        var jobToken = localization.GetString("doc-var-job");
        structured.Elements =
        [
            new("station-name", StructuredPaperElementType.SingleLineField, stationToken),
            new("document-date", StructuredPaperElementType.SingleLineField, dateToken),
            new("author-name", StructuredPaperElementType.SingleLineField, nameToken),
            new("author-position", StructuredPaperElementType.SingleLineField, jobToken),
        ];

        var paperSystem = SEntMan.System<PaperSystem>();
        var printerSystem = SEntMan.System<DocumentPrinterSystem>();
        paperSystem.RefreshStructuredContent((paper, paperComponent), structured);
        paperSystem.TransformContent((paper, paperComponent), content =>
            printerSystem.FormatString(content, "NSS Test", stationTime: "12:34"));

        Assert.Multiple(() =>
        {
            Assert.That(structured.Elements[0].Text, Is.EqualTo("NSS Test"));
            Assert.That(structured.Elements[1].Text, Is.EqualTo("12:34"));
            Assert.That(structured.Elements[2].Text,
                Is.EqualTo(localization.GetString("doc-text-printer-default-name")));
            Assert.That(structured.Elements[3].Text,
                Is.EqualTo(localization.GetString("doc-text-printer-default-job")));
            Assert.That(paperComponent.Content,
                Is.EqualTo("NSS Test\n12:34\n" +
                    localization.GetString("doc-text-printer-default-name") + "\n" +
                    localization.GetString("doc-text-printer-default-job") + "\n"));
        });
    }
}

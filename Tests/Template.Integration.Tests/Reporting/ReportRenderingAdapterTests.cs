using System.Diagnostics;
using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Features.Reporting;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Reporting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Reporting;
using Template.Application.Services.Reporting;
using Template.Domain.Organization;
using Template.Domain.Reporting;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Reporting.Pdf;
using Template.Infrastructure.Reporting.Spreadsheets;
using Template.Integration.Tests.Data;
using UglyToad.PdfPig;

namespace Template.Integration.Tests.Reporting;

/// <summary>
/// Report adapters on real engines (D23-5, D23-6): Playwright headless Chromium of the pinned package version and
/// ClosedXML. PDFs are asserted semantically with PdfPig (pages, text, headings, tables, embedded fonts, escaped markup),
/// workbooks by reading them back (sheets, types, formats, no formulas, quote prefixes). The four catalog reports are
/// built from synthetic aggregates through the real builders and EF InMemory repositories. When
/// <c>TEMPLATE_REPORT_SAMPLES_DIR</c> is set, the rendered samples are written there for visual review.
/// </summary>
[Collection(ChromiumCollection.Name)]
public sealed class ReportRenderingAdapterTests(ChromiumFixture chromium)
{
    [Fact]
    public async Task T_RPT_008_a_Pdf_EscapesUserMarkup_EmbedsTheTemplateFonts_AndPaginatesTables()
    {
        var rows = Enumerable.Range(1, 120).Select(index => (IReadOnlyList<string>)
            [index.ToString(), index == 7 ? "<script>alert('x')</script><img src=http://example.invalid/x.png>" : $"Ação nº {index} — orçamento", $"{index * 10},00 BRL"]).ToArray();
        var document = new ReportDocument("Relatório de teste", "SUB-<b>001</b>", "pt-BR",
            [new ReportField("Gerado em", "01/03/2027 10:00:00 (America/Sao_Paulo)"), new ReportField("Filtros", "x = <y>")],
            [
                new ReportSection("Seção & detalhes", Fields: [new ReportField("Finalidade", "Compra \"urgente\" <i>ok</i>")]),
                new ReportSection("Itens", Table: new ReportTable([new("#", ReportColumnAlignment.End), new("Descrição"), new("Total", ReportColumnAlignment.End)], rows,
                    ["", "Total", "72.600,00 BRL"]), Notes: ["Nota com <tag>"]),
                new ReportSection("Vazia", EmptyMessage: "Nada a exibir.")
            ],
            "test-pdf.v1", "Rodapé <seguro>");

        var bytes = await RenderAsync(document);
        Save("adapter-escaping.pdf", bytes);

        using var pdf = PdfDocument.Open(bytes);
        pdf.NumberOfPages.ShouldBeGreaterThan(1);
        var text = string.Join("\n", pdf.GetPages().Select(page => page.Text));
        text.ShouldContain("Relatório de teste");
        text.ShouldContain("SUB-<b>001</b>");
        text.ShouldContain("<script>alert('x')</script>"); // shown as text, never executed or fetched
        text.ShouldContain("Compra \"urgente\" <i>ok</i>");
        text.ShouldContain("Ação nº 120 — orçamento");
        text.ShouldContain("72.600,00 BRL");
        text.ShouldContain("Nada a exibir.");
        text.ShouldContain("Rodapé <seguro>");
        text.ShouldContain($"Página 1 de {pdf.NumberOfPages}");
        pdf.GetPages().Skip(1).First().Text.ShouldContain("Descrição"); // header row repeats on the next page
        var fonts = pdf.GetPages().SelectMany(page => page.Letters).Select(letter => letter.FontName).Distinct().ToArray();
        fonts.ShouldNotBeEmpty();
        fonts.ShouldAllBe(font => font!.Contains("Roboto"));
        pdf.Information.Title.ShouldBe("Relatório de teste — SUB-<b>001</b>");
        System.Text.Encoding.Latin1.GetString(bytes).ShouldContain("/StructTreeRoot"); // tagged PDF
    }

    [Fact]
    public void T_RPT_008_b_TemplateEncodesEveryValue_AndBlocksScriptsAndNetwork()
    {
        var html = ReportHtmlTemplate.Render(new ReportDocument("<t>", "\"s\"", "pt-BR\"><script>", [new ReportField("<k>", "&v")],
            [new ReportSection("<h>", Table: new ReportTable([new("<c>")], [["<script>x</script>"]]))], "v", "f"));
        html.ShouldNotContain("<script>");
        html.ShouldContain("&lt;script&gt;x&lt;/script&gt;");
        html.ShouldContain("&amp;v");
        html.ShouldContain("default-src 'none'");
        html.ShouldNotContain("http://");
        html.ShouldNotContain("https://");
        ReportHtmlTemplate.Footer(new ReportDocument("t", "s", "pt-BR", [], [], "v", "<img src=x onerror=alert(1)>")).ShouldNotContain("<img");
    }

    [Fact]
    public async Task T_RPT_005_l_BrowserCrash_Timeout_AndLaunchFailure_AreTransient_AndTheRendererRecovers()
    {
        var renderer = chromium.Renderer;
        var document = SmallDocument();
        (await RenderAsync(document)).Length.ShouldBeGreaterThan(0);

        // Crash: the browser process disappears between renders; the next render launches a new browser.
        var browser = renderer.Browser.ShouldNotBeNull();
        await browser.CloseAsync();
        browser.IsConnected.ShouldBeFalse();
        (await RenderAsync(document)).Length.ShouldBeGreaterThan(0);
        renderer.Browser!.IsConnected.ShouldBeTrue();

        // Crash during a render.
        var crashing = renderer.Browser!;
        var pending = RenderAsync(LargeDocument(4000));
        await Task.Delay(50);
        await crashing.CloseAsync();
        try
        {
            await pending;
        }
        catch (ReportRendererUnavailableException)
        {
            // Expected when the close wins the race; a fast render may also complete first.
        }

        (await RenderAsync(document)).Length.ShouldBeGreaterThan(0);

        await using var slow = new PlaywrightPdfRenderer(Options(new ReportRenderingSettings { RenderTimeout = TimeSpan.FromMilliseconds(1) }), NullLogger<PlaywrightPdfRenderer>.Instance);
        await Should.ThrowAsync<ReportRendererUnavailableException>(() => slow.RenderAsync(LargeDocument(500), new MemoryStream(), CancellationToken.None));

        await using var missing = new PlaywrightPdfRenderer(Options(new ReportRenderingSettings
        {
            ExecutablePath = Path.Combine(Path.GetTempPath(), "missing-browser", "chrome.exe"),
            LaunchTimeout = TimeSpan.FromSeconds(5)
        }), NullLogger<PlaywrightPdfRenderer>.Instance);
        await Should.ThrowAsync<ReportRendererUnavailableException>(() => missing.RenderAsync(document, new MemoryStream(), CancellationToken.None));
    }

    [Fact]
    public async Task T_RPT_005_m_CancellationPropagates_WithoutBreakingTheRenderer()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => chromium.Renderer.RenderAsync(SmallDocument(), new MemoryStream(), cancelled.Token));
        (await RenderAsync(SmallDocument())).Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task T_RPT_007_a_Workbook_NeutralizesFormulaInjection_AndKeepsTypedCells()
    {
        string[] payloads = ["=1+1", "+SUM(A1:A2)", "-2+3", "@cmd", "\tTAB", "\rCR", "=HYPERLINK(\"http://example.invalid\")", "Texto normal", "a\u0000b\u0007c"];
        var instant = new DateTime(2027, 3, 1, 10, 0, 0);
        var workbook = new WorkbookDocument("Teste <x>", "pt-BR", "test-xlsx.v1",
        [
            new WorkbookSheet("Dados", [new ReportField("Gerado em", "01/03/2027 10:00:00"), new ReportField("=Filtro", "-x")],
                [new("Texto"), new("Inteiro"), new("Valor"), new("Taxa"), new("Percentual"), new("Data"), new("Data/hora"), new("Decimal")],
                payloads.Select((payload, index) => (IReadOnlyList<WorkbookCell>)
                [
                    WorkbookCell.UserText(payload), WorkbookCell.Integer(index), WorkbookCell.Money(1234.56m + index), WorkbookCell.Rate(5.4321m),
                    WorkbookCell.Percent(0.125m), WorkbookCell.DateValue(new DateOnly(2027, 3, 1)), WorkbookCell.LocalDateTime(instant), WorkbookCell.Decimal(2.5m)
                ]).ToArray(),
                [WorkbookCell.Label("Total"), WorkbookCell.Integer(36), WorkbookCell.Money(11_152.04m), WorkbookCell.Empty, WorkbookCell.Empty, WorkbookCell.Empty, WorkbookCell.Empty, WorkbookCell.Empty])
        ]);
        var bytes = await WriteAsync(workbook);
        Save("adapter-injection.xlsx", bytes);

        using var read = new XLWorkbook(new MemoryStream(bytes));
        read.Properties.Title.ShouldBe("Teste <x>");
        var sheet = read.Worksheet("Dados");
        var headerRow = 5; // title, 2 context rows, blank, header
        sheet.Cell(headerRow, 1).GetString().ShouldBe("Texto");
        sheet.Cell(headerRow, 1).Style.Font.Bold.ShouldBeTrue();
        sheet.SheetView.SplitRow.ShouldBe(headerRow);
        sheet.AutoFilter.IsEnabled.ShouldBeTrue();
        sheet.CellsUsed().ShouldAllBe(cell => !cell.HasFormula);
        sheet.Cell(3, 1).Style.IncludeQuotePrefix.ShouldBeTrue(); // context label "=Filtro"
        for (var index = 0; index < payloads.Length; index++)
        {
            var cell = sheet.Cell(headerRow + 1 + index, 1);
            cell.DataType.ShouldBe(XLDataType.Text);
            cell.Style.IncludeQuotePrefix.ShouldBe(ClosedXmlSpreadsheetWriter.NeedsQuotePrefix(payloads[index]));
        }

        sheet.Cell(headerRow + 1, 1).GetString().ShouldBe("=1+1");
        sheet.Cell(headerRow + 9, 1).GetString().ShouldBe("abc");
        var first = headerRow + 1;
        sheet.Cell(first, 2).DataType.ShouldBe(XLDataType.Number);
        sheet.Cell(first, 3).GetValue<decimal>().ShouldBe(1234.56m);
        sheet.Cell(first, 3).Style.NumberFormat.Format.ShouldBe(ClosedXmlSpreadsheetWriter.MoneyFormat);
        sheet.Cell(first, 4).Style.NumberFormat.Format.ShouldBe(ClosedXmlSpreadsheetWriter.RateFormat);
        sheet.Cell(first, 5).Style.NumberFormat.Format.ShouldBe(ClosedXmlSpreadsheetWriter.PercentFormat);
        sheet.Cell(first, 6).DataType.ShouldBe(XLDataType.DateTime);
        sheet.Cell(first, 6).GetDateTime().ShouldBe(new DateTime(2027, 3, 1));
        sheet.Cell(first, 7).GetDateTime().ShouldBe(instant);
        sheet.Cell(first + payloads.Length, 3).GetValue<decimal>().ShouldBe(11_152.04m);
        sheet.Cell(first + payloads.Length, 3).Style.Font.Bold.ShouldBeTrue();

        using var zip = new ZipArchive(new MemoryStream(bytes));
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open());
            (await reader.ReadToEndAsync()).ShouldNotContain("<f>");
        }

        zip.Entries.ShouldNotContain(entry => entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
        ClosedXmlSpreadsheetWriter.CleanText(new string('x', 40_000)).Length.ShouldBe(ClosedXmlSpreadsheetWriter.MaxCellText);
    }

    [Fact]
    public async Task T_NFR_PERF_6_a_TenThousandRowWorkbook_IsWrittenWellUnderThirtySeconds()
    {
        var rows = Enumerable.Range(0, 10_000).Select(index => (IReadOnlyList<WorkbookCell>)
            [WorkbookCell.UserText($"Linha {index}"), WorkbookCell.Integer(index), WorkbookCell.Money(index * 1.5m), WorkbookCell.DateValue(new DateOnly(2027, 1, 1).AddDays(index % 365))]).ToArray();
        var stopwatch = Stopwatch.StartNew();
        var bytes = await WriteAsync(new WorkbookDocument("Grande", "pt-BR", "v", [new WorkbookSheet("Dados", [], [new("A"), new("B"), new("C"), new("D")], rows)]));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        using var read = new XLWorkbook(new MemoryStream(bytes));
        read.Worksheet("Dados").LastRowUsed()!.RowNumber().ShouldBe(10_000 + 3);
    }

    [Fact]
    public async Task T_RPT_004_b_TheFourCatalogReports_RenderFromRealBuildersAndRepositories()
    {
        await using var provider = Persistence();
        var requisition = ReportSampleData.SubmittedRequisition();
        var department = Department.Create(Guid.NewGuid(), "OPS", "Operações", null, null, ReportSampleData.Now.AddDays(-60)).Department!;
        var costCenter = CostCenter.Create(Guid.NewGuid(), "CC-OPS", "Operações & Suporte", department, Guid.NewGuid(), null, ReportSampleData.Now.AddDays(-60)).CostCenter!;
        var (period, allocation) = ReportSampleData.Budget(costCenter.Id);
        var rfq = ReportSampleData.ClosedRfq();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.AddRange(requisition, department, costCenter, period, allocation, rfq);
            await context.SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var services = read.ServiceProvider;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ReportSampleData.TimeZone);
        async Task<ReportContent> BuildAsync(IReportBuilder builder, ReportParameters parameters)
        {
            var job = ReportSampleData.Job(builder.Type, parameters);
            return await builder.BuildAsync(new ReportBuildContext(job, parameters, ReportSampleData.Now.AddMinutes(1), zone, ReportPolicy.V1), CancellationToken.None);
        }

        var requisitionPdf = await BuildAsync(new RequisitionPdfBuilder(services.GetRequiredService<IPurchaseRequisitionRepository>()),
            new ReportParameters(RequisitionId: requisition.Id, RequisitionVersion: requisition.Version));
        var comparisonPdf = await BuildAsync(new QuoteComparisonPdfBuilder(services.GetRequiredService<IRequestForQuotationRepository>()),
            new ReportParameters(RfqId: rfq.Id, ComparisonSequence: 1));
        var budgetXlsx = await BuildAsync(new BudgetConsumptionXlsxBuilder(services.GetRequiredService<IReportSourceReader>()),
            new ReportParameters(FiscalPeriodId: period.Id));
        var quotesXlsx = await BuildAsync(new SupplierQuotesXlsxBuilder(services.GetRequiredService<IReportSourceReader>()),
            new ReportParameters(From: new DateOnly(2027, 2, 1), To: new DateOnly(2027, 3, 1)));

        var stopwatch = Stopwatch.StartNew();
        var requisitionBytes = await RenderAsync(requisitionPdf.Pdf!);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15)); // NFR-PERF-6
        var comparisonBytes = await RenderAsync(comparisonPdf.Pdf!);
        var budgetBytes = await WriteAsync(budgetXlsx.Workbook!);
        var quotesBytes = await WriteAsync(quotesXlsx.Workbook!);
        Save(requisitionPdf.FileName, requisitionBytes);
        Save(comparisonPdf.FileName, comparisonBytes);
        Save(budgetXlsx.FileName, budgetBytes);
        Save(quotesXlsx.FileName, quotesBytes);

        requisitionPdf.FileName.ShouldBe("requisicao-REQ-2027-000042.pdf");
        using (var pdf = PdfDocument.Open(requisitionBytes))
        {
            var text = string.Join("\n", pdf.GetPages().Select(page => page.Text));
            foreach (var expected in new[] { "Requisição de compra", "REQ-2027-000042", "Posição em (as-of)", "01/03/2027 10:00:00", "America/Sao_Paulo",
                         "Revisão 1 submetida", "RequisitionPolicy v1", "requisition-pdf.v1", "<script>alert(1)</script>", "=HYPERLINK", "Operações & Suporte",
                         "orçamento _fornecedor_ & cia.pdf", "Total estimado", "7.421,00 BRL" })
                text.ShouldContain(expected);
        }

        using (var pdf = PdfDocument.Open(comparisonBytes))
        {
            var text = string.Join("\n", pdf.GetPages().Select(page => page.Text));
            foreach (var expected in new[] { "Mapa comparativo de cotações", "RFQ-2027-000007", "16.200,00 BRL", "5,432100", "BCB PTAX", "SourcingPolicy v1",
                         "Moeda base", "CALC-004" })
                text.ShouldContain(expected);
            // Supplier names may wrap inside their table cell: every word must be present.
            var words = pdf.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text).ToHashSet(StringComparer.Ordinal);
            foreach (var word in "Fornecedor Ágil Ltda @Comercial =Sul S.A. Global Parts Inc.".Split(' '))
                words.ShouldContain(word);
        }

        using (var workbook = new XLWorkbook(new MemoryStream(budgetBytes)))
        {
            workbook.Worksheets.Select(sheet => sheet.Name).ShouldBe(["Resumo", "Reservas", "Movimentos"]);
            var summary = workbook.Worksheet("Resumo");
            var header = summary.Column(1).CellsUsed().First(cell => cell.GetString() == "Centro de custo (código)").Address.RowNumber;
            summary.Cell(header + 1, 1).GetString().ShouldBe("CC-OPS");
            summary.Cell(header + 1, 4).GetValue<decimal>().ShouldBe(175_000m);
            summary.Cell(header + 1, 5).GetValue<decimal>().ShouldBe(12_345.67m);
            summary.Cell(header + 1, 7).GetValue<decimal>().ShouldBe(162_654.33m);
            workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()).ShouldAllBe(cell => !cell.HasFormula);
            var reason = workbook.Worksheet("Movimentos").CellsUsed().Single(cell => cell.GetString() == "=1+1 reforço aprovado");
            reason.Style.IncludeQuotePrefix.ShouldBeTrue();
        }

        using (var workbook = new XLWorkbook(new MemoryStream(quotesBytes)))
        {
            var quotes = workbook.Worksheet("Cotacoes");
            quotes.CellsUsed().Count(cell => cell.GetString() == "RFQ-2027-000007").ShouldBe(3);
            quotes.CellsUsed().Single(cell => cell.GetString() == "@Comercial =Sul S.A.").Style.IncludeQuotePrefix.ShouldBeTrue();
            quotes.CellsUsed().Single(cell => cell.GetString() == "+VAT @ cmd|' /C calc'!A0").Style.IncludeQuotePrefix.ShouldBeTrue();
            workbook.Worksheet("Desempenho").CellsUsed().Count(cell => cell.GetString() == "Global Parts Inc.").ShouldBe(1);
            workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()).ShouldAllBe(cell => !cell.HasFormula);
        }
    }

    private async Task<byte[]> RenderAsync(ReportDocument document)
    {
        using var output = new MemoryStream();
        await chromium.Renderer.RenderAsync(document, output, CancellationToken.None);
        return output.ToArray();
    }

    private static async Task<byte[]> WriteAsync(WorkbookDocument workbook)
    {
        using var output = new MemoryStream();
        await new ClosedXmlSpreadsheetWriter().WriteAsync(workbook, output, CancellationToken.None);
        return output.ToArray();
    }

    private static ServiceProvider Persistence()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestDatabaseName"] = Guid.NewGuid().ToString("N") })
            .Build();
        var services = new ServiceCollection();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: true);
        services.AddScoped<IAuthenticatedUserService>(_ => new AuthenticatedUserService(Guid.NewGuid().ToString(), "integration"));
        return services.BuildServiceProvider();
    }

    private static void Save(string name, byte[] bytes)
    {
        if (Environment.GetEnvironmentVariable("TEMPLATE_REPORT_SAMPLES_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
        }
    }

    private static IOptions<ReportRenderingSettings> Options(ReportRenderingSettings settings) => Microsoft.Extensions.Options.Options.Create(settings);

    private static ReportDocument SmallDocument() => new("Pequeno", "S-1", "pt-BR", [new ReportField("A", "B")], [new ReportSection("Seção", Fields: [new ReportField("C", "D")])], "v", "f");

    private static ReportDocument LargeDocument(int rows) => new("Grande", "G-1", "pt-BR", [],
        [new ReportSection("Linhas", Table: new ReportTable([new("#"), new("Texto")],
            Enumerable.Range(0, rows).Select(index => (IReadOnlyList<string>)[index.ToString(), new string('x', 60)]).ToArray()))], "v", "f");
}

/// <summary>One headless Chromium shared by the rendering tests (launch is the expensive step).</summary>
public sealed class ChromiumFixture : IAsyncDisposable
{
    public PlaywrightPdfRenderer Renderer { get; } = new(Microsoft.Extensions.Options.Options.Create(new ReportRenderingSettings()), NullLogger<PlaywrightPdfRenderer>.Instance);

    public ValueTask DisposeAsync() => Renderer.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class ChromiumCollection : ICollectionFixture<ChromiumFixture>
{
    public const string Name = "chromium";
}

#nullable enable
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Reporting;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using static Template.Application.Services.Reporting.ReportText;

namespace Template.Application.Services.Reporting;

/// <summary>
/// <c>RequisitionPdf</c> (RPT-009): the latest submitted revision as of <c>AsOfUtc</c> from its immutable submission
/// snapshot (REQ-006), the status and timeline up to that instant and the attachment metadata. A requisition never
/// submitted by then is rendered from its draft only while the aggregate version pinned at request is unchanged;
/// otherwise the job fails with <see cref="ReportFailureCodes.SourceChanged"/> (RPT-003).
/// </summary>
public sealed class RequisitionPdfBuilder(IPurchaseRequisitionRepository requisitions) : IReportBuilder
{
    public ReportType Type => ReportType.RequisitionPdf;

    public async Task<ReportContent> BuildAsync(ReportBuildContext context, CancellationToken cancellationToken)
    {
        var requisition = await requisitions.FindAsync(context.Parameters.RequisitionId!.Value, cancellationToken)
            ?? throw new ReportSourceException(ReportFailureCodes.SourceNotFound, "The requisition no longer exists.");
        var asOf = context.AsOfUtc;
        var zone = context.TimeZone;
        var changes = requisition.StateChanges.Where(change => change.AtUtc <= asOf).OrderBy(change => change.Sequence).ToArray();
        if (changes.Length == 0)
            throw new ReportSourceException(ReportFailureCodes.SourceNotFound, "The requisition did not exist at the as-of instant.");
        var status = changes[^1].To.ToString();
        var submission = requisition.Submissions.Where(candidate => candidate.SubmittedAtUtc <= asOf).MaxBy(candidate => candidate.Revision);

        IReadOnlyList<ReportField> header;
        ReportTable lines;
        string currency;
        string revisionLabel;
        if (submission is not null)
        {
            currency = submission.Currency;
            revisionLabel = $"Revisão {submission.Revision} submetida (snapshot imutável)";
            header =
            [
                new("Status na posição", Status(status)),
                new("Centro de custo", $"{submission.CostCenterCode} — {submission.CostCenterName}"),
                new("Necessário até", Date(submission.NeededBy)),
                new("Submetida em", Instant(submission.SubmittedAtUtc, zone)),
                new("Política de requisição", $"RequisitionPolicy v{submission.PolicyVersion}"),
                new("Finalidade", submission.Purpose)
            ];
            lines = new ReportTable(LineColumns,
                submission.Lines.OrderBy(line => line.LineNumber).Select(line => (IReadOnlyList<string>)
                [
                    line.LineNumber.ToString(Culture), line.Description, line.Kind == RequisitionLineKind.Good ? "Bem" : "Serviço",
                    line.CategoryCode, Decimal(line.Quantity), line.UnitCode, Money(line.EstimatedUnitPrice, submission.Currency),
                    Money(line.LineTotal, submission.Currency)
                ]).ToArray(),
                ["", "Total estimado", "", "", "", "", "", Money(submission.EstimatedTotal, submission.Currency)]).Validate();
        }
        else
        {
            if (requisition.Version != context.Parameters.RequisitionVersion)
                throw new ReportSourceException(ReportFailureCodes.SourceChanged, "The draft changed after the report was requested.");
            currency = requisition.Currency;
            revisionLabel = $"Rascunho, revisão {requisition.Revision} (versão {requisition.Version})";
            header =
            [
                new("Status na posição", Status(status)),
                new("Centro de custo", $"{requisition.CostCenterCode} — {requisition.CostCenterName}"),
                new("Necessário até", requisition.NeededBy is { } neededBy ? Date(neededBy) : "—"),
                new("Submetida em", "Não submetida"),
                new("Finalidade", requisition.Purpose ?? "—")
            ];
            lines = new ReportTable(LineColumns,
                requisition.Lines.OrderBy(line => line.LineNumber).Select(line => (IReadOnlyList<string>)
                [
                    line.LineNumber.ToString(Culture), line.Description, line.Kind == RequisitionLineKind.Good ? "Bem" : "Serviço",
                    line.CategoryCode, Decimal(line.Quantity), line.UnitCode, Money(line.EstimatedUnitPrice, requisition.Currency),
                    Money(line.LineTotal, requisition.Currency)
                ]).ToArray(),
                ["", "Total estimado", "", "", "", "", "", Money(requisition.EstimatedTotal, requisition.Currency)]).Validate();
        }

        var attachments = requisition.Attachments.Where(attachment => attachment.AddedAtUtc <= asOf).OrderBy(attachment => attachment.AddedAtUtc).ToArray();
        var document = new ReportDocument(
            "Requisição de compra",
            requisition.Number,
            Language,
            Context(context,
                new ReportField("Requisição", requisition.Number),
                new ReportField("Conteúdo", revisionLabel),
                new ReportField("Moeda", currency),
                new ReportField("Filtros", $"requisitionId = {requisition.Id:D}")),
            [
                new ReportSection("Dados da requisição", Fields: header),
                new ReportSection("Itens", Table: lines, EmptyMessage: "Nenhum item.",
                    Notes: ["Totais calculados pelo servidor: arredondamento de quantidade × preço unitário por linha (CALC-002)."]),
                new ReportSection("Anexos (metadados)",
                    Table: attachments.Length == 0 ? null : new ReportTable(
                        [new("Arquivo"), new("Tipo"), new("Tamanho (bytes)", ReportColumnAlignment.End), new("Incluído em", ReportColumnAlignment.Center)],
                        attachments.Select(attachment => (IReadOnlyList<string>)
                        [
                            attachment.FileName, attachment.MediaType, Integer(attachment.Length), Instant(attachment.AddedAtUtc, zone)
                        ]).ToArray()).Validate(),
                    EmptyMessage: "Nenhum anexo."),
                new ReportSection("Histórico até a posição",
                    Table: new ReportTable(
                        [new("#", ReportColumnAlignment.End), new("De"), new("Para"), new("Revisão", ReportColumnAlignment.End), new("Data/hora", ReportColumnAlignment.Center), new("Motivo")],
                        changes.Select(change => (IReadOnlyList<string>)
                        [
                            change.Sequence.ToString(Culture), change.From is { } from ? Status(from.ToString()) : "—", Status(change.To.ToString()),
                            change.Revision.ToString(Culture), Instant(change.AtUtc, zone), change.Reason ?? change.ReasonCode ?? "—"
                        ]).ToArray()).Validate())
            ],
            context.Job.TemplateVersion,
            $"{requisition.Number} · posição {Instant(asOf, zone)}");
        return new ReportContent(FileName(Type, "requisicao", requisition.Number), Pdf: document);
    }

    private static readonly IReadOnlyList<ReportColumn> LineColumns =
    [
        new("#", ReportColumnAlignment.End), new("Descrição"), new("Tipo"), new("Categoria"), new("Quantidade", ReportColumnAlignment.End),
        new("Unidade"), new("Preço unitário estimado", ReportColumnAlignment.End), new("Total da linha", ReportColumnAlignment.End)
    ];
}

/// <summary>
/// <c>QuoteComparisonPdf</c> (RPT-009): the immutable comparison pinned at request (CALC-004/005), its persisted exchange
/// rates, the RFQ line snapshot, the invited-supplier snapshots, waivers and awards decided up to <c>AsOfUtc</c>. No
/// value is recomputed with today's supplier status or exchange rate (RPT-003).
/// </summary>
public sealed class QuoteComparisonPdfBuilder(IRequestForQuotationRepository rfqs) : IReportBuilder
{
    public ReportType Type => ReportType.QuoteComparisonPdf;

    public async Task<ReportContent> BuildAsync(ReportBuildContext context, CancellationToken cancellationToken)
    {
        var rfq = await rfqs.FindAsync(context.Parameters.RfqId!.Value, cancellationToken)
            ?? throw new ReportSourceException(ReportFailureCodes.SourceNotFound, "The request for quotation no longer exists.");
        var comparison = rfq.Comparisons.SingleOrDefault(candidate => candidate.Sequence == context.Parameters.ComparisonSequence)
            ?? throw new ReportSourceException(ReportFailureCodes.SourceNotFound, "The pinned comparison does not exist.");
        var asOf = context.AsOfUtc;
        var zone = context.TimeZone;
        var baseCurrency = comparison.BaseCurrency;
        var recommended = comparison.Entries.FirstOrDefault(entry => entry.QuoteRevisionId == comparison.RecommendedQuoteRevisionId);

        var ranking = comparison.Entries
            .OrderBy(entry => entry.Rank ?? int.MaxValue).ThenBy(entry => entry.SupplierLegalName, StringComparer.Ordinal).ThenBy(entry => entry.SupplierId)
            .Select(entry => (IReadOnlyList<string>)
            [
                entry.Rank?.ToString(Culture) ?? "—", entry.SupplierLegalName, entry.RevisionNumber.ToString(Culture), entry.Currency,
                Money(entry.RawTotal, entry.Currency), entry.Rate is { } rate ? Rate(rate) : entry.Currency == baseCurrency ? "1" : "—",
                entry.NormalizedTotal is { } normalized ? Money(normalized, baseCurrency) : "sem taxa",
                entry.DeliveryDays.ToString(Culture), Instant(entry.ReceivedAtUtc, zone), Date(entry.ValidUntil),
                $"{entry.CoveredLines}/{entry.TotalLines}", entry.IsValid ? "Sim" : $"Não ({string.Join(", ", entry.InvalidReasonList)})"
            ]).ToArray();

        var rates = rfq.ExchangeRates
            .Where(rate => rate.Purpose == ExchangeRatePurpose.Comparison && rate.RetrievedAtUtc <= comparison.ComputedAtUtc)
            .OrderBy(rate => rate.FromCurrency, StringComparer.Ordinal).ThenBy(rate => rate.RetrievedAtUtc)
            .Select(rate => (IReadOnlyList<string>)
            [
                $"{rate.FromCurrency} → {rate.ToCurrency}", Rate(rate.Rate), rate.Source.ToString(), rate.Provider, Date(rate.QuoteDate),
                Instant(rate.RetrievedAtUtc, zone), rate.Reason ?? "—"
            ]).ToArray();

        var waivers = rfq.Waivers.Where(waiver => waiver.DecidedAtUtc <= asOf).OrderBy(waiver => waiver.DecidedAtUtc)
            .Select(waiver => (IReadOnlyList<string>)
            [
                waiver.Type.ToString(), waiver.RuleId, waiver.Permission, Instant(waiver.DecidedAtUtc, zone), waiver.Reason
            ]).ToArray();

        var awards = rfq.Awards.Where(award => award.ComparisonId == comparison.Id && award.SubmittedAtUtc <= asOf).OrderBy(award => award.SubmittedAtUtc)
            .Select(award => (IReadOnlyList<string>)
            [
                award.SupplierLegalName, award.Rank.ToString(Culture), Money(award.AwardedTotal, award.Currency), Money(award.NormalizedTotal, award.BaseCurrency),
                Money(award.RequisitionCurrencyTotal, award.RequisitionCurrency), Instant(award.SubmittedAtUtc, zone),
                Status(award.EndedAtUtc is { } ended && ended <= asOf ? award.Status.ToString() : AwardStatus.PendingApproval.ToString())
            ]).ToArray();

        var lines = rfq.Lines.OrderBy(line => line.LineNumber).Select(line => (IReadOnlyList<string>)
        [
            line.LineNumber.ToString(Culture), line.Description, line.CategoryCode, Decimal(line.Quantity), line.UnitCode
        ]).ToArray();

        var document = new ReportDocument(
            "Mapa comparativo de cotações",
            $"{rfq.Number} · rodada {rfq.RoundNumber}",
            Language,
            Context(context,
                new ReportField("Cotação", rfq.Number),
                new ReportField("Comparação", $"nº {comparison.Sequence} · {Status(comparison.Status.ToString())} · calculada em {Instant(comparison.ComputedAtUtc, zone)}"),
                new ReportField("Moeda base", baseCurrency),
                new ReportField("Data de referência do câmbio", Date(comparison.CloseLocalDate)),
                new ReportField("Política de cotação", $"SourcingPolicy v{rfq.PolicyVersion}"),
                new ReportField("Filtros", $"rfqId = {rfq.Id:D}; comparação = {comparison.Sequence}")),
            [
                new ReportSection("Dados da cotação", Fields:
                [
                    new("Requisição", rfq.RequisitionNumber),
                    new("Método", rfq.Method == SourcingMethod.Competitive ? "Competitiva" : "Direta"),
                    new("Prazo para propostas", rfq.DeadlineUtc is { } deadline ? Instant(deadline, zone) : "—"),
                    new("Encerrada em", rfq.ClosedAtUtc is { } closed && closed <= asOf ? Instant(closed, zone) + (rfq.ClosedEarly ? " (antecipadamente)" : "") : "—"),
                    new("Cotações válidas", $"{comparison.ValidQuoteCount} de {comparison.RequiredValidQuotes} exigidas"),
                    new("Avisos", comparison.WarningList.Count == 0 ? "Nenhum" : string.Join(", ", comparison.WarningList))
                ]),
                new ReportSection("Itens solicitados (snapshot)", Table: new ReportTable(
                    [new("#", ReportColumnAlignment.End), new("Descrição"), new("Categoria"), new("Quantidade", ReportColumnAlignment.End), new("Unidade")],
                    lines).Validate(), EmptyMessage: "Nenhum item."),
                new ReportSection("Classificação", Table: ranking.Length == 0 ? null : new ReportTable(
                    [
                        new("Posição", ReportColumnAlignment.End), new("Fornecedor"), new("Revisão", ReportColumnAlignment.End), new("Moeda"),
                        new("Total cotado", ReportColumnAlignment.End), new("Taxa", ReportColumnAlignment.End), new($"Total em {baseCurrency}", ReportColumnAlignment.End),
                        new("Prazo (dias)", ReportColumnAlignment.End), new("Recebida em", ReportColumnAlignment.Center), new("Válida até", ReportColumnAlignment.Center),
                        new("Linhas", ReportColumnAlignment.Center), new("Válida")
                    ], ranking).Validate(),
                    EmptyMessage: "Nenhuma proposta registrada.",
                    Notes:
                    [
                        "Normalização CALC-004: total × taxa persistida, arredondado a 2 casas; taxa ausente deixa a comparação incompleta (nunca estimada).",
                        "Desempate CALC-005: total normalizado, prazo de entrega, recebimento, razão social e identificador do fornecedor.",
                        recommended is null
                            ? "Sem recomendação: a comparação não está completa."
                            : $"Recomendação (consultiva, 1º lugar): {recommended.SupplierLegalName}"
                    ]),
                new ReportSection("Taxas de câmbio utilizadas", Table: rates.Length == 0 ? null : new ReportTable(
                    [new("Par"), new("Taxa", ReportColumnAlignment.End), new("Fonte"), new("Provedor"), new("Data da cotação", ReportColumnAlignment.Center),
                        new("Obtida em", ReportColumnAlignment.Center), new("Justificativa")],
                    rates).Validate(), EmptyMessage: $"Todas as propostas estão em {baseCurrency}."),
                new ReportSection("Exceções aprovadas (waivers)", Table: waivers.Length == 0 ? null : new ReportTable(
                    [new("Tipo"), new("Regra"), new("Permissão"), new("Decidido em", ReportColumnAlignment.Center), new("Justificativa")], waivers).Validate(),
                    EmptyMessage: "Nenhuma exceção."),
                new ReportSection("Seleção submetida", Table: awards.Length == 0 ? null : new ReportTable(
                    [
                        new("Fornecedor"), new("Posição", ReportColumnAlignment.End), new("Total cotado", ReportColumnAlignment.End),
                        new($"Total em {baseCurrency}", ReportColumnAlignment.End), new("Total na moeda da requisição", ReportColumnAlignment.End),
                        new("Submetida em", ReportColumnAlignment.Center), new("Situação na posição")
                    ], awards).Validate(), EmptyMessage: "Nenhuma seleção submetida até a posição.")
            ],
            context.Job.TemplateVersion,
            $"{rfq.Number} · comparação {comparison.Sequence} · posição {Instant(asOf, zone)}");
        return new ReportContent(FileName(Type, "comparacao", rfq.Number, $"c{comparison.Sequence}"), Pdf: document);
    }
}

/// <summary>
/// <c>PurchaseOrderPdf</c> (Phase 32, PUR-007): the issued order's immutable commercial snapshot (PUR-002) — supplier,
/// buyer, delivery address, lines with promised dates, freight, discount, the informational taxes-included amount and the
/// total (CALC-006). It is rendered only for an issued order; nothing is recomputed from current master data.
/// </summary>
public sealed class PurchaseOrderPdfBuilder(Interfaces.Purchasing.IPurchaseOrderRepository orders) : IReportBuilder
{
    public ReportType Type => ReportType.PurchaseOrderPdf;

    public async Task<ReportContent> BuildAsync(ReportBuildContext context, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(context.Parameters.PurchaseOrderId!.Value, cancellationToken)
            ?? throw new ReportSourceException(ReportFailureCodes.SourceNotFound, "The purchase order no longer exists.");
        if (order.Number is null || order.IssueDate is not { } issueDate)
            throw new ReportSourceException(ReportFailureCodes.SourceNotFound, "The purchase order is not issued.");
        var zone = context.TimeZone;
        var currency = order.Currency;
        var contact = string.Join(" · ", new[] { order.SupplierContactName, order.SupplierContactEmail, order.SupplierContactPhone }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var lines = new ReportTable(
            [
                new("#", ReportColumnAlignment.End), new("Descrição"), new("Tipo"), new("Quantidade", ReportColumnAlignment.End), new("Unidade"),
                new("Preço unitário", ReportColumnAlignment.End), new("Total da linha", ReportColumnAlignment.End), new("Entrega prometida", ReportColumnAlignment.Center)
            ],
            order.Lines.OrderBy(line => line.LineNumber).Select(line => (IReadOnlyList<string>)
            [
                line.LineNumber.ToString(Culture), line.Description, line.Kind == "Service" ? "Serviço" : "Bem", Decimal(line.Quantity), line.UnitCode,
                Money(line.UnitPrice, currency), Money(line.LineTotal, currency), Date(line.PromisedDeliveryDate)
            ]).ToArray(),
            ["", "Subtotal", "", "", "", "", Money(order.Subtotal, currency), ""]).Validate();
        var document = new ReportDocument(
            "Pedido de compra",
            order.Number,
            Language,
            Context(context,
                new ReportField("Pedido", order.Number),
                new ReportField("Moeda", currency),
                new ReportField("Filtros", $"purchaseOrderId = {order.Id:D}")),
            [
                new ReportSection("Fornecedor", Fields:
                [
                    new("Razão social", order.SupplierLegalName),
                    new("Nome fantasia", order.SupplierTradeName ?? "—"),
                    new("Identificação fiscal", $"{order.SupplierTaxId} ({order.SupplierCountry})"),
                    new("Endereço", order.SupplierAddress ?? "—"),
                    new("Contato", contact.Length == 0 ? "—" : contact)
                ]),
                new ReportSection("Pedido", Fields:
                [
                    new("Número", order.Number),
                    new("Emitido em", $"{Date(issueDate)} ({Instant(order.IssuedAtUtc!.Value, zone)})"),
                    new("Comprador", order.BuyerName ?? "—"),
                    new("Requisição", order.RequisitionNumber),
                    new("Cotação (RFQ)", order.RfqNumber),
                    new("Condições de pagamento", order.PaymentTerms),
                    new("Endereço de entrega", order.DeliveryAddress ?? "—"),
                    new("Observações", order.Notes ?? "—")
                ]),
                new ReportSection("Itens", Table: lines, EmptyMessage: "Nenhum item.",
                    Notes: ["Preços, quantidades e condições copiados da revisão de cotação adjudicada e aprovada (CALC-006); imutáveis após a emissão (PUR-002)."]),
                new ReportSection("Totais", Fields:
                [
                    new("Subtotal", Money(order.Subtotal, currency)),
                    new("Frete", Money(order.Freight, currency)),
                    new("Desconto", Money(order.Discount, currency)),
                    new("Total do pedido", Money(order.Total, currency)),
                    new("Impostos incluídos (informativo)", Money(order.TaxesIncluded, currency))
                ], Notes: ["Total = Σ linhas + frete − desconto (CALC-002). Impostos são o valor cotado já contido nos preços; não há cálculo tributário."])
            ],
            context.Job.TemplateVersion,
            $"{order.Number} · emitido em {Date(issueDate)}");
        return new ReportContent(FileName(Type, "pedido", order.Number), Pdf: document);
    }
}

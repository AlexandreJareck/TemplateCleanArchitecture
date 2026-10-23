#nullable enable
using Template.Application.Interfaces.Reporting;
using Template.Application.Reporting;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Reporting;
using Template.Domain.Sourcing;
using static Template.Application.Services.Reporting.ReportText;

namespace Template.Application.Services.Reporting;

/// <summary>
/// <c>BudgetConsumptionXlsx</c> (RPT-009): allocations of one fiscal period reconstructed at <c>AsOfUtc</c> from their
/// immutable amount history and reservation monetary history (RPT-003). Reserved/committed totals follow the same
/// rule as the aggregate (each reservation's last change at or before the instant), available = amount − reserved −
/// committed (CALC-003). Totals are written as values, never formulas.
/// </summary>
public sealed class BudgetConsumptionXlsxBuilder(IReportSourceReader sources) : IReportBuilder
{
    public ReportType Type => ReportType.BudgetConsumptionXlsx;

    public async Task<ReportContent> BuildAsync(ReportBuildContext context, CancellationToken cancellationToken)
    {
        var parameters = context.Parameters;
        var asOf = context.AsOfUtc;
        var zone = context.TimeZone;
        var period = await sources.FindFiscalPeriodAsync(parameters.FiscalPeriodId!.Value, cancellationToken)
            ?? throw new ReportSourceException(ReportFailureCodes.SourceNotFound, "The fiscal period no longer exists.");
        var allocations = (await sources.ListAllocationsAsync(period.Id, parameters.Currency, parameters.CostCenterId, context.Policy.MaxRows, cancellationToken))
            .Where(allocation => allocation.CreatedAtUtc <= asOf).ToArray();
        if (allocations.Length > context.Policy.MaxRows)
            throw new ReportSourceException(ReportFailureCodes.TooLarge, "Too many allocations for one report.");
        var labels = await sources.GetCostCenterLabelsAsync(allocations.Select(allocation => allocation.CostCenterId).Distinct().ToArray(), cancellationToken);

        var summary = new List<IReadOnlyList<WorkbookCell>>();
        var reservations = new List<IReadOnlyList<WorkbookCell>>();
        var movements = new List<(DateTime At, IReadOnlyList<WorkbookCell> Row)>();
        foreach (var allocation in allocations.OrderBy(allocation => Label(labels, allocation.CostCenterId).Code, StringComparer.Ordinal).ThenBy(allocation => allocation.Currency))
        {
            var (code, name) = Label(labels, allocation.CostCenterId);
            var state = AsOf(allocation, asOf);
            summary.Add(
            [
                WorkbookCell.UserText(code), WorkbookCell.UserText(name), WorkbookCell.Label(allocation.Currency), WorkbookCell.Money(state.Amount),
                WorkbookCell.Money(state.Reserved), WorkbookCell.Money(state.Committed), WorkbookCell.Money(state.Available),
                WorkbookCell.Percent(state.Amount == 0 ? 0 : decimal.Round((state.Reserved + state.Committed) / state.Amount, 4, MidpointRounding.AwayFromZero)),
                WorkbookCell.Integer(state.Reservations.Count)
            ]);

            foreach (var (reservation, last) in state.Reservations)
            {
                reservations.Add(
                [
                    WorkbookCell.UserText(code), WorkbookCell.Label(reservation.RequisitionId.ToString("D")), WorkbookCell.Integer(reservation.RequisitionRevision),
                    WorkbookCell.Label(reservation.Currency), WorkbookCell.Money(last.Amount), WorkbookCell.Label(Status(last.To.ToString())),
                    WorkbookCell.Label(last.Reason?.ToString() ?? string.Empty), WorkbookCell.LocalDateTime(Local(reservation.CreatedAtUtc, zone)),
                    WorkbookCell.LocalDateTime(Local(last.AtUtc, zone))
                ]);
                foreach (var change in reservation.Changes.Where(change => change.AtUtc <= asOf))
                    movements.Add((change.AtUtc,
                    [
                        WorkbookCell.LocalDateTime(Local(change.AtUtc, zone)), WorkbookCell.Label("Reserva"), WorkbookCell.UserText(code),
                        WorkbookCell.Label(reservation.RequisitionId.ToString("D")), WorkbookCell.Label(Status(change.Operation.ToString())),
                        WorkbookCell.Money(change.Amount), WorkbookCell.Label(change.Currency), WorkbookCell.Label(change.Reason?.ToString() ?? string.Empty)
                    ]));
            }

            foreach (var change in allocation.AmountHistory.Where(change => change.AtUtc <= asOf))
                movements.Add((change.AtUtc,
                [
                    WorkbookCell.LocalDateTime(Local(change.AtUtc, zone)), WorkbookCell.Label("Dotação"), WorkbookCell.UserText(code), WorkbookCell.Empty,
                    WorkbookCell.Label("Valor alterado"), WorkbookCell.Money(change.NewAmount), WorkbookCell.Label(allocation.Currency), WorkbookCell.UserText(change.Reason)
                ]));
        }

        if (summary.Count + reservations.Count + movements.Count > context.Policy.MaxRows)
            throw new ReportSourceException(ReportFailureCodes.TooLarge, "The report exceeds the row limit.");

        var currencies = allocations.Select(allocation => allocation.Currency).Distinct().ToArray();
        IReadOnlyList<WorkbookCell>? totals = null;
        if (currencies.Length == 1)
        {
            var states = allocations.Select(allocation => AsOf(allocation, asOf)).ToArray();
            var amount = states.Sum(state => state.Amount);
            var used = states.Sum(state => state.Reserved + state.Committed);
            totals =
            [
                WorkbookCell.Label("Total"), WorkbookCell.Empty, WorkbookCell.Label(currencies[0]), WorkbookCell.Money(amount),
                WorkbookCell.Money(states.Sum(state => state.Reserved)), WorkbookCell.Money(states.Sum(state => state.Committed)),
                WorkbookCell.Money(states.Sum(state => state.Available)),
                WorkbookCell.Percent(amount == 0 ? 0 : decimal.Round(used / amount, 4, MidpointRounding.AwayFromZero)),
                WorkbookCell.Integer(states.Sum(state => state.Reservations.Count))
            ];
        }

        var contextRows = Context(context,
            new ReportField("Período fiscal", $"{period.Code} — {period.Name} ({Date(period.StartDate)} a {Date(period.EndDate)})"),
            new ReportField("Moeda", parameters.Currency ?? (currencies.Length == 1 ? currencies[0] : "todas (sem total geral entre moedas)")),
            new ReportField("Filtros", $"fiscalPeriodId = {period.Id:D}; currency = {parameters.Currency ?? "todas"}; costCenterId = {parameters.CostCenterId?.ToString("D") ?? "todos"}"),
            new ReportField("Regras", "CALC-003 disponível = dotação − reservado − comprometido; valores reconstruídos pelo histórico imutável até a posição"),
            new ReportField("Rótulos", "Código e nome de centro de custo conforme o cadastro na geração (não alteram valores)"));

        var workbook = new WorkbookDocument("Consumo orçamentário", Language, context.Job.TemplateVersion,
        [
            new WorkbookSheet("Resumo", contextRows,
                [
                    new("Centro de custo (código)", 22), new("Centro de custo (nome)", 32), new("Moeda", 8), new("Dotação"), new("Reservado"),
                    new("Comprometido"), new("Disponível"), new("Consumo %", 12), new("Reservas ativas", 14)
                ], summary, totals).Validate(),
            new WorkbookSheet("Reservas", contextRows,
                [
                    new("Centro de custo", 22), new("Requisição (ID)", 38), new("Revisão", 10), new("Moeda", 8), new("Valor"), new("Situação", 16),
                    new("Motivo de liberação", 20), new($"Criada em ({zone.Id})", 22), new($"Última alteração ({zone.Id})", 24)
                ], reservations).Validate(),
            new WorkbookSheet("Movimentos", contextRows,
                [
                    new($"Data/hora ({zone.Id})", 22), new("Origem", 12), new("Centro de custo", 22), new("Requisição (ID)", 38), new("Operação", 16),
                    new("Valor"), new("Moeda", 8), new("Motivo", 40)
                ], movements.OrderBy(movement => movement.At).Select(movement => movement.Row).ToArray()).Validate()
        ]);
        return new ReportContent(FileName(Type, "orcamento", period.Code, Local(asOf, zone).ToString("yyyyMMdd-HHmm", Culture)), Workbook: workbook);
    }

    /// <summary>Allocation amount and reservation totals at <paramref name="asOf"/> from the immutable histories.</summary>
    public static AllocationState AsOf(BudgetAllocation allocation, DateTime asOf)
    {
        var laterChange = allocation.AmountHistory.Where(change => change.AtUtc > asOf).MinBy(change => change.Sequence);
        var amount = laterChange?.PreviousAmount ?? allocation.Amount;
        var reservations = allocation.Reservations
            .Select(reservation => (Reservation: reservation, Last: reservation.Changes.Where(change => change.AtUtc <= asOf).MaxBy(change => change.Sequence)))
            .Where(pair => pair.Last is not null)
            .Select(pair => (pair.Reservation, Last: pair.Last!))
            .OrderBy(pair => pair.Reservation.CreatedAtUtc).ThenBy(pair => pair.Reservation.Id)
            .ToArray();
        var reserved = reservations.Where(pair => pair.Last.To == BudgetReservationStatus.Reserved).Sum(pair => pair.Last.Amount);
        var committed = reservations.Where(pair => pair.Last.To == BudgetReservationStatus.Committed).Sum(pair => pair.Last.Amount);
        return new AllocationState(amount, reserved, committed, amount - reserved - committed,
            reservations.Where(pair => pair.Last.To != BudgetReservationStatus.Released).ToArray());
    }

    private static (string Code, string Name) Label(IReadOnlyDictionary<Guid, (string Code, string Name)> labels, Guid id) =>
        labels.TryGetValue(id, out var label) ? label : (id.ToString("D"), string.Empty);

    public sealed record AllocationState(
        decimal Amount, decimal Reserved, decimal Committed, decimal Available,
        IReadOnlyList<(BudgetReservation Reservation, BudgetReservationChange Last)> Reservations);
}

/// <summary>
/// <c>SupplierQuotesXlsx</c> (RPT-009): quote revisions received in a local-date window with the supplier identity
/// snapshotted at invitation, the persisted comparison outcome and the award, plus per-supplier metrics that the
/// current data supports (invitations, responses, ranking, awards, delivery estimate). Nothing is recomputed with today's
/// supplier status or exchange rate (RPT-003).
/// </summary>
public sealed class SupplierQuotesXlsxBuilder(IReportSourceReader sources) : IReportBuilder
{
    public ReportType Type => ReportType.SupplierQuotesXlsx;

    public async Task<ReportContent> BuildAsync(ReportBuildContext context, CancellationToken cancellationToken)
    {
        var parameters = context.Parameters;
        var zone = context.TimeZone;
        var asOf = context.AsOfUtc;
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(parameters.From!.Value.ToDateTime(TimeOnly.MinValue), zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(parameters.To!.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        var rfqs = await sources.ListRfqsWithQuotesAsync(fromUtc, toUtc, parameters.SupplierId, context.Policy.MaxRows, cancellationToken);
        if (rfqs.Count > context.Policy.MaxRows)
            throw new ReportSourceException(ReportFailureCodes.TooLarge, "Too many requests for quotation for one report.");

        var rows = new List<(DateTime Received, IReadOnlyList<WorkbookCell> Row)>();
        var metrics = new Dictionary<Guid, SupplierMetrics>();
        foreach (var rfq in rfqs)
        {
            var comparisons = rfq.Comparisons.Where(comparison => comparison.ComputedAtUtc <= asOf).ToArray();
            var latestComparison = comparisons.MaxBy(comparison => comparison.Sequence);
            var awards = rfq.Awards.Where(award => award.SubmittedAtUtc <= asOf).ToArray();
            foreach (var invitation in rfq.Invitations.Where(invitation => invitation.InvitedAtUtc <= asOf && (invitation.RemovedAtUtc is null || invitation.RemovedAtUtc > asOf)))
            {
                if (parameters.SupplierId is { } only && invitation.SupplierId != only)
                    continue;
                var supplier = Metrics(metrics, invitation);
                supplier.Invitations++;
                var quote = rfq.Quotes.FirstOrDefault(candidate => candidate.SupplierId == invitation.SupplierId);
                var inWindow = quote?.Revisions.Where(revision => revision.EnteredAtUtc <= asOf && revision.ReceivedAtUtc >= fromUtc && revision.ReceivedAtUtc < toUtc)
                    .OrderBy(revision => revision.RevisionNumber).ToArray() ?? [];
                if (inWindow.Length > 0)
                    supplier.Responses++;
                var entry = latestComparison?.Entries.FirstOrDefault(candidate => candidate.SupplierId == invitation.SupplierId);
                if (entry?.Rank is { } rank)
                {
                    supplier.Ranks.Add(rank);
                    if (rank == 1)
                        supplier.FirstPlaces++;
                }

                if (awards.Any(award => award.SupplierId == invitation.SupplierId))
                    supplier.Awards++;
                foreach (var revision in inWindow)
                {
                    supplier.DeliveryDays.Add(revision.DeliveryDays);
                    var revisionEntry = comparisons.OrderByDescending(comparison => comparison.Sequence).SelectMany(comparison => comparison.Entries)
                        .FirstOrDefault(candidate => candidate.QuoteRevisionId == revision.Id);
                    rows.Add((revision.ReceivedAtUtc,
                    [
                        WorkbookCell.Label(rfq.Number), WorkbookCell.Integer(rfq.RoundNumber), WorkbookCell.Label(rfq.RequisitionNumber),
                        WorkbookCell.UserText(invitation.SupplierLegalName), WorkbookCell.UserText(invitation.SupplierTaxId), WorkbookCell.Integer(revision.RevisionNumber),
                        WorkbookCell.Label(Status(RevisionStatusAt(revision, asOf))), WorkbookCell.Label(revision.Source.ToString()),
                        WorkbookCell.LocalDateTime(Local(revision.ReceivedAtUtc, zone)), WorkbookCell.Label(revision.Currency), WorkbookCell.Money(revision.Subtotal),
                        WorkbookCell.Money(revision.Freight), WorkbookCell.Money(revision.Discount), WorkbookCell.Money(revision.TaxesIncluded), WorkbookCell.Money(revision.Total),
                        WorkbookCell.DateValue(revision.ValidUntil), WorkbookCell.Integer(revision.DeliveryDays), WorkbookCell.UserText(revision.PaymentTerms),
                        revisionEntry?.NormalizedTotal is { } normalized ? WorkbookCell.Money(normalized) : WorkbookCell.Empty,
                        WorkbookCell.Label(revisionEntry is null ? string.Empty : rfq.BaseCurrency),
                        revisionEntry?.Rank is { } revisionRank ? WorkbookCell.Integer(revisionRank) : WorkbookCell.Empty,
                        WorkbookCell.Label(revisionEntry is null ? "Não comparada" : YesNo(revisionEntry.IsValid)),
                        WorkbookCell.Label(YesNo(awards.Any(award => award.QuoteRevisionId == revision.Id))),
                        WorkbookCell.UserText(revision.Notes)
                    ]));
                }
            }
        }

        if (rows.Count + metrics.Count > context.Policy.MaxRows)
            throw new ReportSourceException(ReportFailureCodes.TooLarge, "The report exceeds the row limit.");

        var performance = metrics.Values.OrderBy(metric => metric.LegalName, StringComparer.Ordinal).ThenBy(metric => metric.SupplierId)
            .Select(metric => (IReadOnlyList<WorkbookCell>)
            [
                WorkbookCell.UserText(metric.LegalName), WorkbookCell.UserText(metric.TaxId), WorkbookCell.Integer(metric.Invitations),
                WorkbookCell.Integer(metric.Responses),
                WorkbookCell.Percent(metric.Invitations == 0 ? 0 : decimal.Round((decimal)metric.Responses / metric.Invitations, 4, MidpointRounding.AwayFromZero)),
                WorkbookCell.Integer(metric.Ranks.Count),
                metric.Ranks.Count == 0 ? WorkbookCell.Empty : WorkbookCell.Decimal(decimal.Round((decimal)metric.Ranks.Average(), 2, MidpointRounding.AwayFromZero)),
                WorkbookCell.Integer(metric.FirstPlaces), WorkbookCell.Integer(metric.Awards),
                metric.DeliveryDays.Count == 0 ? WorkbookCell.Empty : WorkbookCell.Decimal(decimal.Round((decimal)metric.DeliveryDays.Average(), 1, MidpointRounding.AwayFromZero))
            ]).ToArray();

        var contextRows = Context(context,
            new ReportField("Janela de recebimento", $"{Date(parameters.From.Value)} a {Date(parameters.To.Value)} (datas locais)"),
            new ReportField("Moeda", "moeda de cada proposta; total normalizado na moeda base da comparação persistida"),
            new ReportField("Filtros", $"from = {parameters.From:yyyy-MM-dd}; to = {parameters.To:yyyy-MM-dd}; supplierId = {parameters.SupplierId?.ToString("D") ?? "todos"}"),
            new ReportField("Política de cotação", string.Join(", ", rfqs.Select(rfq => $"SourcingPolicy v{rfq.PolicyVersion}").Distinct().DefaultIfEmpty("—"))),
            new ReportField("Identificação", "Razão social e CNPJ conforme o snapshot do convite (SUP-004)"));

        var workbook = new WorkbookDocument("Cotações de fornecedores", Language, context.Job.TemplateVersion,
        [
            new WorkbookSheet("Cotacoes", contextRows,
                [
                    new("Cotação", 18), new("Rodada", 8), new("Requisição", 18), new("Fornecedor", 32), new("CNPJ / Tax ID", 20), new("Revisão", 9),
                    new("Situação na posição", 18), new("Fonte", 10), new($"Recebida em ({zone.Id})", 22), new("Moeda", 8), new("Subtotal"), new("Frete"),
                    new("Desconto"), new("Impostos incluídos"), new("Total"), new("Válida até", 12), new("Prazo (dias)", 10), new("Condições de pagamento", 30),
                    new("Total normalizado"), new("Moeda base", 10), new("Posição", 9), new("Válida na comparação", 14), new("Selecionada", 11), new("Observações", 40)
                ], rows.OrderBy(row => row.Received).Select(row => row.Row).ToArray()).Validate(),
            new WorkbookSheet("Desempenho", contextRows,
                [
                    new("Fornecedor", 32), new("CNPJ / Tax ID", 20), new("Convites", 10), new("Respostas na janela", 12), new("Taxa de resposta", 12),
                    new("Comparações classificadas", 14), new("Posição média", 12), new("1º lugar", 9), new("Seleções submetidas", 12), new("Prazo médio (dias)", 12)
                ], performance).Validate()
        ]);
        return new ReportContent(FileName(Type, "cotacoes-fornecedores", $"{parameters.From:yyyyMMdd}", $"{parameters.To:yyyyMMdd}"), Workbook: workbook);
    }

    private static string RevisionStatusAt(QuoteRevision revision, DateTime asOf) =>
        revision.Status == QuoteRevisionStatus.Withdrawn && revision.WithdrawnAtUtc > asOf ? QuoteRevisionStatus.Current.ToString() : revision.Status.ToString();

    private static SupplierMetrics Metrics(Dictionary<Guid, SupplierMetrics> metrics, RfqInvitation invitation)
    {
        if (!metrics.TryGetValue(invitation.SupplierId, out var metric))
            metrics[invitation.SupplierId] = metric = new SupplierMetrics(invitation.SupplierId);
        // The most recent invitation snapshot labels the supplier; amounts never depend on it.
        if (invitation.InvitedAtUtc >= metric.SnapshotAtUtc)
        {
            metric.LegalName = invitation.SupplierLegalName;
            metric.TaxId = invitation.SupplierTaxId;
            metric.SnapshotAtUtc = invitation.InvitedAtUtc;
        }

        return metric;
    }

    private sealed class SupplierMetrics(Guid supplierId)
    {
        public Guid SupplierId { get; } = supplierId;
        public string LegalName { get; set; } = string.Empty;
        public string TaxId { get; set; } = string.Empty;
        public DateTime SnapshotAtUtc { get; set; } = DateTime.MinValue;
        public int Invitations { get; set; }
        public int Responses { get; set; }
        public List<int> Ranks { get; } = [];
        public int FirstPlaces { get; set; }
        public int Awards { get; set; }
        public List<int> DeliveryDays { get; } = [];
    }
}

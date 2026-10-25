using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Features.Approvals;
using Template.Application.Interfaces.Approvals;
using Template.Application.Workflow;
using Xunit.Abstractions;

namespace Template.Performance.Tests;

/// <summary>
/// NFR-PERF-1/2/3 over the real WebApi pipeline (middleware, authorization, EF Core/Npgsql, serialization) against the
/// seeded NFR dataset on PostgreSQL 16.15. Server time is measured in-process (no network), p95 over the samples after
/// a warm-up; the run's numbers are written to artifacts/validation/performance/&lt;RunId&gt;/results.md.
/// </summary>
[Collection(PerformanceCollection.Name)]
public sealed class ApiBudgetTests(PerformanceFixture fixture, ITestOutputHelper output)
{
    private const int Warmup = 5;
    private const int Samples = 60;

    [Fact]
    public async Task NFR_PERF_1_ListEndpoints_P95_WithinThreeHundredMilliseconds()
    {
        Describe();
        var auditor = await fixture.SignInAsync("demo.auditor");
        var data = fixture.Dataset;
        var lists = new (string Name, Func<int, string> Url)[]
        {
            ("requisitions (visible to an auditor, page ≤ 50)", page => $"/api/v2/purchase-requisitions?scope=visible&pageSize=50&page={1 + page % 40}"),
            ("requisitions by status", page => $"/api/v2/purchase-requisitions?scope=visible&pageSize=50&page={1 + page % 20}&status={(page % 2 == 0 ? "Submitted" : "Draft")}"),
            ("requisitions by cost center", page => $"/api/v2/purchase-requisitions?scope=visible&pageSize=50&costCenterId={data.CostCenterIds[page % data.CostCenterIds.Count]}"),
            ("requests for quotation", page => $"/api/v2/requests-for-quotation?pageSize=50&page={1 + page % 20}"),
            ("purchase orders", page => $"/api/v2/purchase-orders?pageSize=50&page={1 + page % 20}"),
            ("receipts", page => $"/api/v2/receipts?pageSize=50&page={1 + page % 20}"),
            ("suppliers (search)", page => $"/api/v2/suppliers?pageSize=50&q=Sint%C3%A9tico%20{page % 10}"),
            ("catalog items (search)", page => $"/api/v2/catalog-items?pageSize=50&q=PITEM0{page % 50:D2}"),
            ("budget allocations", page => $"/api/v2/budget-allocations?pageSize=50&page={1 + page % 2}")
        };

        var failures = new List<string>();
        foreach (var (name, url) in lists)
        {
            var measurement = await PerformanceEvidence.MeasureAsync("NFR-PERF-1", name, 300, Warmup, Samples, index => GetOkAsync(auditor, url(index)));
            Report(measurement, failures);
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task NFR_PERF_2_DetailReads_P95_WithinTwoHundredMilliseconds()
    {
        Describe();
        var auditor = await fixture.SignInAsync("demo.auditor");
        var data = fixture.Dataset;
        var details = new (string Name, Func<int, string> Url)[]
        {
            ("requisition", index => $"/api/v2/purchase-requisitions/{Pick(data.RequisitionIds, index)}"),
            ("request for quotation", index => $"/api/v2/requests-for-quotation/{Pick(data.RfqIds, index)}"),
            ("purchase order", index => $"/api/v2/purchase-orders/{Pick(data.PurchaseOrderIds, index)}"),
            ("receipt", index => $"/api/v2/receipts/{Pick(data.ReceiptIds, index)}"),
            ("supplier", index => $"/api/v2/suppliers/{Pick(data.SupplierIds, index)}")
        };

        var failures = new List<string>();
        foreach (var (name, url) in details)
        {
            var measurement = await PerformanceEvidence.MeasureAsync("NFR-PERF-2", name, 200, Warmup, Samples, index => GetOkAsync(auditor, url(index)));
            Report(measurement, failures);
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task NFR_PERF_3_Commands_P95_WithinFiveHundredMilliseconds()
    {
        Describe();
        var data = fixture.Dataset;
        var requester = await fixture.SignInAsync("demo.requester");
        var buyer = await fixture.SignInAsync("demo.buyer");
        var manager = await fixture.SignInAsync("demo.manager");
        var receiver = await fixture.SignInAsync("demo.receiver");
        var failures = new List<string>();
        const int warmup = 2;
        var samples = data.SubmitFixtures.Count - warmup;

        // Each command acts on its own prepared record; reading the ETag is outside the timed section.
        var submit = await TimedCommandsAsync("submit requisition", data.SubmitFixtures, warmup, async id =>
        {
            var etag = await ETagAsync(requester, $"/api/v2/purchase-requisitions/{id}");
            return () => SendAsync(requester, HttpMethod.Post, $"/api/v2/purchase-requisitions/{id}/submit", null, etag);
        });
        Report(submit, failures);

        var awards = new List<(Guid AwardId, Guid RequisitionId)>();
        var award = await TimedCommandsAsync("submit award", data.AwardFixtures, warmup, async fixtureItem =>
        {
            var etag = await ETagAsync(buyer, $"/api/v2/requests-for-quotation/{fixtureItem.RfqId}");
            return async () =>
            {
                var body = await SendAsync(buyer, HttpMethod.Post, $"/api/v2/requests-for-quotation/{fixtureItem.RfqId}/award",
                    new { quoteRevisionId = fixtureItem.RevisionId, justification = "Menor preço" }, etag);
                var awardId = body.GetProperty("awards").EnumerateArray().Last().GetProperty("id").GetGuid();
                lock (awards)
                    awards.Add((awardId, fixtureItem.RequisitionId));
                return body;
            };
        });
        Report(award, failures);

        // What the Worker and the orchestration do before a decision: start the instance and assign level 1.
        var instances = new List<Guid>();
        foreach (var (awardId, requisitionId) in awards)
        {
            var input = new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, awardId, requisitionId, DateTime.UtcNow, "nfr-performance");
            await using var scope = fixture.Api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IApprovalWorkflowClient>().StartAsync(input, default);
            var activities = scope.ServiceProvider.GetRequiredService<ApprovalWorkflowActivities>();
            var started = await activities.StartAsync(new ApprovalStartRequest(input, ApprovalWorkflowNames.CurrentVersion), default);
            await activities.AssignStepAsync(new ApprovalActivityRequest(awardId, input.CorrelationId), default);
            instances.Add(started.InstanceId!.Value);
        }

        var decide = await TimedCommandsAsync("approval decision (level 1)", instances, warmup, async id =>
        {
            var etag = await ETagAsync(manager, $"/api/v2/approval-instances/{id}");
            return () => SendAsync(manager, HttpMethod.Post, $"/api/v2/approval-instances/{id}/steps/1/decision", new { decision = "Approved" }, etag);
        });
        Report(decide, failures);

        var issue = await TimedCommandsAsync("issue purchase order", data.IssueFixtures, warmup, async id =>
        {
            var etag = await ETagAsync(buyer, $"/api/v2/purchase-orders/{id}");
            return () => SendAsync(buyer, HttpMethod.Post, $"/api/v2/purchase-orders/{id}/issue", null, etag);
        });
        Report(issue, failures);

        var receive = await TimedCommandsAsync("post receipt", data.ReceiveFixtures, warmup, async id =>
        {
            using var order = JsonDocument.Parse(await GetOkAsync(receiver, $"/api/v2/purchase-orders/{id}"));
            var lines = order.RootElement.GetProperty("lines").EnumerateArray().Select(line => new
            {
                poLineNumber = line.GetProperty("lineNumber").GetInt32(),
                received = line.GetProperty("quantity").GetString(),
                accepted = line.GetProperty("quantity").GetString(),
                rejected = "0"
            }).ToArray();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")));
            return () => SendAsync(receiver, HttpMethod.Post, "/api/v2/receipts",
                new { purchaseOrderId = id, receivedOn = today, location = "Doca", deliveryReference = "NF-PERF", lines, attachments = Array.Empty<object>() }, null);
        });
        Report(receive, failures);

        samples.ShouldBeGreaterThan(20);
        failures.ShouldBeEmpty();
    }

    private async Task<Measurement> TimedCommandsAsync<T>(string name, IReadOnlyList<T> items, int warmup, Func<T, Task<Func<Task<JsonElement>>>> prepare)
    {
        var values = new List<double>();
        for (var index = 0; index < items.Count; index++)
        {
            var send = await prepare(items[index]);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await send();
            clock.Stop();
            if (index >= warmup)
                values.Add(clock.Elapsed.TotalMilliseconds);
        }

        return PerformanceEvidence.Record(new Measurement("NFR-PERF-3", name, values, 500));
    }

    private static async Task<string> GetOkAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return body;
    }

    private static async Task<string> ETagAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response.Headers.ETag!.Tag;
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string url, object? body, string? ifMatch)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBeInRange(200, 299, $"{method} {url}: {text}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Guid Pick(IReadOnlyList<Guid> ids, int index) => ids[(int)((index * 7919L) % ids.Count)];

    private void Report(Measurement measurement, List<string> failures)
    {
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{measurement.Budget} {measurement.Operation}: n={measurement.SamplesMs.Count} p50={measurement.P50:F1} p95={measurement.P95:F1} max={measurement.Max:F1} (limit {measurement.LimitMs:F0} ms)"));
        if (!measurement.Passed)
            failures.Add($"{measurement.Operation}: p95 {measurement.P95:F1} ms > {measurement.LimitMs:F0} ms");
    }

    private void Describe()
    {
        var data = fixture.Dataset;
        var counts = string.Join(", ", data.Counts.Select(pair => $"{pair.Key}={pair.Value}"));
        output.WriteLine($"Dataset seeded in {data.Duration.TotalSeconds:F0} s: {counts}");
        PerformanceEvidence.Note($"Dataset (seeded in {data.Duration.TotalSeconds:F0} s): {counts}");
    }
}

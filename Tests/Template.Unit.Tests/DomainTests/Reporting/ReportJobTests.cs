using Shouldly;
using Template.Application.Features.Reporting;
using Template.Domain.Common;
using Template.Domain.Files;
using Template.Domain.Reporting;

namespace Template.Unit.Tests.DomainTests.Reporting;

/// <summary>Report job lifecycle (workflows §6, RPT-001/003/005/006) and stored-file rules (FIL-001/002/005) without mocks.</summary>
public sealed class ReportJobTests
{
    private static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly ReportPolicy Policy = ReportPolicy.V1;

    [Fact]
    public void T_RPT_001_a_Request_PinsSnapshotTemplateAndPolicy_AndRaisesRequested()
    {
        var job = NewJob();

        job.Status.ShouldBe(ReportJobStatus.Pending);
        job.TemplateVersion.ShouldBe("requisition-pdf.v1");
        job.PolicyVersion.ShouldBe(1);
        job.AsOfUtc.ShouldBe(Now);
        job.IsActive.ShouldBeTrue();
        job.Format.ShouldBe(ReportFormat.Pdf);
        job.DomainEvents.Single().ShouldBeOfType<ReportRequested>().EventType.ShouldBe("procurement.report.requested");
    }

    [Fact]
    public void T_RPT_001_b_Request_RejectsInvalidSnapshots_AndFutureAsOf()
    {
        Request(parameters: "").Error!.Field.ShouldBe("parameters");
        Request(parameters: new string('x', ReportJob.ParametersMaxLength + 1)).Error!.Field.ShouldBe("parameters");
        Request(hash: "short").Error!.Field.ShouldBe("parameters");
        Request(timeZone: " ").Error!.Field.ShouldBe("timeZone");
        Request(asOf: Now.AddMinutes(1)).Error!.Field.ShouldBe("asOf");
        Request(type: (ReportType)99).Error!.Code.ShouldBe(ReportErrors.TypeUnknownCode);
        Should.Throw<ArgumentException>(() => ReportJob.Request(Guid.Empty, ReportType.RequisitionPdf, Requester, "{}", Hash, Now, "UTC", Policy, Now));
        Should.Throw<ArgumentException>(() => ReportJob.Request(Guid.NewGuid(), ReportType.RequisitionPdf, Requester, "{}", Hash, Now, "UTC", Policy, DateTime.Now));
    }

    [Fact]
    public void T_RPT_005_a_TransientFailures_RetryWithBackoff_ThenFailAfterThreeAttempts()
    {
        var job = NewJob();
        job.Claim("w1", Policy, Now).Claimed.ShouldBeTrue();
        job.AttemptCount.ShouldBe(1);
        job.FailAttempt("w1", ReportFailureCodes.RendererUnavailable, transient: true, Policy, Now).Final.ShouldBeFalse();
        job.Status.ShouldBe(ReportJobStatus.Pending);
        job.NextAttemptAtUtc.ShouldBe(Now.AddSeconds(30));
        job.IsClaimable(Now.AddSeconds(29)).ShouldBeFalse();

        job.Claim("w1", Policy, Now.AddSeconds(30)).Claimed.ShouldBeTrue();
        job.FailAttempt("w1", ReportFailureCodes.StorageUnavailable, transient: true, Policy, Now.AddSeconds(30)).Final.ShouldBeFalse();
        job.NextAttemptAtUtc.ShouldBe(Now.AddSeconds(90)); // 30 s + 60 s

        job.Claim("w2", Policy, Now.AddSeconds(90)).Claimed.ShouldBeTrue();
        job.AttemptCount.ShouldBe(3);
        job.FailAttempt("w2", ReportFailureCodes.Timeout, transient: true, Policy, Now.AddSeconds(91)).Final.ShouldBeTrue();
        job.Status.ShouldBe(ReportJobStatus.Failed);
        job.FailureCode.ShouldBe(ReportFailureCodes.AttemptsExhausted);
        job.DomainEvents.OfType<ReportFailed>().Single().FailureCode.ShouldBe(ReportFailureCodes.AttemptsExhausted);
        job.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void T_RPT_005_b_PermanentFailure_IsFinal_AndOnlyCataloguedCodesAreExposed()
    {
        var job = NewJob();
        job.Claim("w1", Policy, Now);
        Should.Throw<ArgumentException>(() => job.FailAttempt("w1", "System.NullReferenceException at line 4", transient: false, Policy, Now));
        job.FailAttempt("w1", ReportFailureCodes.SourceChanged, transient: false, Policy, Now).Final.ShouldBeTrue();
        job.FailureCode.ShouldBe(ReportFailureCodes.SourceChanged);
        job.Claim("w1", Policy, Now.AddHours(1)).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void T_RPT_005_c_ExpiredLease_IsReclaimed_AndTheOldClaimantCannotComplete()
    {
        var job = NewJob();
        job.Claim("crashed", Policy, Now);
        job.IsClaimable(Now.AddMinutes(4)).ShouldBeFalse();
        job.ReportProgress("crashed", 40, "rendering", Policy, Now.AddMinutes(4)).ShouldBeNull();
        job.ClaimExpiresAtUtc.ShouldBe(Now.AddMinutes(9)); // progress renews the lease
        job.IsClaimable(Now.AddMinutes(9)).ShouldBeTrue();

        job.Claim("survivor", Policy, Now.AddMinutes(9)).Claimed.ShouldBeTrue();
        job.AttemptCount.ShouldBe(2);
        var fileId = Guid.NewGuid();
        job.Complete("crashed", fileId, "late.pdf", 10, Policy, Now.AddMinutes(10))!.Code.ShouldBe(ReportErrors.LeaseLostCode);
        job.ReportProgress("crashed", 50, "x", Policy, Now).ShouldNotBeNull();
        job.FailAttempt("crashed", ReportFailureCodes.Unexpected, true, Policy, Now).Error.ShouldNotBeNull();

        job.Complete("survivor", fileId, "report.pdf", 1024, Policy, Now.AddMinutes(10)).ShouldBeNull();
        job.Status.ShouldBe(ReportJobStatus.Ready);
        job.ExpiresAtUtc.ShouldBe(Now.AddMinutes(10).AddDays(7));
        job.ProgressPercent.ShouldBe(100);
        job.Complete("survivor", fileId, "report.pdf", 1024, Policy, Now.AddMinutes(11)).ShouldBeNull(); // second completion is a no-op
        job.DomainEvents.OfType<ReportReady>().Count().ShouldBe(1);
        job.Complete("survivor", Guid.NewGuid(), "other.pdf", 5, Policy, Now.AddMinutes(11))!.Code.ShouldBe(ReportErrors.LeaseLostCode);
    }

    [Fact]
    public void T_RPT_005_d_StaleJobWithoutAttemptsLeft_FailsInsteadOfRunningAgain()
    {
        var job = NewJob();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            job.Claim($"w{attempt}", Policy, Now.AddHours(attempt)).Claimed.ShouldBeTrue();
        }

        job.AttemptCount.ShouldBe(3);
        var (claimed, error) = job.Claim("w9", Policy, Now.AddHours(5));
        claimed.ShouldBeFalse();
        error.ShouldBeNull();
        job.Status.ShouldBe(ReportJobStatus.Failed);
        job.FailureCode.ShouldBe(ReportFailureCodes.AttemptsExhausted);
    }

    [Fact]
    public void T_RPT_005_e_GracefulRelease_ReturnsTheAttemptAndMakesTheJobDueAtOnce()
    {
        var job = NewJob();
        job.Claim("w1", Policy, Now);
        job.ReleaseClaim("other", Now).ShouldNotBeNull();
        job.ReleaseClaim("w1", Now.AddSeconds(5)).ShouldBeNull();
        job.Status.ShouldBe(ReportJobStatus.Pending);
        job.AttemptCount.ShouldBe(0);
        job.IsClaimable(Now.AddSeconds(5)).ShouldBeTrue();
    }

    [Fact]
    public void T_RPT_006_a_CancelOnlyPending_AndExpireAfterRetention()
    {
        var pending = NewJob();
        pending.Cancel(Now).ShouldBeNull();
        pending.Status.ShouldBe(ReportJobStatus.Cancelled);
        pending.Cancel(Now).ShouldBeNull(); // repeat returns the current state

        var running = NewJob();
        running.Claim("w1", Policy, Now);
        running.Cancel(Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);

        var fileId = Guid.NewGuid();
        running.Complete("w1", fileId, "out.xlsx", 10, Policy, Now);
        running.IsDownloadable(Now.AddDays(6)).ShouldBeTrue();
        running.IsPastRetention(Now.AddDays(7)).ShouldBeTrue();
        running.IsDownloadable(Now.AddDays(7)).ShouldBeFalse();
        running.Expire(Now.AddDays(6)).ShouldBeFalse();
        running.Expire(Now.AddDays(7)).ShouldBeTrue();
        running.Status.ShouldBe(ReportJobStatus.Expired);
        running.Expire(Now.AddDays(8)).ShouldBeFalse();
    }

    [Fact]
    public void T_RPT_009_a_CatalogMapsFormatsMediaTypesAndTemplates()
    {
        ReportCatalog.Format(ReportType.QuoteComparisonPdf).ShouldBe(ReportFormat.Pdf);
        ReportCatalog.MediaType(ReportType.BudgetConsumptionXlsx).ShouldBe(ReportCatalog.XlsxMediaType);
        ReportCatalog.Extension(ReportType.SupplierQuotesXlsx).ShouldBe(".xlsx");
        Enum.GetValues<ReportType>().Select(ReportCatalog.TemplateVersion).Distinct().Count().ShouldBe(5);
        // Phase 32 (D32-4): the purchase-order document is a system-only PDF whose output is kept as a business record.
        ReportCatalog.Format(ReportType.PurchaseOrderPdf).ShouldBe(ReportFormat.Pdf);
        ReportCatalog.TemplateVersion(ReportType.PurchaseOrderPdf).ShouldBe("purchase-order-pdf.v1");
        Enum.GetValues<ReportType>().Where(ReportCatalog.IsSystemOnly).ShouldBe([ReportType.PurchaseOrderPdf]);
        Enum.GetValues<ReportType>().Where(ReportCatalog.KeepsOutput).ShouldBe([ReportType.PurchaseOrderPdf]);
        Policy.RetryDelay(1).ShouldBe(TimeSpan.FromSeconds(30));
        Policy.RetryDelay(3).ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Windows\\system32\\evil.pdf", "evil.pdf")]
    [InlineData("rel<at>ório:2027?.pdf", "rel_at_ório_2027_.pdf")]
    [InlineData("  .hidden.  ", "hidden")]
    [InlineData("a\u0000b\u001Fc.pdf", "a_b_c.pdf")]
    public void T_FIL_002_a_FileNamesAreSanitized(string raw, string expected) =>
        StoredFile.SanitizeName(raw).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("???")]
    public void T_FIL_002_b_UnusableFileNamesAreRejected(string? raw) => StoredFile.SanitizeName(raw).ShouldBeNull();

    [Fact]
    public void T_FIL_002_c_AvailableRequiresServerDigestAndSizePolicy()
    {
        var file = PendingFile();
        file.MarkAvailable(0, Sha, 100, Now)!.Field.ShouldBe("length");
        file.MarkAvailable(101, Sha, 100, Now)!.Kind.ShouldBe(DomainErrorKind.TooLarge);
        file.MarkAvailable(10, "ABC", 100, Now)!.Field.ShouldBe("sha256");
        file.MarkAvailable(10, Sha.ToUpperInvariant(), 100, Now).ShouldNotBeNull();
        file.MarkAvailable(10, Sha, 100, Now).ShouldBeNull();
        file.Status.ShouldBe(StoredFileStatus.Available);
        file.MarkAvailable(10, Sha, 100, Now).ShouldBeNull(); // same digest: no-op
        file.MarkAvailable(11, Sha, 100, Now)!.Code.ShouldBe(FileErrors.DigestMismatchCode);
        file.IsDownloadable(Now).ShouldBeTrue();
        file.MarkDeleted("expired", Now);
        file.MarkDeleted("again", Now);
        file.DeletionReason.ShouldBe("expired");
        file.IsDownloadable(Now).ShouldBeFalse();
        file.MarkAvailable(10, Sha, 100, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void T_FIL_005_a_CleanupSelectsOldPendingAndExpiredAvailableFilesOnly()
    {
        var policy = FileRetentionPolicy.V1;
        var pending = PendingFile();
        pending.IsCleanupCandidate(policy, Now.AddHours(23)).ShouldBeFalse();
        pending.IsCleanupCandidate(policy, Now.AddHours(24)).ShouldBeTrue();

        var available = PendingFile(expires: Now.AddDays(7));
        available.MarkAvailable(10, Sha, 100, Now);
        available.IsCleanupCandidate(policy, Now.AddDays(6)).ShouldBeFalse();
        available.IsCleanupCandidate(policy, Now.AddDays(7)).ShouldBeTrue();
        available.IsDownloadable(Now.AddDays(7)).ShouldBeFalse();

        var permanent = PendingFile(expires: null);
        permanent.MarkAvailable(10, Sha, 100, Now);
        permanent.IsCleanupCandidate(policy, Now.AddYears(5)).ShouldBeFalse();
        permanent.MarkDeleted("manual", Now);
        permanent.IsCleanupCandidate(policy, Now.AddYears(5)).ShouldBeFalse();
    }

    [Fact]
    public void T_FIL_001_a_CreatePendingValidatesMetadataOnly()
    {
        StoredFile.CreatePending(Guid.NewGuid(), "", Guid.NewGuid(), "a.pdf", "application/pdf", FileClassification.Internal, null, Now, null).Error!.Field.ShouldBe("ownerType");
        StoredFile.CreatePending(Guid.NewGuid(), "ReportJob", Guid.NewGuid(), "?", "application/pdf", FileClassification.Internal, null, Now, null).Error!.Field.ShouldBe("fileName");
        StoredFile.CreatePending(Guid.NewGuid(), "ReportJob", Guid.NewGuid(), "a.pdf", "", FileClassification.Internal, null, Now, null).Error!.Field.ShouldBe("mediaType");
        StoredFile.CreatePending(Guid.NewGuid(), "ReportJob", Guid.NewGuid(), "a.pdf", "application/pdf", FileClassification.Internal, null, Now, Now).Error!.Field.ShouldBe("expiresAt");
        Should.Throw<ArgumentException>(() => StoredFile.CreatePending(Guid.Empty, "ReportJob", Guid.NewGuid(), "a.pdf", "application/pdf", FileClassification.Internal, null, Now, null));
        var file = PendingFile();
        file.Length.ShouldBeNull();
        file.Sha256.ShouldBeNull();
        typeof(StoredFile).GetProperties().Select(property => property.Name).ShouldNotContain("StorageKey");
        typeof(StoredFile).GetProperties().ShouldNotContain(property => property.PropertyType == typeof(byte[]) || property.PropertyType == typeof(Stream));
    }

    [Fact]
    public void T_RPT_003_a_ParameterSnapshotIsCanonicalAndFingerprintStable()
    {
        var requisition = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var first = new ReportParameters(RequisitionId: requisition, RequisitionVersion: 7);
        var second = new ReportParameters(RequisitionVersion: 7, RequisitionId: requisition);
        first.ToCanonicalJson().ShouldBe("""{"requisitionId":"11111111-2222-3333-4444-555555555555","requisitionVersion":"7"}""");
        ReportParameters.Fingerprint(ReportType.RequisitionPdf, first.ToCanonicalJson())
            .ShouldBe(ReportParameters.Fingerprint(ReportType.RequisitionPdf, second.ToCanonicalJson()));
        ReportParameters.Fingerprint(ReportType.RequisitionPdf, first.ToCanonicalJson())
            .ShouldNotBe(ReportParameters.Fingerprint(ReportType.QuoteComparisonPdf, first.ToCanonicalJson()));
        var full = new ReportParameters(Guid.NewGuid(), 3, Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), "BRL", new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), Guid.NewGuid());
        ReportParameters.Parse(full.ToCanonicalJson()).ShouldBe(full);
        Should.Throw<System.Text.Json.JsonException>(() => ReportParameters.Parse("""{"template":"<script>"}"""));
    }

    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly string Hash = new('a', 64);

    private static ReportJob NewJob() => Request().Job!;

    private static (ReportJob? Job, DomainError? Error) Request(
        string parameters = "{}", string? hash = null, string timeZone = "America/Sao_Paulo", DateTime? asOf = null, ReportType type = ReportType.RequisitionPdf) =>
        ReportJob.Request(Guid.NewGuid(), type, Requester, parameters, hash ?? Hash, asOf ?? Now, timeZone, Policy, Now);

    private static StoredFile PendingFile(DateTime? expires = null) =>
        StoredFile.CreatePending(Guid.NewGuid(), "ReportJob", Guid.NewGuid(), "report.pdf", "application/pdf", FileClassification.Confidential, null, Now, expires).File!;
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Template.Application.Interfaces.Reporting;
using Template.Application.Logging;
using Template.Application.Reporting;

namespace Template.Infrastructure.Reporting.Pdf;

/// <summary>
/// <see cref="IPdfRenderer"/> on Playwright headless Chromium (D23-5). One browser per Worker process is launched lazily
/// and relaunched after a crash or disconnect; every render runs in a fresh context with JavaScript disabled, offline,
/// every request aborted and a bounded number of concurrent renders. Launch failures, crashes and timeouts surface as
/// <see cref="ReportRendererUnavailableException"/> so the attempt is retried (RPT-005); logs carry exception types only.
/// </summary>
public sealed class PlaywrightPdfRenderer(IOptions<ReportRenderingSettings> options, ILogger<PlaywrightPdfRenderer> logger) : IPdfRenderer, IAsyncDisposable
{
    private readonly ReportRenderingSettings settings = options.Value;
    private readonly SemaphoreSlim launchGate = new(1, 1);
    private readonly SemaphoreSlim renderSlots = new(options.Value.MaxConcurrentRenders, options.Value.MaxConcurrentRenders);
    private IPlaywright? playwright;
    private IBrowser? browser;

    /// <summary>The running browser, for crash-recovery tests.</summary>
    internal IBrowser? Browser => browser;

    public async Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        var html = ReportHtmlTemplate.Render(document);
        var footer = ReportHtmlTemplate.Footer(document);

        await renderSlots.WaitAsync(cancellationToken);
        try
        {
            var running = await EnsureBrowserAsync(cancellationToken);
            await using var context = await running.NewContextAsync(new BrowserNewContextOptions
            {
                JavaScriptEnabled = false,
                Offline = true,
                Locale = document.Language,
                BypassCSP = false
            }).WaitAsync(settings.RenderTimeout, cancellationToken);
            var page = await context.NewPageAsync();
            await page.RouteAsync("**/*", route => route.AbortAsync());
            var timeout = (float)settings.RenderTimeout.TotalMilliseconds;
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load, Timeout = timeout })
                .WaitAsync(settings.RenderTimeout, cancellationToken);
            var pdf = await page.PdfAsync(new PagePdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                PreferCSSPageSize = true,
                DisplayHeaderFooter = true,
                HeaderTemplate = "<span></span>",
                FooterTemplate = footer,
                Tagged = true,
                Outline = true
            }).WaitAsync(settings.RenderTimeout, cancellationToken);
            await output.WriteAsync(pdf, cancellationToken);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException or InvalidOperationException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(LogEvents.ReportJobFailed, "PDF rendering failed with {ExceptionType}; the browser will be restarted", exception.GetType().Name);
            await ResetAsync();
            throw new ReportRendererUnavailableException("The PDF renderer is unavailable.", exception);
        }
        finally
        {
            renderSlots.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        playwright?.Dispose();
        playwright = null;
    }

    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        if (browser is { IsConnected: true } current)
            return current;
        await launchGate.WaitAsync(cancellationToken);
        try
        {
            if (browser is { IsConnected: true } existing)
                return existing;
            if (browser is not null)
                await ResetAsync();
            playwright ??= await Playwright.CreateAsync();
            var launched = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = (float)settings.LaunchTimeout.TotalMilliseconds,
                ExecutablePath = string.IsNullOrWhiteSpace(settings.ExecutablePath) ? null : settings.ExecutablePath,
                Args = ["--disable-dev-shm-usage", "--disable-extensions", "--disable-gpu", "--no-first-run", "--mute-audio"]
            }).WaitAsync(settings.LaunchTimeout + TimeSpan.FromSeconds(5), cancellationToken);
            browser = launched;
            return launched;
        }
        finally
        {
            launchGate.Release();
        }
    }

    private async Task ResetAsync()
    {
        var previous = browser;
        browser = null;
        if (previous is null)
            return;
        try
        {
            await previous.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException or InvalidOperationException)
        {
            // A crashed browser cannot be closed cleanly; the next render launches a new one.
        }
    }
}

/// <summary>Rendering options of the Worker (<c>ReportRenderingSettings</c>); no secret.</summary>
public sealed class ReportRenderingSettings
{
    public const string SectionName = nameof(ReportRenderingSettings);

    /// <summary>Upper bound of one PDF render (content load and print).</summary>
    public TimeSpan RenderTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan LaunchTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Concurrent renders per Worker process (1–8).</summary>
    public int MaxConcurrentRenders { get; set; } = 2;

    /// <summary>Optional Chromium path; empty uses the Playwright-managed browser of the pinned package version.</summary>
    public string? ExecutablePath { get; set; }

    public static bool IsValid(ReportRenderingSettings settings) =>
        settings.RenderTimeout > TimeSpan.Zero && settings.RenderTimeout <= TimeSpan.FromMinutes(10)
        && settings.LaunchTimeout > TimeSpan.Zero && settings.LaunchTimeout <= TimeSpan.FromMinutes(5)
        && settings.MaxConcurrentRenders is >= 1 and <= 8;
}

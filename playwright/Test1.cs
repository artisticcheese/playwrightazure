using System;
using Microsoft.Playwright.MSTest;
using Microsoft.Playwright;

[TestClass]
public class Tests : PageTest
{
    [TestInitialize]
    public async Task TestInitialize()
    {
        await Context.Tracing.StartAsync(new()
        {
            Title = $"{TestContext.FullyQualifiedTestClassName}.{TestContext.TestName}",
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await Context.Tracing.StopAsync(new()
        {
            Path = Path.Combine(
                Environment.CurrentDirectory,
                "playwright-traces",
                $"{TestContext.FullyQualifiedTestClassName}.{TestContext.TestName}.zip"
            )
        });
    }

    [TestMethod]
    public async Task MyTest()
    {
        using var telemetry = ApplicationInsightsTelemetry.Start(nameof(MyTest));

        try
        {
            await telemetry.TrackStepAsync("Navigate to ipinfo.io", () => Page.GotoAsync("https://ipinfo.io/"));

            await telemetry.TrackStepAsync("Select 8.8.8.8 quick action", async () =>
            {
                await Page.GetByRole(AriaRole.Textbox, new() { Name = "Search any IP data..." }).ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "8.8.8.8" }).ClickAsync();
            });

            await telemetry.TrackStepAsync("Verify expected abuse contact", () => Expect(Page.Locator("#block-summary")).ToContainTextAsync("network-abuse@google.com"));

            telemetry.TrackSuccess();
        }
        catch (Exception ex)
        {
            if (!telemetry.HasTrackedException)
            {
                telemetry.TrackException(ex);
            }

            throw;
        }
    }

}

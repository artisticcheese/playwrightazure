using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

internal sealed class ApplicationInsightsTelemetry : IDisposable
{
    private static readonly Lazy<TelemetryClient?> LazyClient = new(CreateTelemetryClient);
    private static readonly string RunLocation = Environment.MachineName;
    private readonly TelemetryClient? _client;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly string _testName;
    private bool _completed;
    private bool _totalDurationTracked;
    private bool _exceptionTracked;

    private ApplicationInsightsTelemetry(string testName)
    {
        _testName = testName;
        _client = LazyClient.Value;
        TrackEvent("TestStarted");
    }

    public static ApplicationInsightsTelemetry Start(string testName)
        => new(testName);

    public async Task TrackStepAsync(string stepName, Func<Task> stepAction)
    {
        if (stepAction == null)
        {
            throw new ArgumentNullException(nameof(stepAction));
        }

        var startTime = DateTimeOffset.UtcNow;
        var stepStopwatch = Stopwatch.StartNew();

        try
        {
            await stepAction().ConfigureAwait(false);
            TrackAvailability(stepName, startTime, stepStopwatch.Elapsed, success: true);
        }
        catch (Exception ex)
        {
            TrackAvailability(stepName, startTime, stepStopwatch.Elapsed, success: false, ex.Message);
            TrackException(ex);
            throw;
        }
    }

    public void TrackSuccess()
    {
        _completed = true;
        TrackEvent("TestSucceeded");
    }

    public void TrackException(Exception exception)
    {
        _client?.TrackException(exception, BuildProperties());
        _exceptionTracked = true;
    }

    public bool HasTrackedException => _exceptionTracked;

    private void TrackEvent(string eventName)
    {
        _client?.TrackEvent(eventName, BuildProperties());
    }

    private void TrackAvailability(string stepName, DateTimeOffset timestamp, TimeSpan duration, bool success, string? message = null)
    {
        _client?.TrackAvailability(stepName, timestamp, duration, RunLocation, success, message, BuildProperties(stepName, duration));
    }

    public void TrackTotalDuration()
    {
        if (_client == null || _totalDurationTracked)
        {
            return;
        }

        _totalDurationTracked = true;
        var duration = _stopwatch.Elapsed;
        var startTime = DateTimeOffset.UtcNow - duration;
        var message = _completed ? null : "Test failed";

        _client.TrackAvailability(
            "TestTotalDuration",
            startTime,
            duration,
            RunLocation,
            _completed,
            message,
            BuildProperties("TestTotalDuration", duration));
    }

    private Dictionary<string, string> BuildProperties(string? stepName = null, TimeSpan? stepDuration = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["TestName"] = _testName,
            ["ElapsedMs"] = _stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(stepName))
        {
            properties["StepName"] = stepName;
        }

        if (stepDuration.HasValue)
        {
            properties["StepDurationMs"] = stepDuration.Value.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
        }

        return properties;
    }

    public void Dispose()
    {
        if (_client == null)
        {
            return;
        }

        TrackTotalDuration();

        if (!_completed)
        {
            TrackEvent("TestFailed");
        }

        _client.Flush();
        Task.Delay(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
    }

    private static TelemetryClient? CreateTelemetryClient()
    {
        var connectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.ConnectionString = connectionString.Trim();
            return new TelemetryClient(configuration);
        }

        // Older setups surface only instrumentation keys, so fall back when no connection string is present.
        var instrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
        if (!string.IsNullOrWhiteSpace(instrumentationKey))
        {
            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.ConnectionString = $"InstrumentationKey={instrumentationKey.Trim()}";
            return new TelemetryClient(configuration);
        }

        return null;
    }
}

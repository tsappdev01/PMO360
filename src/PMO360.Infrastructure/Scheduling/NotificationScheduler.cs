using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PMO360.Application.Options;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;

namespace PMO360.Infrastructure.Scheduling;

/// <summary>
/// What Power Automate did on a schedule in the BRD's low-code design: the daily reminder sweep
/// (WF-04, WF-05) and the weekly portfolio digest (WF-07).
///
/// The loop wakes every few minutes and asks whether the due time has passed today. Both jobs
/// claim each notification in the database before sending, so running on more than one App
/// Service instance — or restarting mid-sweep — cannot send anything twice. That makes this
/// safe to scale out without a distributed lock.
/// </summary>
public sealed class NotificationScheduler(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<NotificationOptions> options,
    ILogger<NotificationScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly NotificationOptions _options = options.Value;

    private DateOnly _lastSweepDay = DateOnly.MinValue;
    private DateOnly _lastDigestDay = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Notifications are switched off; the scheduler will not run.");
            return;
        }

        logger.LogInformation(
            "Notification scheduler started. Reminder sweep at {SweepTime}, digest {DigestDay} at {DigestTime}.",
            _options.ReminderSweepTime, _options.DigestDay, _options.DigestTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failure must not stop the loop: tomorrow's reminders matter more than
                // today's error.
                logger.LogError(ex, "The notification scheduler failed on this pass.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunDueJobsAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var today = clock.Today;
        var timeOfDay = TimeOnly.FromDateTime(now.DateTime);

        // WF-04, WF-05 — daily, before the working day starts.
        if (_lastSweepDay < today && timeOfDay >= _options.ReminderSweepTime)
        {
            _lastSweepDay = today;
            logger.LogInformation("Running the reminder sweep for {Today}.", today);

            await using var scope = scopes.CreateAsyncScope();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.RunReminderSweepAsync(cancellationToken);
        }

        // WF-07 — the scheduled portfolio digest, [Monday 08:00].
        if (_lastDigestDay < today
            && today.DayOfWeek == _options.DigestDay
            && timeOfDay >= _options.DigestTime)
        {
            _lastDigestDay = today;
            logger.LogInformation("Sending the portfolio digest for {Today}.", today);

            await using var scope = scopes.CreateAsyncScope();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.SendPortfolioDigestAsync(cancellationToken);
        }
    }
}

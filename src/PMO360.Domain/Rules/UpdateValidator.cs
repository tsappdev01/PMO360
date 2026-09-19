using PMO360.Domain.Entities;
using PMO360.Domain.Enums;

namespace PMO360.Domain.Rules;

/// <summary>
/// Section 5.2, enforced on the server. The form also guides the user, but a submission that
/// breaks a rule is rejected here whatever the client did — AC-07 and AC-08 are stated as
/// rejections, not as hints.
/// </summary>
public static class UpdateValidator
{
    /// <summary>Minimum length for an explanation, so a single character cannot satisfy a rule.</summary>
    private const int MinimumExplanation = 10;

    public static IReadOnlyList<ValidationFailure> Validate(
        UpdateSubmission submission,
        Project project,
        IReadOnlyCollection<Milestone> milestones,
        DateOnly today)
    {
        var failures = new List<ValidationFailure>();

        // FR-03. Whole number between 0 and 100.
        if (submission.ProgressPercent is < 0 or > 100)
        {
            failures.Add(new ValidationFailure(
                nameof(submission.ProgressPercent), "FR-03",
                "Progress must be a whole number between 0 and 100 per cent."));
        }

        if (submission.UpdateDate > today)
        {
            failures.Add(new ValidationFailure(
                nameof(submission.UpdateDate), "FR-12",
                "The update date cannot be in the future."));
        }

        // BR-02. At Risk and Delayed require the cause and the recovery action.
        if (ProjectStatusRules.RequiresKeyUpdate(submission.Status)
            && !HasExplanation(submission.KeyUpdate))
        {
            failures.Add(new ValidationFailure(
                nameof(submission.KeyUpdate), "BR-02",
                $"A status of {ProjectStatusRules.DisplayName(submission.Status)} requires a key update "
                + "stating the cause and the recovery action."));
        }

        // BR-03. Management attention requires a reason.
        if (submission.AttentionRequired && !HasExplanation(submission.AttentionReason))
        {
            failures.Add(new ValidationFailure(
                nameof(submission.AttentionReason), "BR-03",
                "Management attention requires a reason before the update can be submitted."));
        }

        // BR-04. Completed means 100% with no open milestones.
        if (submission.Status == ProjectStatus.Completed)
        {
            if (submission.ProgressPercent != 100)
            {
                failures.Add(new ValidationFailure(
                    nameof(submission.ProgressPercent), "BR-04",
                    "A project set to Completed must be at 100 per cent."));
            }

            var open = milestones.Count(m => m.IsIncomplete);
            if (open > 0)
            {
                failures.Add(new ValidationFailure(
                    nameof(submission.Status), "BR-04",
                    $"A project set to Completed must have no open milestones; {open} "
                    + (open == 1 ? "milestone is" : "milestones are") + " still open."));
            }
        }

        // BR-05. Progress may not decrease without an explanation in the key update.
        if (submission.ProgressPercent < project.ProgressPercent && !HasExplanation(submission.KeyUpdate))
        {
            failures.Add(new ValidationFailure(
                nameof(submission.KeyUpdate), "BR-05",
                $"Progress is going down from {project.ProgressPercent}% to {submission.ProgressPercent}%. "
                + "Explain the reduction in the key update."));
        }

        // A closed project has left the reporting cycle; reopening it is a PMO action (FR-05).
        if (!project.IsActive)
        {
            failures.Add(new ValidationFailure(
                nameof(submission.ProjectId), "FR-05",
                "This project is closed. The PMO must reopen it before an update can be submitted."));
        }

        if (submission.MilestoneId is { } milestoneId && milestones.All(m => m.Id != milestoneId))
        {
            failures.Add(new ValidationFailure(
                nameof(submission.MilestoneId), "FR-06",
                "The selected milestone does not belong to this project."));
        }

        return failures;
    }

    private static bool HasExplanation(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length >= MinimumExplanation;
}

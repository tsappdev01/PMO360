using PMO360.Domain.Entities;
using PMO360.Domain.Enums;
using PMO360.Domain.Rules;
using Xunit;

namespace PMO360.Tests;

/// <summary>
/// Section 5.2, one test per rule. These are the rules the acceptance criteria are written
/// against, so they are worth pinning down here as well as in the database: a change that
/// breaks one should fail a build, not a UAT session.
///
/// The same rules are enforced in usp_ProjectUpdate_Submit, which has the last word. These
/// tests cover the copy the form uses to tell the manager what is wrong before they submit.
/// </summary>
public class BusinessRuleTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    private static Project ProjectAt(int progress, ProjectStatus status = ProjectStatus.OnTrack) => new()
    {
        Id = 1,
        ProjectCode = "PRJ-0001",
        Name = "Supplier Portal",
        ProgressPercent = progress,
        Status = status,
        State = RecordState.Active,
        CreatedOn = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.FromHours(4))
    };

    private static UpdateSubmission SubmissionFor(
        ProjectStatus status = ProjectStatus.OnTrack,
        int progress = 50,
        string? keyUpdate = null,
        bool attention = false,
        string? attentionReason = null) => new()
    {
        ProjectId = 1,
        UpdateDate = Today,
        Status = status,
        ProgressPercent = progress,
        KeyUpdate = keyUpdate,
        AttentionRequired = attention,
        AttentionReason = attentionReason
    };

    [Theory]
    [InlineData(ProjectStatus.AtRisk)]
    [InlineData(ProjectStatus.Delayed)]
    public void BR02_AtRiskOrDelayed_without_a_key_update_is_rejected(ProjectStatus status)
    {
        var failures = UpdateValidator.Validate(
            SubmissionFor(status), ProjectAt(50), [], Today);

        Assert.Contains(failures, f => f.Rule == "BR-02");
    }

    [Fact]
    public void BR02_AtRisk_with_the_cause_and_recovery_action_is_accepted()
    {
        var failures = UpdateValidator.Validate(
            SubmissionFor(ProjectStatus.AtRisk,
                keyUpdate: "Vendor API throughput issue; fix and retest agreed for 17-Sep."),
            ProjectAt(50), [], Today);

        Assert.Empty(failures);
    }

    [Fact]
    public void BR02_a_token_key_update_does_not_satisfy_the_rule()
    {
        // "ok" is not a cause and a recovery action, and the rule exists to get those stated.
        var failures = UpdateValidator.Validate(
            SubmissionFor(ProjectStatus.Delayed, keyUpdate: "ok"), ProjectAt(50), [], Today);

        Assert.Contains(failures, f => f.Rule == "BR-02");
    }

    [Fact]
    public void BR03_management_attention_requires_a_reason()
    {
        var failures = UpdateValidator.Validate(
            SubmissionFor(attention: true), ProjectAt(50), [], Today);

        Assert.Contains(failures, f => f.Rule == "BR-03" && f.Field == "AttentionReason");
    }

    [Fact]
    public void BR03_management_attention_with_a_reason_is_accepted()
    {
        var failures = UpdateValidator.Validate(
            SubmissionFor(attention: true, attentionReason: "Sponsor decision needed on the FY27 budget."),
            ProjectAt(50), [], Today);

        Assert.Empty(failures);
    }

    [Fact]
    public void BR04_completed_must_be_at_100_percent()
    {
        var failures = UpdateValidator.Validate(
            SubmissionFor(ProjectStatus.Completed, progress: 95), ProjectAt(95), [], Today);

        Assert.Contains(failures, f => f.Rule == "BR-04" && f.Field == "ProgressPercent");
    }

    [Fact]
    public void BR04_completed_must_have_no_open_milestones()
    {
        var milestones = new List<Milestone>
        {
            new() { Id = 1, Name = "UAT", Status = MilestoneStatus.Completed, PlannedDate = Today.AddDays(-10) },
            new() { Id = 2, Name = "Go-live", Status = MilestoneStatus.InProgress, PlannedDate = Today.AddDays(10) }
        };

        var failures = UpdateValidator.Validate(
            SubmissionFor(ProjectStatus.Completed, progress: 100), ProjectAt(100), milestones, Today);

        Assert.Contains(failures, f => f.Rule == "BR-04" && f.Message.Contains("milestone is still open"));
    }

    [Fact]
    public void BR04_completed_at_100_percent_with_every_milestone_closed_is_accepted()
    {
        var milestones = new List<Milestone>
        {
            new() { Id = 1, Name = "UAT", Status = MilestoneStatus.Completed, PlannedDate = Today.AddDays(-10) },
            new() { Id = 2, Name = "Scope B", Status = MilestoneStatus.Cancelled, PlannedDate = Today.AddDays(-2) }
        };

        var failures = UpdateValidator.Validate(
            SubmissionFor(ProjectStatus.Completed, progress: 100), ProjectAt(100), milestones, Today);

        Assert.Empty(failures);
    }

    [Fact]
    public void BR05_progress_may_not_decrease_without_an_explanation()
    {
        var failures = UpdateValidator.Validate(
            SubmissionFor(progress: 40), ProjectAt(72), [], Today);

        Assert.Contains(failures, f => f.Rule == "BR-05");
    }

    [Fact]
    public void BR05_a_decrease_explained_in_the_key_update_is_accepted()
    {
        var failures = UpdateValidator.Validate(
            SubmissionFor(progress: 40, keyUpdate: "Re-baselined after the SIT scope was widened."),
            ProjectAt(72), [], Today);

        Assert.Empty(failures);
    }

    [Fact]
    public void FR03_progress_outside_0_to_100_is_rejected()
    {
        Assert.Contains(
            UpdateValidator.Validate(SubmissionFor(progress: 140), ProjectAt(50), [], Today),
            f => f.Rule == "FR-03");

        Assert.Contains(
            UpdateValidator.Validate(SubmissionFor(progress: -5), ProjectAt(50), [], Today),
            f => f.Rule == "FR-03");
    }

    [Fact]
    public void FR05_a_closed_project_cannot_be_updated()
    {
        var project = ProjectAt(60);
        project.State = RecordState.Closed;

        var failures = UpdateValidator.Validate(SubmissionFor(progress: 70), project, [], Today);

        Assert.Contains(failures, f => f.Rule == "FR-05");
    }

    [Fact]
    public void FR06_a_milestone_from_another_project_is_rejected()
    {
        var submission = SubmissionFor();
        submission.MilestoneId = 99;

        var failures = UpdateValidator.Validate(submission, ProjectAt(50), [], Today);

        Assert.Contains(failures, f => f.Rule == "FR-06");
    }

    [Fact]
    public void An_update_dated_in_the_future_is_rejected()
    {
        var submission = SubmissionFor();
        submission.UpdateDate = Today.AddDays(1);

        var failures = UpdateValidator.Validate(submission, ProjectAt(50), [], Today);

        Assert.Contains(failures, f => f.Field == "UpdateDate");
    }

    [Fact]
    public void BR01_every_status_has_a_display_name_and_a_definition()
    {
        foreach (var status in ProjectStatusRules.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ProjectStatusRules.DisplayName(status)));
            Assert.False(string.IsNullOrWhiteSpace(ProjectStatusRules.Definition(status)));
        }
    }
}

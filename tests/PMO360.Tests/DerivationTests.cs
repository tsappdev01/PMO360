using PMO360.Domain.Entities;
using PMO360.Domain.Enums;
using PMO360.Domain.Rules;
using Xunit;

namespace PMO360.Tests;

/// <summary>
/// The derivations the dashboard shows: the next milestone (FR-07), what counts as overdue
/// (FR-08), and when a project is Not Reported (BR-06).
/// </summary>
public class DerivationTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    private static Milestone Milestone(int id, string name, DateOnly planned, MilestoneStatus status) =>
        new() { Id = id, Name = name, PlannedDate = planned, Status = status };

    [Fact]
    public void FR07_the_next_milestone_is_the_earliest_incomplete_one_by_planned_date()
    {
        var milestones = new List<Milestone>
        {
            Milestone(1, "Requirements", Today.AddDays(-40), MilestoneStatus.Completed),
            Milestone(2, "Go-live", Today.AddDays(42), MilestoneStatus.NotStarted),
            Milestone(3, "SIT completion", Today.AddDays(5), MilestoneStatus.InProgress),
            Milestone(4, "Cancelled scope", Today.AddDays(1), MilestoneStatus.Cancelled)
        };

        var next = MilestoneRules.NextMilestone(milestones);

        // The cancelled one is earlier but is not incomplete, so it is not "next".
        Assert.Equal("SIT completion", next?.Name);
    }

    [Fact]
    public void FR07_a_tie_on_planned_date_is_broken_by_id_so_the_dashboard_is_stable()
    {
        var milestones = new List<Milestone>
        {
            Milestone(7, "B", Today.AddDays(3), MilestoneStatus.NotStarted),
            Milestone(2, "A", Today.AddDays(3), MilestoneStatus.NotStarted)
        };

        Assert.Equal("A", MilestoneRules.NextMilestone(milestones)?.Name);
        // Reversing the input must not change the answer.
        milestones.Reverse();
        Assert.Equal("A", MilestoneRules.NextMilestone(milestones)?.Name);
    }

    [Fact]
    public void FR07_a_project_with_every_milestone_complete_has_no_next_milestone()
    {
        var milestones = new List<Milestone>
        {
            Milestone(1, "UAT", Today.AddDays(-5), MilestoneStatus.Completed)
        };

        Assert.Null(MilestoneRules.NextMilestone(milestones));
    }

    [Fact]
    public void FR08_a_milestone_past_its_planned_date_and_not_complete_is_overdue()
    {
        Assert.True(Milestone(1, "SIT", Today.AddDays(-1), MilestoneStatus.InProgress).IsOverdueAt(Today));
        Assert.False(Milestone(2, "SIT", Today, MilestoneStatus.InProgress).IsOverdueAt(Today));
        Assert.False(Milestone(3, "SIT", Today.AddDays(-1), MilestoneStatus.Completed).IsOverdueAt(Today));
    }

    [Fact]
    public void FR20_the_rolling_window_excludes_what_is_already_overdue()
    {
        var milestones = new List<Milestone>
        {
            Milestone(1, "Overdue", Today.AddDays(-2), MilestoneStatus.InProgress),
            Milestone(2, "Inside", Today.AddDays(6), MilestoneStatus.NotStarted),
            Milestone(3, "Outside", Today.AddDays(30), MilestoneStatus.NotStarted)
        };

        var due = MilestoneRules.DueWithin(milestones, Today, 14).ToList();

        Assert.Single(due);
        Assert.Equal("Inside", due[0].Name);
    }

    [Fact]
    public void Milestone_slippage_is_measured_against_the_baseline()
    {
        var milestone = new Milestone
        {
            PlannedDate = new DateOnly(2026, 10, 8),
            BaselineDate = new DateOnly(2026, 9, 30),
            ActualDate = null
        };

        // Not yet delivered: slippage is the forecast against the baseline.
        Assert.Equal(8, milestone.SlippageDays);

        milestone.ActualDate = new DateOnly(2026, 10, 2);
        Assert.Equal(2, milestone.SlippageDays);
    }

    [Fact]
    public void BR06_an_active_project_with_no_update_in_the_cycle_is_not_reported()
    {
        var project = new Project
        {
            State = RecordState.Active,
            CreatedOn = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(4)),
            LastUpdateDate = Today.AddDays(-8)
        };

        Assert.True(ReportingRules.IsNotReported(project, Today, 7));
        Assert.Equal(8, ReportingRules.DaysSinceUpdate(project, Today));
    }

    [Fact]
    public void BR06_a_project_that_reported_inside_the_cycle_is_not_flagged()
    {
        var project = new Project
        {
            State = RecordState.Active,
            CreatedOn = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(4)),
            LastUpdateDate = Today.AddDays(-3)
        };

        Assert.False(ReportingRules.IsNotReported(project, Today, 7));
    }

    [Fact]
    public void BR06_a_project_that_has_never_reported_counts_from_its_creation()
    {
        var project = new Project
        {
            State = RecordState.Active,
            CreatedOn = new DateTimeOffset(Today.AddDays(-20).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(4)),
            LastUpdateDate = null
        };

        Assert.True(ReportingRules.IsNotReported(project, Today, 7));

        // A project created two days ago has not missed anything yet.
        project.CreatedOn = new DateTimeOffset(Today.AddDays(-2).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(4));
        Assert.False(ReportingRules.IsNotReported(project, Today, 7));
    }

    [Fact]
    public void BR06_a_closed_project_is_never_reported_as_not_reported()
    {
        var project = new Project
        {
            State = RecordState.Closed,
            CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(4)),
            LastUpdateDate = Today.AddDays(-90)
        };

        Assert.False(ReportingRules.IsNotReported(project, Today, 7));
    }

    [Fact]
    public void FR16_average_progress_covers_the_active_portfolio_only()
    {
        var projects = new List<Project>
        {
            new() { ProgressPercent = 72 },
            new() { ProgressPercent = 54 },
            new() { ProgressPercent = 38 }
        };

        Assert.Equal(55, ReportingRules.AverageProgress(projects));
        Assert.Equal(0, ReportingRules.AverageProgress([]));
    }

    [Theory]
    [InlineData(null, ProjectStatus.AtRisk, true)]
    [InlineData(ProjectStatus.OnTrack, ProjectStatus.AtRisk, true)]
    [InlineData(ProjectStatus.AtRisk, ProjectStatus.AtRisk, false)]
    [InlineData(ProjectStatus.AtRisk, ProjectStatus.Delayed, true)]
    [InlineData(ProjectStatus.Delayed, ProjectStatus.OnTrack, false)]
    public void WF01_and_WF02_alert_only_on_a_change_into_an_exception_status(
        ProjectStatus? previous, ProjectStatus current, bool expected)
    {
        Assert.Equal(expected, ProjectStatusRules.HasWorsened(previous, current));
    }
}

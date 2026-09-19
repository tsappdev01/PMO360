using PMO360.Domain.Entities;

namespace PMO360.Domain.Rules;

public static class MilestoneRules
{
    /// <summary>
    /// FR-07. The next milestone is the earliest incomplete milestone by planned date.
    /// Id breaks a same-date tie so the dashboard shows the same one on every render.
    /// </summary>
    public static Milestone? NextMilestone(IEnumerable<Milestone> milestones) =>
        milestones.Where(m => m.IsIncomplete)
                  .OrderBy(m => m.PlannedDate)
                  .ThenBy(m => m.Id)
                  .FirstOrDefault();

    /// <summary>FR-08.</summary>
    public static IEnumerable<Milestone> Overdue(IEnumerable<Milestone> milestones, DateOnly today) =>
        milestones.Where(m => m.IsOverdueAt(today));

    /// <summary>FR-20. Milestones falling inside the rolling window shown on the dashboard.</summary>
    public static IEnumerable<Milestone> DueWithin(IEnumerable<Milestone> milestones, DateOnly today, int days) =>
        milestones.Where(m => m.IsIncomplete
                              && m.PlannedDate >= today
                              && m.PlannedDate <= today.AddDays(days))
                  .OrderBy(m => m.PlannedDate);
}

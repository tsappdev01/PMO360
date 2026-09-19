using PMO360.Domain.Entities;

namespace PMO360.Domain.Rules;

public static class ReportingRules
{
    /// <summary>
    /// BR-06. An active project with no update inside the agreed period is reported as
    /// Not Reported — "no news" is not the same as "on track". A project that has never been
    /// updated is Not Reported from the day it was created.
    /// </summary>
    public static bool IsNotReported(Project project, DateOnly today, int cycleDays)
    {
        if (!project.IsActive)
        {
            return false;
        }

        var reference = project.LastUpdateDate ?? DateOnly.FromDateTime(project.CreatedOn.Date);
        return reference.AddDays(cycleDays) < today;
    }

    /// <summary>Days since the last update, for the "not updated recently" view (FR-23).</summary>
    public static int DaysSinceUpdate(Project project, DateOnly today)
    {
        var reference = project.LastUpdateDate ?? DateOnly.FromDateTime(project.CreatedOn.Date);
        return today.DayNumber - reference.DayNumber;
    }

    /// <summary>
    /// FR-16. Average progress across the active portfolio. The dashboard labels this
    /// "weighted by active projects", i.e. the plain mean over active projects; closed and
    /// cancelled projects are out of it so a finished project cannot flatter the figure.
    /// </summary>
    public static int AverageProgress(IEnumerable<Project> activeProjects)
    {
        var values = activeProjects.Select(p => p.ProgressPercent).ToList();
        return values.Count == 0 ? 0 : (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);
    }
}

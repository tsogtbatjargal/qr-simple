namespace QrSimple.Api.Tests;

// SplitByRecency is a pure function (no DB), so these run with no Testcontainers fixture —
// cheapest and most valuable tests for docs/plans/0002-inspection-records.md decisions 16-17.
public class InspectionCatalogTests
{
    private static readonly DateOnly Today = new(2026, 8, 29);

    [Fact]
    public void Empty_input_returns_two_empty_lists()
    {
        var (recent, older) = InspectionCatalog.SplitByRecency(
            Array.Empty<DateOnly>(), Today, d => d);

        Assert.Empty(recent);
        Assert.Empty(older);
    }

    [Fact]
    public void All_records_within_six_months_are_all_recent()
    {
        DateOnly[] dates = [Today, Today.AddMonths(-1), Today.AddMonths(-3)];

        var (recent, older) = InspectionCatalog.SplitByRecency(dates, Today, d => d);

        Assert.Equal(3, recent.Count);
        Assert.Empty(older);
    }

    [Fact]
    public void All_records_older_than_six_months_still_surface_the_minimum_recent()
    {
        DateOnly[] dates = [Today.AddMonths(-7), Today.AddMonths(-8), Today.AddMonths(-9), Today.AddMonths(-10), Today.AddMonths(-11)];

        var (recent, older) = InspectionCatalog.SplitByRecency(dates, Today, d => d);

        Assert.Equal(3, recent.Count);
        Assert.Equal(2, older.Count);
        // The three most recent (closest to today) win the promotion, in order.
        Assert.Equal(Today.AddMonths(-7), recent[0]);
        Assert.Equal(Today.AddMonths(-8), recent[1]);
        Assert.Equal(Today.AddMonths(-9), recent[2]);
        Assert.Equal(Today.AddMonths(-10), older[0]);
        Assert.Equal(Today.AddMonths(-11), older[1]);
    }

    [Fact]
    public void Mixed_recent_and_older_records_split_correctly()
    {
        DateOnly[] dates = [Today, Today.AddMonths(-2), Today.AddMonths(-6), Today.AddMonths(-7), Today.AddMonths(-12)];

        var (recent, older) = InspectionCatalog.SplitByRecency(dates, Today, d => d);

        // today.AddMonths(-6) is the inclusive boundary — counts as recent.
        Assert.Equal(3, recent.Count);
        Assert.Contains(Today, recent);
        Assert.Contains(Today.AddMonths(-2), recent);
        Assert.Contains(Today.AddMonths(-6), recent);
        Assert.Equal(2, older.Count);
    }

    [Fact]
    public void Exactly_six_months_ago_counts_as_recent()
    {
        DateOnly[] dates = [Today.AddMonths(-6)];

        var (recent, older) = InspectionCatalog.SplitByRecency(dates, Today, d => d);

        Assert.Single(recent);
        Assert.Empty(older);
    }

    [Fact]
    public void One_day_past_six_months_counts_as_older()
    {
        DateOnly[] dates = [Today.AddMonths(-6).AddDays(-1)];

        var (recent, older) = InspectionCatalog.SplitByRecency(dates, Today, d => d);

        // With only one record and a minimum-recent of 3, it still gets promoted.
        Assert.Single(recent);
        Assert.Empty(older);
    }

    [Fact]
    public void Minimum_recent_does_not_promote_more_than_exists()
    {
        DateOnly[] dates = [Today.AddMonths(-8), Today.AddMonths(-9)];

        var (recent, older) = InspectionCatalog.SplitByRecency(dates, Today, d => d);

        Assert.Equal(2, recent.Count);
        Assert.Empty(older);
    }
}

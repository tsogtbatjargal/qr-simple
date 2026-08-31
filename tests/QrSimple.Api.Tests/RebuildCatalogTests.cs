namespace QrSimple.Api.Tests;

// ContentDisposition is pure (no DB), so these run with no Testcontainers fixture — cheapest
// and most valuable tests for the inline-PDF filename rules in docs/plans/0002-inspection-records.md
// decision 19. This file replaced the SplitByRecency tests, which went when the rebuild history
// page dropped the recent/older split (a 6-month window is meaningless for an event that
// happens years apart).
public class RebuildCatalogTests
{
    [Fact]
    public void Inline_header_names_the_file_after_the_equipment_and_date()
    {
        var header = ContentDisposition.BuildInlineHeader("Haul Truck 12", new DateOnly(2026, 8, 12));

        Assert.StartsWith("inline; ", header);
        Assert.Contains("""filename="Haul Truck 12-Rebuild-2026-08-12.pdf" """.TrimEnd(), header);
    }

    [Fact]
    public void Path_separators_in_an_equipment_name_are_replaced()
    {
        var header = ContentDisposition.BuildInlineHeader("Pump A/B\\C", new DateOnly(2026, 1, 2));

        Assert.DoesNotContain("A/B", header);
        Assert.Contains("Pump A-B-C-Rebuild-2026-01-02.pdf", header);
    }

    [Fact]
    public void A_cyrillic_name_keeps_an_ascii_fallback_and_carries_the_real_name_in_filename_star()
    {
        var header = ContentDisposition.BuildInlineHeader("Экскаватор 7", new DateOnly(2026, 3, 4));

        // The quoted filename must stay ASCII; filename* carries the percent-encoded original.
        var quoted = header[(header.IndexOf("filename=\"", StringComparison.Ordinal) + 10)..];
        quoted = quoted[..quoted.IndexOf('"')];
        Assert.All(quoted, c => Assert.True(c < 128, $"non-ASCII '{c}' leaked into the quoted filename"));

        Assert.Contains("filename*=UTF-8''", header);
        Assert.Contains(Uri.EscapeDataString("Экскаватор"), header);
    }

    [Fact]
    public void An_entirely_non_ascii_name_still_produces_a_usable_ascii_fallback()
    {
        var header = ContentDisposition.BuildInlineHeader("Экскаватор", new DateOnly(2026, 5, 6));

        // "Экскаватор-Rebuild-2026-05-06.pdf" still has ASCII left after stripping Cyrillic, so
        // the fallback is that remainder rather than the "rebuild.pdf" last resort.
        Assert.Contains("Rebuild-2026-05-06.pdf", header);
    }

    [Fact]
    public void A_double_quote_in_the_name_cannot_break_out_of_the_quoted_filename()
    {
        var header = ContentDisposition.BuildInlineHeader("""Pump "Big" One""", new DateOnly(2026, 7, 8));

        var afterFilename = header[(header.IndexOf("filename=\"", StringComparison.Ordinal) + 10)..];
        var quoted = afterFilename[..afterFilename.IndexOf('"')];

        Assert.Contains("Pump 'Big' One", quoted);
    }
}

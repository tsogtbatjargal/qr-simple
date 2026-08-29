namespace QrSimple.Api;

// The server runs in Fly's ord region (Chicago, UTC-5/-6); users are in Mongolia (UTC+8) — a
// 13-14 hour gap. Deriving "today" from DateTime.UtcNow would default the upload form to
// yesterday's date, and shift the 6-month recent/older split by a day, for roughly the first
// 8 hours of every Mongolian working day. Store timestamps in UTC (UploadedAtUtc etc.); use
// this class for anything that means "today" or "as displayed to a person in Mongolia."
//
// A single hardcoded constant, not config and not a per-Organization column: ADR 0001 models
// Organization from day one for eventual multi-tenancy, but a per-org timezone is speculative
// work for a tenant that doesn't exist today, and a config knob nobody turns adds a production
// failure mode if it's ever set to a bad zone ID. If a second Organization with a different
// business timezone is ever added, this is the one place that needs to become per-org.
//
// Verified 2026-08-29: /usr/share/zoneinfo/Asia/Ulaanbaatar is present in the production
// runtime image (mcr.microsoft.com/dotnet/aspnet:10.0) and InvariantGlobalization is not set,
// so FindSystemTimeZoneById resolves there. Re-verified in the devcontainer while implementing
// docs/plans/0002-inspection-records.md — see that plan's Log for the result.
public static class BusinessTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar");

    public static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime);

    public static DateTimeOffset ToBusiness(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, Zone);
}

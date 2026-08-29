namespace QrSimple.Api;

public static class InspectionKinds
{
    public const string Weekly = "Weekly";
    public const string Monthly = "Monthly";
    public const string Quarterly = "Quarterly";
    public const string Annual = "Annual";
    public const string AdHoc = "AdHoc";

    public static readonly string[] All = [Weekly, Monthly, Quarterly, Annual, AdHoc];

    public static bool IsKnown(string kind) => All.Contains(kind);

    // AdHoc is stored without a hyphen (a plain identifier, matching Roles.cs's style) but
    // reads better with one on screen.
    public static string DisplayName(string kind) => kind == AdHoc ? "Ad-hoc" : kind;
}

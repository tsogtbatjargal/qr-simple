namespace QrSimple.Api;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Reader = "Reader";

    public static readonly string[] All = [Admin, Operator, Reader];

    public static bool IsKnown(string role) => All.Contains(role);
}

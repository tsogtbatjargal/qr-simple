namespace QrSimple.Api;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public bool IsActive { get; set; } = true;
}

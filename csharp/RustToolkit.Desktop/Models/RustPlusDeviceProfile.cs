namespace RustToolkit.Desktop.Models;

public sealed record RustPlusDeviceProfile
{
    public Guid ServerId { get; init; }
    public ulong PlayerId { get; init; }
    public int PlayerToken { get; init; }
    public ulong EntityId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
}

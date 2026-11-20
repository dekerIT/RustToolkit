namespace RustToolkit.Desktop.Models;

public sealed record RustPlusServerProfile
{
    public Guid ServerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public int Port { get; init; }
    public ulong PlayerId { get; init; }
    public int PlayerToken { get; init; }
}

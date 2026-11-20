namespace RustToolkit.Desktop.Models;

public sealed class RustPlusStoredProfile
{
    public string CredentialsJson { get; set; } = string.Empty;
    public string SteamAuthToken { get; set; } = string.Empty;
    public ulong SteamId { get; set; }
    public List<RustPlusServerProfile> Servers { get; set; } = [];
    public List<RustPlusDeviceProfile> Devices { get; set; } = [];
    public HashSet<string> PersistentIds { get; set; } = new(StringComparer.Ordinal);
}

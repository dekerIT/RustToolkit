using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public sealed class RustPlusProfileStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RustToolkit/RustPlus/v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RustToolkit",
        "rustplus.dat");

    public RustPlusStoredProfile? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(_path);
        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("Could not decrypt the saved Rust+ profile for this Windows user.", exception);
        }

        try
        {
            return JsonSerializer.Deserialize<RustPlusStoredProfile>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("The saved Rust+ profile is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The saved Rust+ profile is invalid.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Save(RustPlusStoredProfile profile)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new IOException("Could not determine the Rust+ profile directory.");
        Directory.CreateDirectory(directory);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(profile, JsonOptions);
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var temporaryPath = $"{_path}.tmp";
                File.WriteAllBytes(temporaryPath, encrypted);
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Delete()
    {
        File.Delete($"{_path}.tmp");
        File.Delete(_path);
    }
}

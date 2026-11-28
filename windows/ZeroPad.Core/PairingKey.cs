using System.Security.Cryptography;

namespace ZeroPad.Server;

public static class PairingKey
{
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroPad", "pairing.key");

    public static byte[] LoadOrCreate(string? path = null)
    {
        path ??= DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var existing = Convert.FromBase64String(File.ReadAllText(path).Trim());
            if (existing.Length != 32) throw new InvalidDataException("Pairing key file must contain a 32-byte key. Delete it to generate a new key.");
            return existing;
        }
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(path, Convert.ToBase64String(key));
        return key;
    }
}

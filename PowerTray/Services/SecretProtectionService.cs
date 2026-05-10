using System.Security.Cryptography;
using System.Text;

namespace PowerTray.Services;

public static class SecretProtectionService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PowerTray.BiosSetupPassword.v1");

    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return string.Empty;
        }

        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string? Unprotect(string encryptedSecret)
    {
        if (string.IsNullOrWhiteSpace(encryptedSecret))
        {
            return null;
        }

        try
        {
            byte[] protectedBytes = Convert.FromBase64String(encryptedSecret);
            byte[] plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to decrypt saved BIOS setup password.");
            return null;
        }
    }
}

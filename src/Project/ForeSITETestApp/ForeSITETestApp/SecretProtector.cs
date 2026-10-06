using System.Security.Cryptography;
using System.Text;

namespace ForeSITETestApp;

internal static class SecretProtector
{
    private const string Prefix = "dpapi:";

    public static bool IsProtected(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;
        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;
        try
        {
            byte[] encrypted = Convert.FromBase64String(value[Prefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException) { return string.Empty; }
        catch (FormatException) { return string.Empty; }
    }
}

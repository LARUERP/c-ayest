using System;
using System.Linq;
using System.Security.Cryptography;

namespace FusionDarkRP.Telephone;

public static class PhonePin
{
    public static bool Valid(string pin)
    {
        return pin != null &&
               pin.Length == 4 &&
               pin.All(c => c >= '0' && c <= '9');
    }

    public static string Salt()
    {
        return Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(16));
    }

    public static string Hash(string pin, string salt)
    {
        if (!Valid(pin))
        {
            throw new InvalidOperationException(
                "Le code doit contenir quatre chiffres.");
        }

        using Rfc2898DeriveBytes deriveBytes =
            new Rfc2898DeriveBytes(
                pin,
                Convert.FromBase64String(salt),
                100000,
                HashAlgorithmName.SHA256);

        return Convert.ToBase64String(
            deriveBytes.GetBytes(32));
    }

    public static bool Verify(
        string pin,
        string salt,
        string hash)
    {
        if (!Valid(pin) ||
            string.IsNullOrEmpty(salt) ||
            string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(Hash(pin, salt)),
                Convert.FromBase64String(hash));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

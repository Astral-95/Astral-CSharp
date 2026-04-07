using System.Security.Cryptography;

namespace Astral.Tokens;

public static class TokenGenerator
{
    public static string GenerateToken(int Length = 32)
    {
        var ByteLength = (int)Math.Ceiling(Length * 3 / 4.0); // reverse base64 expansion
        var Bytes = new byte[ByteLength];
        using (var Rng = RandomNumberGenerator.Create())
        {
            Rng.GetBytes(Bytes);
        }
        return Convert.ToBase64String(Bytes)[..Length]; // trim to exact length
    }
}
namespace Kosync;

public static class Utility
{
    // The MD5 KOReader puts on the wire as `x-auth-key`. It is not password
    // storage - it is the protocol's password-equivalent, and the inner step
    // of the stored `bcrypt(md5(password))` (ADR 0005). Use PasswordHasher
    // for anything that gets written to or compared against the database.
    public static string Md5Hash(string password)
    {
        byte[] hash = MD5.HashData(Encoding.ASCII.GetBytes(password));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

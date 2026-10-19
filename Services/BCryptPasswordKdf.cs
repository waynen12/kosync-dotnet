namespace Kosync.Services;

public class BCryptPasswordKdf : IPasswordKdf
{
    public string Hash(string value, int workFactor)
    {
        return BCrypt.Net.BCrypt.HashPassword(value, workFactor);
    }

    public bool Verify(string value, string hash)
    {
        // A stored value that isn't a well-formed bcrypt hash makes the
        // library throw rather than return false; to a caller asking
        // "do these credentials match" that is still just a no.
        try
        {
            return BCrypt.Net.BCrypt.Verify(value, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    public bool IsHash(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            BCrypt.Net.BCrypt.InterrogateHash(value);
            return true;
        }
        catch (BCrypt.Net.HashInformationException)
        {
            return false;
        }
    }
}

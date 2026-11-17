namespace Kosync.Tests.TestSupport;

public static class AuthHeaders
{
    // Sends what a stock KOReader client sends: the password MD5'd, never the
    // password itself.
    public static void SetCredentials(this HttpRequestMessage request, string username, string plainTextPassword)
    {
        request.SetCredentialsWithRawKey(username, Utility.Md5Hash(plainTextPassword));
    }

    public static void SetCredentialsWithRawKey(this HttpRequestMessage request, string username, string rawAuthKey)
    {
        request.Headers.Add(Constants.AuthHeaders.Username, username);
        request.Headers.Add(Constants.AuthHeaders.AuthKey, rawAuthKey);
    }
}

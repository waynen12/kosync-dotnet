namespace Kosync.Tests.TestSupport;

public static class AuthHeaders
{
    public static void SetCredentials(this HttpRequestMessage request, string username, string plainTextPassword)
    {
        request.Headers.Add("x-auth-user", username);
        request.Headers.Add("x-auth-key", Utility.HashPassword(plainTextPassword));
    }

    public static void SetCredentialsWithRawKey(this HttpRequestMessage request, string username, string rawAuthKey)
    {
        request.Headers.Add("x-auth-user", username);
        request.Headers.Add("x-auth-key", rawAuthKey);
    }
}

namespace Kosync.Services;

public class UserService
{
    private IHttpContextAccessor _contextAccessor;
    private KosyncDbContext _db;
    private PasswordHasher _passwordHasher;

    private bool userLoadAttempted = false;


    private string? _username = "";
    public string? Username
    {
        get
        {
            LoadUser();
            return _username;
        }
    }

    private bool _isAuthenticated = false;
    public bool IsAuthenticated
    {
        get
        {
            LoadUser();
            return _isAuthenticated;
        }
    }

    private bool _isActive = false;
    public bool IsActive
    {
        get
        {
            LoadUser();
            return _isActive;
        }
    }

    private bool _isAdmin = false;
    public bool IsAdmin
    {
        get
        {
            LoadUser();
            return _isAdmin;
        }
    }


    public UserService(IHttpContextAccessor contextAccessor, KosyncDbContext db, PasswordHasher passwordHasher)
    {
        _contextAccessor = contextAccessor;
        _db = db;
        _passwordHasher = passwordHasher;
    }

    private void LoadUser()
    {
        if (userLoadAttempted) { return; }

        userLoadAttempted = true;

        _username = _contextAccessor?.HttpContext?.Request.Headers[Constants.AuthHeaders.Username];

        // KOReader sends MD5(password) as the auth key; the stored credential
        // is bcrypt over exactly that value (ADR 0005). So the User is looked
        // up by username and the credential verified, rather than matched in
        // the query the way a bare hash could be.
        string? authKey = _contextAccessor?.HttpContext?.Request.Headers[Constants.AuthHeaders.AuthKey];

        if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(authKey)) { return; }

        var user = _db.Users.FirstOrDefault(i => i.Username == _username);

        if (user is null) { return; }

        if (!_passwordHasher.VerifyAuthKey(authKey, user.PasswordHash)) { return; }


        _isAuthenticated = true;

        _isActive = user.IsActive;

        _isAdmin = user.IsAdministrator;
    }
}
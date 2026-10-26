using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Kosync.Controllers;

[ApiController]
public class SyncController : ControllerBase
{
    private ILogger<SyncController> _logger;

    private ProxyService _proxyService;
    private IPService _ipService;
    private KosyncDbContext _db;
    private UserService _userService;
    private PasswordHasher _passwordHasher;
    private TenantContext _tenant;
    private PortalOptions _portal;


    public SyncController(ILogger<SyncController> logger, ProxyService proxyService, IPService ipService, KosyncDbContext db, UserService userService, PasswordHasher passwordHasher, TenantContext tenant, IOptions<PortalOptions> portal)
    {
        _logger = logger;
        _proxyService = proxyService;
        _ipService = ipService;
        _db = db;
        _userService = userService;
        _passwordHasher = passwordHasher;
        _tenant = tenant;
        _portal = portal.Value;
    }

    // The sync half of "an account does nothing until its password is
    // replaced" (ADR 0006). Returns the refusal to hand back, or null to
    // carry on.
    //
    // Called after the authentication and active checks and before anything
    // is read or written, on every endpoint an authenticated client can
    // reach. Unlike the tenant scoping below, forgetting the call here does
    // leak something real - a Provisional Password that works - so each
    // endpoint's test asserts the refusal rather than trusting the call site.
    //
    // The message names the portal because to stock KOReader a bare 401 is
    // indistinguishable from a wrong password, and re-typing the password is
    // then exactly the wrong thing for the User to try.
    private ObjectResult? RefusalIfPasswordMustChange(string attempt)
    {
        if (!_userService.MustChangePassword) { return null; }

        LogWarning($"{attempt} from user [{_userService.Username}] refused: their password is provisional and must be changed.");

        return StatusCode(401, new
        {
            message = Constants.Portal.PasswordChangeRequired(_portal.LoginUrl)
        });
    }

    // A KOReader client says who it is with the x-auth-user header, and
    // nothing else on the server knows "who" that way (ADR 0004) - so the
    // sync endpoints scope themselves here, explicitly, before the first read
    // of anybody's Books, Devices or Documents. Omitting the call does not
    // leak another User's rows; it returns an empty library, which is the
    // whole reason the filters fail closed.
    private void ScopeToAuthenticatedUser()
    {
        _tenant.SetUser(_userService.UserId!.Value);
    }

    [HttpGet("/")]
    public IActionResult Index()
    {
        return Ok("kosync-dotnet server is running.");
    }

    [HttpGet("/healthcheck")]
    public ObjectResult HealthCheck()
    {
        return StatusCode(200, new
        {
            state = "OK"
        });
    }

    [HttpGet("/users/auth")]
    public ObjectResult AuthoriseUser()
    {
        string? username = Request.Headers[Constants.AuthHeaders.Username];
        string? authKey = Request.Headers[Constants.AuthHeaders.AuthKey];

        if (username is null || authKey is null)
        {
            LogWarning("Request to /users/auth without credentials");

            return StatusCode(401, new
            {
                message = "Invalid credentials"
            });
        }

        if (!_userService.IsAuthenticated)
        {
            LogWarning($"Login to [{username}] attempted with invalid credentials.");

            return StatusCode(401, new
            {
                message = "User could not be found"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"Login to inactive account [{username}] attempted.");

            return StatusCode(401, new
            {
                message = "User is inactive"
            });
        }

        var refusal = RefusalIfPasswordMustChange("Login");
        if (refusal is not null) { return refusal; }

        LogInfo($"User [{username}] logged in.");
        return StatusCode(200, new
        {
            username = _userService.Username
        });
    }

    [HttpPost("/users/create")]
    public ObjectResult CreateUser(UserCreateRequest payload)
    {
        if (Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.RegistrationDisabled) == "true")
        {
            LogWarning("Account creation attempted but registration is disabled.");
            return StatusCode(402, new
            {
                message = "User registration is disabled"
            });
        }

        var existing = _db.Users.FirstOrDefault(u => u.Username == payload.username);
        if (existing is not null)
        {
            LogInfo($"Account creation attempted with existing username - [{payload.username}].");
            return StatusCode(402, new
            {
                message = "User already exists"
            });
        }

        // KOReader has already MD5'd the password before sending it, so the
        // payload is the auth key this User will present on every later
        // request - it only needs wrapping, not hashing from plaintext.
        var user = new User()
        {
            Username = payload.username,
            PasswordHash = _passwordHasher.HashAuthKey(payload.password),
            // One of the two routes where the password is not Provisional:
            // the person registering typed it into their own KOReader, so
            // there is nobody else's choice to replace (ADR 0006).
            PasswordState = PasswordState.UserChosen,
        };

        _db.Users.Add(user);
        _db.SaveChanges();

        LogInfo($"User [{payload.username}] created.");
        return StatusCode(201, new
        {
            username = payload.username
        });
    }

    [HttpPut("/syncs/progress")]
    public ObjectResult SyncProgress(DocumentRequest payload)
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated progress update received.");
            }
            else
            {
                LogWarning($"Unauthenticated progress update for user [{_userService.Username}] received.");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"Progress update from inactive user [{_userService.Username}] received.");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        var refusal = RefusalIfPasswordMustChange("Progress update");
        if (refusal is not null) { return refusal; }

        ScopeToAuthenticatedUser();

        var user = _db.Users
            .Include(i => i.Documents)
                .ThenInclude(d => d.SyncEvents)
            .Include(i => i.Devices)
            .FirstOrDefault(i => i.Username == _userService.Username)!;

        var document = user.Documents.SingleOrDefault(i => i.DocumentHash == payload.document);
        if (document is null)
        {
            document = new Document()
            {
                DocumentHash = payload.document,
                User = user,
                // A brand-new Document gets its own Book until deliberately
                // merged into another (GLOSSARY "Book"), and that Book belongs
                // to the same User.
                Book = new Book() { User = user },
            };
            user.Documents.Add(document);
        }

        var device = user.Devices.SingleOrDefault(i => i.DeviceId == payload.device_id);
        if (device is null)
        {
            device = new Device()
            {
                DeviceId = payload.device_id,
                DeviceName = payload.device,
                User = user,
            };
            user.Devices.Add(device);
        }
        else
        {
            device.DeviceName = payload.device;
        }

        var timestamp = DateTime.UtcNow;

        // Regression protection: a SyncEvent only becomes the Document's
        // current progress if it's at least as far along as the existing
        // current SyncEvent. Comparison is scoped to this Document alone -
        // never against other Documents on the same Book (they may be
        // different editions with incomparable percentages). Every push is
        // still recorded either way so nothing is silently lost, just not
        // promoted. The superseded event's IsCurrent is cleared so exactly
        // one SyncEvent per Document ever holds the current-progress pointer.
        var existingCurrent = document.SyncEvents.Current();
        var isCurrent = existingCurrent is null || payload.percentage >= existingCurrent.Percentage;

        if (isCurrent && existingCurrent is not null)
        {
            existingCurrent.IsCurrent = false;
        }

        var syncEvent = new SyncEvent()
        {
            Document = document,
            Device = device,
            Progress = payload.progress,
            Percentage = payload.percentage,
            Timestamp = timestamp,
            IsCurrent = isCurrent,
        };

        _db.SyncEvents.Add(syncEvent);
        _db.SaveChanges();

        LogInfo($"Received progress update for user [{_userService.Username}] from device [{payload.device}] with document hash [{payload.document}].");
        return StatusCode(200, new
        {
            document = document.DocumentHash,
            timestamp = syncEvent.Timestamp
        });
    }

    [HttpGet("/syncs/progress/{documentHash}")]
    public IActionResult GetProgress(string documentHash)
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated progress request received.");
            }
            else
            {
                LogWarning($"Unauthenticated progress request for user [{_userService.Username}] received.");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"Progress request from inactive user [{_userService.Username}] received.");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        var refusal = RefusalIfPasswordMustChange("Progress request");
        if (refusal is not null) { return refusal; }

        ScopeToAuthenticatedUser();

        // No `d.UserId == user.Id` here any more: the tenant filter is what
        // scopes this, which is the point of enforcing it in one place.
        var document = _db.Documents
            .Include(d => d.SyncEvents)
            .ThenInclude(s => s.Device)
            .SingleOrDefault(d => d.DocumentHash == documentHash);

        var syncEvent = document?.SyncEvents.Current();

        if (syncEvent is null)
        {
            LogInfo($"Document hash [{documentHash}] not found for user [{_userService.Username}].");
            return StatusCode(502, new
            {
                message = "Document not found on server"
            });
        }

        LogInfo($"Received progress request for user [{_userService.Username}] with document hash [{documentHash}].");

        var time = new DateTimeOffset(syncEvent.Timestamp);

        var result = new
        {
            device = syncEvent.Device.DeviceName,
            device_id = syncEvent.Device.DeviceId,
            document = document!.DocumentHash,
            percentage = syncEvent.Percentage,
            progress = syncEvent.Progress,
            timestamp = time.ToUnixTimeSeconds()
        };

        string json = System.Text.Json.JsonSerializer.Serialize(result);

        return new ContentResult()
        {
            Content = json,
            ContentType = "application/json",
            StatusCode = 200
        };
    }

    private void LogInfo(string text)
    {
        Log(LogLevel.Information, text);
    }

    private void LogWarning(string text)
    {
        Log(LogLevel.Warning, text);
    }

    private void Log(LogLevel level, string text)
    {
        string logMsg = $"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] [{_ipService.ClientIP}]";


        // If trusted proxies are set but this request comes from another address, mark it
        if (_proxyService.TrustedProxies.Length > 0 &&
            !_ipService.TrustedProxy)
        {
            logMsg += "*";
        }

        logMsg += $" {text}";

        _logger?.Log(level, logMsg);
    }
}

using Microsoft.AspNetCore.Mvc;

namespace Kosync.Controllers;

[ApiController]
public class ManagementController : ControllerBase
{
    private ILogger<ManagementController> _logger;

    private ProxyService _proxyService;
    private IPService _ipService;
    private KosyncDbContext _db;
    private UserService _userService;
    private UserAdminService _userAdminService;
    private TenantContext _tenant;


    public ManagementController(ILogger<ManagementController> logger, ProxyService proxyService, IPService ipService, KosyncDbContext db, UserService userService, UserAdminService userAdminService, TenantContext tenant)
    {
        _logger = logger;
        _proxyService = proxyService;
        _ipService = ipService;
        _db = db;
        _userService = userService;
        _userAdminService = userAdminService;
        _tenant = tenant;
    }

    // Same explicit scoping step the sync endpoints take, for the same
    // reason (ADR 0004): a request says who it is with the x-auth-user
    // header, and nothing else on the server knows "who" that way. Only
    // GetDocuments needs it here - every other endpoint in this controller
    // is an account operation, and Users carry no tenant filter.
    private void ScopeToAuthenticatedUser()
    {
        _tenant.SetUser(_userService.UserId!.Value);
    }

    [HttpGet("/manage/users")]
    public async Task<ObjectResult> GetUsers()
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated GET request to /manage/users.");
            }
            else
            {
                LogWarning($"Unauthenticated GET request to /manage/users with username [{_userService.Username}].");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsAdmin)
        {
            LogWarning($"Unauthorized GET request to /manage/users from user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"GET request to /manage/users received from inactive user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }


        // Projected back down to the four fields this endpoint has always
        // returned. UserAdminService answers with everything the portal's
        // Users page shows - roles, counts, last sync - and widening a
        // documented wire response is not this ticket's business.
        var users = (await _userAdminService.GetUsersAsync()).Select(i => new
        {
            id = i.Id,
            username = i.Username,
            isAdministrator = i.IsAdministrator,
            isActive = i.IsActive,
            documentCount = i.DocumentCount
        }).ToList();

        LogInfo($"User [{_userService.Username}] requested /manage/users");
        return StatusCode(200, users);
    }

    [HttpPost("/manage/users")]
    public async Task<ObjectResult> CreateUser(UserCreateRequest payload)
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated POST request to /manage/users.");
            }
            else
            {
                LogWarning($"Unauthenticated POST request to /manage/users with username [{_userService.Username}].");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsAdmin)
        {
            LogWarning($"Unauthorized POST request to /manage/users from user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"POST request to /manage/users received from inactive user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        // No role in the request, so this creates a plain User. The portal's
        // Users page is where an Administrator is made (issue #28) - adding a
        // field to a documented KOReader-adjacent request shape is a separate
        // decision from building the page that needs it.
        var result = await _userAdminService.CreateAsync(
            _userService.UserId!.Value, payload.username, payload.password, isAdministrator: false);

        if (result.Succeeded)
        {
            LogInfo($"User [{payload.username}] created by user [{_userService.Username}]");
        }

        return Respond(result);
    }

    [HttpDelete("/manage/users")]
    public async Task<ObjectResult> DeleteUser(string username)
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated DELETE request to /manage/users.");
            }
            else
            {
                LogWarning($"Unauthenticated DELETE request to /manage/users with username [{_userService.Username}].");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        // This used to let a non-Administrator through for their own account,
        // so that a User could delete themselves. Issue #28 refuses
        // self-deletion outright - an account is removed by an Administrator
        // who is not the one being removed - so the carve-out now only led to
        // a refusal one step later, and reads as a capability that no longer
        // exists.
        if (!_userService.IsAdmin)
        {
            LogWarning($"Unauthorized DELETE request to /manage/users from user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"DELETE request to /manage/users from inactive user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        var result = await _userAdminService.DeleteAsync(_userService.UserId!.Value, username);

        if (result.Succeeded)
        {
            LogInfo($"User [{username}] has been deleted by [{_userService.Username}]");
        }
        else
        {
            LogWarning($"DELETE request to /manage/users for [{username}] from [{_userService.Username}] refused: {result.Message}");
        }

        // The one endpoint here that answers a missing target with 404 rather
        // than 400. Kept as it was - its callers are scripts.
        return Respond(result, notFoundStatus: 404);
    }

    [HttpGet("/manage/users/documents")]
    public ObjectResult GetDocuments(string username)
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated GET request to /manage/users/documents.");
            }
            else
            {
                LogWarning($"Unauthenticated GET request to /manage/users/documents with username [{_userService.Username}].");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }


        // The one endpoint under /manage/* that reads a library rather than
        // managing an account, so it is the one that does not widen for an
        // Administrator (issue #27, ADR 0004): tenancy is absolute, and a
        // request for anybody but yourself gets the same refusal whoever
        // asks. Deliberately not a 403 or a "user does not exist" - the
        // answer must not tell the caller which of those it is, nor that
        // being an Administrator made any difference.
        if (!string.Equals(username, _userService.Username, StringComparison.OrdinalIgnoreCase))
        {
            LogWarning($"Unauthorized GET request to /manage/users/documents for user [{username}] from user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"GET request to /manage/users/documents received from inactive user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        LogInfo($"User [{_userService.Username}] requested their own documents");

        // Scoped the way every other read of a library is, by the global
        // query filters. Replacing the old IgnoreQueryFilters plus a
        // hand-written UserId predicate is the actual fix: there is no
        // longer a "which User" to get wrong, because the query cannot
        // name one.
        ScopeToAuthenticatedUser();

        var documents = _db.Documents
            .Include(d => d.SyncEvents)
            .ThenInclude(s => s.Device)
            .ToList()
            .Select(d =>
            {
                var current = d.SyncEvents.Current();

                return new
                {
                    documentHash = d.DocumentHash,
                    progress = current?.Progress,
                    percentage = current?.Percentage,
                    device = current?.Device.DeviceName,
                    deviceId = current?.Device.DeviceId,
                    timestamp = current?.Timestamp,
                };
            });

        return StatusCode(200, documents);
    }

    [HttpDelete("/manage/users/documents")]
    public ObjectResult DeleteUserDocument(string username, string documentHash)
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated DELETE request to /manage/users/documents.");
            }
            else
            {
                LogWarning($"Unauthenticated DELETE request to /manage/users/documents with username [{_userService.Username}].");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsAdmin &&
            !username.Equals(_userService.Username, StringComparison.OrdinalIgnoreCase))
        {
            LogWarning($"Unauthorized DELETE request to /manage/users/documents from user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"DELETE request to /manage/users/documents from inactive user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        var user = _db.Users.IgnoreQueryFilters().Include(i => i.Documents).FirstOrDefault(i => i.Username == username)!;

        var document = user.Documents.SingleOrDefault(i => i.DocumentHash == documentHash);

        if (document is null)
        {
            return StatusCode(404, new
            {
                message = $"Document hash [{documentHash}] was not found for user [{username}]."
            });
        }

        var bookId = document.BookId;

        _db.Documents.Remove(document);
        _db.SaveChanges();

        RemoveOrphanedBooks([bookId]);

        LogInfo($"User [{_userService.Username}] deleted document with hash [{documentHash}] for user [{username}].");

        return StatusCode(200, new
        {
            message = "Success"
        });
    }

    [HttpPut("/manage/users/active")]
    public async Task<ObjectResult> UpdateUserActive(string username)
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated PUT request to /manage/users/active.");
            }
            else
            {
                LogWarning($"Unauthenticated PUT request to /manage/users/active with username [{_userService.Username}].");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsAdmin)
        {
            LogWarning($"Unauthorized PUT request to /manage/users/active from user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"PUT request to /manage/users/active received from inactive user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        // Toggling, which is this endpoint's documented shape - the portal
        // says which state it means instead (UserAdminService.SetActiveAsync).
        //
        // The blanket "cannot update admin user" refusal that used to sit here
        // is gone. It was reaching for the self and last-Administrator guards
        // and caught far more than them (ADR 0006); the service now applies
        // exactly those two, so deactivating another Administrator while one
        // remains is allowed and deactivating yourself is not.
        var result = await _userAdminService.ToggleActiveAsync(_userService.UserId!.Value, username);

        if (result.Succeeded)
        {
            LogInfo($"User [{username}] updated by user [{_userService.Username}]: {result.Message}");
        }
        else
        {
            LogWarning($"PUT request to /manage/users/active for [{username}] from [{_userService.Username}] refused: {result.Message}");
        }

        return Respond(result);
    }

    [HttpPut("/manage/users/password")]
    public async Task<ObjectResult> UpdatePassword(string username, PasswordChangeRequest payload)
    {
        if (!_userService.IsAuthenticated)
        {
            if (string.IsNullOrEmpty(_userService.Username))
            {
                LogWarning("Unauthenticated PUT request to /manage/users/password.");
            }
            else
            {
                LogWarning($"Unauthenticated PUT request to /manage/users/password with username [{_userService.Username}].");
            }

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsAdmin)
        {
            LogWarning($"Unauthorized PUT request to /manage/users/password from user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        if (!_userService.IsActive)
        {
            LogWarning($"PUT request to /manage/users/password received from inactive user [{_userService.Username}].");

            return StatusCode(401, new
            {
                message = "Unauthorized"
            });
        }

        // No admin guard here either, for the reason ADR 0006 gives: setting
        // a password demotes nobody and removes nobody, and the refusal that
        // used to sit here stopped an Administrator changing their own
        // password at all - including the seeded admin, whose Provisional
        // Password is the one thing that account must be able to replace.
        //
        // Whose choice the new password counts as is the service's business:
        // set for yourself it is yours, set for somebody else it is
        // Provisional.
        var result = await _userAdminService.ResetPasswordAsync(
            _userService.UserId!.Value, username, payload.password);

        if (result.Succeeded)
        {
            LogInfo(username == _userService.Username
                ? $"User [{_userService.Username}] changed their own password."
                : $"User [{username}]'s password was set to a provisional one by [{_userService.Username}].");
        }

        return Respond(result);
    }

    // One status code per refusal, so every endpoint above answers the same
    // reason the same way. Only the missing-target case differs between them,
    // and only because DELETE /manage/users has always answered it with a 404.
    private ObjectResult Respond(UserAdminResult result, int notFoundStatus = 400)
    {
        var status = result.Outcome switch
        {
            UserAdminOutcome.Succeeded => 200,
            UserAdminOutcome.NotAnAdministrator => 401,
            UserAdminOutcome.UserNotFound => notFoundStatus,
            _ => 400,
        };

        return StatusCode(status, new
        {
            message = result.Message
        });
    }

    private void RemoveOrphanedBooks(IEnumerable<int> bookIds)
    {
        var orphanedBooks = _db.Books
            .IgnoreQueryFilters()
            .Where(b => bookIds.Contains(b.Id) && !b.Documents.Any());
        _db.Books.RemoveRange(orphanedBooks);
        _db.SaveChanges();
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

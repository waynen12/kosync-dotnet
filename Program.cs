using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();


builder.Services.Configure<PasswordHashingOptions>(
    builder.Configuration.GetSection(Constants.Passwords.ConfigurationSection));

builder.Services.Configure<PasswordPolicyOptions>(
    builder.Configuration.GetSection(Constants.Passwords.ConfigurationSection));

builder.Services.Configure<PortalOptions>(
    builder.Configuration.GetSection(Constants.Portal.ConfigurationSection));

builder.Services.Configure<SyncTraceOptions>(
    builder.Configuration.GetSection(Constants.SyncTrace.ConfigurationSection));

builder.Services.Configure<ForwardedHeadersSettings>(
    builder.Configuration.GetSection(Constants.ForwardedHeaders.ConfigurationSection));

builder.Services.AddMemoryCache();

builder.Services.AddSingleton<ProxyService, ProxyService>();
builder.Services.AddSingleton<IPasswordKdf, BCryptPasswordKdf>();
builder.Services.AddSingleton<PasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<PasswordPolicy, PasswordPolicy>();
builder.Services.AddScoped<TenantContext, TenantContext>();
builder.Services.AddScoped<IPService, IPService>();
builder.Services.AddScoped<RequestLogFormatter, RequestLogFormatter>();
builder.Services.AddScoped<UserService, UserService>();
builder.Services.AddScoped<PortalTenantResolver, PortalTenantResolver>();
builder.Services.AddScoped<UserAdminService, UserAdminService>();
builder.Services.AddScoped<DeviceDashboardService, DeviceDashboardService>();
builder.Services.AddScoped<BookDashboardService, BookDashboardService>();
builder.Services.AddScoped<DocumentRegistry, DocumentRegistry>();

// Bound directly rather than through DI: this value is needed to build the
// DbContext registration itself and to seed/migrate the database below,
// both of which happen before anything else could resolve IOptions from the
// container.
var storageOptions = new StorageOptions();
builder.Configuration.GetSection(Constants.Storage.ConfigurationSection).Bind(storageOptions);

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, storageOptions.ResolvedDataDirectory);

builder.Services.AddDbContext<KosyncDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, Constants.Storage.DatabaseFileName)}"));


builder.Services.AddControllers();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

// Bound directly rather than through DI, matching storageOptions above: the
// cookie's SecurePolicy is set inside the AddCookie delegate below, which
// Program.cs builds before anything could resolve IOptions from the
// container.
var authenticationOptions = new AuthenticationOptions();
builder.Configuration.GetSection(Constants.Authentication.ConfigurationSection).Bind(authenticationOptions);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = AppRoutes.Login;
        options.Cookie.SecurePolicy = authenticationOptions.ResolvedCookieSecurePolicy;
    });

// The one named policy: the Users administration page (issue #28). The
// requirement re-reads the User row on every request rather than trusting a
// claim, which is why the capability can be revoked - see
// AdministratorRequirement.
builder.Services.AddAuthorization(options =>
    options.AddPolicy(Constants.Authorization.Administrator, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new AdministratorRequirement());
    }));

builder.Services.AddScoped<IAuthorizationHandler, AdministratorRequirementHandler>();


if (Environment.GetEnvironmentVariable("SINGLE_LINE_LOGGING") == "true")
{

    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
    });
}


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
    var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
    KosyncDbInitializer.Initialize(db, tenant, dataDirectory, passwordHasher, logger, storageOptions.ResolvedMaxBackupsRetained);

    // Said once, out loud, when the trace is on. Without it an operator who
    // sees no trace lines cannot tell "the setting did not take" from "the
    // device never reached this server" - and telling those apart is usually
    // the reason they turned it on.
    if (scope.ServiceProvider.GetRequiredService<IOptions<SyncTraceOptions>>().Value.IsEnabled)
    {
        logger.LogWarning(Constants.SyncTrace.EnabledNotice);
    }
}

// Explicit options rather than the parameterless UseForwardedHeaders(),
// which reads ForwardedHeadersOptions from DI and defaults it to
// ForwardedHeaders.None - a silent no-op nothing here ever configured
// (issue #52).
//
// The middleware is added at all only when a network was configured. Its
// own CheckKnownAddress only runs when KnownProxies or KnownIPNetworks is
// non-empty (checkKnownIps in ForwardedHeadersMiddleware) - with both
// empty it skips that check and trusts the header from anyone, which is the
// opposite of what an unconfigured install must do. Leaving the middleware
// out of the pipeline entirely is what actually reproduces "rewrite
// nothing", matching the no-op this replaces.
var forwardedHeadersSettings = app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;

if (forwardedHeadersSettings.ParsedKnownNetworks.Count > 0)
{
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = Constants.ForwardedHeaders.Enabled,
    };

    // ForwardedHeadersOptions ships KnownProxies and KnownIPNetworks
    // pre-populated with the IPv6 loopback and 127.0.0.0/8 - harmless
    // defaults for a process running directly on a host, but this one runs
    // in a container alongside others on the same Docker network, where
    // "loopback" does not mean "nothing else can reach this". Cleared so
    // the configured list above is the only thing trusted.
    forwardedHeadersOptions.KnownProxies.Clear();
    forwardedHeadersOptions.KnownIPNetworks.Clear();

    foreach (var network in forwardedHeadersSettings.ParsedKnownNetworks)
    {
        forwardedHeadersOptions.KnownIPNetworks.Add(network);
    }

    app.UseForwardedHeaders(forwardedHeadersOptions);
}

// First in the pipeline, because the requests worth tracing are the ones
// that never reach an endpoint: a 404 on a path we do not serve is the
// diagnosis, and anything placed later would not see it. After
// UseForwardedHeaders, because the line it writes names the client.
app.UseMiddleware<SyncTraceMiddleware>();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// After authentication, because it needs to know who is asking; before the
// endpoints, because the whole point is that a gated User's request never
// reaches one (ADR 0006).
app.UseMiddleware<PasswordChangeGateMiddleware>();

app.UseAntiforgery();

app.MapControllers();

// Sign-out (issue #43). An endpoint rather than a page, because there is no
// screen left: the SignOutForm control in the chrome posts straight here, so
// one click signs out with nothing in between.
//
// POST only, which is the whole point. A GET sign-out is performed by
// anything that merely fetches the URL - a link preview, a prefetching
// browser - and by any page that can get a browser to follow a link, which is
// logout-by-CSRF. The confirm screen this replaced existed to make signing
// out a post; removing the screen does not remove that.
//
// The unused IFormCollection is what enforces it. UseAntiforgery validates
// only endpoints carrying antiforgery metadata, and a minimal API endpoint
// gets that metadata from binding form data - so binding the posted form is
// how this endpoint asks to be checked. Without the parameter the middleware
// finds no metadata and waves every post through, token or not
// (SignOutTests).
app.MapPost(AppRoutes.Logout, async (
    HttpContext context,
    IFormCollection form,
    IPService ipService,
    ILoggerFactory loggerFactory) =>
{
    await PortalSignOut.EndAsync(context, ipService, loggerFactory);

    return Results.Redirect(AppRoutes.Login);
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }

using System.Net;
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

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = AppRoutes.Login;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
    KosyncDbInitializer.Initialize(db, tenant, dataDirectory, passwordHasher, logger);

    // Said once, out loud, when the trace is on. Without it an operator who
    // sees no trace lines cannot tell "the setting did not take" from "the
    // device never reached this server" - and telling those apart is usually
    // the reason they turned it on.
    if (scope.ServiceProvider.GetRequiredService<IOptions<SyncTraceOptions>>().Value.IsEnabled)
    {
        logger.LogWarning(Constants.SyncTrace.EnabledNotice);
    }
}

app.UseForwardedHeaders();

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

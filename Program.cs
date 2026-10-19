using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();


builder.Services.Configure<PasswordHashingOptions>(
    builder.Configuration.GetSection(Constants.Passwords.ConfigurationSection));

builder.Services.AddMemoryCache();

builder.Services.AddSingleton<ProxyService, ProxyService>();
builder.Services.AddSingleton<IPasswordKdf, BCryptPasswordKdf>();
builder.Services.AddSingleton<PasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IPService, IPService>();
builder.Services.AddScoped<UserService, UserService>();
builder.Services.AddScoped<DeviceDashboardService, DeviceDashboardService>();
builder.Services.AddScoped<BookDashboardService, BookDashboardService>();

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");

builder.Services.AddDbContext<KosyncDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "kosync.sqlite3")}"));


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

builder.Services.AddAuthorization();


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
    var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
    KosyncDbInitializer.Initialize(db, dataDirectory, passwordHasher, logger);
}

app.UseForwardedHeaders();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }

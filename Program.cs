using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();


builder.Services.AddSingleton<ProxyService, ProxyService>();
builder.Services.AddScoped<IPService, IPService>();
builder.Services.AddScoped<UserService, UserService>();

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");

builder.Services.AddDbContext<KosyncDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "kosync.sqlite3")}"));


builder.Services.AddControllers();


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
    KosyncDbInitializer.Initialize(db, dataDirectory, logger);
}

app.UseForwardedHeaders();

app.MapControllers();



app.Run();

public partial class Program { }

using InvestmentPlanner.Api.Data;
using InvestmentPlanner.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// =============================================================
// CORE SERVICES
// =============================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<AmfiFundDataService>();

// =============================================================
// CORS
// =============================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebApp", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// =============================================================
// MFAPI CONFIGURATION
// =============================================================

var mfApiBaseUrl =
    builder.Configuration["MfApi:BaseUrl"]
    ?? "https://api.mfapi.in/";

// =============================================================
// NAV SERVICE
// =============================================================

builder.Services.AddHttpClient<NAVService>(client =>
{
    client.BaseAddress = new Uri(mfApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// =============================================================
// MFAPI DATA SERVICE
// Used by the controlled database update process.
// =============================================================

builder.Services.AddHttpClient<MfApiService>(client =>
{
    client.BaseAddress = new Uri(mfApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// =============================================================
// SCHEME SERVICE
// =============================================================

builder.Services.AddHttpClient<SchemeService>(client =>
{
    client.BaseAddress = new Uri(mfApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// =============================================================
// APPLICATION SERVICES
// =============================================================

builder.Services.AddSingleton<AnalyticsCacheService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<MFTrackerService>();
builder.Services.AddScoped<FundAnalyticsUpdateService>();

// =============================================================
// BUILD APPLICATION
// =============================================================

var app = builder.Build();

// =============================================================
// GENERATE / UPDATE FUNDS.JSON
// =============================================================

using (var scope = app.Services.CreateScope())
{
    var amfiFundDataService =
        scope.ServiceProvider
            .GetRequiredService<AmfiFundDataService>();

    await amfiFundDataService.GenerateFundsJsonAsync();
}

// =============================================================
// DEVELOPMENT TOOLS
// =============================================================

// SWAGGER
app.UseSwagger();
app.UseSwaggerUI();

// =============================================================
// HTTP PIPELINE
// =============================================================

app.UseCors("AllowWebApp");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// =============================================================
// START API
// =============================================================

app.Run();
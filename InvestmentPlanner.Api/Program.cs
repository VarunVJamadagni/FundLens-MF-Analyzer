using InvestmentPlanner.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<MFTrackerService>();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
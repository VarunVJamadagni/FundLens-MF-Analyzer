using InvestmentPlanner.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Core services ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// The Web (Razor Pages) app calls this API server-side, but CORS is enabled
// as well in case the API is ever called directly from a browser.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// MFAPI base address - used by both NAVService and SchemeService.
var mfApiBaseUrl = builder.Configuration["MfApi:BaseUrl"] ?? "https://api.mfapi.in/";

builder.Services.AddHttpClient<NAVService>(client =>
{
    client.BaseAddress = new Uri(mfApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<SchemeService>(client =>
{
    client.BaseAddress = new Uri(mfApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// AnalyticsService and MFTrackerService don't call MFAPI directly - they
// depend on NAVService, so no HttpClient registration is needed here.
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<MFTrackerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowWebApp");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

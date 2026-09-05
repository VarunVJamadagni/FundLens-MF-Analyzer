var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Named HttpClient used by Razor Page handlers to call InvestmentPlanner.Api
// server-side. Update "InvestmentPlannerApi:BaseUrl" in appsettings.json (or
// appsettings.Development.json) to match wherever your Api project is
// actually running - check its launchSettings.json for the exact port.
var apiBaseUrl = builder.Configuration["InvestmentPlannerApi:BaseUrl"] ?? "https://localhost:7001/";

builder.Services.AddHttpClient("InvestmentPlannerApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(20);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();

// Scaffold placeholder. The full ASP.NET Core sample (rule evaluation endpoint backed by
// a singleton RulewrightEngine) lands in a follow-up milestone — see the README roadmap.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Rulewright ASP.NET Core sample — full sample coming in a follow-up milestone.");

app.Run();

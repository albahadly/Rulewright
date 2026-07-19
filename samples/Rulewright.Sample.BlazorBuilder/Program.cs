using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Rulewright.Sample.BlazorBuilder;
using Rulewright.Sample.BlazorBuilder.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<EngineService>();
builder.Services.AddScoped<ExampleCatalogService>();
builder.Services.AddScoped<RuleDocumentState>();

await builder.Build().RunAsync();

var builder = WebApplication.CreateBuilder(args);

builder.AddBasicServiceDefaults();
builder.AddApplicationServices();

builder.Services.AddGrpc();

var app = builder.Build();

// 🔹 Expose Prometheus scraping endpoint
app.UseOpenTelemetryPrometheusScrapingEndpoint();  

app.MapDefaultEndpoints();

app.MapGrpcService<BasketService>();

app.Run();

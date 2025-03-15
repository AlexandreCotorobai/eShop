﻿using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;

var builder = WebApplication.CreateBuilder(args);

// Get Jaeger host from environment variables (for Docker compatibility)
string jaegerHost = builder.Configuration["JAEGER_HOST"] ?? "localhost";
int jaegerPort = int.TryParse(builder.Configuration["JAEGER_PORT"], out var port) ? port : 6831;


builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning();

builder.AddDefaultOpenApi(withApiVersioning);

var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";

var meter = new Meter("Ordering.API");
builder.Services.AddSingleton(meter);

builder.Services.AddSingleton(meter.CreateCounter<long>("order_placed_count", description: "Total number of orders placed"));
builder.Services.AddSingleton(meter.CreateUpDownCounter<int>("active_orders", description: "Number of currently active orders"));
builder.Services.AddSingleton(meter.CreateHistogram<double>("total_purchase_amount", unit: "USD", description: "Total amount of purchases"));
builder.Services.AddSingleton(meter.CreateHistogram<double>("order_value", unit: "USD", description: "Value of an order"));
builder.Services.AddSingleton(meter.CreateCounter<double>("total_revenue", unit: "USD", description: "Total revenue"));
builder.Services.AddSingleton(meter.CreateCounter<int>("order_item_quantity", description: "Quantity of items in an order"));
builder.Services.AddSingleton(meter.CreateCounter<int>("orders_by_user", description: "Number of orders by user"));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OrderAPI"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter()
            .AddJaegerExporter(options =>
            {
                options.AgentHost = jaegerHost;
                options.AgentPort = jaegerPort;
            });
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("Ordering.API")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("http://localhost:4317");
            }).AddPrometheusExporter();
    });

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.MapDefaultEndpoints();

var orders = app.NewVersionedApi("Orders");

orders.MapOrdersApiV1()
      .RequireAuthorization();

app.UseDefaultOpenApi();
app.Run();

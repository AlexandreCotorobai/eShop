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
builder.Services.AddSingleton(meter.CreateCounter<long>("mk_order_placed_count", description: "Número total de orders."));
builder.Services.AddSingleton(meter.CreateGauge<int>("mk_active_orders", description: "Número de orders ativas."));
builder.Services.AddSingleton(meter.CreateHistogram<double>("mk_total_purchase_amount", unit: "USD", description: "Soma total das compras feitas."));
builder.Services.AddSingleton(meter.CreateHistogram<double>("mk_order_value", unit: "USD", description: "Valor de uma order."));
builder.Services.AddSingleton(meter.CreateCounter<double>("mk_total_revenue", unit: "USD", description: "Receita total."));
builder.Services.AddSingleton(meter.CreateCounter<int>("mk_order_item_quantity", description: "Quantidade de items em uma order."));
builder.Services.AddSingleton(meter.CreateCounter<int>("mk_orders_by_user", description: "Número de orders por usuário."));

builder.Services.AddSingleton(meter.CreateUpDownCounter<int>("active_orders", description: "Number of currently active orders"));

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
            });
    });

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.MapDefaultEndpoints();

var orders = app.NewVersionedApi("Orders");

orders.MapOrdersApiV1();
    //   .RequireAuthorization();

app.UseDefaultOpenApi();
app.Run();

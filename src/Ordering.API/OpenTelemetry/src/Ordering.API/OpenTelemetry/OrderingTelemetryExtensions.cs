using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Resources;
using System.Text.Json;
using OpenTelemetry.Instrumentation.AspNetCore;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.API.OpenTelemetry
{
    public static class OrderingTelemetryExtensions
    {
        // Define constants for instrumentation
        public const string ServiceName = "eShop.Ordering";
        public const string ServiceVersion = "1.0.0";
        
        // Create ActivitySource and Meter for tracking ordering
        private static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);
        private static readonly Meter Meter = new(ServiceName, ServiceVersion);
        
        // Create metrics
        private static readonly Counter<long> OrdersCreatedCounter = Meter.CreateCounter<long>(
            "orders_created_total", 
            "Number of orders created");
            
        private static readonly Counter<long> OrdersCompletedCounter = Meter.CreateCounter<long>(
            "orders_completed_total", 
            "Number of orders completed successfully");
            
        private static readonly Counter<long> OrdersFailedCounter = Meter.CreateCounter<long>(
            "orders_failed_total", 
            "Number of orders that failed");
            
        private static readonly Histogram<double> OrderProcessingTime = Meter.CreateHistogram<double>(
            "order_processing_time", 
            "ms", 
            "Time taken to process an order from submission to completion");
        
        private static readonly Histogram<double> OrderValue = Meter.CreateHistogram<double>(
            "order_value", 
            "USD", 
            "Value of the order in USD");
        
        public static WebApplicationBuilder AddOrderingTelemetry(this WebApplicationBuilder builder)
        {
            // Configure OpenTelemetry - this is minimal as most is configured in AppHost
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(ServiceName, ServiceVersion)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = builder.Environment.EnvironmentName,
                        ["host.name"] = Environment.MachineName,
                    }))
                .WithTracing(tracing => 
                {
                    // Add activity sources
                    tracing.AddSource(ServiceName);
                    
                    // Add standard instrumentations
                    tracing.AddAspNetCoreInstrumentation(options => 
                    {
                        // Filter out health check endpoints
                        options.Filter = context => 
                            !context.Request.Path.StartsWithSegments("/hc") && 
                            !context.Request.Path.StartsWithSegments("/health");
                            
                        // Add order-specific tags
                        options.EnrichWithHttpRequest = (activity, request) => 
                        {
                            if (request.Path.StartsWithSegments("/api/orders"))
                            {
                                activity.SetTag("order.flow", true);
                                activity.SetTag("order.endpoint", request.Path);
                            }
                        };
                    });
                    
                    // Add HTTP client instrumentation
                    tracing.AddHttpClientInstrumentation();
                    
                    // Add EF Core instrumentation with data protection
                    tracing.AddEntityFrameworkCoreInstrumentation(options => 
                    {
                        // Don't include potentially sensitive SQL in traces
                        options.SetDbStatementForText = false;
                    });
                    
                    // Note: Exporters are configured in AppHost
                })
                .WithMetrics(metrics => 
                {
                    // Add meters
                    metrics.AddMeter(ServiceName);
                    
                    // Add runtime metrics
                    metrics.AddRuntimeInstrumentation();
                    metrics.AddAspNetCoreInstrumentation();
                    metrics.AddHttpClientInstrumentation();
                    
                    // Note: Exporters are configured in AppHost
                });
                
            // Register our telemetry service as a singleton
            builder.Services.AddSingleton<OrderingTelemetryService>();
            
            return builder;
        }
        
        // Public methods for recording metrics
        public static void RecordOrderCreated(int orderId, double value = 0)
        {
            // Increment the counter
            OrdersCreatedCounter.Add(1);
            
            // Record order value if provided
            if (value > 0)
            {
                OrderValue.Record(value);
            }
            
            // Create a span for the order creation
            using var activity = ActivitySource.StartActivity("OrderCreated", ActivityKind.Producer);
            activity?.SetTag("order.id", orderId);
            activity?.SetTag("order.value", value);
        }
        
        public static void RecordOrderCompleted(int orderId, double processingTimeMs)
        {
            // Increment the counter
            OrdersCompletedCounter.Add(1);
            
            // Record the processing time
            OrderProcessingTime.Record(processingTimeMs);
            
            // Create a span for the order completion
            using var activity = ActivitySource.StartActivity("OrderCompleted");
            activity?.SetTag("order.id", orderId);
            activity?.SetTag("order.processing_time_ms", processingTimeMs);
        }
        
        public static void RecordOrderFailed(int orderId, string reason)
        {
            // Increment the counter
            OrdersFailedCounter.Add(1);
            
            // Create a span for the order failure
            using var activity = ActivitySource.StartActivity("OrderFailed");
            activity?.SetTag("order.id", orderId);
            activity?.SetTag("order.failure_reason", reason);
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        
        public static void RecordOrderStatusChange(int orderId, int fromStatusId, int toStatusId)
        {
            // Create a span for the status change
            using var activity = ActivitySource.StartActivity("OrderStatusChanged");
            activity?.SetTag("order.id", orderId);
            activity?.SetTag("order.status.from", fromStatusId);
            activity?.SetTag("order.status.to", toStatusId);
        }
        
        // Start a new span for order processing - returns Activity that must be disposed
        public static Activity StartOrderProcessing(int orderId)
        {
            var activity = ActivitySource.StartActivity("ProcessOrder", ActivityKind.Consumer);
            activity?.SetTag("order.id", orderId);
            return activity;
        }
    }
    
    // Service for application code to access telemetry features
    public class OrderingTelemetryService
    {
        private readonly ILogger<OrderingTelemetryService> _logger;
        
        public OrderingTelemetryService(ILogger<OrderingTelemetryService> logger)
        {
            _logger = logger;
            
            // Log that telemetry service is initialized
            _logger.LogInformation("OrderingTelemetryService initialized");
            
            // Create a test activity for verification
            using var activity = new Activity("TelemetryServiceInit").Start();
            activity?.SetTag("service", "OrderingTelemetryService");
        }
        
        // Public methods for application code to use
        public void RecordOrderCreated(int orderId, double value = 0)
        {
            OrderingTelemetryExtensions.RecordOrderCreated(orderId, value);
            _logger.LogInformation("Order {OrderId} created with value {Value}", orderId, value);
        }
        
        public void RecordOrderCompleted(int orderId, double processingTimeMs)
        {
            OrderingTelemetryExtensions.RecordOrderCompleted(orderId, processingTimeMs);
            _logger.LogInformation("Order {OrderId} completed in {ProcessingTimeMs}ms", orderId, processingTimeMs);
        }
        
        public void RecordOrderFailed(int orderId, string reason)
        {
            OrderingTelemetryExtensions.RecordOrderFailed(orderId, reason);
            _logger.LogWarning("Order {OrderId} failed: {Reason}", orderId, reason);
        }
        
        public void RecordOrderStatusChange(int orderId, int fromStatusId, int toStatusId)
        {
            OrderingTelemetryExtensions.RecordOrderStatusChange(orderId, fromStatusId, toStatusId);
            _logger.LogInformation("Order {OrderId} status changed from {FromStatus} to {ToStatus}", 
                orderId, fromStatusId, toStatusId);
        }
        
        public Activity StartOrderProcessing(int orderId)
        {
            var activity = OrderingTelemetryExtensions.StartOrderProcessing(orderId);
            _logger.LogInformation("Started processing order {OrderId}", orderId);
            return activity;
        }
        
        // Helper methods for masking sensitive data
        public string MaskSensitiveData(string input, bool isCardInfo = false)
        {
            if (string.IsNullOrEmpty(input))
                return input;
                
            if (isCardInfo)
            {
                // Mask all but last 4 digits for card numbers
                if (input.Length > 4)
                {
                    return new string('*', input.Length - 4) + input.Substring(input.Length - 4);
                }
                return new string('*', input.Length);
            }
            
            // For emails, mask everything before @
            if (input.Contains('@'))
            {
                var parts = input.Split('@');
                if (parts.Length == 2)
                {
                    return "****@" + parts[1];
                }
            }
            
            // Generic masking for other PII
            if (input.Length > 3)
            {
                // Show only first and last character
                return input[0] + new string('*', input.Length - 2) + input[input.Length - 1];
            }
            
            return "***";
        }
        
        // Helper method to sanitize JSON payloads
        public string SanitizeJsonPayload(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                using var outputStream = new MemoryStream();
                using var writer = new Utf8JsonWriter(outputStream, new JsonWriterOptions { Indented = false });
                
                SanitizeJsonElement(doc.RootElement, writer);
                
                writer.Flush();
                return System.Text.Encoding.UTF8.GetString(outputStream.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sanitize JSON payload");
                return "*** Sanitized Content ***";
            }
        }
        
        private void SanitizeJsonElement(JsonElement element, Utf8JsonWriter writer)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        // Check if this is a sensitive property
                        bool isSensitive = IsSensitiveProperty(property.Name);
                        
                        writer.WritePropertyName(property.Name);
                        
                        if (isSensitive)
                        {
                            // Mask sensitive values
                            writer.WriteStringValue("*** REDACTED ***");
                        }
                        else
                        {
                            // Process normal values recursively
                            SanitizeJsonElement(property.Value, writer);
                        }
                    }
                    writer.WriteEndObject();
                    break;
                    
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        SanitizeJsonElement(item, writer);
                    }
                    writer.WriteEndArray();
                    break;
                    
                default:
                    // Copy the element as is for non-object, non-array types
                    element.WriteTo(writer);
                    break;
            }
        }
        
        private bool IsSensitiveProperty(string propertyName)
        {
            // Define sensitive property names
            var sensitiveProperties = new[]
            {
                "cardnumber", "cardNumber", "card_number",
                "cvv", "securitycode", "securityNumber", "security_number",
                "password", "secret", "token", "key",
                "ssn", "socialSecurity", "social_security"
            };
            
            return sensitiveProperties.Any(p => 
                propertyName.Equals(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
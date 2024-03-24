using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var greeterMeter = new Meter("OtPrGrYa.Example", "1.0.0");
var countGreetings = greeterMeter.CreateCounter<int>("greetings.count", description: "Counts the number of greetings");

var greeterActivitySource = new ActivitySource("OtPrGrJa.Example");

var builder = WebApplication.CreateBuilder(args);

var tracingOtlpEndpoint = builder.Configuration["OTLP_ENDPOINT_URL"];
var otel = builder.Services.AddOpenTelemetry();

otel.ConfigureResource(resource => resource
    .AddService(serviceName: builder.Environment.ApplicationName));

otel.WithMetrics(metrics => metrics
    .AddAspNetCoreInstrumentation()
    .AddMeter(greeterMeter.Name)
    .AddMeter("Microsoft.AspNetCore.Hosting")
    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
    .AddPrometheusExporter());

otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
    tracing.AddSource(greeterActivitySource.Name);
    if (tracingOtlpEndpoint != null)
    {
        tracing.AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new Uri(tracingOtlpEndpoint);
        });
    }
    else
    {
        tracing.AddConsoleExporter();
    }
});

var app = builder.Build();
app.MapPrometheusScrapingEndpoint();

app.MapGet("/", SendGreeting);

async Task<String> SendGreeting(ILogger<Program> logger)
{
    using var activity = greeterActivitySource.StartActivity("GreeterActivity");

    logger.LogInformation("Sending greeting");

    countGreetings.Add(1);

    activity?.SetTag("greeting", "Hello World!");

    return "Hello World!";
}

app.Run();

builder.Services.AddHttpClient();

app.MapGet("/NestedGreeting", SendNestedGreeting);

async Task SendNestedGreeting(int nestlevel, ILogger<Program> logger, HttpContext context, IHttpClientFactory clientFactory)
{
    using var activity = greeterActivitySource.StartActivity("GreeterActivity");

    if (nestlevel <= 5)
    {
        logger.LogInformation("Sending greeting, level {nestlevel}", nestlevel);

        countGreetings.Add(1);

        activity?.SetTag("nest-level", nestlevel);

        await context.Response.WriteAsync($"Nested Greeting, level: {nestlevel}\r\n");

        if (nestlevel > 0)
        {
            var request = context.Request;
            var url = new Uri($"{request.Scheme}://{request.Host}{request.Path}?nestlevel={nestlevel - 1}");

            var nestedResult = await clientFactory.CreateClient().GetStringAsync(url);
            await context.Response.WriteAsync(nestedResult);
        }
    }
    else
    {
        logger.LogError("Greeting nest level {nestlevel} too high", nestlevel);
        await context.Response.WriteAsync("Nest level too high, max is 5");
    }
}
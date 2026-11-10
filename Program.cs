using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

var app = builder.Build();
var instance = app.Configuration["INSTANCE_ID"] ?? $"{Environment.MachineName}-{Environment.ProcessId}";

app.MapHealthChecks("/", new HealthCheckOptions
{
    ResponseWriter = (context, report) =>
    {
        context.Response.Headers["X-Instance"] = instance;
        context.Response.ContentType = "text/plain; charset=utf-8";
        var status = report.Status.ToString().ToLowerInvariant();
        return context.Response.WriteAsync($"{status}\ninstance: {instance}\n");
    }
});

app.MapHealthChecks("/healthz");
app.Run();

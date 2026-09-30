using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

var connectionString = builder.Configuration.GetValue<string>("AzureWebJobsStorage");

builder.Services.AddSingleton(new TableServiceClient(connectionString));

// Configure BlobServiceClient with a shorter retry timeout so it doesn't hang startup
builder.Services.AddSingleton(new BlobServiceClient(
    connectionString,
    new Azure.Storage.Blobs.BlobClientOptions
    {
        Retry =
        {
            MaxRetries = 1,
            NetworkTimeout = TimeSpan.FromSeconds(2)
        }
    }));

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

var host = builder.Build();

// Run the container creation in the BACKGROUND so it doesn't block host startup
_ = Task.Run(async () =>
{
    try
    {
        var blobServiceClient = host.Services.GetRequiredService<BlobServiceClient>();
        var containerClient = blobServiceClient.GetBlobContainerClient("staff-docs");
        await containerClient.CreateIfNotExistsAsync();
        Console.WriteLine("✓ staff-docs blob container created/verified.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Could not create staff-docs blob container: {ex.Message}");
        Console.WriteLine("Make sure Azurite is running: azurite --silent --location ./azurite");
    }
});

await host.RunAsync();
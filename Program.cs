using Base64AppBlazor.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Configure URLs - use environment variable or default to available ports
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrEmpty(urls))
{
    // Default to ports that are less likely to conflict
    urls = "http://localhost:5001;https://localhost:5002";
}
builder.WebHost.UseUrls(urls);

// Configure Kestrel server limits to allow larger file uploads (50MB)
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50MB
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// Add services to the container.
builder.Services.AddRazorPages();

// Configure Blazor Server with improved connection settings
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = !builder.Environment.IsProduction();
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
    options.MaxBufferedUnacknowledgedRenderBatches = 20;
});

// Configure SignalR with longer timeouts and better connection handling
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = !builder.Environment.IsProduction();
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 64 * 1024 * 1024; // 64MB for large file transfers
});

builder.Services.AddScoped<Base64ConverterService>();

// Add response compression for better performance
builder.Services.AddResponseCompression();

// Configure form options to allow larger file uploads (50MB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50MB
    options.ValueLengthLimit = 50 * 1024 * 1024; // 50MB
    options.MultipartHeadersLengthLimit = 50 * 1024 * 1024; // 50MB
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseResponseCompression();

app.UseRouting();

app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// Launch browser automatically when running the published exe
var isProduction = !app.Environment.IsDevelopment();
if (isProduction)
{
    // Use ApplicationStarted callback to ensure server is fully ready
    var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    
    appLifetime.ApplicationStarted.Register(() =>
    {
        // Launch browser on a background thread to avoid blocking
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for server to be fully ready
                await Task.Delay(2500);
                
                // Try to get the HTTPS URL first, then HTTP
                var urlToOpen = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) 
                    ?? urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() 
                    ?? "http://localhost:5001";
                
                if (!string.IsNullOrEmpty(urlToOpen))
                {
                    var processStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = urlToOpen,
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        ErrorDialog = false
                    };
                    
                    // Start process and immediately detach - don't track it
                    var process = System.Diagnostics.Process.Start(processStartInfo);
                    // Explicitly detach - don't wait or track the process
                    // The OS will clean it up when browser closes
                }
            }
            catch
            {
                // Silently ignore - user can manually open browser if needed
            }
        }, CancellationToken.None);
    });
}

app.Run();


using System.IO.Compression;
using Base64AppBlazor.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
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
    // Left at the framework default (10). Raising it buffers more in-flight render batches, and
    // this app's batches can each carry a multi-megabyte textarea value -- more buffering here
    // costs memory without improving throughput.
});

// Configure SignalR with longer timeouts and better connection handling
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = !builder.Environment.IsProduction();
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    // A 50MB file encodes to ~67MB of base64, which the client can paste back into the textarea.
    // The old 64MB ceiling was below that, so a max-size round trip killed the circuit instead
    // of producing an error.
    options.MaximumReceiveMessageSize = 96 * 1024 * 1024;
});

// Stateless and thread-safe -- no need for a fresh instance per circuit.
builder.Services.AddSingleton<Base64ConverterService>();

// Response compression was previously a no-op: the default EnableForHttps is false and this app
// redirects everything to HTTPS, so nothing was ever compressed.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/javascript",
        "application/json",
        "application/wasm",
        "image/svg+xml",
        "text/css",
        "text/plain"
    });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

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

// Must run BEFORE UseStaticFiles: static files short-circuit the pipeline, so when compression
// was registered after them it never saw a static response.
app.UseResponseCompression();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Fingerprinted framework assets and local css/js: let the browser reuse them across
        // reloads instead of re-fetching on every launch.
        ctx.Context.Response.Headers.CacheControl = app.Environment.IsDevelopment()
            ? "no-cache"
            : "public,max-age=604800";
    }
});

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
                // ApplicationStarted already fires once Kestrel is accepting connections; this is
                // just a small margin. The old 2500ms was added straight onto perceived startup.
                await Task.Delay(250);

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


using Microsoft.AspNetCore.Server.Kestrel.Core;

using System.Text;

using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Stores;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
//设置文件上传最大不然超过会报错 （iis 等配置需要自行在配置文件设置）
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = int.MaxValue; // if don't set default value is: 30 MB
});
builder.Services.AddSingleton<TusDiskStorageOptionHelper>();

builder.Services.AddSingleton(CreateTusConfigurationForCleanupService);

//后台任务删除文件碎片
builder.Services.AddHostedService<ExpiredFilesCleanupService>();


static DefaultTusConfiguration CreateTusConfigurationForCleanupService(IServiceProvider services)
{
    string path = services.GetRequiredService<TusDiskStorageOptionHelper>().StorageDiskPath;

    // Simplified configuration just for the ExpiredFilesCleanupService to show load order of configs.
    return new DefaultTusConfiguration
    {
        Store = new TusDiskStore(path),
        Expiration = new AbsoluteExpiration(TimeSpan.FromMinutes(5))
    };
}

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    _ = app.UseSwagger();
    _ = app.UseSwaggerUI();
}
//跨域
app.UseCors(builder => builder
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin()
            .WithExposedHeaders(tusdotnet.Helpers.CorsHelper.GetExposedHeaders())
    );

app.MapGet("/", () => "嘤嘤嘤，有问题看官网：https://github.com/tusdotnet/tusdotnet/tree/master");

// Handle downloads (must be set before MapTus)
app.MapGet("/files/{fileId}", DownloadFileEndpoint.HandleRoute);

app.MapTus("/files", TusConfigurationFactory);

app.Run();

static Task<DefaultTusConfiguration> TusConfigurationFactory(HttpContext httpContext)
{
    ILogger<Program> logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();

    // Change the value of EnableOnAuthorize in appsettings.json to enable or disable
    // the new authorization event.
    //var enableAuthorize = httpContext.RequestServices.GetRequiredService<IOptions<OnAuthorizeOption>>().Value.EnableOnAuthorize;

    string diskStorePath = httpContext.RequestServices.GetRequiredService<TusDiskStorageOptionHelper>().StorageDiskPath;

    DefaultTusConfiguration config = new()
    {
        Store = new TusDiskStore(diskStorePath),
        //MetadataParsingStrategy = MetadataParsingStrategy.AllowEmptyValues,
        UsePipelinesIfAvailable = true,
        Events = new Events
        {
            OnAuthorizeAsync = ctx =>
            {
                // Note: This event is called even if RequireAuthorization is called on the endpoint.
                // In that case this event is not required but can be used as fine-grained authorization control.
                // This event can also be used as a "on request started" event to prefetch data or similar.

                //if (!enableAuthorize)
                //    return Task.CompletedTask;

                //if (ctx.HttpContext.User.Identity?.IsAuthenticated != true)
                //{
                //    ctx.HttpContext.Response.Headers.Add("WWW-Authenticate", new StringValues("Basic realm=tusdotnet-test-net6.0"));
                //    ctx.FailRequest(HttpStatusCode.Unauthorized);
                //    return Task.CompletedTask;
                //}

                //if (ctx.HttpContext.User.Identity.Name != "test")
                //{
                //    ctx.FailRequest(HttpStatusCode.Forbidden, "'test' is the only allowed user");
                //    return Task.CompletedTask;
                //}

                // Do other verification on the user; claims, roles, etc.

                // Verify different things depending on the intent of the request.
                // E.g.:
                //   Does the file about to be written belong to this user?
                //   Is the current user allowed to create new files or have they reached their quota?
                //   etc etc
                switch (ctx.Intent)
                {
                    case IntentType.CreateFile:
                        break;
                    case IntentType.ConcatenateFiles:
                        break;
                    case IntentType.WriteFile:
                        break;
                    case IntentType.DeleteFile:
                        break;
                    case IntentType.GetFileInfo:
                        break;
                    case IntentType.GetOptions:
                        break;
                    default:
                        break;
                }

                return Task.CompletedTask;
            },

            OnBeforeCreateAsync = ctx =>
            {
                // Partial files are not complete so we do not need to validate
                // the metadata in our example.
                if (ctx.FileConcatenation is FileConcatPartial)
                {
                    return Task.CompletedTask;
                }

                if (!ctx.Metadata.ContainsKey("name") || ctx.Metadata["name"].HasEmptyValue)
                {
                    ctx.FailRequest("name metadata must be specified. ");
                }

                if (!ctx.Metadata.ContainsKey("contentType") || ctx.Metadata["contentType"].HasEmptyValue)
                {
                    ctx.FailRequest("contentType metadata must be specified. ");
                }

                return Task.CompletedTask;
            },
            OnCreateCompleteAsync = ctx =>
            {
                logger.LogInformation($"Created file {ctx.FileId} using {ctx.Store.GetType().FullName}");
                return Task.CompletedTask;
            },
            OnBeforeDeleteAsync = ctx =>
            {
                // Can the file be deleted? If not call ctx.FailRequest(<message>);
                return Task.CompletedTask;
            },
            OnDeleteCompleteAsync = ctx =>
            {
                logger.LogInformation($"Deleted file {ctx.FileId} using {ctx.Store.GetType().FullName}");
                return Task.CompletedTask;
            },
            OnFileCompleteAsync = async ctx =>
            {
                logger.LogInformation($"Upload of {ctx.FileId} completed using {ctx.Store.GetType().FullName}");
                // If the store implements ITusReadableStore one could access the completed file here.
                // The default TusDiskStore implements this interface:
                ITusFile file = await ctx.GetFileAsync();
                Dictionary<string, Metadata> metadata = await file.GetMetadataAsync(ctx.CancellationToken);
                string data = metadata["data"].GetString(Encoding.UTF8);
                using Stream content = await file.GetContentAsync(ctx.CancellationToken);
            }
        },
        // Set an expiration time where incomplete files can no longer be updated.
        // This value can either be absolute or sliding.
        // Absolute expiration will be saved per file on create
        // Sliding expiration will be saved per file on create and updated on each patch/update.
        Expiration = new AbsoluteExpiration(TimeSpan.FromMinutes(5))
    };

    return Task.FromResult(config);
}

public sealed class ExpiredFilesCleanupService : IHostedService, IDisposable
{
    private readonly ITusExpirationStore _expirationStore;
    private readonly ExpirationBase _expiration;
    private readonly ILogger<ExpiredFilesCleanupService> _logger;
    private Timer? _timer;

    public ExpiredFilesCleanupService(ILogger<ExpiredFilesCleanupService> logger, DefaultTusConfiguration config)
    {
        _logger = logger;
        _expirationStore = (ITusExpirationStore)config.Store;
        _expiration = config.Expiration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_expiration == null)
        {
            _logger.LogInformation("Not running cleanup job as no expiration has been set.");
            return;
        }

        await RunCleanup(cancellationToken);
        _timer = new Timer(async (e) => await RunCleanup((CancellationToken)e!), cancellationToken, TimeSpan.Zero, _expiration.Timeout);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = (_timer?.Change(Timeout.Infinite, 0));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private async Task RunCleanup(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Running cleanup job...");
            int numberOfRemovedFiles = await _expirationStore.RemoveExpiredFilesAsync(cancellationToken);
            _logger.LogInformation($"Removed {numberOfRemovedFiles} expired files. Scheduled to run again in {_expiration.Timeout.TotalMilliseconds} ms");
        }
        catch (Exception exc)
        {
            _logger.LogWarning("Failed to run cleanup job: " + exc.Message);
        }
    }
}

public class TusDiskStorageOptionHelper
{
    public string StorageDiskPath { get; }

    public TusDiskStorageOptionHelper()
    {
        string path = Path.Combine(Environment.CurrentDirectory, "wwwroot", "tusfiles");
        if (!File.Exists(path))
        {
            _ = Directory.CreateDirectory(path);
        }

        StorageDiskPath = path;
    }
}

public static class DownloadFileEndpoint
{
    public static async Task HandleRoute(HttpContext context)
    {
        DefaultTusConfiguration config = context.RequestServices.GetRequiredService<DefaultTusConfiguration>();

        if (config.Store is not ITusReadableStore store)
        {
            return;
        }

        string? fileId = (string?)context.Request.RouteValues["fileId"];
        ITusFile file = await store.GetFileAsync(fileId, context.RequestAborted);

        if (file == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync($"File with id {fileId} was not found.", context.RequestAborted);
            return;
        }

        Stream fileStream = await file.GetContentAsync(context.RequestAborted);
        Dictionary<string, Metadata> metadata = await file.GetMetadataAsync(context.RequestAborted);

        context.Response.ContentType = GetContentTypeOrDefault(metadata);
        context.Response.ContentLength = fileStream.Length;

        if (metadata.TryGetValue("name", out Metadata? nameMeta))
        {
            context.Response.Headers.Append("Content-Disposition",
                new[] { $"attachment; filename=\"{nameMeta.GetString(Encoding.UTF8)}\"" });
        }

        using (fileStream)
        {
            await fileStream.CopyToAsync(context.Response.Body, 81920, context.RequestAborted);
        }
    }

    private static string GetContentTypeOrDefault(Dictionary<string, Metadata> metadata)
    {
        return metadata.TryGetValue("contentType", out Metadata? contentType) ? contentType.GetString(Encoding.UTF8) : "application/octet-stream";
    }
}
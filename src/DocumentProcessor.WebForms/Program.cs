using DocumentProcessor.WebForms.Components;
using DocumentProcessor.WebForms.Configuration;
using DocumentProcessor.WebForms.Data;
using DocumentProcessor.WebForms.Logging;
using DocumentProcessor.WebForms.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Static AppSettings / AppConfiguration accessors (Database.*, Bedrock.*, Storage.* keys)
// read the host configuration from here on.
AppConfiguration.Configure(builder.Configuration);

// ---------------------------------------------------------------------------
// Logging. Global.asax added a TextWriterTraceListener on ~/App_Data/trace.log with
// AutoFlush; System.Diagnostics.Trace is replaced by ILogger, and the same file keeps
// receiving the entries (the error page still says the error is in the trace log).
// Console/debug providers from the default builder stay registered.
// ---------------------------------------------------------------------------
var traceLogPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "trace.log");
Exception? traceLogFailure = null;
try
{
    builder.Logging.AddProvider(new FileLoggerProvider(traceLogPath));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // A read-only content root must not stop the app; the console logger still runs.
    traceLogFailure = ex;
}

// [PORT-NOTE] dropped ServicePointManager.SecurityProtocol |= Tls12: .NET uses the operating
// system's TLS defaults (TLS 1.2/1.3), and the AWS SDK requires TLS 1.2 on its own.

// ---------------------------------------------------------------------------
// Database connection (Application_Start): AWS Secrets Manager first when
// Database.UseSecretsManager=true (default AWS credential chain), otherwise or on failure
// ConnectionStrings:DefaultConnection from configuration.
// ---------------------------------------------------------------------------
var connection = await DatabaseConnectionResolver.ResolveAsync();

// Application[Global.DatabaseInfoKey] -> a singleton the layout reads for its banner/footer.
builder.Services.AddSingleton(connection.Info);

// DocumentDbContext.ConnectionString (static) -> DI. The factory serves the components and the
// pipeline's using-blocks; AddDbContextFactory also registers DocumentDbContext as scoped.
// Microsoft.Data.SqlClient defaults to Encrypt=true: the configured connection string sets
// TrustServerCertificate=true and the Secrets Manager one sets Encrypt/TrustServerCertificate
// explicitly, so both connect as before (see the PORT-TODO notes in appsettings.json).
builder.Services.AddDbContextFactory<DocumentDbContext>(options =>
    options.UseSqlServer(connection.ConnectionString));

// Application services (formerly new-ed up in Default.aspx.cs / DocumentPipeline).
builder.Services.AddSingleton<IDocumentStorage, FileSystemDocumentStorage>();
builder.Services.AddSingleton<DocumentTextExtractor>();
builder.Services.AddSingleton<IDocumentSummarizer, BedrockDocumentSummarizer>();
builder.Services.AddTransient<DocumentPipeline>();

// ---------------------------------------------------------------------------
// Upload limits. Web.config allowed 500 MB per request (httpRuntime maxRequestLength=512000 KB,
// IIS requestLimits maxAllowedContentLength=524288000) so that Storage.MaxFilesPerUpload (10)
// files of Storage.MaxFileSizeMegabytes (50 MB) fit in one post.
// ---------------------------------------------------------------------------
const long MaxRequestBodyBytes = 524_288_000;

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = MaxRequestBodyBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxRequestBodyBytes);

// Blazor Server (interactive server render mode). InputFile streams each IBrowserFile to the
// server over the SignalR circuit in chunks; the per-file limit is the maxAllowedSize passed to
// OpenReadStream (Storage.MaxFileSizeMegabytes, see UploadEventArgs). The hub message limit only
// has to hold one chunk, so it is raised from the 32 KB default to 1 MB to cut round trips on
// 50 MB files without letting a client push arbitrarily large messages.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 1024 * 1024);

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

if (traceLogFailure != null)
{
    startupLogger.LogWarning(traceLogFailure, "Could not open the trace log at {TraceLogPath}.", traceLogPath);
}

if (connection.Warning != null)
{
    startupLogger.LogWarning("{DatabaseWarning}", connection.Warning);
}

try
{
    DatabaseInitializer.EnsureSchema(connection.ConnectionString, app.Environment.ContentRootPath);
}
catch (Exception ex)
{
    // Do not take the whole application down: the pages already report a failure to read
    // documents, and this way the error is visible in one place.
    startupLogger.LogError(ex, "Could not prepare the database.");
}

// ---------------------------------------------------------------------------
// Application_Error + <customErrors mode="RemoteOnly" defaultRedirect="~/ErrorPage.aspx">.
// Outside Development, unhandled exceptions re-execute the ErrorPage component; the exception
// handler middleware logs the exception (the old Trace.TraceError in Application_Error).
// In Development the developer exception page shows the details, as RemoteOnly did locally.
// ---------------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// <error statusCode="404" redirect="~/ErrorPage.aspx?code=404" />: status codes without a body
// (unknown URLs included) re-execute the ErrorPage component with ?code=<status>.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

// [PORT-NOTE] No UseHttpsRedirection/UseHsts: the legacy site did not require HTTPS.
// [PORT-NOTE] No authentication/authorization: the legacy site had none (no <authentication>/<authorization>).

app.UseAntiforgery();

// wwwroot/Content, wwwroot/Scripts and favicon.png keep their legacy URLs.
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.Endpoints;
using FPTUniRAG.Hubs;
using FPTUniRAG.Options;
using FPTUniRAG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataProtectionKeyDirectory = Path.Combine(builder.Environment.ContentRootPath, ".keys");
Directory.CreateDirectory(dataProtectionKeyDirectory);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDataProtection()
    .SetApplicationName("FPTUniRAG")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyDirectory));
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<RagIngestionOptions>(builder.Configuration.GetSection("RagIngestion"));
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<IAccountManagementService, AccountManagementService>();
builder.Services.AddScoped<ISubjectManagementService, SubjectManagementService>();
builder.Services.AddScoped<ITeacherHeaderSubjectNotifier, SignalRTeacherHeaderSubjectNotifier>();
builder.Services.AddScoped<ICredentialEmailSender, SmtpCredentialEmailSender>();
builder.Services.AddSingleton<IPasswordService, Pbkdf2PasswordService>();
builder.Services.AddScoped<ITesseractOcrService, TesseractOcrService>();
builder.Services.AddScoped<IDocumentTextExtractor, DocumentTextExtractor>();
builder.Services.AddScoped<IFixedChunkingService, FixedChunkingService>();
builder.Services.AddScoped<ISemanticChunkingService, SemanticChunkingService>();
builder.Services.AddScoped<ITeacherDocumentWorkflowService, TeacherDocumentWorkflowService>();
builder.Services.AddHttpClient<IOpenRouterEmbeddingService, OpenRouterEmbeddingService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RagIngestionOptions>>().Value;
    client.BaseAddress = new Uri(options.OpenRouter.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IOpenRouterChatCompletionService, OpenRouterChatCompletionService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RagIngestionOptions>>().Value;
    client.BaseAddress = new Uri(options.OpenRouter.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IStripePaymentService, StripePaymentService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StripeOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IChunkEmbeddingStore, PostgresChunkEmbeddingStore>();
builder.Services.AddScoped<IStudentChunkRetrievalService, StudentChunkRetrievalService>();
builder.Services.AddScoped<IStudentChatService, StudentChatService>();
builder.Services.AddScoped<IEmbeddingConfigurationService, EmbeddingConfigurationService>();
builder.Services.AddScoped<IEmbeddingBenchmarkService, EmbeddingBenchmarkService>();
builder.Services.AddScoped<AccountCookieAuthenticationEvents>();
builder.Services.AddHostedService<AdminAccountInitializationHostedService>();
builder.Services.AddSignalR();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = AccountNavigation.LoginPath;
        options.AccessDeniedPath = AccountNavigation.LoginPath;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.EventsType = typeof(AccountCookieAuthenticationEvents);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("TeacherOrAdmin", policy => policy.RequireRole("teacher", "admin"));
    options.AddPolicy("StudentOrAdmin", policy => policy.RequireRole("student", "admin"));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Login", "");
    options.Conventions.AuthorizePage("/AdminDashboard", "AdminOnly");
    options.Conventions.AuthorizePage("/Accounts", "AdminOnly");
    options.Conventions.AuthorizePage("/Subjects", "AdminOnly");
    options.Conventions.AuthorizeFolder("/Subjects", "AdminOnly");
    options.Conventions.AuthorizePage("/Analysis", "AdminOnly");
    options.Conventions.AuthorizePage("/ChangePassword");
    options.Conventions.AuthorizePage("/TeacherHome", "TeacherOrAdmin");
    options.Conventions.AuthorizePage("/TeacherUpload", "TeacherOrAdmin");
    options.Conventions.AuthorizePage("/TeacherDocuments", "TeacherOrAdmin");
    options.Conventions.AuthorizePage("/TeacherDocumentDetails", "TeacherOrAdmin");
    options.Conventions.AuthorizePage("/StudentDashboard", "StudentOrAdmin");
    options.Conventions.AuthorizePage("/StudentPlans", "StudentOrAdmin");
    options.Conventions.AuthorizePage("/SubscriptionPlans", "AdminOnly");
    options.Conventions.AuthorizePage("/EmbeddingSettings", "AdminOnly");
    options.Conventions.AuthorizePage("/EmbeddingBenchmark", "AdminOnly");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.Use(async (context, next) =>
{
    if (!HttpMethods.IsGet(context.Request.Method))
    {
        await next();
        return;
    }

    var endpoint = context.GetEndpoint();
    var isProtectedPage = endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>()?.Count > 0;
    var isAuthPage = context.Request.Path.Equals(AccountNavigation.LoginPath, StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.Equals(AccountNavigation.LogoutPath, StringComparison.OrdinalIgnoreCase);

    if (!isProtectedPage && !isAuthPage)
    {
        await next();
        return;
    }

    context.Response.OnStarting(static state =>
    {
        var response = (HttpResponse)state;
        response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
        return Task.CompletedTask;
    }, context.Response);

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapAdminAccountApiEndpoints();
app.MapTeacherSubjectApiEndpoints();
app.MapStudentChatApiEndpoints();
app.MapHub<TeacherHeaderSubjectHub>("/hubs/teacher-header-subjects");
app.MapHub<StudentChatHub>("/hubs/student-chat");

app.Run();

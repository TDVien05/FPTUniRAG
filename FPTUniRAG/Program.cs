using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.Endpoints;
using FPTUniRAG.Hubs;
using FPTUniRAG.Options;
using FPTUniRAG.Services;
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

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<RagIngestionOptions>(builder.Configuration.GetSection("RagIngestion"));
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
builder.Services.AddHttpClient<IChunkEmbeddingStore, QdrantChunkEmbeddingStore>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RagIngestionOptions>>().Value;
    client.BaseAddress = new Uri(options.Qdrant.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(2);
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
});
builder.Services.AddScoped<AccountCookieAuthenticationEvents>();
builder.Services.AddHostedService<AdminAccountInitializationHostedService>();
builder.Services.AddSignalR();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
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

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapAdminAccountApiEndpoints();
app.MapTeacherSubjectApiEndpoints();
app.MapHub<TeacherHeaderSubjectHub>("/hubs/teacher-header-subjects");

app.Run();

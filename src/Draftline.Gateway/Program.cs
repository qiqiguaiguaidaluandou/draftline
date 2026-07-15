using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Draftline.Core.Contracts;
using Draftline.Gateway.AntiCorruption;
using Draftline.Gateway.Auth;
using Draftline.Gateway.ClickOnce;
using Draftline.Gateway.Components;
using Draftline.Gateway.Endpoints;
using Draftline.Gateway.Stores;
using Draftline.Gateway;

var builder = WebApplication.CreateBuilder(args);

// 支持本地私密配置覆盖（不提交 git）
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// JSON：枚举走字符串（FlowType=Pricing/DrawingSelection），web 默认 camelCase。客户端 HttpJson 用同一套。
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------- 数据库 (PostgreSQL) ----------
var conn = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<DraftlineDbContext>(options => options.UseNpgsql(conn));

// ---------- 选项（来自 appsettings） ----------
var configOptions = new ConfigStoreOptions();
builder.Configuration.GetSection("Config").Bind(configOptions);
builder.Services.AddSingleton(configOptions);

var storageOptions = new ServerStorageOptions();
builder.Configuration.GetSection("Storage").Bind(storageOptions);
builder.Services.AddSingleton(storageOptions);

// 客户端 ClickOnce 发布物托管（后端即分发点，免装 nginx）。
var clickOnceOptions = new ClickOnceOptions();
builder.Configuration.GetSection("ClickOnce").Bind(clickOnceOptions);
builder.Services.AddSingleton(clickOnceOptions);

// ---------- 认证/授权（本地凭证，管理员维护） ----------
var jwtOptions = new JwtOptions();
builder.Configuration.GetSection("Jwt").Bind(jwtOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordService, PasswordService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAuthService, DbAuthService>();
builder.Services.AddScoped<IAdminService, AdminService>();

// ---------- 管理后台（Blazor Server + Cookie 鉴权，仅管理员） ----------
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication("AdminCookie")
    .AddCookie("AdminCookie", o =>
    {
        o.Cookie.Name = "draftline_admin";
        o.LoginPath = "/admin/login";
        o.AccessDeniedPath = "/admin/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy("AdminOnly", p => p.RequireClaim("draftline:isAdmin", "true")));

// ---------- 字段提供者 ----------
builder.Services.AddSingleton<IFieldProvider, ServerFieldProvider>();

// ---------- 存储服务 ----------
builder.Services.AddSingleton<IServerBatchStore, FileServerBatchStore>();
builder.Services.AddSingleton<IConfigStore, InMemoryConfigStore>();

// ---------- 后台采集服务（按计划主动取数） ----------
builder.Services.AddHostedService<DataIngestionService>();

// ---------- 防腐层接缝（均为真实接口，无假数据回退） ----------
// EBS 取数（EbsPlmSource）+ PLM 富化（变更状态 + 图纸下载），鉴权复用 EbsTokenProvider。
// URL 留空则对应调用在运行时报错并按计划重采；不会造假数据。
var ebsOptions = new EbsOptions();
builder.Configuration.GetSection("Ebs").Bind(ebsOptions);
builder.Services.AddSingleton(ebsOptions);
builder.Services.AddSingleton<EbsTokenProvider>();

var plmOptions = new PlmOptions();
builder.Configuration.GetSection("Plm").Bind(plmOptions);
builder.Services.AddSingleton(plmOptions);
builder.Services.AddHttpClient<PlmClient>();
builder.Services.AddHttpClient<IEbsPlmSource, EbsPlmSource>();

// 回传：核价价格 → SRM，挑图机加结果 → EBS（CUX_AI_MACH_DRW_RST）。同一 RemoteSubmitSink 按流程分派，
// 鉴权都复用 EBS 的 JWT。URL 留空则对应回传在运行时报错、批次不置 Done、可重试（不造假）。
var srmOptions = new SrmOptions();
builder.Configuration.GetSection("Srm").Bind(srmOptions);
builder.Services.AddSingleton(srmOptions);
builder.Services.AddHttpClient<ISubmitSink, RemoteSubmitSink>();

var app = builder.Build();

// 启动种子：仅当用户表为空且配置了 Admin 段时，建第一个管理员（引导入口）。
await SeedBootstrapAdminAsync(app);

app.UseStaticFiles();
app.UseClickOnceDistribution(clickOnceOptions);   // 客户端安装/更新包，公开可下（鉴权之前）
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapDraftlineApi();
app.MapAdminAuthEndpoints();                 // /admin/login、/admin/logout（Cookie 登入登出）
app.MapRazorComponents<App>()                // /admin/* 管理后台页面
   .AddInteractiveServerRenderMode();

app.Run();

// 引导管理员：避免"有库无人能登录"。生产请在 appsettings.local.json 配 Admin 段并改默认口令。
static async Task SeedBootstrapAdminAsync(WebApplication app)
{
    var section = app.Configuration.GetSection("Admin");
    var empId = section["EmployeeId"];
    var password = section["Password"];
    if (string.IsNullOrWhiteSpace(empId) || string.IsNullOrWhiteSpace(password))
        return; // 未配置则不种子

    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("BootstrapAdmin");
    try
    {
        var db = sp.GetRequiredService<DraftlineDbContext>();
        if (!await db.Database.CanConnectAsync())
        {
            logger.LogWarning("种子管理员跳过：数据库不可达。");
            return;
        }
        if (await db.AppUsers.AnyAsync())
            return; // 已有用户，不再种子

        var pwd = sp.GetRequiredService<IPasswordService>();
        db.AppUsers.Add(new AppUser
        {
            EmployeeId = empId.Trim(),
            DisplayName = section["DisplayName"] ?? "系统管理员",
            Department = section["Department"],
            Position = section["Position"],
            PasswordHash = pwd.Hash(password),
            IsActive = true,
            IsAdmin = true,
            MustChangePassword = true, // 首登强制改默认口令
        });
        await db.SaveChangesAsync();
        logger.LogInformation("已创建引导管理员 {EmployeeId}（首登须改密）。", empId);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "种子管理员失败（可能尚未应用迁移），可稍后手动创建。");
    }
}

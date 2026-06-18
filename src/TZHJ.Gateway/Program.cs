using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TZHJ.Core.Contracts;
using TZHJ.Gateway.AntiCorruption;
using TZHJ.Gateway.Auth;
using TZHJ.Gateway.Endpoints;
using TZHJ.Gateway.Stores;
using TZHJ.Gateway;

var builder = WebApplication.CreateBuilder(args);

// 支持本地私密配置覆盖（不提交 git）
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// JSON：枚举走字符串（FlowType=Pricing/DrawingSelection），web 默认 camelCase。客户端 HttpJson 用同一套。
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------- 数据库 (PostgreSQL) ----------
var conn = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<TzhjDbContext>(options => options.UseNpgsql(conn));

// ---------- 选项（来自 appsettings） ----------
var fake = new FakeOptions();
builder.Configuration.GetSection("Fake").Bind(fake);
builder.Services.AddSingleton(fake);

var configOptions = new ConfigStoreOptions();
builder.Configuration.GetSection("Config").Bind(configOptions);
builder.Services.AddSingleton(configOptions);

var storageOptions = new ServerStorageOptions();
builder.Configuration.GetSection("Storage").Bind(storageOptions);
builder.Services.AddSingleton(storageOptions);

// ---------- 认证/授权（占位） ----------
builder.Services.AddSingleton<ITokenService, FakeTokenService>();
builder.Services.AddSingleton<IAuthService, FakeAuthService>();

// ---------- 字段提供者 ----------
builder.Services.AddSingleton<IFieldProvider, ServerFieldProvider>();

// ---------- 存储服务 ----------
builder.Services.AddSingleton<IServerBatchStore, FileServerBatchStore>();
builder.Services.AddSingleton<IConfigStore, InMemoryConfigStore>();
builder.Services.AddScoped<IAuditStore, PgAuditStore>();
builder.Services.AddScoped<IOperationLogStore, PgOperationLogStore>();

// ---------- 后台采集服务 (模拟主动获取) ----------
builder.Services.AddHostedService<DataIngestionService>();

// ---------- 防腐层接缝 ----------
builder.Services.AddSingleton<FakeDataSource>();
builder.Services.AddSingleton<IEbsPlmSource>(sp => sp.GetRequiredService<FakeDataSource>());
builder.Services.AddSingleton<ISubmitSink>(sp => sp.GetRequiredService<FakeDataSource>());

var app = builder.Build();

app.MapTzhjApi();

app.Run();

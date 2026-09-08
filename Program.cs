using ExamSystem;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 数据库（可用环境变量 EXAM_DB 覆盖，便于隔离测试）
var connString = Environment.GetEnvironmentVariable("EXAM_DB")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=exam.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connString));

// 服务
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<IGradingService, GradingService>();
builder.Services.AddScoped<IRankingService, RankingService>();
builder.Services.AddScoped<IUserService, UserService>();

// JWT
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "ExamSystem-Dev-Secret-Change-Me-2026";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ExamSystem",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ExamSystemClient",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
        // SignalR 使用 URL 查询字符串携带 token
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var access = ctx.Request.Query["access_token"];
                var path = ctx.Request.Path;
                if (!string.IsNullOrEmpty(access) && path.StartsWithSegments("/hubs/ranking"))
                    ctx.Token = access;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddControllers()
    .AddJsonOptions(o => {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS（手机浏览器 + SignalR 跨域）
builder.Services.AddCors(o => o.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// 监听所有网卡，方便手机浏览器在局域网内访问
builder.WebHost.UseUrls("http://0.0.0.0:5000");

var app = builder.Build();

// 中间件
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");

// 静态文件（前端页面）
app.UseDefaultFiles();
app.UseStaticFiles();

// 管理后台：按 User-Agent 返回桌面版或移动版
app.MapGet("/admin", async (HttpContext ctx, IWebHostEnvironment env) =>
{
    var ua = ctx.Request.Headers.UserAgent.ToString();
    var file = IsMobile(ua) ? "admin-mobile.html" : "admin-desktop.html";
    var path = Path.Combine(env.WebRootPath!, file);
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(path);
});

// API 未匹配时回退到 index.html（前端路由）
app.MapFallbackToFile("index.html");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RankingHub>("/hubs/ranking");

// 建库 + 种子数据
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    EnsureColumn(db, "Users", "JobNo", "TEXT");
    EnsureColumn(db, "Users", "Department", "TEXT");
    EnsureColumn(db, "Exams", "CandidatePassword", "TEXT");
    EnsureColumn(db, "Exams", "PaperMode", "INTEGER NOT NULL DEFAULT 0");
    EnsureColumn(db, "ExamQuestions", "SessionId", "INTEGER");
    Seed(db);
}

app.Run();

/// <summary>判断是否为移动端浏览器（用于管理后台按 UA 分流）。</summary>
static bool IsMobile(string userAgent)
{
    if (string.IsNullOrEmpty(userAgent)) return false;
    var ua = userAgent.ToLowerInvariant();
    return ua.Contains("android") || ua.Contains("iphone") || ua.Contains("ipad")
        || ua.Contains("ipod") || ua.Contains("windows phone") || ua.Contains("mobile");
}

/// <summary>SQLite 轻量迁移：列不存在时 ALTER TABLE 补齐（避免旧库因新增字段报错）。</summary>
static void EnsureColumn(AppDbContext db, string table, string column, string type)
{
    var conn = db.Database.GetDbConnection();
    var wasOpen = conn.State == System.Data.ConnectionState.Open;
    if (!wasOpen) conn.Open();
    try
    {
        bool exists = false;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (r.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
        }
        if (!exists)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            alter.ExecuteNonQuery();
        }
    }
    finally
    {
        if (!wasOpen) conn.Close();
    }
}

static void Seed(AppDbContext db)
{
    if (db.Users.Any()) return;

    db.Users.Add(new User { Username = "admin", DisplayName = "管理员", PasswordHash = PasswordHelper.Hash("admin"), Role = UserRole.Admin });
    foreach (var s in new[] { "student1", "student2", "student3" })
        db.Users.Add(new User { Username = s, DisplayName = s, PasswordHash = PasswordHelper.Hash(s), Role = UserRole.Candidate });
    db.SaveChanges();

    var scopes = new[] { "数学", "语文", "英语" };
    var rnd = new Random(2026);
    foreach (var scope in scopes)
    {
        // 单选 6
        for (int i = 1; i <= 6; i++)
            db.Questions.Add(MakeQ(scope, QuestionType.Single, $"【{scope}】单选题 {i}", rnd.Next(0, 4)));
        // 多选 4（答案 2 个字母）
        for (int i = 1; i <= 4; i++)
        {
            int a = rnd.Next(0, 3); int b = rnd.Next(a + 1, 4);
            db.Questions.Add(MakeQ(scope, QuestionType.Multiple, $"【{scope}】多选题 {i}", -1, new[] { a, b }));
        }
        // 判断 5
        for (int i = 1; i <= 5; i++)
            db.Questions.Add(MakeJudge(scope, $"【{scope}】判断题 {i}", rnd.Next(0, 2) == 0));
    }
    db.SaveChanges();
}

static Question MakeQ(string scope, QuestionType type, string content, int answerIdx, int[]? multi = null)
{
    var opts = new[] { "选项 A", "选项 B", "选项 C", "选项 D" };
    string answer;
    if (multi != null) answer = string.Concat(multi.OrderBy(x => x).Select(x => "ABCD"[x]));
    else answer = "ABCD"[answerIdx].ToString();
    return new Question
    {
        Scope1 = scope,
        Type = type,
        Content = content,
        OptionA = opts[0], OptionB = opts[1], OptionC = opts[2], OptionD = opts[3],
        Answer = answer
    };
}

static Question MakeJudge(string scope, string content, bool correct)
{
    return new Question
    {
        Scope1 = scope,
        Type = QuestionType.Judge,
        Content = content,
        OptionA = "正确", OptionB = "错误",
        Answer = correct ? "A" : "B"
    };
}

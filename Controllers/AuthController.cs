using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExamSystem.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJwtService _jwt;
    // 考生（导入自动创建）账号的登录时间窗：考试开始前 N 分钟起、至考试结束止
    private const int LoginWindowMinutes = 10;
    public AuthController(AppDbContext db, IJwtService jwt) => (_db, _jwt) = (db, jwt);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user == null || !PasswordHelper.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "用户名或密码错误" });

        // 登录时间窗门控：导入考生账号（Exam）仅允许在"被指定参加的考试"的时间窗内登录；
        // 用户管理创建（General）与管理员账号无限制。
        // 注意：仅认 TargetMode=Specified 且名单含该考生的考试——全员(All)考试面向通用考生账号，
        // 不应为导入考生打开常驻登录窗口，否则只要有任意全员考试在进行，导入考生就能随时登录，违背限制本意。
        if (user.AccountType == UserAccountType.Exam)
        {
            var now = DateTime.UtcNow;
            var inWindow = await IsWithinAnyExamLoginWindowAsync(user.Id, now);
            if (!inWindow)
                return Unauthorized(new
                {
                    message = $"当前不在考试登录时间范围内（仅限被指定考试的「开始前 {LoginWindowMinutes} 分钟 ~ 考试结束」），无法登录。"
                });
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _jwt.GenerateToken(user);
        return Ok(new LoginResponse
        {
            Token = token,
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            IsAdmin = user.Role == UserRole.Admin
        });
    }

    /// <summary>该考生是否有任意一场「指定人员」考试（且其为被指认定名单）处于登录时间窗 [开始-10min, 结束] 内。无明确起止时间的考试视为放行。</summary>
    private async Task<bool> IsWithinAnyExamLoginWindowAsync(int userId, DateTime now)
    {
        var exams = await _db.Exams
            .Where(e => e.Status == ExamStatus.Published && e.TargetMode == TargetMode.Specified)
            .Where(e => e.TargetUserIds != null && ("," + e.TargetUserIds + ",").Contains("," + userId + ","))
            .ToListAsync();
        foreach (var e in exams)
        {
            if (e.StartTime == null || e.EndTime == null) return true;
            var openAt = e.StartTime.Value.AddMinutes(-LoginWindowMinutes);
            if (now >= openAt && now <= e.EndTime.Value) return true;
        }
        return false;
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var id = int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FindAsync(id);
        if (user == null) return Unauthorized();
        return Ok(new LoginResponse
        {
            Token = "",
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            IsAdmin = user.Role == UserRole.Admin
        });
    }

    // 登录页提示：返回最近一场指定人员考试的统一登录密码（匿名可访问）
    [HttpGet("candidate-hint")]
    [AllowAnonymous]
    public async Task<IActionResult> CandidateHint()
    {
        var exam = await _db.Exams
            .Where(e => e.Status == ExamStatus.Published && e.CandidatePassword != null)
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync();
        if (exam == null)
            return Ok(new { password = (string?)null, title = (string?)null, message = "暂无进行中的指定人员考试" });
        return Ok(new { password = exam.CandidatePassword, title = exam.Title, message = $"用户名 = 工号，密码 = {exam.CandidatePassword}" });
    }

    // 当前用户改密码（任何已登录用户）
    [HttpPut("me/password")]
    [Authorize]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.OldPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { message = "旧密码和新密码不能为空" });
        var id = int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FindAsync(id);
        if (user == null) return Unauthorized();
        if (!PasswordHelper.Verify(req.OldPassword, user.PasswordHash))
            return BadRequest(new { message = "旧密码错误" });
        user.PasswordHash = PasswordHelper.Hash(req.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

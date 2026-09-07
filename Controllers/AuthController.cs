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
    public AuthController(AppDbContext db, IJwtService jwt) => (_db, _jwt) = (db, jwt);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user == null || !PasswordHelper.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "用户名或密码错误" });

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

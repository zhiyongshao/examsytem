using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExamSystem.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUserService _userSvc;
    public UsersController(AppDbContext db, IUserService userSvc)
    {
        _db = db;
        _userSvc = userSvc;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);

    // 管理员列出用户
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var list = await _db.Users.OrderBy(u => u.Role).ThenBy(u => u.Id)
            .Select(u => new UserBrief
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                Role = u.Role.ToString(),
                JobNo = u.JobNo,
                Department = u.Department,
                CreatedAt = u.CreatedAt
            }).ToListAsync();
        return Ok(list);
    }

    // 新增用户
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "用户名和密码不能为空" });
        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new { message = "用户名已存在" });

        var user = new User
        {
            Username = req.Username,
            PasswordHash = PasswordHelper.Hash(req.Password),
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? req.Username : req.DisplayName,
            Role = req.Role
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Ok(new UserBrief { Id = user.Id, Username = user.Username, DisplayName = user.DisplayName, Role = user.Role.ToString() });
    }

    // 修改用户信息（姓名/工号/部门/角色）
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { message = "用户不存在" });

        // 不允许把最后一位管理员降级
        if (req.Role == UserRole.Candidate && user.Role == UserRole.Admin)
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == UserRole.Admin);
            if (adminCount <= 1) return BadRequest(new { message = "至少保留一位管理员" });
        }

        if (req.DisplayName != null) user.DisplayName = req.DisplayName;
        if (req.JobNo != null) user.JobNo = req.JobNo;
        if (req.Department != null) user.Department = req.Department;
        if (req.Role.HasValue) user.Role = req.Role.Value;

        await _db.SaveChangesAsync();
        return Ok(new UserBrief
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            JobNo = user.JobNo,
            Department = user.Department,
            CreatedAt = user.CreatedAt
        });
    }

    // 删除用户
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (id == CurrentUserId) return BadRequest(new { message = "不能删除自己" });
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { message = "用户不存在" });

        // 不允许删除最后一位管理员
        if (user.Role == UserRole.Admin)
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == UserRole.Admin);
            if (adminCount <= 1) return BadRequest(new { message = "至少保留一位管理员" });
        }

        // 阻止删除已经被考试引用的考生
        var usedByExam = await _db.Exams.AnyAsync(e => e.TargetUserIds != null && e.TargetUserIds.Contains(id.ToString()));
        if (usedByExam) return BadRequest(new { message = "该用户已被考试引用，不能删除" });

        var hasSession = await _db.ExamSessions.AnyAsync(s => s.UserId == id);
        if (hasSession) return BadRequest(new { message = "该用户已有考试记录，不能删除" });

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // 管理员重置任意用户密码
    [HttpPut("{id}/password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { message = "新密码不能为空" });
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { message = "用户不存在" });
        user.PasswordHash = PasswordHelper.Hash(req.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // 导入考生名单（姓名/工号/部门），自动创建考生账号
    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "请上传文件" });
        await using var stream = file.OpenReadStream();
        var isExcel = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                   || file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);
        var result = await _userSvc.ImportCandidatesAsync(stream, isExcel);
        return Ok(result);
    }

    // 下载考生名单模板
    [HttpGet("import-template")]
    public IActionResult ImportTemplate()
    {
        var bytes = _userSvc.BuildCandidateTemplateXlsx();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "考生名单模板.xlsx");
    }
}

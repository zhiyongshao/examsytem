using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ExamSystem.Controllers;

[ApiController]
[Route("api/exams")]
[Authorize(Roles = "Admin")]
public class ExamsController : ControllerBase
{
    private readonly IExamService _svc;
    public ExamsController(IExamService svc) => _svc = svc;

    // 管理员：考试列表
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _svc.ListAsync());

    // 管理员：考试详情（含二维矩阵）
    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(int id)
    {
        var d = await _svc.DetailAsync(id);
        return d == null ? NotFound() : Ok(d);
    }

    // 管理员：发布考试（二维选题）
    [HttpPost]
    public async Task<IActionResult> Publish([FromBody] PublishExamRequest req)
    {
        try
        {
            var adminId = int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
            var dto = await _svc.PublishAsync(req, adminId);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // 管理员：关闭考试
    [HttpPost("{id}/close")]
    public async Task<IActionResult> Close(int id)
        => await _svc.CloseAsync(id) ? Ok(new { success = true }) : NotFound();
}

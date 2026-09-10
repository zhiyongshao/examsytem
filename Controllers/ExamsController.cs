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

    // 管理员：考试入口信息（分享给考生）
    [HttpGet("{id}/entrance")]
    public async Task<IActionResult> Entrance(int id)
    {
        var d = await _svc.GetEntranceAsync(id);
        return d == null ? NotFound() : Ok(d);
    }

    // 匿名：按考试独立入口短链取公开信息（供登录页/倒计时页使用，不含题目）
    [HttpGet("entry/{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> EntryBySlug(string slug)
    {
        var d = await _svc.GetEntryBySlugAsync(slug);
        return d == null ? NotFound() : Ok(d);
    }

    // 管理员：实时监控（监考看板）—— 候选人的登录/作答状态、分数与排名
    [HttpGet("{id}/monitor")]
    public async Task<IActionResult> Monitor(int id)
    {
        var d = await _svc.GetMonitorAsync(id);
        return d == null ? NotFound() : Ok(d);
    }

    // 管理员：修改考试
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateExamRequest req)
    {
        try
        {
            var d = await _svc.UpdateAsync(id, req);
            return d == null ? NotFound(new { message = "考试不存在" }) : Ok(d);
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

    // 管理员：删除考试前预览关联数据（考生数、作答记录数）
    [HttpGet("{id}/delete-preview")]
    public async Task<IActionResult> DeletePreview(int id)
    {
        var d = await _svc.GetDeletePreviewAsync(id);
        return d == null ? NotFound(new { message = "考试不存在" }) : Ok(d);
    }

    // 管理员：删除考试（可级联清除作答记录 / 仅本场引用的考生）
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, [FromBody] DeleteExamRequest? req)
    {
        var cascadeSessions = req?.CascadeSessions ?? false;
        var cascadeUsers = req?.CascadeUsers ?? false;
        try
        {
            return await _svc.DeleteAsync(id, cascadeSessions, cascadeUsers)
                ? Ok(new { success = true })
                : NotFound(new { message = "考试不存在" });
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

    // 管理员：基于现有考试重新发布（克隆+重新抽题）
    [HttpPost("{id}/republish")]
    public async Task<IActionResult> Republish(int id)
    {
        try
        {
            var adminId = int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
            var dto = await _svc.RepublishAsync(id, adminId);
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
}

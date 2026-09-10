using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamSystem.Controllers;

[ApiController]
[Route("api/questions")]
[Authorize(Roles = "Admin")]
public class QuestionsController : ControllerBase
{
    private readonly IQuestionService _svc;
    public QuestionsController(IQuestionService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List(string? scope, string? type, string? keyword, int page = 1, int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;
        var (items, total) = await _svc.ListAsync(scope, type, keyword, page, pageSize);
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var q = await _svc.GetAsync(id);
        return q == null ? NotFound() : Ok(q);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] QuestionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Scope1) || string.IsNullOrWhiteSpace(req.Content))
            return BadRequest(new { message = "知识范围1 与 题干 为必填" });
        var dto = await _svc.CreateAsync(req);
        return Ok(dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] QuestionRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? Ok(new { success = true }) : NotFound();

    public record BatchDeleteRequest(List<int> Ids);

    [HttpPost("batch-delete")]
    public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0)
            return BadRequest(new { message = "未选择任何题目" });
        var removed = await _svc.DeleteManyAsync(req.Ids);
        return Ok(new { success = true, removed });
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "请上传文件" });
        await using var stream = file.OpenReadStream();
        var isExcel = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                   || file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);
        var result = isExcel ? await _svc.ImportExcelAsync(stream) : await _svc.ImportCsvAsync(stream);
        return Ok(result);
    }

    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = _svc.BuildTemplateXlsx();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "题目导入模板.xlsx");
    }
}

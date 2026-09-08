using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamSystem.Controllers;

[ApiController]
[Route("api/history")]
[Authorize(Roles = "Admin")]
public class HistoryController : ControllerBase
{
    private readonly AppDbContext _db;
    public HistoryController(AppDbContext db) => _db = db;

    // 历史考试概览
    [HttpGet("exams")]
    public async Task<IActionResult> Exams()
    {
        var exams = await _db.Exams.Include(e => e.Questions).Include(e => e.Questions).ToListAsync();
        var sessions = await _db.ExamSessions
            .Where(s => s.Status == SessionStatus.Graded)
            .GroupBy(s => s.ExamId)
            .Select(g => new { ExamId = g.Key, Scores = g.Select(s => s.Score).ToList() })
            .ToDictionaryAsync(g => g.ExamId, g => g.Scores);

        var result = exams.Select(e =>
        {
            var scores = sessions.GetValueOrDefault(e.Id, new List<double>());
            var total = e.TotalScore;
            double passRate = 0;
            if (scores.Count > 0 && total > 0)
                passRate = scores.Count(s => s >= total * 0.6) * 100.0 / scores.Count;
            return new HistoryExamDto
            {
                Id = e.Id,
                Title = e.Title,
                Status = e.Status.ToString(),
                TotalScore = total,
                ParticipantCount = scores.Count,
                AverageScore = scores.Count > 0 ? Math.Round(scores.Average(), 2) : 0,
                MaxScore = scores.Count > 0 ? scores.Max() : 0,
                PassRate = Math.Round(passRate, 1),
                CreatedAt = e.CreatedAt
            };
        }).OrderByDescending(e => e.CreatedAt).ToList();

        return Ok(result);
    }

    // 某场考试成绩明细
    [HttpGet("exams/{id}/results")]
    public async Task<IActionResult> Results(int id)
    {
        var exam = await _db.Exams.Include(e => e.Questions).FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound();
        var total = exam.TotalScore;

        var sessions = await _db.ExamSessions
            .Where(s => s.ExamId == id && s.Status == SessionStatus.Graded)
            .Include(s => s.Answers).ToListAsync();
        var users = await _db.Users.ToDictionaryAsync(u => u.Id);

        var rows = sessions.Select(s => new ResultRow
        {
            DisplayName = users.GetValueOrDefault(s.UserId)?.DisplayName ?? "(未知)",
            Username = users.GetValueOrDefault(s.UserId)?.Username ?? "",
            Score = Math.Round(s.Score, 2),
            TotalScore = total,
            CorrectCount = s.Answers.Count(a => a.IsCorrect),
            TotalCount = s.Answers.Count,
            DurationSeconds = s.SubmitTime.HasValue ? Math.Round((s.SubmitTime.Value - s.StartTime).TotalSeconds) : 0,
            Result = total > 0 && s.Score >= total * 0.6 ? "合格" : "不合格",
            SubmitTime = s.SubmitTime ?? s.StartTime
        }).OrderByDescending(r => r.Score).ThenBy(r => r.DurationSeconds).ToList();

        for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
        return Ok(rows);
    }

    // 导出成绩 CSV
    [HttpGet("exams/{id}/export")]
    public async Task<IActionResult> Export(int id)
    {
        var rows = (await Results(id) as OkObjectResult)?.Value as List<ResultRow> ?? new();
        var exam = await _db.Exams.FindAsync(id);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("排名,姓名,用户名,得分,总分,答对题数,总题数,用时(秒),结果,交卷时间");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                r.Rank, Escape(r.DisplayName), Escape(r.Username),
                r.Score, r.TotalScore, r.CorrectCount, r.TotalCount,
                r.DurationSeconds, r.Result, r.SubmitTime.ToString("yyyy-MM-dd HH:mm:ss")));
        }
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fileName = $"成绩_{exam?.Title ?? id.ToString()}_{DateTime.Now:yyyyMMdd}.csv";
        return File(bytes, "text/csv", fileName);
    }

    private static string Escape(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
}

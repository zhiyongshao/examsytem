using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExamSystem.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = "Candidate")]
public class ExamSessionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IExamService _examSvc;
    private readonly IGradingService _grading;
    private readonly IRankingService _ranking;
    public ExamSessionController(AppDbContext db, IExamService examSvc, IGradingService grading, IRankingService ranking)
        => (_db, _examSvc, _grading, _ranking) = (db, examSvc, grading, ranking);

    private int CurrentUserId => int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);

    // 考生：可参加的考试
    [HttpGet("exams/available")]
    public async Task<IActionResult> Available() => Ok(await _examSvc.AvailableAsync(CurrentUserId));

    // 考生：开始考试（返回题目，不含答案）
    [HttpPost("exams/{id}/start")]
    public async Task<IActionResult> Start(int id)
    {
        var exam = await _db.Exams.Include(e => e.Questions).ThenInclude(q => q.Question)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound(new { message = "考试不存在" });
        if (exam.Status != ExamStatus.Published)
            return BadRequest(new { message = "考试未发布或已关闭" });

        // 有效期门控：训练考试不受时间窗限制（随时可刷）
        if (!exam.IsTraining)
        {
            var now = DateTime.UtcNow;
            if (exam.StartTime != null && now < exam.StartTime)
                return BadRequest(new { message = "考试未开始，开始时间为 " + exam.StartTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") });
            if (exam.EndTime != null && now >= exam.EndTime)
                return BadRequest(new { message = "考试已结束，无法进入" });
        }

        // 权限：指定人员
        if (exam.TargetMode == TargetMode.Specified)
        {
            var ids = (exam.TargetUserIds ?? "").Split(',').Where(x => int.TryParse(x, out _)).Select(int.Parse).ToHashSet();
            if (!ids.Contains(CurrentUserId))
                return Forbid();
        }

        // 训练考试：每次都开新会话（拿到独立随机卷），支持并发公共账号与反复刷；
        // 普通考试：复用进行中的答卷，避免重复开卷
        ExamSession? session;
        if (exam.IsTraining)
        {
            session = new ExamSession { ExamId = id, UserId = CurrentUserId, StartTime = DateTime.UtcNow };
            _db.ExamSessions.Add(session);
            await _db.SaveChangesAsync();
        }
        else
        {
            session = await _db.ExamSessions
                .FirstOrDefaultAsync(s => s.ExamId == id && s.UserId == CurrentUserId && s.Status == SessionStatus.InProgress);
            if (session == null)
            {
                session = new ExamSession { ExamId = id, UserId = CurrentUserId, StartTime = DateTime.UtcNow };
                _db.ExamSessions.Add(session);
                await _db.SaveChangesAsync();
            }
        }

        List<ClientQuestion> questions;
        if (exam.PaperMode == ExamPaperMode.PerCandidate)
        {
            // 按考生随机卷：该会话尚未抽题则独立抽一份并存库；已抽则复用（幂等）
            var existing = await _db.ExamQuestions
                .Where(q => q.SessionId == session.Id)
                .OrderBy(q => q.Order)
                .Include(q => q.Question)
                .ToListAsync();
            List<ExamQuestion> paper;
            if (existing.Count > 0)
            {
                paper = existing;
            }
            else
            {
                paper = await _examSvc.DrawSessionPaperAsync(exam.Id, session.Id);
                paper = await _db.ExamQuestions
                    .Where(q => q.SessionId == session.Id)
                    .OrderBy(q => q.Order)
                    .Include(q => q.Question)
                    .ToListAsync();
            }
            questions = paper.Select(q => ToClientQuestion(q)).ToList();
        }
        else
        {
            questions = exam.Questions.OrderBy(q => q.Order)
                .Select(q => ToClientQuestion(q)).ToList();
        }

        // 截止时间：训练考试以"开始+时长"为限（可反复刷、到点自动交）；普通考试以考试结束时间为准
        var endTime = exam.IsTraining
            ? session.StartTime.AddMinutes(exam.DurationMinutes)
            : (exam.EndTime ?? session.StartTime.AddMinutes(exam.DurationMinutes));
        return Ok(new StartExamResponse
        {
            SessionId = session.Id,
            ExamId = exam.Id,
            Title = exam.Title,
            DurationMinutes = exam.DurationMinutes,
            StartTime = session.StartTime,
            EndTime = endTime,
            TotalScore = exam.TotalScore,
            Questions = questions
        });
    }

    // 考生：当前答卷状态（剩余时间）
    [HttpGet("exams/{id}/status")]
    public async Task<IActionResult> Status(int id)
    {
        var exam = await _db.Exams.FindAsync(id);
        if (exam == null) return NotFound();
        var session = await _db.ExamSessions
            .FirstOrDefaultAsync(s => s.ExamId == id && s.UserId == CurrentUserId);
        if (session == null) return Ok(new { inProgress = false });
        var deadline = exam.EndTime ?? session.StartTime.AddMinutes(exam.DurationMinutes);
        var remaining = (deadline - DateTime.UtcNow).TotalSeconds;
        return Ok(new
        {
            inProgress = session.Status == SessionStatus.InProgress,
            sessionId = session.Id,
            remainingSeconds = Math.Max(0, (int)remaining),
            status = session.Status.ToString()
        });
    }

    // 考生：交卷（提前或时间到），实时判分并广播排名
    [HttpPost("exams/{id}/submit")]
    public async Task<IActionResult> Submit(int id, [FromBody] SubmitRequest req)
    {
        try
        {
            var result = await _grading.GradeAsync(id, CurrentUserId, req.SessionId, req.Answers, req.Early);
            await _ranking.BroadcastAsync(id); // 实时广播排名
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // 考生：查看考卷（提交/强制提交后，显示题目、考生答案、正确答案，错题标识）
    // sessionId 可选：提供时精确回看该次答卷（共享账号/并发下保证是"自己刚答的那份"）；
    // 未提供则回退到该用户最近一次提交（向后兼容普通考试）。
    [HttpGet("exams/{id}/review")]
    public async Task<IActionResult> Review(int id, [FromQuery] int? sessionId = null)
    {
        ExamSession? session;
        if (sessionId.HasValue && sessionId.Value > 0)
        {
            session = await _db.ExamSessions
                .Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.ExamId == id && s.UserId == CurrentUserId && s.Status == SessionStatus.Graded);
        }
        else
        {
            session = await _db.ExamSessions
                .Include(s => s.Answers)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(s => s.ExamId == id && s.UserId == CurrentUserId && s.Status == SessionStatus.Graded);
        }
        if (session == null)
            return BadRequest(new { message = "尚未提交答卷，无法查看考卷" });

        var exam = await _db.Exams.FindAsync(id);

        // 试卷题目与分值：固定卷用考试级题集，随机卷用本会话独立题集
        List<ExamQuestion> eqList;
        if (exam != null && exam.PaperMode == ExamPaperMode.PerCandidate)
            eqList = await _db.ExamQuestions
                .Where(q => q.SessionId == session.Id)
                .OrderBy(q => q.Order)
                .Include(q => q.Question)
                .ToListAsync();
        else
            eqList = await _db.ExamQuestions
                .Where(q => q.ExamId == id && q.SessionId == null)
                .OrderBy(q => q.Order)
                .Include(q => q.Question)
                .ToListAsync();

        var ansById = session.Answers.ToDictionary(a => a.QuestionId);
        var list = eqList.Select(eq =>
        {
            var q = eq.Question!;
            var opts = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(q.OptionA)) opts["A"] = q.OptionA;
            if (!string.IsNullOrWhiteSpace(q.OptionB)) opts["B"] = q.OptionB;
            if (!string.IsNullOrWhiteSpace(q.OptionC)) opts["C"] = q.OptionC;
            if (!string.IsNullOrWhiteSpace(q.OptionD)) opts["D"] = q.OptionD;
            ansById.TryGetValue(q.Id, out var a);
            return new ReviewQuestionDto
            {
                Id = q.Id,
                Type = q.Type.ToString(),
                Content = q.Content,
                Options = opts,
                Score = eq.Score,
                Selected = a?.Selected ?? "",
                CorrectAnswer = q.Answer,
                IsCorrect = a?.IsCorrect ?? false,
                ScoreGot = a?.ScoreGot ?? 0
            };
        }).ToList();

        return Ok(new ExamReviewResponse
        {
            ExamId = id,
            SessionId = session.Id,
            Title = exam?.Title ?? "",
            Score = session.Score,
            TotalScore = exam?.TotalScore ?? 0,
            Questions = list
        });
    }

    // 考生/管理员：实时排名
    [HttpGet("exams/{id}/ranking")]
    public async Task<IActionResult> Ranking(int id) => Ok(await _ranking.ComputeAsync(id));

    private static ClientQuestion ToClientQuestion(ExamQuestion eq)
    {
        var q = eq.Question!;
        var opts = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(q.OptionA)) opts["A"] = q.OptionA;
        if (!string.IsNullOrWhiteSpace(q.OptionB)) opts["B"] = q.OptionB;
        if (!string.IsNullOrWhiteSpace(q.OptionC)) opts["C"] = q.OptionC;
        if (!string.IsNullOrWhiteSpace(q.OptionD)) opts["D"] = q.OptionD;
        return new ClientQuestion
        {
            Id = q.Id,
            Type = q.Type.ToString(),
            Content = q.Content,
            Score = eq.Score,
            Options = opts
        };
    }
}

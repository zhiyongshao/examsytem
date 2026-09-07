using Microsoft.EntityFrameworkCore;

namespace ExamSystem;

public interface IExamService
{
    Task<ExamDetailDto> PublishAsync(PublishExamRequest req, int adminId);
    Task<bool> CloseAsync(int examId);
    Task<List<ExamSummaryDto>> ListAsync();
    Task<ExamDetailDto?> DetailAsync(int examId);
    Task<List<ExamSummaryDto>> AvailableAsync(int userId);
}

public class ExamService : IExamService
{
    private readonly AppDbContext _db;
    public ExamService(AppDbContext db) => _db = db;

    public async Task<ExamDetailDto> PublishAsync(PublishExamRequest req, int adminId)
    {
        if (req.Rules == null || req.Rules.Count == 0)
            throw new InvalidOperationException("至少需要一条选题规则");
        if (req.StartTime == null || req.EndTime == null)
            throw new InvalidOperationException("请设置考试的开始时间与结束时间");
        if (req.EndTime <= req.StartTime)
            throw new InvalidOperationException("结束时间需晚于开始时间");
        var durationMinutes = (int)Math.Round((req.EndTime.Value - req.StartTime.Value).TotalMinutes);
        if (durationMinutes < 1) durationMinutes = 1;
        foreach (var rule in req.Rules)
        {
            var hasScope = !string.IsNullOrWhiteSpace(rule.Scope1)
                        || !string.IsNullOrWhiteSpace(rule.Scope2)
                        || !string.IsNullOrWhiteSpace(rule.Scope3);
            if (!hasScope)
                throw new InvalidOperationException("每条选题规则至少需要填写一个知识范围");
        }
        if (req.TargetMode == TargetMode.Specified && (req.TargetUserIds == null || req.TargetUserIds.Count == 0))
            throw new InvalidOperationException("指定人员发布时必须选择考生");

        var exam = new Exam
        {
            Title = req.Title,
            DurationMinutes = durationMinutes,
            CreatedBy = adminId,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            TargetMode = req.TargetMode,
            TargetUserIds = req.TargetMode == TargetMode.Specified
                ? string.Join(",", req.TargetUserIds) : null,
            Status = ExamStatus.Published
        };

        // 指定人员发布：生成统一随机密码，应用到所有指定考生
        if (req.TargetMode == TargetMode.Specified)
        {
            var candidatePassword = PasswordHelper.Random(6);
            exam.CandidatePassword = candidatePassword;
            var targetUsers = await _db.Users.Where(u => req.TargetUserIds.Contains(u.Id)).ToListAsync();
            foreach (var u in targetUsers)
                u.PasswordHash = PasswordHelper.Hash(candidatePassword);
        }

        int order = 0;
        var usedQuestionIds = new HashSet<int>();
        var shortfalls = new List<string>();

        foreach (var rule in req.Rules)
        {
            exam.Rules.Add(new ExamRule
            {
                Scope1 = rule.Scope1,
                Scope2 = rule.Scope2,
                Scope3 = rule.Scope3,
                Type = rule.Type,
                Count = rule.Count,
                ScorePerQuestion = rule.ScorePerQuestion
            });

            // 抽题：规则的三范围任一被题目的任一范围"包含"（子串命中）+ 题型匹配，排除已用
            var scopes = new[] { rule.Scope1, rule.Scope2, rule.Scope3 }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToArray();
            var candidates = await _db.Questions
                .Where(q => scopes.Any(s => (q.Scope1 ?? "").Contains(s)
                                         || (q.Scope2 ?? "").Contains(s)
                                         || (q.Scope3 ?? "").Contains(s))
                            && q.Type == rule.Type
                            && !usedQuestionIds.Contains(q.Id))
                .Select(q => q.Id).ToListAsync();

            if (candidates.Count < rule.Count)
            {
                var scopeLabel = string.Join("/", scopes);
                shortfalls.Add($"范围「{scopeLabel}」+ 题型「{rule.Type}」：需 {rule.Count} 道，题库仅有 {candidates.Count} 道");
                continue;
            }

            // 随机抽 count 道
            var picked = Shuffle(candidates).Take(rule.Count).ToList();
            foreach (var qid in picked)
            {
                usedQuestionIds.Add(qid);
                exam.Questions.Add(new ExamQuestion
                {
                    QuestionId = qid,
                    Score = rule.ScorePerQuestion,
                    Order = order++
                });
            }
        }

        if (shortfalls.Count > 0)
            throw new InvalidOperationException("题库题量不足：\n" + string.Join("\n", shortfalls));

        exam.TotalQuestions = exam.Questions.Count;
        exam.TotalScore = exam.Questions.Sum(q => q.Score);

        _db.Exams.Add(exam);
        await _db.SaveChangesAsync();
        return await DetailAsync(exam.Id) ?? throw new InvalidOperationException("发布失败");
    }

    public async Task<bool> CloseAsync(int examId)
    {
        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return false;
        exam.Status = ExamStatus.Closed;
        exam.EndTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ExamSummaryDto>> ListAsync()
    {
        var exams = await _db.Exams.Include(e => e.Questions).ToListAsync();
        var sums = await _db.ExamSessions
            .GroupBy(s => s.ExamId)
            .Select(g => new { ExamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ExamId, g => g.Count);

        return exams.Select(e => new ExamSummaryDto
        {
            Id = e.Id,
            Title = e.Title,
            DurationMinutes = e.DurationMinutes,
            TotalQuestions = e.TotalQuestions,
            TotalScore = e.TotalScore,
            Status = e.Status.ToString(),
            TargetMode = e.TargetMode.ToString(),
            CreatedAt = e.CreatedAt,
            StartTime = e.StartTime,
            EndTime = e.EndTime,
            ParticipantCount = sums.GetValueOrDefault(e.Id, 0)
        }).OrderByDescending(e => e.CreatedAt).ToList();
    }

    public async Task<ExamDetailDto?> DetailAsync(int examId)
    {
        var exam = await _db.Exams.Include(e => e.Rules).Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == examId);
        if (exam == null) return null;

        var dto = new ExamDetailDto
        {
            Id = exam.Id,
            Title = exam.Title,
            DurationMinutes = exam.DurationMinutes,
            TotalQuestions = exam.TotalQuestions,
            TotalScore = exam.TotalScore,
            Status = exam.Status.ToString(),
            TargetMode = exam.TargetMode.ToString(),
            StartTime = exam.StartTime,
            EndTime = exam.EndTime,
            CandidatePassword = exam.CandidatePassword,
            TargetUserIds = string.IsNullOrWhiteSpace(exam.TargetUserIds)
                ? new() : exam.TargetUserIds.Split(',').Select(int.Parse).ToList(),
            Rules = exam.Rules.Select(r => new MatrixCell
            {
                Scope1 = r.Scope1, Scope2 = r.Scope2, Scope3 = r.Scope3,
                Type = r.Type.ToString(), Count = r.Count,
                ScorePerQuestion = r.ScorePerQuestion,
                SubTotal = r.Count * r.ScorePerQuestion
            }).ToList()
        };
        return dto;
    }

    public async Task<List<ExamSummaryDto>> AvailableAsync(int userId)
    {
        var all = await _db.Exams.Include(e => e.Questions).ToListAsync();
        var submitted = (await _db.ExamSessions
            .Where(s => s.UserId == userId && (s.Status == SessionStatus.Submitted || s.Status == SessionStatus.Graded))
            .Select(s => s.ExamId).ToListAsync()).ToHashSet();

        var result = new List<ExamSummaryDto>();
        var now = DateTime.UtcNow;
        foreach (var e in all.Where(e => e.Status == ExamStatus.Published))
        {
            if (submitted.Contains(e.Id)) continue;
            // 已结束（过了结束时间）→ 不可见 / 无法进入
            if (e.EndTime != null && now >= e.EndTime) continue;
            if (e.TargetMode == TargetMode.Specified)
            {
                var ids = (e.TargetUserIds ?? "").Split(',').Where(x => int.TryParse(x, out _)).Select(int.Parse).ToHashSet();
                if (!ids.Contains(userId)) continue;
            }
            result.Add(new ExamSummaryDto
            {
                Id = e.Id,
                Title = e.Title,
                DurationMinutes = e.DurationMinutes,
                TotalQuestions = e.TotalQuestions,
                TotalScore = e.TotalScore,
                Status = e.Status.ToString(),
                TargetMode = e.TargetMode.ToString(),
                CreatedAt = e.CreatedAt,
                StartTime = e.StartTime,
                EndTime = e.EndTime
            });
        }
        return result.OrderBy(e => e.CreatedAt).ToList();
    }

    private static List<int> Shuffle(List<int> list)
    {
        var rnd = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}

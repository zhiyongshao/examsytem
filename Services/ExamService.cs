using Microsoft.EntityFrameworkCore;

namespace ExamSystem;

public interface IExamService
{
    Task<ExamDetailDto> PublishAsync(PublishExamRequest req, int adminId);
    Task<bool> CloseAsync(int examId);
    Task<List<ExamSummaryDto>> ListAsync();
    Task<ExamDetailDto?> DetailAsync(int examId);
    Task<List<ExamSummaryDto>> AvailableAsync(int userId);

    // 考试管理（新增）
    Task<ExamDetailDto?> UpdateAsync(int examId, UpdateExamRequest req);
    Task<bool> DeleteAsync(int examId);
    Task<ExamDetailDto> RepublishAsync(int sourceExamId, int adminId);
    Task<ExamEntranceDto?> GetEntranceAsync(int examId);

    // 随机卷：按会话（考生）独立抽题并落库，返回该考生的题集
    Task<List<ExamQuestion>> DrawSessionPaperAsync(int examId, int sessionId);
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

        // 先把请求中的选题规则写入考试（供抽题与总分计算使用）
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
        }

        // 校验题库题量 + 按规则计算总分/总题数（两种卷型都依据规则，确保总分准确）
        int totalQuestions = 0;
        int totalScore = 0;
        var shortfalls = new List<string>();
        foreach (var rule in exam.Rules)
        {
            var scopes = new[] { rule.Scope1, rule.Scope2, rule.Scope3 }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToArray();
            var poolCount = await _db.Questions
                .CountAsync(q => scopes.Any(s => (q.Scope1 ?? "").Contains(s)
                                             || (q.Scope2 ?? "").Contains(s)
                                             || (q.Scope3 ?? "").Contains(s))
                                && q.Type == rule.Type);
            if (poolCount < rule.Count)
                shortfalls.Add($"范围「{string.Join("/", scopes)}」+ 题型「{rule.Type}」：需 {rule.Count} 道，题库仅有 {poolCount} 道");
            totalQuestions += rule.Count;
            totalScore += rule.Count * rule.ScorePerQuestion;
        }
        if (shortfalls.Count > 0)
            throw new InvalidOperationException("题库题量不足：\n" + string.Join("\n", shortfalls));

        // 固定卷：发布时一次性抽题；随机卷：不预抽，由考生开始考试时各自抽
        if (req.PaperMode == ExamPaperMode.Fixed)
            exam.Questions = await DrawPaperAsync(exam.Rules.ToList(), exam.Id, null);

        exam.PaperMode = req.PaperMode;
        exam.TotalQuestions = totalQuestions;
        exam.TotalScore = totalScore;

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
            PaperMode = exam.PaperMode.ToString(),
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
                PaperMode = e.PaperMode.ToString(),
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

    /// <summary>
    /// 按规则从题库抽题，返回 ExamQuestion 列表（未保存）。
    /// usedQuestionIds 跨规则去重，避免同一份卷内题目重复。
    /// sessionId 为 null 表示固定卷（归属整场考试）；非 null 表示随机卷（归属某考生会话）。
    /// </summary>
    private async Task<List<ExamQuestion>> DrawPaperAsync(List<ExamRule> rules, int examId, int? sessionId)
    {
        var result = new List<ExamQuestion>();
        var usedQuestionIds = new HashSet<int>();
        int order = 0;
        foreach (var rule in rules)
        {
            var scopes = new[] { rule.Scope1, rule.Scope2, rule.Scope3 }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToArray();
            var candidates = await _db.Questions
                .Where(q => scopes.Any(s => (q.Scope1 ?? "").Contains(s)
                                         || (q.Scope2 ?? "").Contains(s)
                                         || (q.Scope3 ?? "").Contains(s))
                            && q.Type == rule.Type
                            && !usedQuestionIds.Contains(q.Id))
                .Select(q => q.Id).ToListAsync();

            if (candidates.Count < rule.Count)
                throw new InvalidOperationException(
                    $"范围「{string.Join("/", scopes)}」+ 题型「{rule.Type}」：需 {rule.Count} 道，题库仅有 {candidates.Count} 道");

            var picked = Shuffle(candidates).Take(rule.Count).ToList();
            foreach (var qid in picked)
            {
                usedQuestionIds.Add(qid);
                result.Add(new ExamQuestion
                {
                    ExamId = examId,
                    SessionId = sessionId,
                    QuestionId = qid,
                    Score = rule.ScorePerQuestion,
                    Order = order++
                });
            }
        }
        return result;
    }

    /// <summary>随机卷：为某考生的答卷独立抽题并落库（幂等：已抽过则直接返回）。</summary>
    public async Task<List<ExamQuestion>> DrawSessionPaperAsync(int examId, int sessionId)
    {
        var existing = await _db.ExamQuestions
            .Where(q => q.SessionId == sessionId).ToListAsync();
        if (existing.Count > 0) return existing;

        var exam = await _db.Exams.Include(e => e.Rules).FirstOrDefaultAsync(e => e.Id == examId);
        if (exam == null) throw new InvalidOperationException("考试不存在");
        if (exam.PaperMode != ExamPaperMode.PerCandidate)
            throw new InvalidOperationException("仅「按考生随机」模式的考试需要按会话抽题");

        var paper = await DrawPaperAsync(exam.Rules.ToList(), examId, sessionId);
        _db.ExamQuestions.AddRange(paper);
        await _db.SaveChangesAsync();
        return paper;
    }

    // ===== 考试管理：修改 =====
    public async Task<ExamDetailDto?> UpdateAsync(int examId, UpdateExamRequest req)
    {
        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return null;

        if (string.IsNullOrWhiteSpace(req.Title))
            throw new InvalidOperationException("考试标题不能为空");
        exam.Title = req.Title.Trim();

        var hasSessions = await _db.ExamSessions.AnyAsync(s => s.ExamId == examId);
        if (hasSessions)
        {
            // 已有考生作答：仅允许改标题；时间、对象都不允许，避免历史成绩错乱
            await _db.SaveChangesAsync();
            return await DetailAsync(examId);
        }

        // 时间校验
        if (req.StartTime == null || req.EndTime == null)
            throw new InvalidOperationException("请设置开始与结束时间");
        if (req.EndTime <= req.StartTime)
            throw new InvalidOperationException("结束时间需晚于开始时间");
        exam.StartTime = req.StartTime;
        exam.EndTime = req.EndTime;
        exam.DurationMinutes = Math.Max(1, (int)Math.Round((req.EndTime.Value - req.StartTime.Value).TotalMinutes));

        // 目标对象
        if (req.TargetMode.HasValue)
        {
            var newMode = req.TargetMode.Value;
            if (newMode == TargetMode.Specified)
            {
                if (req.TargetUserIds == null || req.TargetUserIds.Count == 0)
                    throw new InvalidOperationException("指定人员模式必须选择至少 1 名考生");
                var newPwd = PasswordHelper.Random(6);
                exam.TargetMode = TargetMode.Specified;
                exam.TargetUserIds = string.Join(",", req.TargetUserIds);
                exam.CandidatePassword = newPwd;
                var targetUsers = await _db.Users.Where(u => req.TargetUserIds.Contains(u.Id)).ToListAsync();
                foreach (var u in targetUsers) u.PasswordHash = PasswordHelper.Hash(newPwd);
            }
            else
            {
                exam.TargetMode = TargetMode.All;
                exam.TargetUserIds = null;
                exam.CandidatePassword = null;
            }
        }

        await _db.SaveChangesAsync();
        return await DetailAsync(examId);
    }

    // ===== 考试管理：删除（无考生作答时允许）=====
    public async Task<bool> DeleteAsync(int examId)
    {
        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return false;

        var hasSessions = await _db.ExamSessions.AnyAsync(s => s.ExamId == examId);
        if (hasSessions)
            throw new InvalidOperationException("该考试已有考生作答，无法删除。\n如需让考生不可见，可使用「关闭」操作。");

        var rules = await _db.ExamRules.Where(r => r.ExamId == examId).ToListAsync();
        var questions = await _db.ExamQuestions.Where(q => q.ExamId == examId).ToListAsync();
        _db.ExamRules.RemoveRange(rules);
        _db.ExamQuestions.RemoveRange(questions);
        _db.Exams.Remove(exam);
        await _db.SaveChangesAsync();
        return true;
    }

    // ===== 考试管理：基于现有考试重新发布（克隆 + 重新抽题）=====
    public async Task<ExamDetailDto> RepublishAsync(int sourceExamId, int adminId)
    {
        var source = await _db.Exams.Include(e => e.Rules).FirstOrDefaultAsync(e => e.Id == sourceExamId);
        if (source == null) throw new InvalidOperationException("源考试不存在");

        var exam = new Exam
        {
            Title = source.Title + " - 副本",
            DurationMinutes = source.DurationMinutes,
            CreatedBy = adminId,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            TargetMode = source.TargetMode,
            TargetUserIds = source.TargetUserIds,
            PaperMode = source.PaperMode,
            Status = ExamStatus.Published
        };

        // 复制规则
        foreach (var r in source.Rules)
        {
            exam.Rules.Add(new ExamRule
            {
                Scope1 = r.Scope1,
                Scope2 = r.Scope2,
                Scope3 = r.Scope3,
                Type = r.Type,
                Count = r.Count,
                ScorePerQuestion = r.ScorePerQuestion
            });
        }

        // 校验题库题量 + 按规则计算总分/总题数（两种卷型都依据规则）
        int pcTotalQuestions = 0;
        int pcTotalScore = 0;
        var pcShortfalls = new List<string>();
        foreach (var rule in exam.Rules)
        {
            var scopes = new[] { rule.Scope1, rule.Scope2, rule.Scope3 }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToArray();
            var poolCount = await _db.Questions
                .CountAsync(q => scopes.Any(s => (q.Scope1 ?? "").Contains(s)
                                             || (q.Scope2 ?? "").Contains(s)
                                             || (q.Scope3 ?? "").Contains(s))
                                && q.Type == rule.Type);
            if (poolCount < rule.Count)
                pcShortfalls.Add($"范围「{string.Join("/", scopes)}」+ 题型「{rule.Type}」：需 {rule.Count} 道，题库仅有 {poolCount} 道");
            pcTotalQuestions += rule.Count;
            pcTotalScore += rule.Count * rule.ScorePerQuestion;
        }
        if (pcShortfalls.Count > 0)
            throw new InvalidOperationException("题库题量不足：\n" + string.Join("\n", pcShortfalls));

        // 固定卷：重新抽题并落库；随机卷：不预抽（考生开始时各自抽）
        if (source.PaperMode == ExamPaperMode.Fixed)
            exam.Questions = await DrawPaperAsync(exam.Rules.ToList(), exam.Id, null);

        exam.TotalQuestions = pcTotalQuestions;
        exam.TotalScore = pcTotalScore;

        // 指定人员：重新生成密码
        if (exam.TargetMode == TargetMode.Specified && !string.IsNullOrWhiteSpace(exam.TargetUserIds))
        {
            var newPwd = PasswordHelper.Random(6);
            exam.CandidatePassword = newPwd;
            var targetIds = exam.TargetUserIds.Split(',')
                .Where(x => int.TryParse(x, out _)).Select(int.Parse).ToList();
            var targetUsers = await _db.Users.Where(u => targetIds.Contains(u.Id)).ToListAsync();
            foreach (var u in targetUsers) u.PasswordHash = PasswordHelper.Hash(newPwd);
        }

        // 总分/总题数已在上面按规则计算（固定卷=已抽题合计，随机卷=规则合计）

        _db.Exams.Add(exam);
        await _db.SaveChangesAsync();
        return await DetailAsync(exam.Id) ?? throw new InvalidOperationException("重新发布失败");
    }

    // ===== 考试管理：查看入口信息 =====
    public async Task<ExamEntranceDto?> GetEntranceAsync(int examId)
    {
        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return null;

        var dto = new ExamEntranceDto
        {
            Id = exam.Id,
            Title = exam.Title,
            Status = exam.Status.ToString(),
            TargetMode = exam.TargetMode.ToString(),
            StartTime = exam.StartTime,
            EndTime = exam.EndTime,
            FrontendUrl = "/exam.html",
            CandidatePassword = exam.CandidatePassword
        };

        if (exam.TargetMode == TargetMode.Specified && !string.IsNullOrWhiteSpace(exam.TargetUserIds))
        {
            var ids = exam.TargetUserIds.Split(',')
                .Where(x => int.TryParse(x, out _)).Select(int.Parse).ToList();
            var users = await _db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
            dto.TargetUsers = users.Select(u => new CandidateEntranceUser
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                Department = u.Department
            }).ToList();
        }

        return dto;
    }
}

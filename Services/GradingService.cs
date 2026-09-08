using Microsoft.EntityFrameworkCore;

namespace ExamSystem;

public interface IGradingService
{
    Task<ExamResultResponse> GradeAsync(int examId, int userId, List<SubmitAnswer> answers, bool early);
}

public class GradingService : IGradingService
{
    private readonly AppDbContext _db;
    public GradingService(AppDbContext db) => _db = db;

    public async Task<ExamResultResponse> GradeAsync(int examId, int userId, List<SubmitAnswer> answers, bool early)
    {
        // 取进行中的答卷（没有则新建）
        var session = await _db.ExamSessions
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.ExamId == examId && s.UserId == userId && s.Status == SessionStatus.InProgress);
        if (session == null)
            session = await _db.ExamSessions
                .Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.ExamId == examId && s.UserId == userId);

        if (session == null)
            throw new InvalidOperationException("未找到考试答卷，请先开始考试");

        // 试卷题目与分值：固定卷用考试级题集，随机卷用本会话独立题集
        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) throw new InvalidOperationException("考试不存在");
        List<ExamQuestion> examQuestionList;
        if (exam.PaperMode == ExamPaperMode.PerCandidate)
            examQuestionList = await _db.ExamQuestions.Where(q => q.SessionId == session.Id).ToListAsync();
        else
            examQuestionList = await _db.ExamQuestions.Where(q => q.ExamId == examId && q.SessionId == null).ToListAsync();
        var examQuestions = examQuestionList.ToDictionary(q => q.QuestionId);
        var correctAnswers = await _db.Questions
            .Where(q => examQuestions.Keys.Contains(q.Id))
            .ToDictionaryAsync(q => q.Id, q => q.Answer);

        // 清空旧答案（支持重交）
        _db.ExamAnswers.RemoveRange(session.Answers);
        session.Answers.Clear();

        double total = 0;
        int correct = 0;
        foreach (var eq in examQuestions.Values)
        {
            var submitted = answers.FirstOrDefault(a => a.QuestionId == eq.QuestionId);
            var selected = submitted?.Selected ?? "";
            var std = correctAnswers.GetValueOrDefault(eq.QuestionId, "");
            bool isCorrect = IsCorrect(selected, std);
            if (isCorrect) { correct++; total += eq.Score; }
            session.Answers.Add(new ExamAnswer
            {
                QuestionId = eq.QuestionId,
                Selected = selected,
                IsCorrect = isCorrect,
                ScoreGot = isCorrect ? eq.Score : 0
            });
        }

        session.Score = total;
        session.SubmitTime = DateTime.UtcNow;
        session.Status = SessionStatus.Graded;
        await _db.SaveChangesAsync();

        var examTotal = examQuestions.Values.Sum(q => q.Score);
        return new ExamResultResponse
        {
            SessionId = session.Id,
            ExamId = examId,
            Score = Math.Round(total, 2),
            TotalScore = examTotal,
            CorrectCount = correct,
            TotalCount = examQuestions.Count,
            Early = early,
            SubmitTime = session.SubmitTime.Value
        };
    }

    private static bool IsCorrect(string selected, string standard)
    {
        var s = new string(selected.ToUpperInvariant().Where(c => c is 'A' or 'B' or 'C' or 'D').Distinct().OrderBy(c => c).ToArray());
        var t = new string(standard.ToUpperInvariant().Where(c => c is 'A' or 'B' or 'C' or 'D').Distinct().OrderBy(c => c).ToArray());
        return s == t;
    }
}

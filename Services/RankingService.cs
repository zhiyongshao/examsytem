using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ExamSystem;

public interface IRankingService
{
    Task<List<RankingItem>> ComputeAsync(int examId);
    Task BroadcastAsync(int examId);
}

public class RankingService : IRankingService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<RankingHub> _hub;
    public RankingService(AppDbContext db, IHubContext<RankingHub> hub) => (_db, _hub) = (db, hub);

    public async Task<List<RankingItem>> ComputeAsync(int examId)
    {
        var exam = await _db.Exams.FindAsync(examId);
        var total = exam?.TotalScore ?? 0;

        // 考试结束后仍在作答的，自动按 0 分收卷并纳入排名
        if (exam != null && exam.EndTime != null && DateTime.UtcNow >= exam.EndTime.Value)
        {
            var expired = await _db.ExamSessions
                .Where(s => s.ExamId == examId && s.Status == SessionStatus.InProgress)
                .ToListAsync();
            if (expired.Count > 0)
            {
                foreach (var s in expired)
                {
                    s.Status = SessionStatus.Graded;
                    s.SubmitTime = exam.EndTime.Value;
                    s.Score = 0;
                }
                await _db.SaveChangesAsync();
            }
        }

        var sessions = await _db.ExamSessions
            .Where(s => s.ExamId == examId && s.Status == SessionStatus.Graded)
            .Include(s => s.Answers)
            .ToListAsync();
        var users = await _db.Users.ToDictionaryAsync(u => u.Id);

        var items = sessions.Select(s => new RankingItem
        {
            UserId = s.UserId,
            DisplayName = users.GetValueOrDefault(s.UserId)?.DisplayName ?? "(未知)",
            Score = s.Score,
            TotalScore = total,
            CorrectCount = s.Answers.Count(a => a.IsCorrect),
            TotalCount = s.Answers.Count,
            DurationSeconds = s.SubmitTime.HasValue ? (s.SubmitTime.Value - s.StartTime).TotalSeconds : 0,
            Passed = total > 0 && s.Score >= total * 0.6
        }).ToList();

        // 同分按用时短者靠前
        items = items.OrderByDescending(i => i.Score).ThenBy(i => i.DurationSeconds).ToList();
        for (int i = 0; i < items.Count; i++) items[i].Rank = i + 1;
        return items;
    }

    public async Task BroadcastAsync(int examId)
    {
        var items = await ComputeAsync(examId);
        await _hub.Clients.Group(RankingHub.GroupName(examId))
            .SendAsync("RankingUpdated", items);
    }
}

using Microsoft.AspNetCore.SignalR;

namespace ExamSystem;

/// <summary>
/// 实时排名通道。考生前端连接后 JoinExam(examId) 加入分组，
/// 服务端在有人交卷后向该分组广播 "RankingUpdated"。
/// </summary>
public class RankingHub : Hub
{
    public Task JoinExam(int examId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(examId));

    public Task LeaveExam(int examId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(examId));

    public static string GroupName(int examId) => $"exam-{examId}";
}

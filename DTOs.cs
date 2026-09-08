using System.ComponentModel.DataAnnotations;

namespace ExamSystem;

// ===== 鉴权 =====
public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginResponse
{
    public string Token { get; set; } = "";
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsAdmin { get; set; }
}

public class CreateUserRequest
{
    [Required] public string Username { get; set; } = "";
    [Required] public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Candidate;
    public UserAccountType AccountType { get; set; } = UserAccountType.General; // 默认无限制；导入考生时置 Exam
}

public class UpdateUserRequest
{
    public string? DisplayName { get; set; }
    public string? JobNo { get; set; }
    public string? Department { get; set; }
    public UserRole? Role { get; set; }
}

public class ChangePasswordRequest
{
    [Required] public string OldPassword { get; set; } = "";
    [Required] public string NewPassword { get; set; } = "";
}

public class ResetPasswordRequest
{
    [Required] public string NewPassword { get; set; } = "";
}

public class UserBrief
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string AccountType { get; set; } = ""; // General / Exam
    public string? JobNo { get; set; }
    public string? Department { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ===== 题目 =====
public class QuestionRequest
{
    public string Scope1 { get; set; } = "";
    public string? Scope2 { get; set; }
    public string? Scope3 { get; set; }
    public QuestionType Type { get; set; }
    public string Content { get; set; } = "";
    public string OptionA { get; set; } = "";
    public string OptionB { get; set; } = "";
    public string OptionC { get; set; } = "";
    public string OptionD { get; set; } = "";
    public string Answer { get; set; } = "";
}

public class QuestionDto
{
    public int Id { get; set; }
    public string Scope1 { get; set; } = "";
    public string? Scope2 { get; set; }
    public string? Scope3 { get; set; }
    public string Type { get; set; } = "";
    public string Content { get; set; } = "";
    public string OptionA { get; set; } = "";
    public string OptionB { get; set; } = "";
    public string OptionC { get; set; } = "";
    public string OptionD { get; set; } = "";
    public string Answer { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public class ImportResult
{
    public int Total { get; set; }
    public int Success { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

// ===== 考生名单导入 =====
public class CandidateBrief
{
    public int Id { get; set; }
    public string Username { get; set; } = "";     // 用户名 = 工号
    public string DisplayName { get; set; } = "";  // 姓名
    public string? JobNo { get; set; }
    public string? Department { get; set; }
}

public class CandidateImportResult
{
    public int Total { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<CandidateBrief> Items { get; set; } = new();
}

// ===== 发布考试（选题规则）=====
public class ExamRuleRequest
{
    public string? Scope1 { get; set; }
    public string? Scope2 { get; set; }
    public string? Scope3 { get; set; }
    public QuestionType Type { get; set; }
    public int Count { get; set; }
    public int ScorePerQuestion { get; set; }
}

public class PublishExamRequest
{
    [Required] public string Title { get; set; } = "";
    public int DurationMinutes { get; set; } = 60;
    public TargetMode TargetMode { get; set; } = TargetMode.All;
    public List<int> TargetUserIds { get; set; } = new();
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public ExamPaperMode PaperMode { get; set; } = ExamPaperMode.Fixed; // 卷型：固定卷 / 按考生随机
    public List<ExamRuleRequest> Rules { get; set; } = new();
}

/// <summary>规则行：知识范围（三字段）+ 题型 + 题数 + 分值 + 小计。</summary>
public class MatrixCell
{
    public string? Scope1 { get; set; }
    public string? Scope2 { get; set; }
    public string? Scope3 { get; set; }
    public string Type { get; set; } = "";
    public int Count { get; set; }
    public int ScorePerQuestion { get; set; }
    public int SubTotal { get; set; }
}

public class ExamSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int DurationMinutes { get; set; }
    public int TotalQuestions { get; set; }
    public int TotalScore { get; set; }
    public string Status { get; set; } = "";
    public string TargetMode { get; set; } = "";
    public string PaperMode { get; set; } = ""; // Fixed / PerCandidate
    public DateTime CreatedAt { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int ParticipantCount { get; set; }
}

public class ExamDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int DurationMinutes { get; set; }
    public int TotalQuestions { get; set; }
    public int TotalScore { get; set; }
    public string Status { get; set; } = "";
    public string TargetMode { get; set; } = "";
    public string PaperMode { get; set; } = ""; // Fixed / PerCandidate
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? CandidatePassword { get; set; }
    public List<int> TargetUserIds { get; set; } = new();
    public List<MatrixCell> Rules { get; set; } = new();
}

// ===== 考试作答 =====
public class SubmitAnswer
{
    public int QuestionId { get; set; }
    public string Selected { get; set; } = "";
}

public class SubmitRequest
{
    public List<SubmitAnswer> Answers { get; set; } = new();
    public bool Early { get; set; }
}

public class StartExamResponse
{
    public int SessionId { get; set; }
    public int ExamId { get; set; }
    public string Title { get; set; } = "";
    public int DurationMinutes { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int TotalScore { get; set; }
    public List<ClientQuestion> Questions { get; set; } = new();
}

/// <summary>下发给考生的题目（不含标准答案）。</summary>
public class ClientQuestion
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Content { get; set; } = "";
    public int Score { get; set; }
    public Dictionary<string, string> Options { get; set; } = new(); // {A:..,B:..}
}

public class ExamResultResponse
{
    public int SessionId { get; set; }
    public int ExamId { get; set; }
    public double Score { get; set; }
    public int TotalScore { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public bool Early { get; set; }
    public DateTime SubmitTime { get; set; }
}

public class RankingItem
{
    public int Rank { get; set; }
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public double Score { get; set; }
    public int TotalScore { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public double DurationSeconds { get; set; }
    public bool Passed { get; set; }
}

// ===== 历史 / 导出 =====
public class HistoryExamDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public int TotalScore { get; set; }
    public int ParticipantCount { get; set; }
    public double AverageScore { get; set; }
    public double MaxScore { get; set; }
    public double PassRate { get; set; } // 60% 为合格线
    public DateTime CreatedAt { get; set; }
}

// ===== 考试编辑 =====
public class UpdateExamRequest
{
    [Required] public string Title { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TargetMode? TargetMode { get; set; }
    public List<int>? TargetUserIds { get; set; }
}

// ===== 考试入口信息（管理员分享给考生）=====
public class ExamEntranceDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string TargetMode { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string FrontendUrl { get; set; } = "/exam.html"; // 相对路径，由前端拼接 origin
    public string? CandidatePassword { get; set; }          // Specified 时有值
    public List<CandidateEntranceUser> TargetUsers { get; set; } = new();
}

public class CandidateEntranceUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";   // 登录用户名（= 工号）
    public string DisplayName { get; set; } = ""; // 姓名
    public string? Department { get; set; }
}

public class ResultRow
{
    public int Rank { get; set; }
    public string DisplayName { get; set; } = "";
    public string Username { get; set; } = "";
    public double Score { get; set; }
    public int TotalScore { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public double DurationSeconds { get; set; }
    public string Result { get; set; } = ""; // 合格/不合格
    public DateTime SubmitTime { get; set; }
}

// ===== 考试实时监控（监考看板）=====
/// <summary>单个考生的实时状态。Status：未登陆 / 未开始 / 考试中 / 已交卷。</summary>
public class MonitorCandidateDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? JobNo { get; set; }
    public string? Department { get; set; }
    public string Status { get; set; } = ""; // 未登陆 / 未开始 / 考试中 / 已交卷
    public double Score { get; set; }
    public int? Rank { get; set; }
}

public class ExamMonitorDto
{
    public int ExamId { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string PaperMode { get; set; } = ""; // Fixed / PerCandidate
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public int TotalScore { get; set; }
    public bool IsEnded { get; set; } // 是否已过结束时间（之后自动按全员分数排名）
    public int TotalCandidates { get; set; }  // 应到
    public int LoggedInCount { get; set; }    // 已登录（未登陆之外）
    public int InProgressCount { get; set; }  // 考试中
    public int SubmittedCount { get; set; }   // 已交卷
    public List<MonitorCandidateDto> Candidates { get; set; } = new();
}

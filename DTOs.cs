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
    public bool IsTraining { get; set; } = false; // 训练考试：供考生反复刷题，不受时间窗限制
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
    public bool IsTraining { get; set; }
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
    public bool IsTraining { get; set; }
}

// ===== 考试作答 =====
public class SubmitAnswer
{
    public int QuestionId { get; set; }
    public string Selected { get; set; } = "";
}

public class SubmitRequest
{
    public int SessionId { get; set; } // 本次作答的会话（答卷）ID，用于共享账号下精确判分与回看
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

/// <summary>考卷回顾中单题：题目、考生答案、标准答案、是否正确、得分。</summary>
public class ReviewQuestionDto
{
    public int Id { get; set; }
    public string Type { get; set; } = "";       // Single / Multiple / Judge
    public string Content { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new(); // {A:..,B:..}
    public int Score { get; set; }
    public string Selected { get; set; } = "";    // 考生所选（字母，可能为空）
    public string CorrectAnswer { get; set; } = ""; // 标准答案（字母）
    public bool IsCorrect { get; set; }
    public double ScoreGot { get; set; }
}

/// <summary>考卷回顾：提交/强制提交后供考生回看（含正确答案与对错标识）。</summary>
public class ExamReviewResponse
{
    public int ExamId { get; set; }
    public int SessionId { get; set; }
    public string Title { get; set; } = "";
    public double Score { get; set; }
    public int TotalScore { get; set; }
    public List<ReviewQuestionDto> Questions { get; set; } = new();
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
    public DateTime? SubmitTime { get; set; }   // 提交时间（用于同分排序：早提交优先）
    public DateTime? LoginTime { get; set; }     // 最近登录时间（用于最终并列：登录晚优先）
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
    public string FrontendUrl { get; set; } = "/exam.html"; // 兼容字段（= EntryUrl）
    public string EntrySlug { get; set; } = "";             // 独立入口短链串，如 Ab3kZ9
    public string EntryUrl { get; set; } = "/exam.html";    // 独立入口短链，如 /e/Ab3kZ9（每场不同）
    public string? CandidatePassword { get; set; }          // Specified 时有值
    public List<CandidateEntranceUser> TargetUsers { get; set; } = new();
}

/// <summary>考试独立入口公开信息（匿名可访问，供登录页与倒计时页使用，不含题目）。</summary>
public class ExamEntryPublicDto
{
    public int ExamId { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";       // Published / Closed / Draft
    public string TargetMode { get; set; } = "";   // All / Specified
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public int TotalScore { get; set; }
    public bool HasPassword { get; set; }          // 是否指定人员（有统一登录密码）
    public string? CandidatePassword { get; set; } // 仅指定人员返回，供登录页提示
}

public class CandidateEntranceUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";   // 登录用户名（= 工号）
    public string DisplayName { get; set; } = ""; // 姓名
    public string? Department { get; set; }
}

// ===== 删除前预览 + 级联清除（需求1）=====
/// <summary>删除考试的请求体：是否一并清除本场作答记录 / 仅被本场引用的考生。</summary>
public class DeleteExamRequest
{
    public bool CascadeSessions { get; set; }   // 一并删除本考试的全部作答记录（含成绩）
    public bool CascadeUsers { get; set; }      // 一并删除仅被本场引用的考生账号
}

/// <summary>删除考试前的关联数据预览，供管理员确认。</summary>
public class ExamDeletePreviewDto
{
    public int ExamId { get; set; }
    public string Title { get; set; } = "";
    public bool HasSessions { get; set; }                 // 是否已有作答记录
    public int SessionCount { get; set; }                 // 作答记录总数
    public int SubmittedCount { get; set; }               // 已交卷
    public int InProgressCount { get; set; }              // 进行中
    public int CandidateCount { get; set; }               // 与本场关联的考生数（信息展示）
    public int ExclusiveCandidateCount { get; set; }      // 仅被本场引用、可安全级联删除的考生数
    public bool CanDeleteWithoutCascade { get; set; }     // = !HasSessions
}

/// <summary>删除用户的请求体：是否一并清除该用户的作答记录。</summary>
public class DeleteUserRequest
{
    public bool CascadeSessions { get; set; }   // 一并删除该用户的全部作答记录（含成绩）
}

/// <summary>某用户的作答记录按考试分布。</summary>
public class UserSessionByExamDto
{
    public int ExamId { get; set; }
    public string ExamTitle { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>删除用户前的关联数据预览，供管理员确认。</summary>
public class UserDeletePreviewDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public int SessionCount { get; set; }                 // 作答记录总数
    public int ReferencedExamCount { get; set; }          // 被多少场考试（指定人员）引用
    public List<UserSessionByExamDto> ByExam { get; set; } = new();
    public bool CanDeleteWithoutCascade { get; set; }     // = SessionCount == 0
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

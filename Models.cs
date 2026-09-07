namespace ExamSystem;

// ===== 枚举 =====
public enum UserRole
{
    Candidate = 0, // 考生：仅考试
    Admin = 1      // 管理员：题库维护 + 发布考试
}

public enum QuestionType
{
    Single = 0,   // 单选
    Multiple = 1, // 多选
    Judge = 2     // 判断
}

public enum ExamStatus
{
    Draft = 0,
    Published = 1,
    Closed = 2
}

public enum TargetMode
{
    All = 0,       // 全员可考
    Specified = 1  // 指定人员
}

public enum SessionStatus
{
    InProgress = 0,
    Submitted = 1,
    Graded = 2,
    Expired = 3
}

// ===== 实体 =====
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? JobNo { get; set; }          // 工号（考生名单导入）
    public string? Department { get; set; }     // 部门（考生名单导入）
    public UserRole Role { get; set; } = UserRole.Candidate;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 题目。知识范围允许三个字段，同一题可同时属于三种范围。
/// 题型：单选 / 多选 / 判断。选项固定 4 个；判断题仅用 A(正确) / B(错误)。
/// Answer：单选/判断为单个字母（如 "A"）；多选为字母组合（如 "ACD"）。
/// </summary>
public class Question
{
    public int Id { get; set; }
    public string Scope1 { get; set; } = "";   // 知识范围 1（必填）
    public string? Scope2 { get; set; }         // 知识范围 2（可选）
    public string? Scope3 { get; set; }         // 知识范围 3（可选）
    public QuestionType Type { get; set; }
    public string Content { get; set; } = "";   // 题干
    public string OptionA { get; set; } = "";
    public string OptionB { get; set; } = "";
    public string OptionC { get; set; } = "";
    public string OptionD { get; set; } = "";
    public string Answer { get; set; } = "";    // 标准答案（字母）
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 发布的考试（试卷定义）。
/// 通过 Rules（范围 × 题型 的二维规则）抽题生成 Questions。
/// </summary>
public class Exam
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int DurationMinutes { get; set; }
    public int TotalQuestions { get; set; }
    public int TotalScore { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public TargetMode TargetMode { get; set; } = TargetMode.All;
    public string? TargetUserIds { get; set; } // 指定人员：逗号分隔的 UserId
    public string? CandidatePassword { get; set; } // 指定人员发布时生成的统一随机登录密码
    public List<ExamRule> Rules { get; set; } = new();
    public List<ExamQuestion> Questions { get; set; } = new();
}

/// <summary>选题规则：知识范围（三字段，同题库）+ 题型 + 抽题数 + 每题分值。</summary>
public class ExamRule
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public string? Scope1 { get; set; }
    public string? Scope2 { get; set; }
    public string? Scope3 { get; set; }
    public QuestionType Type { get; set; }
    public int Count { get; set; }
    public int ScorePerQuestion { get; set; }
}

/// <summary>考试实际抽出的题目（含分值与顺序）。</summary>
public class ExamQuestion
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public int QuestionId { get; set; }
    public int Score { get; set; }
    public int Order { get; set; }
    public Question? Question { get; set; }
}

/// <summary>一次考试参与（答卷）。</summary>
public class ExamSession
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public int UserId { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? SubmitTime { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.InProgress;
    public double Score { get; set; }
    public List<ExamAnswer> Answers { get; set; } = new();
}

public class ExamAnswer
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int QuestionId { get; set; }
    public string Selected { get; set; } = ""; // 考生所选字母
    public bool IsCorrect { get; set; }
    public double ScoreGot { get; set; }
}

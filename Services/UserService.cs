using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ExamSystem;

public interface IUserService
{
    Task<CandidateImportResult> ImportCandidatesAsync(Stream stream, bool isExcel);
    byte[] BuildCandidateTemplateXlsx();
    Task<UserDeletePreviewDto?> GetDeletePreviewAsync(int userId);
    Task<bool> DeleteAsync(int userId, bool cascadeSessions);
}

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    public UserService(AppDbContext db) => _db = db;

    public async Task<CandidateImportResult> ImportCandidatesAsync(Stream stream, bool isExcel)
    {
        var result = new CandidateImportResult();
        var seenUsernames = new HashSet<string>();
        var touched = new List<(User User, string Name)>();
        var fatalErrors = new List<string>(); // 致命冲突：与现有 General 账号重名，整个导入拒绝
        var tempPwd = PasswordHelper.Random(8); // 占位密码，发布时会重置为统一随机密码

        void HandleRow(int lineNo, string name, string jobNo, string dept)
        {
            // 完全空行不计入
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(jobNo) && string.IsNullOrWhiteSpace(dept))
            {
                result.Total--;
                return;
            }
            if (string.IsNullOrWhiteSpace(jobNo)) { result.Errors.Add($"第{lineNo}行：工号为空（工号作为登录用户名）"); result.Failed++; return; }
            if (seenUsernames.Contains(jobNo)) { result.Errors.Add($"第{lineNo}行：工号「{jobNo}」与前面重复"); result.Failed++; return; }
            seenUsernames.Add(jobNo);

            var displayName = string.IsNullOrWhiteSpace(name) ? jobNo : name;
            var existing = _db.Users.Local.FirstOrDefault(u => u.Username == jobNo)
                        ?? _db.Users.FirstOrDefault(u => u.Username == jobNo);

            if (existing != null)
            {
                if (existing.Role == UserRole.Admin) { result.Errors.Add($"第{lineNo}行：工号「{jobNo}」与管理员账号冲突，已跳过"); result.Failed++; return; }
                // 不允许用 General（通用）账号作为考生重复导入：整个导入拒绝，不让任何数据落库
                if (existing.AccountType == UserAccountType.General)
                {
                    if (!fatalErrors.Contains(jobNo)) fatalErrors.Add(jobNo);
                    result.Failed++;
                    return;
                }
                existing.DisplayName = displayName;
                existing.JobNo = jobNo;
                existing.Department = dept;
                existing.AccountType = UserAccountType.Exam; // 导入即考试账号：仅考试时间窗内可登录
                result.Updated++;
                touched.Add((existing, displayName));
            }
            else
            {
                var u = new User
                {
                    Username = jobNo,
                    DisplayName = displayName,
                    JobNo = jobNo,
                    Department = dept,
                    PasswordHash = PasswordHelper.Hash(tempPwd),
                    Role = UserRole.Candidate,
                    AccountType = UserAccountType.Exam // 导入即考试账号：仅考试时间窗内可登录
                };
                _db.Users.Add(u);
                result.Created++;
                touched.Add((u, displayName));
            }
        }

        if (isExcel)
        {
            using var wb = new XLWorkbook(stream);
            IXLWorksheet? sheet = null;
            foreach (var ws in wb.Worksheets)
            {
                var first = ws.FirstRowUsed();
                if (first == null) continue;
                var h0 = first.Cell(1).GetString().Trim();
                if (h0.Equals("姓名", StringComparison.OrdinalIgnoreCase) || h0.Equals("Name", StringComparison.OrdinalIgnoreCase))
                { sheet = ws; break; }
            }
            if (sheet == null) { result.Errors.Add("未找到含「姓名」表头的名单页"); return result; }

            var headerRow = sheet.FirstRowUsed()!;
            var header = headerRow.Cells().Select(c => c.GetString().Trim()).ToArray();
            int iName = FindCol(header, "姓名", "Name");
            int iJob = FindCol(header, "工号", "JobNo");
            int iDept = FindCol(header, "部门", "Department");
            if (iJob < 0) { result.Errors.Add("缺少必填列：工号"); return result; }

            foreach (var row in sheet.RowsUsed().Skip(1))
            {
                result.Total++;
                string Get(int idx) => idx >= 0 && idx < header.Length ? row.Cell(idx + 1).GetString().Trim() : "";
                HandleRow(row.RowNumber(), Get(iName), Get(iJob), Get(iDept));
            }
        }
        else
        {
            var rows = CsvHelper.Parse(stream);
            if (rows.Count == 0) { result.Errors.Add("文件为空"); return result; }

            int headerIdx = rows.FindIndex(r => r.Length > 0 &&
                (r[0].Trim().Equals("姓名", StringComparison.OrdinalIgnoreCase) || r[0].Trim().Equals("Name", StringComparison.OrdinalIgnoreCase)));
            if (headerIdx < 0) { result.Errors.Add("未找到表头（需包含「姓名」列）"); return result; }

            var header = rows[headerIdx].Select(h => h.Trim()).ToArray();
            int iName = FindCol(header, "姓名", "Name");
            int iJob = FindCol(header, "工号", "JobNo");
            int iDept = FindCol(header, "部门", "Department");
            if (iJob < 0) { result.Errors.Add("缺少必填列：工号"); return result; }

            for (int r = headerIdx + 1; r < rows.Count; r++)
            {
                result.Total++;
                var row = rows[r];
                string Get(int idx) => idx >= 0 && idx < row.Length ? row[idx].Trim() : "";
                HandleRow(r + 1, Get(iName), Get(iJob), Get(iDept));
            }
        }

        // 致命冲突：名单含与现有 General（通用）账号重名的工号 → 整个导入拒绝，不落库
        if (fatalErrors.Count > 0)
        {
            var bad = string.Join("、", fatalErrors.Distinct());
            throw new InvalidOperationException(
                $"导入被拒绝：名单中含有与现有 General（通用）账号重名的工号（{bad}）。通用账号不可作为考生导入，请移除这些工号后重试。");
        }

        await _db.SaveChangesAsync();
        foreach (var (u, name) in touched)
            result.Items.Add(new CandidateBrief
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = name,
                JobNo = u.JobNo,
                Department = u.Department
            });

        return result;
    }

    // ===== 删除前预览 + 级联删除（需求1）=====
    public async Task<UserDeletePreviewDto?> GetDeletePreviewAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return null;

        var sessions = await _db.ExamSessions.Where(s => s.UserId == userId).ToListAsync();

        // 注意：TargetUserIds 是逗号分隔的 ID 串，必须用精确拆分比较，不能用字符串 Contains（否则用户 2 会误命中 "12,23"）。
        var referencedExamCount = (await _db.Exams
                .Where(e => !string.IsNullOrWhiteSpace(e.TargetUserIds)).ToListAsync())
            .Count(e => e.TargetUserIds!.Split(',').Any(x => int.TryParse(x, out var v) && v == userId));

        var byExam = (await _db.Exams.ToListAsync())
            .Where(e => sessions.Any(s => s.ExamId == e.Id))
            .Select(e => new UserSessionByExamDto
            {
                ExamId = e.Id,
                ExamTitle = e.Title,
                Count = sessions.Count(s => s.ExamId == e.Id)
            }).ToList();

        return new UserDeletePreviewDto
        {
            UserId = user.Id,
            Username = user.Username,
            SessionCount = sessions.Count,
            ReferencedExamCount = referencedExamCount,
            ByExam = byExam,
            CanDeleteWithoutCascade = sessions.Count == 0
        };
    }

    public async Task<bool> DeleteAsync(int userId, bool cascadeSessions)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;

        // 不允许删除最后一位管理员
        if (user.Role == UserRole.Admin)
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == UserRole.Admin);
            if (adminCount <= 1) throw new InvalidOperationException("至少保留一位管理员");
        }

        var hasSession = await _db.ExamSessions.AnyAsync(s => s.UserId == userId);
        // 精确判断：用户 ID 是否出现在某场考试的指定名单里（逗号分隔，避免 "2" 误命中 "12,23"）
        var examsWithTargets = await _db.Exams
            .Where(e => !string.IsNullOrWhiteSpace(e.TargetUserIds)).ToListAsync();
        var referenced = examsWithTargets
            .Any(e => e.TargetUserIds!.Split(',').Any(x => int.TryParse(x, out var v) && v == userId));
        if ((hasSession || referenced) && !cascadeSessions)
            throw new InvalidOperationException("该用户已被考试引用或已有作答记录，无法删除。\n如需彻底删除，请在弹窗中勾选「一并删除作答记录」。");

        if (cascadeSessions && hasSession)
        {
            var sessionIds = await _db.ExamSessions.Where(s => s.UserId == userId).Select(s => s.Id).ToListAsync();
            var answers = await _db.ExamAnswers.Where(a => sessionIds.Contains(a.SessionId)).ToListAsync();
            _db.ExamAnswers.RemoveRange(answers);
            var sessions = await _db.ExamSessions.Where(s => s.UserId == userId).ToListAsync();
            _db.ExamSessions.RemoveRange(sessions);
        }

        // 从其它考试的指定名单中移除该用户，避免悬空引用
        var examsWithUser = examsWithTargets;
        foreach (var e in examsWithUser)
        {
            var ids = e.TargetUserIds!.Split(',').Where(x => int.TryParse(x, out _)).Select(int.Parse).ToList();
            if (ids.Contains(userId))
            {
                ids.Remove(userId);
                e.TargetUserIds = ids.Count > 0 ? string.Join(",", ids) : null;
            }
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return true;
    }

    public byte[] BuildCandidateTemplateXlsx()
    {
        using var wb = new XLWorkbook();

        var wsInfo = wb.AddWorksheet("填写说明");
        var lines = new[]
        {
            "考生名单导入 · 填写说明",
            "1. 在「考生名单」页填写，每行一名考生，不要改动表头。",
            "2. 工号：作为登录用户名（同一份名单内工号不能重复），必填。",
            "3. 姓名：考生姓名，用于显示；留空则用工号显示。",
            "4. 部门：可选，留空即可。",
            "5. 登录密码：发布考试时系统自动生成一个统一随机密码（所有人相同），发布成功后返回。",
            "6. 若工号已存在（非管理员账号），系统会自动更新其姓名/部门。",
            "7. 请不要新增/删除列，也不要修改表头文字。"
        };
        for (int i = 0; i < lines.Length; i++)
        {
            var cell = wsInfo.Cell(i + 1, 1);
            cell.Value = lines[i];
            if (i == 0) cell.Style.Font.Bold = true;
        }
        wsInfo.Column(1).Width = 90;

        var ws = wb.AddWorksheet("考生名单");
        var headers = new[] { "姓名", "工号", "部门" };
        for (int c = 0; c < headers.Length; c++)
        {
            var hc = ws.Cell(1, c + 1);
            hc.Value = headers[c];
            hc.Style.Font.Bold = true;
            hc.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }
        var examples = new[]
        {
            new[] { "张三", "100234", "生产部" },
            new[] { "李四", "100235", "质量部" },
            new[] { "王五", "100236", "研发部" },
        };
        for (int r = 0; r < examples.Length; r++)
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(r + 2, c + 1).Value = examples[r][c];

        // 工号列设为文本，避免前导零丢失
        ws.Range(2, 2, 1000, 2).Style.NumberFormat.Format = "@";
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static int FindCol(string[] header, params string[] names)
        => Array.FindIndex(header, h => names.Any(n => h.Equals(n, StringComparison.OrdinalIgnoreCase)));
}

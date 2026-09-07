using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ExamSystem;

public interface IUserService
{
    Task<CandidateImportResult> ImportCandidatesAsync(Stream stream, bool isExcel);
    byte[] BuildCandidateTemplateXlsx();
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
                existing.DisplayName = displayName;
                existing.JobNo = jobNo;
                existing.Department = dept;
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
                    Role = UserRole.Candidate
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

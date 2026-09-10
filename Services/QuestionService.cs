using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

namespace ExamSystem;

public interface IQuestionService
{
    Task<(List<QuestionDto> Items, int Total)> ListAsync(string? scope, string? type, string? keyword, int page, int pageSize);
    Task<QuestionDto?> GetAsync(int id);
    Task<QuestionDto> CreateAsync(QuestionRequest req);
    Task<QuestionDto?> UpdateAsync(int id, QuestionRequest req);
    Task<bool> DeleteAsync(int id);
    Task<int> DeleteManyAsync(IEnumerable<int> ids);
    Task<ImportResult> ImportCsvAsync(Stream csvStream);
    Task<ImportResult> ImportExcelAsync(Stream xlsxStream);
    byte[] BuildTemplateXlsx();
}

public class QuestionService : IQuestionService
{
    private readonly AppDbContext _db;
    public QuestionService(AppDbContext db) => _db = db;

    public async Task<(List<QuestionDto> Items, int Total)> ListAsync(string? scope, string? type, string? keyword, int page, int pageSize)
    {
        var q = _db.Questions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(scope))
            q = q.Where(x => x.Scope1 == scope || x.Scope2 == scope || x.Scope3 == scope);
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<QuestionType>(type, true, out var t))
            q = q.Where(x => x.Type == t);
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(x => x.Content.Contains(keyword));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToDto(x)).ToListAsync();
        return (items, total);
    }

    public async Task<QuestionDto?> GetAsync(int id)
        => await _db.Questions.Where(x => x.Id == id).Select(x => ToDto(x)).FirstOrDefaultAsync();

    public async Task<QuestionDto> CreateAsync(QuestionRequest req)
    {
        var q = ToEntity(req);
        _db.Questions.Add(q);
        await _db.SaveChangesAsync();
        return ToDto(q);
    }

    public async Task<QuestionDto?> UpdateAsync(int id, QuestionRequest req)
    {
        var q = await _db.Questions.FindAsync(id);
        if (q == null) return null;
        q.Scope1 = req.Scope1;
        q.Scope2 = req.Scope2;
        q.Scope3 = req.Scope3;
        q.Type = req.Type;
        q.Content = req.Content;
        q.OptionA = req.OptionA;
        q.OptionB = req.OptionB;
        q.OptionC = req.OptionC;
        q.OptionD = req.OptionD;
        q.Answer = NormalizeAnswer(req.Answer, req.Type);
        q.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(q);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var q = await _db.Questions.FindAsync(id);
        if (q == null) return false;
        _db.Questions.Remove(q);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<int> ids)
    {
        var list = await _db.Questions.Where(x => ids.Contains(x.Id)).ToListAsync();
        if (list.Count == 0) return 0;
        _db.Questions.RemoveRange(list);
        await _db.SaveChangesAsync();
        return list.Count;
    }

    public async Task<ImportResult> ImportCsvAsync(Stream csvStream)
    {
        var result = new ImportResult();
        var rows = CsvHelper.Parse(csvStream);
        if (rows.Count == 0) { result.Errors.Add("文件为空"); return result; }

        // 解析表头：兼容顶部带 # 注释说明的模板，自动定位表头行
        int headerIdx = rows.FindIndex(r => r.Length > 0 && r[0].Trim().Equals("Scope1", StringComparison.OrdinalIgnoreCase));
        if (headerIdx < 0) { result.Errors.Add("未找到表头（需包含 Scope1 列）"); return result; }

        var header = rows[headerIdx].Select(h => h.Trim()).ToArray();
        var col = (string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int iScope1 = col("Scope1"), iScope2 = col("Scope2"), iScope3 = col("Scope3"),
            iType = col("Type"), iContent = col("Content"),
            iA = col("OptionA"), iB = col("OptionB"), iC = col("OptionC"), iD = col("OptionD"),
            iAnswer = col("Answer");

        if (iType < 0 || iContent < 0 || iAnswer < 0)
        {
            result.Errors.Add("缺少必填列：Type / Content / Answer");
            return result;
        }

        for (int r = headerIdx + 1; r < rows.Count; r++)
        {
            result.Total++;
            var row = rows[r];
            try
            {
                var typeStr = Get(row, iType);
                var type = ParseType(typeStr);
                if (type == null) { result.Errors.Add($"第{r + 1}行：题型无效「{typeStr}」"); result.Failed++; continue; }

                var content = Get(row, iContent);
                if (string.IsNullOrWhiteSpace(content)) { result.Errors.Add($"第{r + 1}行：题干为空"); result.Failed++; continue; }

                // 判断题默认选项
                var optA = Get(row, iA);
                var optB = Get(row, iB);
                if (type == QuestionType.Judge)
                {
                    if (string.IsNullOrWhiteSpace(optA)) optA = "正确";
                    if (string.IsNullOrWhiteSpace(optB)) optB = "错误";
                }
                var optC = Get(row, iC);
                var optD = Get(row, iD);

                var answer = NormalizeAnswer(Get(row, iAnswer), type.Value);
                if (string.IsNullOrWhiteSpace(answer)) { result.Errors.Add($"第{r + 1}行：答案无效"); result.Failed++; continue; }

                var q = new Question
                {
                    Scope1 = Get(row, iScope1),
                    Scope2 = Get(row, iScope2),
                    Scope3 = Get(row, iScope3),
                    Type = type.Value,
                    Content = content,
                    OptionA = optA,
                    OptionB = optB,
                    OptionC = optC,
                    OptionD = optD,
                    Answer = answer
                };
                _db.Questions.Add(q);
                result.Success++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"第{r + 1}行：{ex.Message}");
                result.Failed++;
            }
        }
        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<ImportResult> ImportExcelAsync(Stream xlsxStream)
    {
        var result = new ImportResult();
        using var wb = new XLWorkbook(xlsxStream);

        // 找到含 Scope1 表头的题目页（忽略「填写说明」等说明页）
        IXLWorksheet? sheet = null;
        foreach (var ws in wb.Worksheets)
        {
            var first = ws.FirstRowUsed();
            if (first == null) continue;
            if (first.Cell(1).GetString().Trim().Equals("Scope1", StringComparison.OrdinalIgnoreCase))
            {
                sheet = ws;
                break;
            }
        }
        if (sheet == null) { result.Errors.Add("未找到含 Scope1 表头的题目页"); return result; }

        var headerRow = sheet.FirstRowUsed()!;
        var header = headerRow.Cells().Select(c => c.GetString().Trim()).ToArray();
        var col = (string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int iScope1 = col("Scope1"), iScope2 = col("Scope2"), iScope3 = col("Scope3"),
            iType = col("Type"), iContent = col("Content"),
            iA = col("OptionA"), iB = col("OptionB"), iC = col("OptionC"), iD = col("OptionD"),
            iAnswer = col("Answer");

        if (iType < 0 || iContent < 0 || iAnswer < 0)
        {
            result.Errors.Add("缺少必填列：Type / Content / Answer");
            return result;
        }

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            result.Total++;
            try
            {
                string Get(int idx) => idx >= 0 && idx < header.Length ? row.Cell(idx + 1).GetString().Trim() : "";
                var typeStr = Get(iType);
                var type = ParseType(typeStr);
                if (type == null) { result.Errors.Add($"第{row.RowNumber()}行：题型无效「{typeStr}」"); result.Failed++; continue; }

                var content = Get(iContent);
                if (string.IsNullOrWhiteSpace(content)) { result.Errors.Add($"第{row.RowNumber()}行：题干为空"); result.Failed++; continue; }

                var optA = Get(iA);
                var optB = Get(iB);
                if (type == QuestionType.Judge)
                {
                    if (string.IsNullOrWhiteSpace(optA)) optA = "正确";
                    if (string.IsNullOrWhiteSpace(optB)) optB = "错误";
                }
                var optC = Get(iC);
                var optD = Get(iD);
                var answer = NormalizeAnswer(Get(iAnswer), type.Value);
                if (string.IsNullOrWhiteSpace(answer)) { result.Errors.Add($"第{row.RowNumber()}行：答案无效"); result.Failed++; continue; }

                _db.Questions.Add(new Question
                {
                    Scope1 = Get(iScope1),
                    Scope2 = Get(iScope2),
                    Scope3 = Get(iScope3),
                    Type = type.Value,
                    Content = content,
                    OptionA = optA,
                    OptionB = optB,
                    OptionC = optC,
                    OptionD = optD,
                    Answer = answer
                });
                result.Success++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"第{row.RowNumber()}行：{ex.Message}");
                result.Failed++;
            }
        }
        await _db.SaveChangesAsync();
        return result;
    }

    public byte[] BuildTemplateXlsx()
    {
        using var wb = new XLWorkbook();

        // 说明页
        var wsInfo = wb.AddWorksheet("填写说明");
        var lines = new[]
        {
            "题目批量导入 · 填写说明",
            "1. 在「题目」页填写，每行一道题，不要改动表头。",
            "2. Scope1/2/3 为知识范围，同一题可属多个范围；不填的留空。",
            "3. Type 题型：Single(单选) / Multiple(多选) / Judge(判断)，可直接下拉选择。",
            "4. OptionA~D 为四个选项；判断题只需填 A、B 两列（正确/错误），留空系统自动补。",
            "5. Answer 标准答案：",
            "      · 单选 → 单个字母，如 A / B / C / D",
            "      · 多选 → 字母组合，如 AB、ACD（顺序不限）",
            "      · 判断 → A(正确) 或 B(错误)；也可直接填 对/错、是/否、true/false，系统自动识别",
            "6. 在 Excel 中填写无需处理逗号，内容直接写即可。",
            "7. 请不要新增/删除列，也不要修改表头文字。"
        };
        for (int i = 0; i < lines.Length; i++)
        {
            var cell = wsInfo.Cell(i + 1, 1);
            cell.Value = lines[i];
            if (i == 0) cell.Style.Font.Bold = true;
        }
        wsInfo.Column(1).Width = 90;

        // 题目页
        var ws = wb.AddWorksheet("题目");
        var headers = new[] { "Scope1", "Scope2", "Scope3", "Type", "Content", "OptionA", "OptionB", "OptionC", "OptionD", "Answer" };
        for (int c = 0; c < headers.Length; c++)
        {
            var hc = ws.Cell(1, c + 1);
            hc.Value = headers[c];
            hc.Style.Font.Bold = true;
            hc.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }
        var examples = new[]
        {
            new[] { "物理", "", "", "Single", "1+1=?", "1", "2", "3", "4", "B" },
            new[] { "物理", "力学", "", "Multiple", "下列哪些是力？（多选）", "重力", "摩擦力", "质量", "速度", "AB" },
            new[] { "物理", "", "", "Judge", "光速比声速快", "正确", "错误", "", "", "正确" },
            new[] { "数学", "", "", "Single", "圆周率约等于？", "3.0", "3.14", "3.14159", "3.1415926", "B" },
        };
        for (int r = 0; r < examples.Length; r++)
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(r + 2, c + 1).Value = examples[r][c];

        // 下拉选项（减少填错）
        ws.Range(2, 4, 1000, 4).CreateDataValidation().List("Single,Multiple,Judge", true);
        ws.Range(2, 10, 1000, 10).CreateDataValidation().List("A,B,AB,正确,错误,对,错,是,否,true,false,yes,no", true);

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ===== 内部工具 =====
    private static Question ToEntity(QuestionRequest req) => new()
    {
        Scope1 = req.Scope1,
        Scope2 = req.Scope2,
        Scope3 = req.Scope3,
        Type = req.Type,
        Content = req.Content,
        OptionA = req.OptionA,
        OptionB = req.OptionB,
        OptionC = req.OptionC,
        OptionD = req.OptionD,
        Answer = NormalizeAnswer(req.Answer, req.Type)
    };

    private static QuestionDto ToDto(Question q) => new()
    {
        Id = q.Id,
        Scope1 = q.Scope1,
        Scope2 = q.Scope2,
        Scope3 = q.Scope3,
        Type = q.Type.ToString(),
        Content = q.Content,
        OptionA = q.OptionA,
        OptionB = q.OptionB,
        OptionC = q.OptionC,
        OptionD = q.OptionD,
        Answer = q.Answer,
        UpdatedAt = q.UpdatedAt
    };

    private static string NormalizeAnswer(string answer, QuestionType type)
    {
        if (string.IsNullOrWhiteSpace(answer)) return "";
        answer = answer.Trim();
        // 判断题兼容中英文同义词，直接映射成 A(正确)/B(错误)
        if (type == QuestionType.Judge)
        {
            var up = answer.ToUpperInvariant();
            if (up is "A" or "正确" or "对" or "TRUE" or "YES" or "T" or "Y" or "√") return "A";
            if (up is "B" or "错误" or "错" or "FALSE" or "NO" or "F" or "N" or "×") return "B";
        }
        var letters = answer.Trim().ToUpperInvariant()
            .Where(c => c is 'A' or 'B' or 'C' or 'D').Distinct().OrderBy(c => c);
        var joined = string.Concat(letters);
        if (type == QuestionType.Single || type == QuestionType.Judge)
            return joined.Length > 0 ? joined[0].ToString() : "";
        return joined; // 多选保留组合
    }

    private static QuestionType? ParseType(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.ToLower() switch
        {
            "single" or "单选" or "单选题" => QuestionType.Single,
            "multiple" or "多选" or "多选题" => QuestionType.Multiple,
            "judge" or "判断" or "判断题" or "truefalse" => QuestionType.Judge,
            _ => null
        };
    }

    private static string Get(string[] row, int idx) => idx >= 0 && idx < row.Length ? row[idx] : "";
}

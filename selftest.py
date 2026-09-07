import urllib.request, json, sys, urllib.parse

BASE = "http://localhost:5000"

def req(method, path, data=None, token=None, raw=None, ctype=None, filename=None):
    url = BASE + path
    headers = {}
    body = None
    if token:
        headers["Authorization"] = "Bearer " + token
    if raw is not None:
        body = raw
        headers["Content-Type"] = ctype or "application/json"
    elif data is not None:
        body = json.dumps(data).encode()
        headers["Content-Type"] = "application/json"
    r = urllib.request.Request(url, data=body, method=method, headers=headers)
    try:
        with urllib.request.urlopen(r) as resp:
            return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

def j(resp):
    try:
        return json.loads(resp)
    except Exception:
        return resp

def check(name, cond, extra=""):
    print(("PASS " if cond else "FAIL ") + name + ("  " + extra if extra else ""))
    if not cond:
        global FAILED; FAILED += 1

FAILED = 0

# 1. 管理员登录
st, body = req("POST", "/api/auth/login", {"Username": "admin", "Password": "admin"})
check("管理员登录", st == 200)
admin = j(body)["token"]

# 2. 题库数量（种子应有 45 题）
st, body = req("GET", "/api/questions?pageSize=1", token=admin)
total = j(body).get("total", 0)
check("题库种子题目数=45", total == 45, f"total={total}")

# 3. 单题创建
st, body = req("POST", "/api/questions", {
    "Scope1": "物理", "Scope2": "力学", "Type": "Single",
    "Content": "1+1=?", "OptionA": "1", "OptionB": "2", "OptionC": "3", "OptionD": "4", "Answer": "B"
}, token=admin)
check("创建单题", st == 200)
new_id = j(body)["id"]

# 4. 查询（按范围）
st, body = req("GET", "/api/questions?scope=" + urllib.parse.quote("物理"), token=admin)
check("按知识范围查询", j(body)["total"] >= 1)

# 5. 批量 CSV 导入
csv = "Scope1,Scope2,Scope3,Type,Content,OptionA,OptionB,OptionC,OptionD,Answer\n"
csv += "化学,,,Single,水化学式?,H2O,CO2,O2,N2,A\n"
csv += "化学,,,Judge,铁能导电,正确,错误,,,A\n"
csv += "化学,,,Multiple,哪些是金属,铁,铜,氧,硫,AB\n"
boundary = "----btest123"
raw = (f"--{boundary}\r\n").encode()
raw += b'Content-Disposition: form-data; name="file"; filename="q.csv"\r\n'
raw += b"Content-Type: text/csv\r\n\r\n"
raw += csv.encode("utf-8") + b"\r\n"
raw += f"--{boundary}--\r\n".encode()
st, body = req("POST", "/api/questions/import", raw=raw,
               ctype=f"multipart/form-data; boundary={boundary}", token=admin)
imp = j(body)
check("CSV 批量导入成功数=3", st == 200 and imp.get("success") == 3, str(imp))

# 6. 发布考试（二维选题：数学/单选3、数学/判断2、语文/多选2）
rules = [
    {"Scope": "数学", "Type": "Single", "Count": 3, "ScorePerQuestion": 5},
    {"Scope": "数学", "Type": "Judge", "Count": 2, "ScorePerQuestion": 5},
    {"Scope": "语文", "Type": "Multiple", "Count": 2, "ScorePerQuestion": 10},
]
st, body = req("POST", "/api/exams", {
    "Title": "期中模拟考", "DurationMinutes": 30,
    "TargetMode": "All", "Rules": rules
}, token=admin)
check("发布考试(二维选题)", st == 200, f"status={st} body={body[:120]}")
exam = j(body)
exam_id = exam["id"]
check("考试总分=45", exam["totalScore"] == 45, f"totalScore={exam['totalScore']}")
check("考试总题数=7", exam["totalQuestions"] == 7, f"n={exam['totalQuestions']}")
check("矩阵含数学行", "数学" in exam["scopes"])

# 7. 考生登录
st, body = req("POST", "/api/auth/login", {"Username": "student1", "Password": "student1"})
check("考生登录", st == 200)
stu = j(body)["token"]
check("考生角色非Admin", j(body)["isAdmin"] == False)

# 8. 考生可见考试
st, body = req("GET", "/api/exams/available", token=stu)
avail = j(body)
check("考生可见该考试", any(e["id"] == exam_id for e in avail), str([e["id"] for e in avail]))

# 9. 开始考试
st, body = req("POST", f"/api/exams/{exam_id}/start", token=stu)
start = j(body)
check("开始考试返回题目(不含答案)", st == 200 and len(start["questions"]) == 7)
check("题目不含标准答案字段", all("Answer" not in q for q in start["questions"]))

# 10. 取标准答案并交卷（全对）
qids = [q["id"] for q in start["questions"]]
answers = []
for qid in qids:
    st2, b2 = req("GET", f"/api/questions/{qid}", token=admin)
    ans = j(b2)["answer"]
    answers.append({"QuestionId": qid, "Selected": ans})
st, body = req("POST", f"/api/exams/{exam_id}/submit", {"Answers": answers, "Early": True}, token=stu)
res = j(body)
check("交卷判分-全对满分", st == 200 and res["score"] == 45, f"score={res.get('score')} correct={res.get('correctCount')}")

# 11. 第二个考生交卷（故意错几题）
st, body = req("POST", "/api/auth/login", {"Username": "student2", "Password": "student2"})
stu2 = j(body)["token"]
req("POST", f"/api/exams/{exam_id}/start", token=stu2)
answers2 = []
for qid in qids:
    st2, b2 = req("GET", f"/api/questions/{qid}", token=admin)
    ans = j(b2)["answer"]
    # 故意把单选/判断选错：反转
    wrong = "C" if ans != "C" else "D"
    answers2.append({"QuestionId": qid, "Selected": wrong})
req("POST", f"/api/exams/{exam_id}/submit", {"Answers": answers2, "Early": True}, token=stu2)

# 12. 实时排名
st, body = req("GET", f"/api/exams/{exam_id}/ranking", token=stu)
rank = j(body)
check("排名含2人且第1为student1", st == 200 and len(rank) == 2 and rank[0]["displayName"] == "student1",
      str([(r["rank"], r["displayName"], r["score"]) for r in rank]))

# 13. 历史成绩
st, body = req("GET", "/api/history/exams", token=admin)
hist = j(body)
check("历史考试概览有记录", st == 200 and len(hist) >= 1)
st, body = req("GET", f"/api/history/exams/{exam_id}/results", token=admin)
rows = j(body)
check("成绩明细2行", st == 200 and len(rows) == 2)

# 14. 导出 CSV
st, body = req("GET", f"/api/history/exams/{exam_id}/export", token=admin)
check("导出成绩CSV", st == 200 and "排名" in body and "student1" in body, f"len={len(body)}")

# 15. 关闭考试
st, body = req("POST", f"/api/exams/{exam_id}/close", token=admin)
check("关闭考试", st == 200)

# 16. 权限校验：考生不能访问题库
st, body = req("GET", "/api/questions", token=stu)
check("考生无题库权限(403)", st == 403, f"status={st}")

print("\n==== 自测完成，失败项：%d ====" % FAILED)
sys.exit(1 if FAILED else 0)

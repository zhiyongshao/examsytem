import urllib.request, json, sys, urllib.parse, datetime

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
    {"Scope1": "数学", "Type": "Single", "Count": 3, "ScorePerQuestion": 5},
    {"Scope1": "数学", "Type": "Judge", "Count": 2, "ScorePerQuestion": 5},
    {"Scope1": "语文", "Type": "Multiple", "Count": 2, "ScorePerQuestion": 10},
]
st, body = req("POST", "/api/exams", {
    "Title": "期中模拟考", "DurationMinutes": 30,
    "TargetMode": "All", "Rules": rules,
    "StartTime": "2026-01-01T00:00:00", "EndTime": "2026-12-31T23:59:59"
}, token=admin)
check("发布考试(二维选题)", st == 200, f"status={st} body={body[:120]}")
exam = j(body)
exam_id = exam["id"]
check("考试总分=45", exam["totalScore"] == 45, f"totalScore={exam['totalScore']}")
check("考试总题数=7", exam["totalQuestions"] == 7, f"n={exam['totalQuestions']}")
check("矩阵含数学行", any((c.get("scope1") or "") == "数学" for c in exam.get("rules", [])),
      str([(c.get("scope1"), c.get("type"), c.get("count")) for c in exam.get("rules", [])]))

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

# 14a. 考试管理：列表
st, body = req("GET", "/api/exams", token=admin)
exams = j(body)
check("管理-列表考试含1条", st == 200 and len(exams) == 1 and exams[0]["id"] == exam_id,
      f"len={len(exams)} ids={[e['id'] for e in exams]}")

# 14b. 考试管理：详情
st, body = req("GET", f"/api/exams/{exam_id}", token=admin)
det = j(body)
check("管理-详情标题/总分", st == 200 and det["title"] == "期中模拟考" and det["totalScore"] == 45,
      f"title={det.get('title')} total={det.get('totalScore')}")

# 14c. 考试管理：修改（已有作答，仅允许改标题；时间/对象变更会被忽略，不报错）
st, body = req("PUT", f"/api/exams/{exam_id}", {
    "Title": "期中模拟考-V2",
    "StartTime": "2026-01-01T00:00:00Z",
    "EndTime": "2026-12-31T23:59:00Z",
    "TargetMode": "All",
    "TargetUserIds": []
}, token=admin)
upd = j(body)
check("管理-修改标题生效", st == 200 and upd.get("title") == "期中模拟考-V2",
      f"status={st} title={upd.get('title')}")

# 14d. 考试管理：查看入口（All 模式，无密码、无指定用户）
st, body = req("GET", f"/api/exams/{exam_id}/entrance", token=admin)
ent = j(body)
check("管理-入口含前端路径", st == 200 and ent.get("targetMode") == "All" and (ent.get("frontendUrl") or "").startswith("/e/"),
      f"ent={ent}")

# 14e. 考试管理：重新发布（克隆+重抽题）
st, body = req("POST", f"/api/exams/{exam_id}/republish", token=admin)
rep = j(body)
new_exam_id = rep.get("id") if isinstance(rep, dict) else None
check("管理-重新发布新ID且标题加副本", st == 200 and new_exam_id and new_exam_id != exam_id and "副本" in (rep.get("title") or ""),
      f"status={st} new_id={new_exam_id} title={rep.get('title')}")
check("管理-新考试总分仍为45", st == 200 and rep.get("totalScore") == 45,
      f"totalScore={rep.get('totalScore')}")

# 14f. 考试管理：删除（先删无作答的副本，应成功）
if new_exam_id:
    st, body = req("DELETE", f"/api/exams/{new_exam_id}", token=admin)
    check("管理-删除无作答考试成功", st == 200, f"status={st}")
    st, body = req("GET", f"/api/exams/{new_exam_id}", token=admin)
    check("管理-删除后404", st == 404, f"status={st}")

# 14g. 考试管理：删除（有作答的原考试，应被拒绝 400）
st, body = req("DELETE", f"/api/exams/{exam_id}", token=admin)
check("管理-有作答的考试不可删", st == 400 and "作答记录" in (body or "") and "无法删除" in (body or ""),
      f"status={st} body={body[:120]}")

# 15. 关闭考试
st, body = req("POST", f"/api/exams/{exam_id}/close", token=admin)
check("关闭考试", st == 200)

# 16. 权限校验：考生不能访问题库
st, body = req("GET", "/api/questions", token=stu)
check("考生无题库权限(403)", st == 403, f"status={st}")

# 17. 按考生随机卷（PerCandidate）：发布时选择，每名考生开始考试时独立随机抽题
pc_rules = [
    {"Scope1": "数学", "Type": "Single", "Count": 6, "ScorePerQuestion": 5},
    {"Scope1": "语文", "Type": "Single", "Count": 6, "ScorePerQuestion": 5},
    {"Scope1": "英语", "Type": "Single", "Count": 6, "ScorePerQuestion": 5},
]
st, body = req("POST", "/api/exams", {
    "Title": "随机卷测试", "DurationMinutes": 30,
    "TargetMode": "All", "Rules": pc_rules, "PaperMode": "PerCandidate",
    "StartTime": "2026-01-01T00:00:00", "EndTime": "2026-12-31T23:59:59"
}, token=admin)
check("发布随机卷(PerCandidate)", st == 200, f"status={st} body={body[:120]}")
pc = j(body)
pc_id = pc["id"]
check("随机卷-返回paperMode=PerCandidate", pc.get("paperMode") == "PerCandidate", f"pm={pc.get('paperMode')}")
check("随机卷总分=90", pc["totalScore"] == 90, f"total={pc['totalScore']}")
check("随机卷总题数=18", pc["totalQuestions"] == 18, f"n={pc['totalQuestions']}")

def start_as(user):
    st, body = req("POST", "/api/auth/login", {"Username": user, "Password": user})
    tok = j(body)["token"]
    st, body = req("POST", f"/api/exams/{pc_id}/start", token=tok)
    return j(body)

a = start_as("student1")
b = start_as("student2")
c = start_as("student3")
check("随机卷-考生A开始返回18题", a.get("questions") and len(a["questions"]) == 18, f"n={len(a.get('questions',[]))}")
check("随机卷-考生B开始返回18题", b.get("questions") and len(b["questions"]) == 18, f"n={len(b.get('questions',[]))}")
check("随机卷-考生C开始返回18题", c.get("questions") and len(c["questions"]) == 18, f"n={len(c.get('questions',[]))}")

# 同一考生重复开始应一致（按会话存储，幂等）
a2 = start_as("student1")
check("随机卷-同考生重复开始卷面一致",
      [q["id"] for q in a["questions"]] == [q["id"] for q in a2["questions"]],
      f"a={[q['id'] for q in a['questions']][:6]}.. a2={[q['id'] for q in a2['questions']][:6]}..")

# 不同考生卷面应不同（至少一对顺序/集合不同）
ids_a = [q["id"] for q in a["questions"]]
ids_b = [q["id"] for q in b["questions"]]
ids_c = [q["id"] for q in c["questions"]]
diff = (ids_a != ids_b) or (ids_b != ids_c) or (ids_a != ids_c)
check("随机卷-不同考生卷面不同", diff, f"a={ids_a} b={ids_b} c={ids_c}")

# ============ 18. 账号类型 & 登录时间窗 & 监考看板 ============
NOW = datetime.datetime.utcnow()
def ISO(dt): return dt.strftime("%Y-%m-%dT%H:%M:%S")
future_start, future_end = ISO(NOW + datetime.timedelta(hours=2)), ISO(NOW + datetime.timedelta(hours=3))
past_start,   past_end   = ISO(NOW - datetime.timedelta(hours=1)), ISO(NOW + datetime.timedelta(hours=3))
ended_start,  ended_end  = ISO(NOW - datetime.timedelta(hours=3)), ISO(NOW - datetime.timedelta(hours=2))

def import_candidate(name, job):
    csv = "姓名,工号,部门\n" + job + "," + job + ",测试部\n"
    boundary = "----bcand"
    raw = ("--%s\r\n" % boundary).encode()
    raw += b'Content-Disposition: form-data; name="file"; filename="c.csv"\r\n'
    raw += b"Content-Type: text/csv\r\n\r\n"
    raw += csv.encode("utf-8") + b"\r\n"
    raw += ("--%s--\r\n" % boundary).encode()
    st, body = req("POST", "/api/users/import", raw=raw,
                   ctype="multipart/form-data; boundary=%s" % boundary, token=admin)
    return j(body)

def publish_specified(title, start, end, jobs):
    uids = []
    for job in jobs:
        imp = import_candidate(title, job)
        uids.append(imp["items"][0]["id"])
    st, body = req("POST", "/api/exams", {
        "Title": title, "DurationMinutes": 30,
        "TargetMode": "Specified", "TargetUserIds": uids,
        "Rules": [{"Scope1": "数学", "Type": "Single", "Count": 2, "ScorePerQuestion": 5}],
        "StartTime": start, "EndTime": end
    }, token=admin)
    return st, j(body)

# 18a. 未来考试：窗口外登录应被拒（401）
st_f, body_f = publish_specified("未来考试", future_start, future_end, ["FUTUREU"])
pwd_f = body_f.get("candidatePassword")
check("未来考试-发布成功且有统一密码", st_f == 200 and bool(pwd_f), f"status={st_f} pwd={pwd_f}")
st, body = req("POST", "/api/auth/login", {"Username": "FUTUREU", "Password": pwd_f})
check("未来考试-窗口外登录被拒(401)", st == 401, f"status={st} body={body[:80]}")

# 18b. 进行中考试：窗口内可登录(200)；另含一名永不登录的考生用于'未登陆'校验
st_p, body_p = publish_specified("进行中考试", past_start, past_end, ["WITHINU", "NEVERU"])
pwd_p = body_p.get("candidatePassword")
pid = body_p["id"]
st, body = req("POST", "/api/auth/login", {"Username": "WITHINU", "Password": pwd_p})
check("进行中考试-窗口内登录成功(200)", st == 200, f"status={st} body={body[:80]}")
# General 账号（seed student1）不受时间窗限制
st, body = req("POST", "/api/auth/login", {"Username": "student1", "Password": "student1"})
check("General账号-不受登录时间窗限制", st == 200, f"status={st}")

# 18c. 已结束考试：登录应被拒（401）
st_e, body_e = publish_specified("已结束考试", ended_start, ended_end, ["ENDEDU"])
pwd_e = body_e.get("candidatePassword")
st, body = req("POST", "/api/auth/login", {"Username": "ENDEDU", "Password": pwd_e})
check("已结束考试-登录被拒(401)", st == 401, f"status={st} body={body[:80]}")

# 18d. 监考看板：WITHINU 已登录未作答=未开始；NEVERU=未登陆
st, body = req("GET", f"/api/exams/{pid}/monitor", token=admin)
mon = j(body)
check("监考-候选名单含2人", st == 200 and mon["totalCandidates"] == 2, f"n={mon.get('totalCandidates')} body={str(mon)[:120]}")
def _find(lst, un): return next((c for c in lst if c["username"] == un), None)
wu = _find(mon["candidates"], "WITHINU")
nv = _find(mon["candidates"], "NEVERU")
check("监考-WITHINU状态=未开始", wu and wu["status"] == "未开始", f"wu={wu}")
check("监考-NEVERU状态=未登陆", nv and nv["status"] == "未登陆", f"nv={nv}")

# 18e. WITHINU 开始=考试中，交卷=已交卷+分数+排名
wtok = j(req("POST", "/api/auth/login", {"Username": "WITHINU", "Password": pwd_p})[1])["token"]
st, body = req("POST", f"/api/exams/{pid}/start", token=wtok)
pstart = j(body)
ans = []
for q in pstart["questions"]:
    st2, b2 = req("GET", f"/api/questions/{q['id']}", token=admin)
    ans.append({"QuestionId": q["id"], "Selected": j(b2)["answer"]})
req("POST", f"/api/exams/{pid}/submit", {"Answers": ans, "Early": True}, token=wtok)
st, body = req("GET", f"/api/exams/{pid}/monitor", token=admin)
mon2 = j(body)
wu2 = _find(mon2["candidates"], "WITHINU")
check("监考-WITHINU交卷后=已交卷", wu2 and wu2["status"] == "已交卷", f"wu2={wu2}")
check("监考-WITHINU得分=总分", wu2 and wu2["score"] == mon2["totalScore"], f"score={wu2.get('score')} total={mon2.get('totalScore')}")
check("监考-WITHINU排名第1", wu2 and wu2["rank"] == 1, f"rank={wu2.get('rank')}")
check("监考-已交卷计数=1", mon2["submittedCount"] == 1, f"sub={mon2['submittedCount']}")

print("\n==== 自测完成，失败项：%d ====" % FAILED)
sys.exit(1 if FAILED else 0)

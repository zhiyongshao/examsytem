import subprocess, time, urllib.request, json, os, urllib.parse, sys

API = "http://127.0.0.1:5000"
DB = "E:/Workbuddy/ExamSystem/exam_tpltest_%d.db" % os.getpid()

# 模拟前端下载的模板：顶部带 # 说明 + 判断题行
TEMPLATE = "\ufeff" + (
    "# ============ 题目批量导入 · 填写说明 ============\n"
    "# 3. Type 题型：Single / Multiple / Judge\n"
    "# 5. 判断 -> A(正确) 或 B(错误)\n"
    "# ==================================================\n"
    "Scope1,Scope2,Scope3,Type,Content,OptionA,OptionB,OptionC,OptionD,Answer\n"
    "物理,,,\"Single\",\"1+1=?\",\"1\",\"2\",\"3\",\"4\",\"B\"\n"
    "物理,力学,,\"Multiple\",\"下列哪些是力？\",\"重力\",\"摩擦力\",\"质量\",\"速度\",\"AB\"\n"
    "物理,,,\"Judge\",\"光速比声速快\",\"正确\",\"错误\",\"\",\"\",\"A\"\n"
    "数学,,,\"Single\",\"圆周率？\",\"3.0\",\"3.14\",\"3.14159\",\"3.1415926\",\"B\"\n"
)

def req(method, path, token=None, data=None, raw=None):
    headers = {"Content-Type": "application/json"}
    if token: headers["Authorization"] = "Bearer " + token
    body = json.dumps(data).encode() if data is not None else None
    r = urllib.request.Request(API + path, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(r, timeout=10) as resp:
            return resp.status, resp.read().decode("utf-8-sig")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8-sig")

p = subprocess.Popen(["C:/Program Files/dotnet/dotnet.exe", "run", "-c", "Release", "--no-build"],
                     cwd="E:/Workbuddy/ExamSystem", env={**os.environ, "EXAM_DB": "Data Source=" + DB},
                     stdout=open("E:/Workbuddy/ExamSystem/tpltest.log", "w"), stderr=subprocess.STDOUT)
try:
    time.sleep(14)
    st, body = req("POST", "/api/auth/login", data={"username": "admin", "password": "admin"})
    tok = json.loads(body)["token"]
    print("login:", st)
    # 上传带 # 注释的模板
    boundary = "----tpltest"
    parts = []
    parts.append(("--" + boundary).encode())
    parts.append(b'Content-Disposition: form-data; name="file"; filename="t.csv"')
    parts.append(b"Content-Type: text/csv")
    parts.append(b"")
    parts.append(TEMPLATE.encode("utf-8-sig"))
    parts.append(("--" + boundary + "--").encode())
    parts.append(b"")
    payload = b"\r\n".join(parts)
    r = urllib.request.Request(API + "/api/questions/import",
        data=payload, headers={"Authorization": "Bearer " + tok, "Content-Type": "multipart/form-data; boundary=" + boundary}, method="POST")
    with urllib.request.urlopen(r, timeout=10) as resp:
        res = json.loads(resp.read().decode("utf-8-sig"))
    print("import status:", resp.status)
    print("success:", res.get("success"), "failed:", res.get("failed"), "errors:", res.get("errors"))
    ok = res.get("success") == 4 and res.get("failed") == 0
    print("RESULT:", "PASS" if ok else "FAIL")
finally:
    p.terminate()
    for f in [DB, DB + "-wal", DB + "-shm"]:
        try: os.remove(f)
        except: pass

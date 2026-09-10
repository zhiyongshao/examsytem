    // ==== 状态 ====
    let currentExamIdForExport = null;
    let allScopes = [];
    let importedCandidates = [];
    let qPage = 1;
    let qPageSize = 15;
    let qTotal = 0;
    let qTotalPages = 1;
    let selectedQIds = new Set();

    // ==== 初始化 ====
    (function init() {
      try {
        var user = getUser();
        console.log('[Admin] user:', JSON.stringify(user));
        if (!user || !user.role) {
          alert('未登录或登录已过期，请重新登录');
          location.href = 'index.html';
          return;
        }
        if (user.role !== 'Admin') {
          alert('您不是管理员，无法访问此页面');
          location.href = 'exam.html';
          return;
        }
        document.getElementById('userName').textContent = user.displayName || user.username;
        loadScopes();
        loadQuestions();
        loadHistory();
        loadExams();
        initMatrix();
        initExamTimes();
      } catch(err) {
        console.error('[Admin] Init error:', err);
        alert('页面初始化失败：' + err.message);
      }
    })();

    // ==== Tab 切换 ====
    function switchTab(tab) {
      document.getElementById('tabQuestions').style.display = tab === 'questions' ? '' : 'none';
      document.getElementById('tabPublish').style.display   = tab === 'publish'   ? '' : 'none';
      document.getElementById('tabExams').style.display     = tab === 'exams'     ? '' : 'none';
      document.getElementById('tabHistory').style.display   = tab === 'history'   ? '' : 'none';
      document.getElementById('tabMonitor').style.display   = tab === 'monitor'   ? '' : 'none';
      // 同步顶部 tab 按钮
      var topBtns = document.querySelectorAll('#tabBar button');
      for (var i = 0; i < topBtns.length; i++) {
        var b = topBtns[i];
        b.className = b.getAttribute('data-tab') === tab ? 'btn btn-primary btn-sm' : 'btn btn-outline btn-sm';
      }
      // 同步侧边栏
      var sideItems = document.querySelectorAll('#adminSidebar .admin-sidebar-item');
      for (var j = 0; j < sideItems.length; j++) {
        var s = sideItems[j];
        s.className = s.getAttribute('data-tab') === tab ? 'admin-sidebar-item active' : 'admin-sidebar-item';
      }
      if (tab === 'history') loadHistory();
      if (tab === 'exams') {
        var examBody = document.getElementById('examMgmtBody');
        // 防御：若 tbody 仍是初始"加载中..."（老缓存/admin.js 旧版本导致 loadExams 缺失），强制 reload
        if (examBody && examBody.textContent.indexOf('加载中') !== -1) {
          try { location.reload(); return; } catch(e) { loadExams(); }
        } else {
          loadExams();
        }
      }
      if (tab === 'monitor') { loadMonitorExams(); }
      else { stopMonitorPolling(); }
    }

    // ==================== 考试监控（监考看板）====================
    var monitorTimer = null;
    var monitorExamId = null;

    async function loadMonitorExams() {
      try {
        var list = await api('GET', '/exams');
        var sel = document.getElementById('monitorExamSelect');
        if (!list || !list.length) {
          sel.innerHTML = '<option value="">暂无考试</option>';
          document.getElementById('monitorBody').innerHTML = '<tr><td colspan="7" class="empty-state">暂无考试</td></tr>';
          document.getElementById('monitorSummary').textContent = '';
          return;
        }
        // 默认优先显示进行中的，其次按 ID 倒序
        list.sort(function(a, b) {
          if (a.status === 'Published' && b.status !== 'Published') return -1;
          if (b.status === 'Published' && a.status !== 'Published') return 1;
          return b.id - a.id;
        });
        sel.innerHTML = list.map(function(e) {
          var tag = e.status === 'Published' ? '（进行中）' : '（已关闭）';
          return '<option value="' + e.id + '">' + escHtml(e.title) + tag + '</option>';
        }).join('');
        monitorExamId = sel.value;
        startMonitorPolling();
      } catch(e) { toast('加载考试列表失败：' + e.message, 'error'); }
    }

    function onMonitorExamChange() {
      monitorExamId = document.getElementById('monitorExamSelect').value;
      startMonitorPolling();
    }

    function startMonitorPolling() {
      stopMonitorPolling();
      if (!monitorExamId) return;
      document.getElementById('monitorLiveTag').style.display = '';
      loadMonitorOnce();
      monitorTimer = setInterval(loadMonitorOnce, 3000);
    }

    function stopMonitorPolling() {
      if (monitorTimer) { clearInterval(monitorTimer); monitorTimer = null; }
      var tag = document.getElementById('monitorLiveTag');
      if (tag) tag.style.display = 'none';
    }

    async function loadMonitorOnce() {
      if (!monitorExamId) return;
      try {
        var d = await api('GET', '/exams/' + monitorExamId + '/monitor');
        renderMonitor(d);
      } catch(e) { console.error('[Monitor] error:', e); }
    }

    function renderMonitor(d) {
      var sum = document.getElementById('monitorSummary');
      sum.innerHTML = '应到 <b>' + d.totalCandidates + '</b> · 已登录 <b>' + d.loggedInCount + '</b> · 考试中 <b>' + d.inProgressCount + '</b> · 已交卷 <b>' + d.submittedCount + '</b>'
        + (d.isEnded ? ' · <span style="color:var(--danger);">已结束（已自动排名）</span>' : '');

      var rows = '';
      for (var i = 0; i < d.candidates.length; i++) {
        var c = d.candidates[i];
        var statusBadge;
        if (c.status === '已交卷') statusBadge = '<span class="tag" style="background:#f6ffed;color:#52c41a;">已交卷</span>';
        else if (c.status === '考试中') statusBadge = '<span class="tag" style="background:#e6f7ff;color:#1890ff;">考试中</span>';
        else if (c.status === '未开始') statusBadge = '<span class="tag" style="background:#fff7e6;color:#fa8c16;">未开始</span>';
        else statusBadge = '<span class="tag tag-scope">未登陆</span>';
        var score = c.status === '已交卷' ? (c.score + ' / ' + d.totalScore) : '—';
        var rank = c.rank ? ('第 ' + c.rank + ' 名') : '—';
        rows += '<tr>' +
          '<td>' + (i + 1) + '</td>' +
          '<td><strong>' + escHtml(c.displayName || c.username) + '</strong></td>' +
          '<td>' + escHtml(c.jobNo || '-') + '</td>' +
          '<td>' + escHtml(c.department || '-') + '</td>' +
          '<td>' + statusBadge + '</td>' +
          '<td>' + score + '</td>' +
          '<td>' + rank + '</td>' +
        '</tr>';
      }
      if (!d.candidates.length) rows = '<tr><td colspan="7" class="empty-state">无考生</td></tr>';
      document.getElementById('monitorBody').innerHTML = rows;
    }

    // ---- 监考页面：全屏 ----
    function toggleMonitorFullscreen() {
      var el = document.getElementById('monitorFullscreenWrap');
      if (!el) return;
      var fsEl = document.fullscreenElement || document.webkitFullscreenElement || document.msFullscreenElement;
      if (!fsEl) {
        var req = el.requestFullscreen || el.webkitRequestFullscreen || el.msRequestFullscreen;
        if (req) {
          try { req.call(el); } catch (e) { console.warn('[Monitor] requestFullscreen failed:', e); }
        } else {
          toast('当前浏览器不支持全屏 API', 'error');
        }
      } else {
        var exit = document.exitFullscreen || document.webkitExitFullscreen || document.msExitFullscreen;
        if (exit) {
          try { exit.call(document); } catch (e) { console.warn('[Monitor] exitFullscreen failed:', e); }
        }
      }
    }

    // Esc 或浏览器退出全屏时，按钮文字要同步
    ['fullscreenchange', 'webkitfullscreenchange', 'msfullscreenchange'].forEach(function (ev) {
      document.addEventListener(ev, function () {
        var btn = document.getElementById('btnMonitorFullscreen');
        if (!btn) return;
        var fsEl = document.fullscreenElement || document.webkitFullscreenElement || document.msFullscreenElement;
        btn.textContent = fsEl ? '⛶ 退出全屏' : '⛶ 全屏';
      });
    });

    // ==================== 题库管理 ====================

    async function loadScopes() {
      try {
        var qs = await api('GET', '/questions?pageSize=999');
        var scopeSet = new Set();
        var items = qs.items || qs;
        for (var i = 0; i < items.length; i++) {
          var q = items[i];
          if (q.scope1) scopeSet.add(q.scope1);
          if (q.scope2) scopeSet.add(q.scope2);
          if (q.scope3) scopeSet.add(q.scope3);
        }
        allScopes = Array.from(scopeSet).sort();
        var sel = document.getElementById('filterScope');
        sel.innerHTML = '<option value="">全部范围</option>' + allScopes.map(function(s) { return '<option value="' + s + '">' + s + '</option>'; }).join('');
        updateScopeDatalist();
      } catch(e) { console.error('Load scopes error:', e); }
    }

    function updateScopeDatalist() {
      var dl = document.getElementById('scopeList');
      if (dl) dl.innerHTML = allScopes.map(function(s) { return '<option value="' + s + '">'; }).join('');
    }

    var debounceTimer = null;
    function debounceLoadQuestions() { clearTimeout(debounceTimer); debounceTimer = setTimeout(function() { loadQuestions(true); }, 400); }

    async function loadQuestions(resetPage) {
      if (resetPage) qPage = 1;
      var tbody = document.getElementById('questionTableBody');
      try {
        var url = '/questions?page=' + qPage + '&pageSize=' + qPageSize;
        var key = (document.getElementById('searchKey').value || '').trim();
        var scope = document.getElementById('filterScope').value;
        var type = document.getElementById('filterType').value;
        if (key) url += '&keyword=' + encodeURIComponent(key);
        if (scope) url += '&scope=' + encodeURIComponent(scope);
        if (type) url += '&type=' + type;

        console.log('[Admin] Loading questions:', url);
        var data = await api('GET', url);
        var items = data.items || data;
        console.log('[Admin] Got', items.length, 'questions');
        renderQuestionTable(items);

        // 后端返回字段名为 total；兼容旧字段名 totalCount
        qTotal = (data.total != null ? data.total : (data.totalCount != null ? data.totalCount : items.length));
        qTotalPages = Math.max(1, Math.ceil(qTotal / qPageSize));
        renderQPager();
        updateBatchBar();
      } catch(e) {
        console.error('[Admin] Load questions error:', e);
        tbody.innerHTML = '<tr><td colspan="7" class="empty-state" style="color:var(--danger);">❌ 加载失败：' + escHtml(e.message) + '<br><br><button class="btn btn-outline btn-sm" onclick="loadQuestions()">重试</button></td></tr>';
      }
    }

    function renderQuestionTable(items) {
      var tbody = document.getElementById('questionTableBody');
      if (!items || !items.length) {
        tbody.innerHTML = '<tr><td colspan="7" class="empty-state"><div class="icon">📭</div><p>暂无题目</p><p style="font-size:12px;margin-top:4px;">点击上方「新建题目」或「批量导入」添加</p></td></tr>';
        return;
      }
      var html = '';
      for (var i = 0; i < items.length; i++) {
        var q = items[i];
        var scopes = [q.scope1, q.scope2, q.scope3].filter(Boolean);
        var scopeTags = scopes.map(function(s) { return '<span class="tag tag-scope">' + escHtml(s) + '</span>'; }).join('');
        var typeLabel = TYPE_LABELS[q.type] || q.type;
        var checked = selectedQIds.has(q.id) ? ' checked' : '';
        html += '<tr>' +
          '<td class="col-check"><input type="checkbox" class="q-check" data-qid="' + q.id + '"' + checked + ' onchange="onQRowCheck(this,' + q.id + ')"></td>' +
          '<td>' + q.id + '</td>' +
          '<td>' + scopeTags + '</td>' +
          '<td><span class="tag tag-type">' + typeLabel + '</span></td>' +
          '<td style="max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="' + escHtml(q.content) + '">' + escHtml(q.content) + '</td>' +
          '<td><strong>' + escHtml(q.answer) + '</strong></td>' +
          '<td><div class="action-btns">' +
            '<button class="btn btn-primary btn-sm" onclick="editQuestion(' + q.id + ')">✏️</button>' +
            '<button class="btn btn-danger btn-sm" onclick="deleteQuestion(' + q.id + ')">🗑️</button>' +
          '</div></td>' +
        '</tr>';
      }
      tbody.innerHTML = html;
      // 渲染后同步全选框的半选/全选状态
      syncQSelectAllState();
    }

    // ---- 多选 / 全选 / 批量删除 ----
    function onQRowCheck(cb, id) {
      if (cb.checked) selectedQIds.add(id); else selectedQIds.delete(id);
      syncQSelectAllState();
      updateBatchBar();
    }

    function onQSelectAll(cb) {
      var boxes = document.querySelectorAll('#questionTableBody input.q-check');
      for (var i = 0; i < boxes.length; i++) {
        var b = boxes[i];
        b.checked = cb.checked;
        var id = parseInt(b.getAttribute('data-qid'), 10);
        if (cb.checked) selectedQIds.add(id); else selectedQIds.delete(id);
      }
      updateBatchBar();
    }

    function syncQSelectAllState() {
      var boxes = document.querySelectorAll('#questionTableBody input.q-check');
      var sa = document.getElementById('qSelectAll');
      if (!sa) return;
      if (boxes.length === 0) { sa.checked = false; sa.indeterminate = false; return; }
      var checkedCount = 0;
      for (var i = 0; i < boxes.length; i++) if (boxes[i].checked) checkedCount++;
      sa.checked = checkedCount === boxes.length;
      sa.indeterminate = checkedCount > 0 && checkedCount < boxes.length;
    }

    function updateBatchBar() {
      var bar = document.getElementById('qBatchBar');
      if (!bar) return;
      var n = selectedQIds.size;
      bar.style.display = n > 0 ? '' : 'none';
      var cnt = document.getElementById('qSelectedCount');
      if (cnt) cnt.textContent = n;
    }

    function clearQSelection() {
      selectedQIds.clear();
      var boxes = document.querySelectorAll('#questionTableBody input.q-check');
      for (var i = 0; i < boxes.length; i++) boxes[i].checked = false;
      var sa = document.getElementById('qSelectAll');
      if (sa) { sa.checked = false; sa.indeterminate = false; }
      updateBatchBar();
    }

    async function batchDeleteQuestions() {
      if (selectedQIds.size === 0) { toast('请先勾选要删除的题目', 'warn'); return; }
      var ids = Array.from(selectedQIds);
      if (!confirm('确定批量删除选中的 ' + ids.length + ' 道题目吗？此操作不可撤销！')) return;
      try {
        var res = await api('POST', '/questions/batch-delete', { ids: ids });
        var removed = (res && res.removed) || ids.length;
        toast('已删除 ' + removed + ' 道题目', 'success');
        selectedQIds.clear();
        // 若当前页被删空且不是第一页，回退一页避免空白
        if (qPage > 1 && qTotal - removed <= (qPage - 1) * qPageSize) qPage = qPage - 1;
        loadScopes();
        loadQuestions();
      } catch(e) { toast(e.message, 'error'); }
    }

    // ---- 分页 ----
    function renderQPager() {
      var p = document.getElementById('questionPagination');
      if (!p) return;
      var sizes = [10, 15, 20, 30, 50];
      var sizeOpts = sizes.map(function(s) {
        return '<option value="' + s + '"' + (s === qPageSize ? ' selected' : '') + '>' + s + '</option>';
      }).join('');
      var html =
        '<button class="btn btn-outline btn-sm" ' + (qPage <= 1 ? 'disabled' : '') + ' onclick="goQPage(' + (qPage - 1) + ')">‹ 上一页</button>' +
        '<span class="q-page-info">第 ' + qPage + ' / ' + qTotalPages + ' 页，共 ' + qTotal + ' 题</span>' +
        '<button class="btn btn-outline btn-sm" ' + (qPage >= qTotalPages ? 'disabled' : '') + ' onclick="goQPage(' + (qPage + 1) + ')">下一页 ›</button>' +
        '<span class="q-page-size">每页 <select id="qPageSizeSel" onchange="changeQPageSize(this.value)">' + sizeOpts + '</select> 条</span>';
      p.innerHTML = html;
    }

    function goQPage(pg) {
      pg = Math.max(1, Math.min(qTotalPages, pg));
      if (pg === qPage) return;
      qPage = pg;
      loadQuestions();
    }

    function changeQPageSize(v) {
      qPageSize = parseInt(v, 10) || 15;
      qPage = 1;
      loadQuestions();
    }

    function openQuestionModal(id) {
      document.getElementById('modalTitle').textContent = id ? '编辑题目' : '新建题目';
      document.getElementById('qId').value = id || '';
      document.getElementById('qScope1').value = '';
      document.getElementById('qScope2').value = '';
      document.getElementById('qScope3').value = '';
      document.getElementById('qType').value = 'Single';
      document.getElementById('qContent').value = '';
      document.getElementById('qOptA').value = '';
      document.getElementById('qOptB').value = '';
      document.getElementById('qOptC').value = '';
      document.getElementById('qOptD').value = '';
      document.getElementById('qAnswer').value = '';
      document.getElementById('optionsGroup').style.display = '';
      document.getElementById('questionModal').classList.add('active');
      if (id) loadQuestionDetail(id);
      // 聚焦第一个输入框
      setTimeout(function() { document.getElementById('qScope1').focus(); }, 100);
    }

    async function loadQuestionDetail(id) {
      try {
        var q = await api('GET', '/questions/' + id);
        document.getElementById('qScope1').value = q.scope1 || '';
        document.getElementById('qScope2').value = q.scope2 || '';
        document.getElementById('qScope3').value = q.scope3 || '';
        document.getElementById('qType').value = q.type || 'Single';
        document.getElementById('qContent').value = q.content || '';
        document.getElementById('qOptA').value = q.optionA || '';
        document.getElementById('qOptB').value = q.optionB || '';
        document.getElementById('qOptC').value = q.optionC || '';
        document.getElementById('qOptD').value = q.optionD || '';
        document.getElementById('qAnswer').value = q.answer || '';
        toggleOptions(q.type);
      } catch(e) { toast(e.message, 'error'); }
    }

    function toggleOptions(type) {
      document.getElementById('optionsGroup').style.display = type === 'Judge' ? 'none' : '';
    }

    function closeQuestionModal() { document.getElementById('questionModal').classList.remove('active'); }

    async function saveQuestion() {
      var id = document.getElementById('qId').value;
      var body = {
        scope1: document.getElementById('qScope1').value.trim(),
        scope2: document.getElementById('qScope2').value.trim() || null,
        scope3: document.getElementById('qScope3').value.trim() || null,
        type: document.getElementById('qType').value,
        content: document.getElementById('qContent').value.trim(),
        optionA: document.getElementById('qOptA').value.trim(),
        optionB: document.getElementById('qOptB').value.trim(),
        optionC: document.getElementById('qOptC').value.trim(),
        optionD: document.getElementById('qOptD').value.trim(),
        answer: document.getElementById('qAnswer').value.trim().toUpperCase()
      };
      if (!body.scope1) { toast('请填写知识范围 1', 'error'); return; }
      if (!body.content) { toast('请填写题目内容', 'error'); return; }
      if (!body.answer) { toast('请填写标准答案', 'error'); return; }
      if (body.type !== 'Judge' && !body.answer.match(/^[ABCD]{1,4}$/)) { toast('答案格式不正确（单选填1个字母，多选填多个字母如AB）', 'error'); return; }
      if (body.type === 'Judge' && !['A','B'].includes(body.answer)) { toast('判断题答案应为 A（正确）或 B（错误）', 'error'); return; }

      try {
        if (id) {
          await api('PUT', '/questions/' + id, body);
          toast('修改成功', 'success');
        } else {
          await api('POST', '/questions', body);
          toast('创建成功', 'success');
        }
        closeQuestionModal();
        loadQuestions();
        loadScopes();
      } catch(e) { toast(e.message, 'error'); }
    }

    function editQuestion(id) { openQuestionModal(id); }

    async function deleteQuestion(id) {
      if (!confirm('确定删除这道题吗？此操作不可撤销！')) return;
      try {
        await api('DELETE', '/questions/' + id);
        toast('已删除', 'success');
        selectedQIds.delete(id);
        updateBatchBar();
        loadQuestions();
      } catch(e) { toast(e.message, 'error'); }
    }

    // ---- Excel 模板下载 ----
    async function downloadXlsxTemplate() {
      try {
        var token = getToken();
        var res = await fetch(API + '/questions/template', {
          headers: { 'Authorization': 'Bearer ' + token }
        });
        if (!res.ok) throw new Error('下载失败');
        var blob = await res.blob();
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = '题目导入模板.xlsx';
        a.click();
        URL.revokeObjectURL(url);
        toast('Excel 模板已下载，按「填写说明」页填写后导入即可', 'success');
      } catch(e) { toast(e.message, 'error'); }
    }

    // ---- CSV 模板下载 ----
    function downloadCsvTemplate() {
      var csv = '\uFEFF'; // BOM for Excel UTF-8
      // === 填写说明（# 开头为注释行，导入时自动忽略）===
      csv += '# ============ 题目批量导入 · 填写说明 ============\n';
      csv += '# 1. 第一行是表头，请勿删除或修改表头列名（列顺序也请勿调整）\n';
      csv += '# 2. Scope1 / Scope2 / Scope3：知识范围，同一道题可同时属于多个范围；不填的留空\n';
      csv += '# 3. Type 题型：Single(单选) / Multiple(多选) / Judge(判断)\n';
      csv += '# 4. OptionA~D：四个选项内容；判断题只需填 A、B 两列（正确/错误），留空系统会自动补\n';
      csv += '# 5. Answer 标准答案：\n';
      csv += '#      · 单选  → 单个字母  如 A / B / C / D\n';
      csv += '#      · 多选  → 字母组合  如 AB、ACD（顺序不限）\n';
      csv += '#      · 判断  → A 表示正确，B 表示错误\n';
      csv += '#      ⚠ 判断题不要写 对/错、是/否、yes/no、true/false，否则该行导入失败\n';
      csv += '# 6. 任意一列（题目/选项/范围）的内容里若含英文逗号(,)，必须用英文双引号把整列包起来\n';
      csv += '#      例如：某选项内容是 "A,B" → CSV里写成 "A,B"（带双引号）\n';
      csv += '#      不加引号的话，逗号会被当成列分隔符，导致该行列数错位、导入失败\n';
      csv += '# 7. 本文件顶部的 # 说明行在导入时会被自动跳过，可放心保留\n';
      csv += '# ==================================================\n';
      csv += 'Scope1,Scope2,Scope3,Type,Content,OptionA,OptionB,OptionC,OptionD,Answer\n';
      csv += '物理,,,"Single","1+1=?","1","2","3","4","B"\n';
      csv += '物理,力学,,"Multiple","下列哪些是力？（多选）","重力","摩擦力","质量","速度","AB"\n';
      csv += '物理,,,"Judge","光速比声速快","正确","错误","","","A"\n';
      csv += '数学,,,"Single","圆周率约等于？","3.0","3.14","3.14159","3.1415926","B"\n';

      var blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
      var url = URL.createObjectURL(blob);
      var a = document.createElement('a');
      a.href = url;
      a.download = '题目导入模板.csv';
      a.click();
      URL.revokeObjectURL(url);
      toast('模板已下载，请按文件顶部的填写说明填写后导入', 'success');
    }

    // ---- CSV 导入 ----
    async function importCsv(input) {
      var file = input.files[0];
      if (!file) return;
      var formData = new FormData();
      formData.append('file', file);
      try {
        var token = getToken();
        var res = await fetch(API + '/questions/import', {
          method: 'POST',
          headers: { 'Authorization': 'Bearer ' + token },
          body: formData
        });
        var _raw = await res.text();
        var data = null;
        try { data = _raw ? JSON.parse(_raw) : null; } catch { data = { message: _raw.slice(0,200) }; }
        if (!res.ok) throw new Error((data && data.message) || (res.status === 401 ? '登录已过期或未登录，请重新登录' : '导入失败（HTTP ' + res.status + '）'));
        var msg = '导入完成：成功 ' + (data.success || 0) + ' 条';
        if (data.failed > 0) msg += '，失败 ' + data.failed + ' 条';
        toast(msg, data.failed > 0 ? 'warn' : 'success');
        if (data.errors && data.errors.length) console.warn('Import errors:', data.errors);
        loadQuestions();
        loadScopes();
      } catch(e) { toast(e.message, 'error'); }
      input.value = '';
    }

    // ==================== 发布考试 ====================

    function initMatrix() { addMatrixRow(); addMatrixRow(); addMatrixRow(); }

    function addMatrixRow() {
      var tbody = document.getElementById('matrixBody');
      var row = document.createElement('tr');
      row.innerHTML =
        '<td><input type="text" placeholder="范围1" class="m-scope1" value="' + (allScopes[0]||'') + '" list="scopeList"></td>' +
        '<td><input type="text" placeholder="可选" class="m-scope2" list="scopeList"></td>' +
        '<td><input type="text" placeholder="可选" class="m-scope3" list="scopeList"></td>' +
        '<td><select class="m-type">' +
          '<option value="Single">单选</option>' +
          '<option value="Multiple">多选</option>' +
          '<option value="Judge">判断</option>' +
        '</select></td>' +
        '<td><input type="number" class="m-count" value="2" min="0" max="50"></td>' +
        '<td><input type="number" class="m-score" value="10" min="0.5" max="100" step="0.5"></td>' +
        '<td><button class="btn btn-danger btn-sm" onclick="this.closest(\'tr\').remove()">✕</button></td>';
      tbody.appendChild(row);
    }

    // ==== 考生名单导入 ====
    async function importCandidates(input) {
      var file = input.files[0];
      if (!file) return;
      var formData = new FormData();
      formData.append('file', file);
      try {
        var token = getToken();
        var res = await fetch(API + '/users/import', {
          method: 'POST',
          headers: { 'Authorization': 'Bearer ' + token },
          body: formData
        });
        var _raw = await res.text();
        var data = null;
        try { data = _raw ? JSON.parse(_raw) : null; } catch { data = { message: _raw.slice(0,200) }; }
        if (!res.ok) throw new Error((data && data.message) || (res.status === 401 ? '登录已过期或未登录，请重新登录' : '导入失败（HTTP ' + res.status + '）'));
        importedCandidates = data.items || [];
        var msg = '名单导入完成：新建 ' + (data.created || 0) + ' 人';
        if (data.updated > 0) msg += '，更新（与系统已有账号重名、已更新资料并纳入本次考试）' + data.updated + ' 人';
        if (data.failed > 0) msg += '，失败 ' + data.failed + ' 人';
        toast(msg, data.failed > 0 ? 'warn' : 'success');
        if (data.errors && data.errors.length) {
          alert(msg + '\n\n失败明细：\n' + data.errors.join('\n'));
        }
        renderCandidates();
      } catch(e) { toast(e.message, 'error'); }
      input.value = '';
    }

    function renderCandidates() {
      var el = document.getElementById('candidatePanel');
      var clearBtn = document.getElementById('btnClearCandidates');
      if (!importedCandidates.length) {
        el.innerHTML = '';
        clearBtn.style.display = 'none';
        return;
      }
      clearBtn.style.display = '';
      var html = '<div class="table-wrap" style="margin-bottom:10px;"><table><thead><tr><th>#</th><th>工号(用户名)</th><th>姓名</th><th>部门</th></tr></thead><tbody>';
      for (var i = 0; i < importedCandidates.length; i++) {
        var c = importedCandidates[i];
        html += '<tr>' +
          '<td>' + (i + 1) + '</td>' +
          '<td><strong>' + escHtml(c.username || c.jobNo || '') + '</strong></td>' +
          '<td>' + escHtml(c.displayName || '') + '</td>' +
          '<td>' + escHtml(c.department || '-') + '</td>' +
          '</tr>';
      }
      html += '</tbody></table></div>';
      html += '<p style="font-size:12px;color:var(--text-secondary);">已指定 ' + importedCandidates.length + ' 名考生，发布后仅名单内考生可参加本考试。登录用户名为工号，统一登录密码将在发布成功后返回。</p>';
      el.innerHTML = html;
    }

    function clearCandidates() {
      importedCandidates = [];
      renderCandidates();
    }

    async function downloadCandidateTemplate() {
      try {
        var token = getToken();
        var res = await fetch(API + '/users/import-template', { headers: { 'Authorization': 'Bearer ' + token } });
        if (!res.ok) throw new Error('下载失败');
        var blob = await res.blob();
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = '考生名单模板.xlsx';
        a.click();
        URL.revokeObjectURL(url);
        toast('名单模板已下载，按「填写说明」页填写后导入即可', 'success');
      } catch(e) { toast(e.message, 'error'); }
    }

    // ==== 考试有效期 ====
    function initExamTimes() {
      var now = new Date();
      var start = new Date(now.getTime() + 5 * 60000);
      var end = new Date(now.getTime() + 65 * 60000);
      var s = document.getElementById('examStartTime');
      var e = document.getElementById('examEndTime');
      s.value = toLocalInput(start);
      e.value = toLocalInput(end);
      calcDuration();
    }
    function toLocalInput(d) {
      var pad = function(n) { return String(n).padStart(2, '0'); };
      return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }
    function calcDuration() {
      var s = document.getElementById('examStartTime').value;
      var e = document.getElementById('examEndTime').value;
      var el = document.getElementById('examDurationShow');
      if (!s || !e) { el.textContent = '—'; return 0; }
      var mins = Math.round((new Date(e) - new Date(s)) / 60000);
      if (mins <= 0) { el.textContent = '⚠ 结束时间需晚于开始时间'; el.style.color = 'var(--danger)'; return -1; }
      el.style.color = '';
      el.textContent = mins + ' 分钟';
      return mins;
    }

    // ==== 用户管理 ====
    var _userMgmtCache = [];
    async function openUserMgmt() {
      document.getElementById('userMgmtModal').classList.add('active');
      await refreshUserMgmt();
    }
    function closeUserMgmt() { document.getElementById('userMgmtModal').classList.remove('active'); }
    async function refreshUserMgmt() {
      try {
        var list = await api('GET', '/users');
        _userMgmtCache = list || [];
        var me = getUser();
        var rows = '';
        for (var i = 0; i < _userMgmtCache.length; i++) {
          var u = _userMgmtCache[i];
          var isMe = u.username === me.username;
          rows += '<tr>' +
            '<td>' + u.id + (isMe ? ' <span style="color:var(--text-secondary);font-size:11px;">(我)</span>' : '') + '</td>' +
            '<td><strong>' + escHtml(u.username) + '</strong></td>' +
            '<td>' + escHtml(u.displayName || '') + '</td>' +
            '<td>' + escHtml(u.jobNo || '-') + '</td>' +
            '<td>' + escHtml(u.department || '-') + '</td>' +
            '<td><span class="tag ' + (u.role === 'Admin' ? 'tag-type' : 'tag-scope') + '">' + (u.role === 'Admin' ? '管理员' : '考生') + '</span></td>' +
            '<td style="font-size:12px;color:var(--text-secondary);">' + new Date(u.createdAt).toLocaleString() + '</td>' +
            '<td><div class="action-btns">' +
              '<button class="btn btn-primary btn-sm" onclick="openUserEdit(' + u.id + ')" title="编辑">✏️</button>' +
              '<button class="btn btn-outline btn-sm" onclick="resetUserPwd(' + u.id + ',\'' + escHtml(u.username).replace(/'/g, "\\'") + '\')" title="重置密码">🔑</button>' +
              (!isMe ? '<button class="btn btn-danger btn-sm" onclick="deleteUser(' + u.id + ',\'' + escHtml(u.username).replace(/'/g, "\\'") + '\')" title="删除">🗑</button>' : '') +
            '</div></td>' +
          '</tr>';
        }
        if (!_userMgmtCache.length) rows = '<tr><td colspan="8" class="empty-state">暂无用户</td></tr>';
        document.getElementById('userMgmtBody').innerHTML = rows;
      } catch(e) { toast('加载用户列表失败：' + e.message, 'error'); }
    }
    function openUserCreateForm() {
      document.getElementById('userEditTitle').textContent = '新增用户';
      document.getElementById('ueId').value = '';
      document.getElementById('ueUsername').value = '';
      document.getElementById('ueUsername').disabled = false;
      document.getElementById('uePassword').value = '';
      document.getElementById('uePassword').style.display = '';
      document.getElementById('uePwdRequired').style.display = '';
      document.getElementById('ueDisplayName').value = '';
      document.getElementById('ueJobNo').value = '';
      document.getElementById('ueDepartment').value = '';
      document.getElementById('ueRole').value = 'Candidate';
      document.getElementById('userEditModal').classList.add('active');
    }
    function openUserEdit(id) {
      var u = _userMgmtCache.find(function(x) { return x.id === id; });
      if (!u) return;
      document.getElementById('userEditTitle').textContent = '编辑用户 #' + id;
      document.getElementById('ueId').value = id;
      document.getElementById('ueUsername').value = u.username;
      document.getElementById('ueUsername').disabled = true; // 用户名不可改（避免与发布逻辑冲突）
      document.getElementById('uePassword').value = '';
      document.getElementById('uePassword').style.display = 'none'; // 编辑时不显示密码，单独走「重置密码」
      document.getElementById('uePwdRequired').style.display = 'none';
      document.getElementById('ueDisplayName').value = u.displayName || '';
      document.getElementById('ueJobNo').value = u.jobNo || '';
      document.getElementById('ueDepartment').value = u.department || '';
      document.getElementById('ueRole').value = u.role;
      document.getElementById('userEditModal').classList.add('active');
    }
    function closeUserEdit() { document.getElementById('userEditModal').classList.remove('active'); }
    async function saveUserEdit() {
      var id = document.getElementById('ueId').value;
      var username = document.getElementById('ueUsername').value.trim();
      var password = document.getElementById('uePassword').value;
      var displayName = document.getElementById('ueDisplayName').value.trim();
      var jobNo = document.getElementById('ueJobNo').value.trim();
      var department = document.getElementById('ueDepartment').value.trim();
      var role = document.getElementById('ueRole').value;
      if (!username) { toast('请填写用户名', 'error'); return; }
      try {
        if (id) {
          await api('PUT', '/users/' + id, {
            displayName: displayName, jobNo: jobNo, department: department, role: role
          });
          toast('已更新', 'success');
        } else {
          if (!password) { toast('请填写密码', 'error'); return; }
          await api('POST', '/users', {
            username: username, password: password,
            displayName: displayName || username, role: role
          });
          toast('已创建', 'success');
        }
        closeUserEdit();
        await refreshUserMgmt();
      } catch(e) { toast(e.message, 'error'); }
    }
    async function deleteUser(id, username) {
      var preview;
      try {
        preview = await api('GET', '/users/' + id + '/delete-preview');
      } catch (e) { toast(e.message, 'error'); return; }

      var body = '将删除用户「<strong>' + escHtml(preview.Username || username) + '</strong>」。';
      if (preview.sessionCount > 0) {
        body += '<br>该用户有 <b>' + preview.sessionCount + '</b> 份作答记录，分布：<ul style="margin:6px 0 0 18px;padding:0;">';
        for (var i = 0; i < preview.byExam.length; i++) {
          var b = preview.byExam[i];
          body += '<li>' + escHtml(b.examTitle) + '：' + b.count + ' 份</li>';
        }
        body += '</ul>';
      } else {
        body += '<br>暂无作答记录。';
      }
      if (preview.referencedExamCount > 0) {
        body += '<br>该用户被 <b>' + preview.referencedExamCount + '</b> 场考试（指定人员）引用，删除后会从对应名单中移除。';
      }

      var checks = '';
      if (preview.sessionCount > 0) {
        checks += '<label style="display:block;margin:6px 0;"><input type="checkbox" id="cdSessions"> 一并删除该用户的 ' + preview.sessionCount + ' 份作答记录（含成绩）</label>';
      }
      if (!checks) {
        checks = '<p style="color:var(--text-secondary);font-size:13px;">该用户无作答记录，可直接删除。</p>';
      }

      openCascadeModal('删除用户', body, checks, async function () {
        var cascadeSessions = document.getElementById('cdSessions') ? document.getElementById('cdSessions').checked : (preview.sessionCount === 0);
        if (preview.sessionCount > 0 && !cascadeSessions) {
          toast('请先勾选「一并删除作答记录」', 'warn'); return;
        }
        try {
          await api('DELETE', '/users/' + id, { cascadeSessions: cascadeSessions });
          toast('已删除', 'success');
          closeCascadeModal();
          await refreshUserMgmt();
        } catch (e) { toast(e.message, 'error'); }
      });
    }
    async function resetUserPwd(id, username) {
      var np = prompt('为用户「' + username + '」设置新密码：');
      if (np == null) return;
      if (np.length < 3) { toast('密码长度不能少于 3 位', 'error'); return; }
      try {
        await api('PUT', '/users/' + id + '/password', { newPassword: np });
        toast('密码已重置', 'success');
      } catch(e) { toast(e.message, 'error'); }
    }

    // ==== 修改我的密码 ====
    function openMyPassword() {
      document.getElementById('mpOld').value = '';
      document.getElementById('mpNew').value = '';
      document.getElementById('mpNew2').value = '';
      document.getElementById('myPwdModal').classList.add('active');
      setTimeout(function() { document.getElementById('mpOld').focus(); }, 100);
    }
    function closeMyPwd() { document.getElementById('myPwdModal').classList.remove('active'); }
    async function saveMyPwd() {
      var oldPwd = document.getElementById('mpOld').value;
      var newPwd = document.getElementById('mpNew').value;
      var newPwd2 = document.getElementById('mpNew2').value;
      if (!oldPwd || !newPwd) { toast('请填写完整', 'error'); return; }
      if (newPwd.length < 3) { toast('新密码长度不能少于 3 位', 'error'); return; }
      if (newPwd !== newPwd2) { toast('两次新密码输入不一致', 'error'); return; }
      try {
        await api('PUT', '/auth/me/password', { oldPassword: oldPwd, newPassword: newPwd });
        toast('密码已修改', 'success');
        closeMyPwd();
      } catch(e) { toast(e.message, 'error'); }
    }

    async function publishExam() {
      var title = document.getElementById('examTitle').value.trim();
      if (!title) { toast('请填写考试标题', 'error'); document.getElementById('examTitle').focus(); return; }

      var startVal = document.getElementById('examStartTime').value;
      var endVal = document.getElementById('examEndTime').value;
      if (!startVal || !endVal) { toast('请设置考试的开始时间和结束时间', 'error'); return; }
      var start = new Date(startVal);
      var end = new Date(endVal);
      if (end <= start) { toast('结束时间需晚于开始时间', 'error'); return; }

      var rules = [];
      var rows = document.querySelectorAll('#matrixBody tr');
      for (var r = 0; r < rows.length; r++) {
        var tr = rows[r];
        var scope1 = tr.querySelector('.m-scope1').value.trim();
        var scope2 = tr.querySelector('.m-scope2').value.trim() || null;
        var scope3 = tr.querySelector('.m-scope3').value.trim() || null;
        var type = tr.querySelector('.m-type').value;
        var count = parseInt(tr.querySelector('.m-count').value || '0');
        var score = parseFloat(tr.querySelector('.m-score').value || '0');
        if (scope1 && type && count > 0 && score > 0) rules.push({ scope1: scope1, scope2: scope2, scope3: scope3, type: type, count: count, scorePerQuestion: score });
      }

      if (!rules.length) { toast('请至少添加一条有效的选题规则（范围+题型+题数+分值都不能为空）', 'error'); return; }

      var body = {
        title: title,
        startTime: start.toISOString(),
        endTime: end.toISOString(),
        targetMode: importedCandidates.length ? 'Specified' : 'All',
        targetUserIds: importedCandidates.map(function(c) { return c.id; }),
        paperMode: document.getElementById('examPaperMode').value || 'Fixed',
        rules: rules
      };

      try {
        var btn = document.getElementById('btnPublish');
        btn.disabled = true;
        btn.textContent = '发布中...';
        var result = await api('POST', '/exams', body);
        btn.disabled = false;
        btn.textContent = '🚀 发布考试';

        var summary = '✅ 「' + title + '」发布成功！总题数 ' + result.totalQuestions + ' 道，总分 ' + result.totalScore + ' 分，' +
          (result.paperMode === 'PerCandidate' ? '随机卷' : '固定卷');
        if (importedCandidates.length) summary += '，指定 ' + importedCandidates.length + ' 人' + (result.candidatePassword ? '（统一密码 ' + result.candidatePassword + '）' : '');
        toast(summary, 'success');

        // 重置表单
        document.getElementById('examTitle').value = '';
        document.getElementById('examDesc').value = '';
        document.getElementById('matrixBody').innerHTML = '';
        initMatrix();
        clearCandidates();
        initExamTimes();

        // 弹出考试入口（独立链接 + 二维码 + 考生密码/名单）
        showExamEntrance(result.id);
      } catch(e) {
        toast(e.message, 'error');
        document.getElementById('btnPublish').disabled = false;
        document.getElementById('btnPublish').textContent = '🚀 发布考试';
      }
    }

    // ==================== 考试管理 ====================

    var _examMgmtCache = [];
    var _examMgmtTimer = null;
    function debounceLoadExams() { clearTimeout(_examMgmtTimer); _examMgmtTimer = setTimeout(loadExams, 400); }

    async function loadExams() {
      var tbody = document.getElementById('examMgmtBody');
      try {
        var list = await api('GET', '/exams');
        _examMgmtCache = list || [];
        var key = (document.getElementById('examSearch').value || '').trim().toLowerCase();
        var status = document.getElementById('examStatusFilter').value;
        var target = document.getElementById('examTargetFilter').value;
        var filtered = _examMgmtCache.filter(function(e) {
          if (key && (e.title || '').toLowerCase().indexOf(key) === -1) return false;
          if (status && e.status !== status) return false;
          if (target && e.targetMode !== target) return false;
          return true;
        });
        renderExamMgmt(filtered);
      } catch(e) {
        console.error('[Admin] loadExams error:', e);
        tbody.innerHTML = '<tr><td colspan="8" class="empty-state" style="color:var(--danger);">❌ 加载失败：' + escHtml(e.message) + '</td></tr>';
      }
    }

    function renderExamMgmt(list) {
      var tbody = document.getElementById('examMgmtBody');
      if (!list || !list.length) {
        tbody.innerHTML = '<tr><td colspan="8" class="empty-state"><div class="icon">📭</div><p>暂无考试</p><p style="font-size:12px;margin-top:4px;">请到「发布考试」标签创建</p></td></tr>';
        return;
      }
      var html = '';
      for (var i = 0; i < list.length; i++) {
        var e = list[i];
        var statusBadge = e.status === 'Published'
          ? '<span class="tag" style="background:#e6f7ff;color:#1890ff;">进行中</span>'
          : '<span class="tag tag-scope">已关闭</span>';
        var targetBadge = e.targetMode === 'Specified'
          ? '<span class="tag tag-type">指定</span>'
          : '<span class="tag tag-scope">全员</span>';
        var paperBadge = e.paperMode === 'PerCandidate'
          ? '<span class="tag" style="background:#fff7e6;color:#fa8c16;">随机卷</span>'
          : '<span class="tag tag-scope">固定卷</span>';
        var validity = (e.startTime ? new Date(e.startTime).toLocaleString() : '-') +
                       '<br>~<br>' + (e.endTime ? new Date(e.endTime).toLocaleString() : '-');
        var titleEsc = escHtml(e.title).replace(/'/g, "\\'");
        var pcount = e.participantCount || 0;
        html += '<tr>' +
          '<td>' + e.id + '</td>' +
          '<td style="max-width:220px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="' + escHtml(e.title) + '"><strong>' + escHtml(e.title) + '</strong><br>' + paperBadge + '</td>' +
          '<td>' + statusBadge + '</td>' +
          '<td>' + targetBadge + '</td>' +
          '<td>' + e.totalQuestions + '题<br><span style="font-size:12px;color:var(--text-secondary);">' + e.totalScore + ' 分</span></td>' +
          '<td>' + pcount + ' 人</td>' +
          '<td style="font-size:12px;">' + validity + '</td>' +
          '<td><div class="action-btns">' +
            '<button class="btn btn-outline btn-sm" onclick="showExamEntrance(' + e.id + ')" title="查看入口">🔗</button>' +
            '<button class="btn btn-primary btn-sm" onclick="editExamMgmt(' + e.id + ')" title="修改">✏️</button>' +
            (e.status === 'Published'
              ? '<button class="btn btn-outline btn-sm" onclick="closeExamMgmt(' + e.id + ',\'' + titleEsc + '\')" title="关闭（让考生不可见）">🛑</button>'
              : '<button class="btn btn-outline btn-sm" disabled title="已关闭" style="opacity:.4;cursor:not-allowed;">🛑</button>') +
            '<button class="btn btn-success btn-sm" onclick="republishExam(' + e.id + ',\'' + titleEsc + '\')" title="重新发布（克隆+重抽题）">♻️</button>' +
            '<button class="btn btn-danger btn-sm" onclick="confirmDeleteExam(' + e.id + ',\'' + titleEsc + '\',' + pcount + ')" title="删除">🗑</button>' +
          '</div></td>' +
        '</tr>';
      }
      tbody.innerHTML = html;
    }

    async function closeExamMgmt(id, title) {
      if (!confirm('确定要关闭考试「' + title + '」吗？\n关闭后考生将无法再看到此考试。')) return;
      try {
        await api('POST', '/exams/' + id + '/close');
        toast('已关闭', 'success');
        loadExams();
      } catch(e) { toast(e.message, 'error'); }
    }

    // --- 修改考试 ---
    var eeUsersCache = null; // 编辑表单内考生列表的缓存（避免重复拉取）

    async function renderEditTargetUsers() {
      var box = document.getElementById('eeTargetUsers');
      box.innerHTML = '<div style="color:var(--text-secondary);font-size:13px;">加载中…</div>';
      try {
        if (!eeUsersCache) eeUsersCache = await api('GET', '/users');
        // 当前已选中的考生（可能是空，也可能是从 All → Specified 切换前从未填过）
        var checked = document.querySelectorAll('.ee-target:checked');
        var ids = {};
        for (var i = 0; i < checked.length; i++) ids[parseInt(checked[i].value)] = 1;
        var html = '';
        var visible = 0;
        for (var j = 0; j < eeUsersCache.length; j++) {
          var u = eeUsersCache[j];
          // 只列出候选人：排除管理员（admin 除外，按角色过滤）
          if (u.role !== 'Candidate') continue;
          visible++;
          html += '<label style="display:inline-flex;align-items:center;margin-right:14px;margin-bottom:6px;font-size:13px;cursor:pointer;">' +
                  '<input type="checkbox" class="ee-target" value="' + u.id + '"' + (ids[u.id] ? ' checked' : '') + ' style="margin-right:4px;"> ' +
                  escHtml(u.username) + ' (' + escHtml(u.displayName || '') + ')' +
                  '</label>';
        }
        box.innerHTML = html || '<div style="color:var(--text-secondary);font-size:13px;">⚠ 系统中暂无考生账号，请先到「用户管理」新增</div>';
      } catch(e) {
        box.innerHTML = '<div style="color:var(--danger);font-size:13px;">加载考生失败：' + escHtml(e.message || '') + '</div>';
      }
    }

    async function editExamMgmt(id) {
      try {
        var d = await api('GET', '/exams/' + id);
        document.getElementById('eeId').value = id;
        document.getElementById('eeTitle').value = d.title || '';
        document.getElementById('eeStartTime').value = d.startTime ? toLocalInput(new Date(d.startTime)) : '';
        document.getElementById('eeEndTime').value = d.endTime ? toLocalInput(new Date(d.endTime)) : '';
        document.getElementById('eeTargetMode').value = d.targetMode || 'All';
        toggleEditTargetUsers(d.targetMode);
        // 清空勾选与缓存：每次打开编辑表单都重新加载候选名单，保证新创建的考生能立刻看到
        document.getElementById('eeTargetUsers').innerHTML = '';
        var checkedOld = document.querySelectorAll('.ee-target:checked');
        for (var k = 0; k < checkedOld.length; k++) checkedOld[k].checked = false;
        eeUsersCache = null;

        if (d.targetMode === 'Specified') {
          await renderEditTargetUsers();
          // 预勾选该考试原有的指定考生
          var ids = {};
          for (var i = 0; i < (d.targetUserIds || []).length; i++) ids[d.targetUserIds[i]] = 1;
          var preselected = document.querySelectorAll('.ee-target');
          for (var n = 0; n < preselected.length; n++) {
            if (ids[parseInt(preselected[n].value)]) preselected[n].checked = true;
          }
        }
        document.getElementById('examEditModal').classList.add('active');
      } catch(e) { toast(e.message, 'error'); }
    }

    async function toggleEditTargetUsers(mode) {
      document.getElementById('eeTargetUsersPanel').style.display = mode === 'Specified' ? '' : 'none';
      // 切换到「指定人员」时，若尚未加载考生列表则懒加载（覆盖从 All 切到 Specified 的场景）
      if (mode === 'Specified') {
        var box = document.getElementById('eeTargetUsers');
        if (!box.innerHTML || box.innerHTML.indexOf('加载中') !== -1 || box.innerHTML.indexOf('暂无考生') !== -1) {
          await renderEditTargetUsers();
        }
      }
    }

    function closeExamEdit() { document.getElementById('examEditModal').classList.remove('active'); }

    async function saveExamEdit() {
      var id = document.getElementById('eeId').value;
      var title = document.getElementById('eeTitle').value.trim();
      var start = document.getElementById('eeStartTime').value;
      var end = document.getElementById('eeEndTime').value;
      var mode = document.getElementById('eeTargetMode').value;
      if (!title) { toast('请填写考试标题', 'error'); return; }
      if (!start || !end) { toast('请设置起止时间', 'error'); return; }
      if (new Date(end) <= new Date(start)) { toast('结束时间需晚于开始时间', 'error'); return; }
      var targetIds = [];
      if (mode === 'Specified') {
        var checks = document.querySelectorAll('.ee-target:checked');
        for (var i = 0; i < checks.length; i++) targetIds.push(parseInt(checks[i].value));
        if (!targetIds.length) { toast('指定人员模式请至少选择 1 名考生', 'error'); return; }
      }
      try {
        await api('PUT', '/exams/' + id, {
          title: title,
          startTime: new Date(start).toISOString(),
          endTime: new Date(end).toISOString(),
          targetMode: mode,
          targetUserIds: targetIds
        });
        toast('已保存', 'success');
        closeExamEdit();
        loadExams();
      } catch(e) { toast(e.message, 'error'); }
    }

    // ===== 删除确认弹窗（级联清除，需求1）=====
    function ensureCascadeModal() {
      if (document.getElementById('cascadeDeleteModal')) return;
      var d = document.createElement('div');
      d.className = 'modal-overlay';
      d.id = 'cascadeDeleteModal';
      d.innerHTML =
        '<div class="modal" style="max-width:560px;">' +
          '<h3 id="cdTitle"></h3>' +
          '<div id="cdBody" style="font-size:14px;line-height:1.7;"></div>' +
          '<div id="cdChecks" style="margin:14px 0;"></div>' +
          '<div class="modal-footer">' +
            '<button class="btn btn-outline" onclick="closeCascadeModal()">取消</button>' +
            '<button class="btn btn-danger" id="cdConfirm">确认删除</button>' +
          '</div>' +
        '</div>';
      document.body.appendChild(d);
    }
    function openCascadeModal(title, bodyHtml, checksHtml, onConfirm) {
      ensureCascadeModal();
      document.getElementById('cdTitle').textContent = title;
      document.getElementById('cdBody').innerHTML = bodyHtml;
      document.getElementById('cdChecks').innerHTML = checksHtml;
      var btn = document.getElementById('cdConfirm');
      btn.onclick = onConfirm;
      document.getElementById('cascadeDeleteModal').classList.add('active');
    }
    function closeCascadeModal() {
      var m = document.getElementById('cascadeDeleteModal');
      if (m) m.classList.remove('active');
    }

    // --- 删除考试（先预览关联数据，再由管理员勾选是否级联清除）---
    async function confirmDeleteExam(id, title, pcount) {
      var preview;
      try {
        preview = await api('GET', '/exams/' + id + '/delete-preview');
      } catch (e) { toast(e.message, 'error'); return; }

      var body = '将删除考试「<strong>' + escHtml(preview.Title || title) + '</strong>」。';
      if (preview.sessionCount > 0) {
        body += '<br>关联 <b>' + preview.sessionCount + '</b> 份作答记录（已交卷 ' + preview.submittedCount + '、进行中 ' + preview.inProgressCount + '）。';
      } else {
        body += '<br>暂无作答记录。';
      }
      if (preview.candidateCount > 0) {
        body += '<br>关联考生 <b>' + preview.candidateCount + '</b> 名（其中仅被本场引用、可安全删除的 ' + preview.exclusiveCandidateCount + ' 名）。';
      }

      var checks = '';
      if (preview.sessionCount > 0) {
        checks += '<label style="display:block;margin:6px 0;"><input type="checkbox" id="cdSessions"> 一并删除本考试的 ' + preview.sessionCount + ' 份作答记录（含成绩）</label>';
      }
      if (preview.exclusiveCandidateCount > 0) {
        // 有作答记录时，级联删考生需先勾选"删除作答记录"（避免悬空会话）；无作答记录时直接可勾选。
        var usersDisabled = preview.sessionCount > 0 ? ' disabled' : '';
        var usersHint = preview.sessionCount > 0 ? '（需先勾选上方"删除作答记录"）' : '';
        checks += '<label style="display:block;margin:6px 0;"><input type="checkbox" id="cdUsers"' + usersDisabled + '> 一并删除仅被本场引用的 ' + preview.exclusiveCandidateCount + ' 名考生账号' + usersHint + '</label>';
      }
      if (!checks) {
        checks = '<p style="color:var(--text-secondary);font-size:13px;">该考试无作答记录，可直接删除。</p>';
      }

      openCascadeModal('删除考试', body, checks, async function () {
        var cascadeSessions = document.getElementById('cdSessions') ? document.getElementById('cdSessions').checked : (preview.sessionCount === 0);
        var cascadeUsers = document.getElementById('cdUsers') ? document.getElementById('cdUsers').checked : false;
        if (preview.sessionCount > 0 && !cascadeSessions) {
          toast('请先勾选「一并删除作答记录」', 'warn'); return;
        }
        try {
          await api('DELETE', '/exams/' + id, { cascadeSessions: cascadeSessions, cascadeUsers: cascadeUsers });
          toast('已删除', 'success');
          closeCascadeModal();
          loadExams();
        } catch (e) { toast(e.message, 'error'); }
      });

      // 有作答记录时：勾选"删除作答记录"后才放行"级联删考生"；无作答记录时 cdUsers 本就可用
      var sEl = document.getElementById('cdSessions');
      var uEl = document.getElementById('cdUsers');
      if (sEl && uEl) {
        sEl.addEventListener('change', function () { uEl.disabled = !sEl.checked; });
      }
    }

    // --- 重新发布 ---
    async function republishExam(id, title) {
      if (!confirm('将基于「' + title + '」重新发布一份新考试：\n\n' +
                   '· 标题自动加「- 副本」后缀（之后可在「修改」中改名）\n' +
                   '· 选题规则保留，但会重新随机抽题\n' +
                   '· 指定人员模式会重新生成新考生密码（覆盖原密码）\n\n继续？')) return;
      try {
        var result = await api('POST', '/exams/' + id + '/republish');
        var msg = '♻️ 重新发布成功！\n\n新考试 ID：' + result.id + '\n新标题：' + result.title + '\n题目数：' + result.totalQuestions;
        if (result.candidatePassword) msg += '\n新考生密码：' + result.candidatePassword;
        alert(msg);
        loadExams();
      } catch(e) { toast(e.message, 'error'); }
    }

    // --- 查看入口 ---
    async function showExamEntrance(id) {
      try {
        var d = await api('GET', '/exams/' + id + '/entrance');
        var statusText = d.status === 'Published' ? '进行中' : d.status === 'Closed' ? '已关闭' : d.status;
        var statusColor = d.status === 'Published' ? 'background:#e6f7ff;color:#1890ff;' : 'background:#f0f0f0;color:#666;';
        var entryUrl = d.entryUrl || d.frontendUrl || '/exam.html';
        var fullUrl = window.location.origin + entryUrl;
        var pwdEsc = (d.candidatePassword || '').replace(/'/g, "\\'");
        var urlEsc = fullUrl.replace(/'/g, "\\'");

        var html = '<div style="margin-bottom:14px;">' +
          '<div style="font-size:13px;color:var(--text-secondary);margin-bottom:4px;">考试标题</div>' +
          '<div style="font-size:16px;font-weight:600;">' + escHtml(d.title) +
            ' <span class="tag" style="margin-left:6px;' + statusColor + '">' + statusText + '</span></div>' +
        '</div>' +
        '<div style="display:flex;gap:18px;align-items:flex-start;margin-bottom:14px;flex-wrap:wrap;">' +
          '<div style="text-align:center;">' +
            '<div style="font-size:13px;color:var(--text-secondary);margin-bottom:6px;">扫码进入（考生手机扫此码）</div>' +
            '<div id="qrBox" style="width:180px;height:180px;padding:8px;background:#fff;border:1px solid #eee;border-radius:8px;"></div>' +
          '</div>' +
          '<div style="flex:1;min-width:240px;">' +
            '<div style="font-size:13px;color:var(--text-secondary);margin-bottom:4px;">考生入口链接（每场独立，可分享）</div>' +
            '<div style="display:flex;gap:8px;align-items:center;">' +
              '<input type="text" readonly value="' + escHtml(fullUrl) + '" style="flex:1;font-family:monospace;">' +
              '<button class="btn btn-primary btn-sm" onclick="copyToClipboard(\'' + urlEsc + '\', this)">📋 复制</button>' +
            '</div>' +
          '</div>' +
        '</div>';

        if (d.targetMode === 'Specified') {
          html += '<div style="margin-bottom:14px;">' +
            '<div style="font-size:13px;color:var(--text-secondary);margin-bottom:4px;">考生统一登录密码（分享给考生）</div>' +
            '<div style="display:flex;gap:8px;align-items:center;">' +
              '<input type="text" readonly value="' + escHtml(d.candidatePassword || '') + '" style="flex:1;font-family:monospace;font-weight:600;color:var(--danger);">' +
              '<button class="btn btn-primary btn-sm" onclick="copyToClipboard(\'' + pwdEsc + '\', this)">📋 复制</button>' +
            '</div>' +
          '</div>' +
          '<div>' +
            '<div style="font-size:13px;color:var(--text-secondary);margin-bottom:6px;">考生名单（用户名 = 工号，密码如上）</div>' +
            '<div class="table-wrap" style="max-height:240px;overflow-y:auto;"><table><thead><tr><th>#</th><th>用户名</th><th>姓名</th><th>部门</th></tr></thead><tbody>';
          var users = d.targetUsers || [];
          for (var i = 0; i < users.length; i++) {
            var u = users[i];
            html += '<tr><td>' + (i + 1) + '</td><td><strong>' + escHtml(u.username) + '</strong></td><td>' + escHtml(u.displayName || '') + '</td><td>' + escHtml(u.department || '-') + '</td></tr>';
          }
          html += '</tbody></table></div></div>';
        } else {
          html += '<div style="padding:10px 14px;background:var(--bg);border-radius:6px;color:var(--text-secondary);font-size:13px;">📌 此考试为「全员可考」模式，所有考生登录后即可在「考试列表」中看到。如需独立入口，可改用「指定人员」模式发布。</div>';
        }
        document.getElementById('entranceBody').innerHTML = html;
        document.getElementById('examEntranceModal').classList.add('active');

        // 生成二维码（vendored qrcode.min.js）
        try {
          var qrBox = document.getElementById('qrBox');
          if (qrBox && typeof QRCode !== 'undefined') {
            qrBox.innerHTML = '';
            new QRCode(qrBox, {
              text: fullUrl,
              width: 164, height: 164,
              colorDark: '#000000', colorLight: '#ffffff',
              correctLevel: QRCode.CorrectLevel.M
            });
          }
        } catch (e) { console.error('QR gen error:', e); }
      } catch(e) { toast(e.message, 'error'); }
    }

    function closeExamEntrance() { document.getElementById('examEntranceModal').classList.remove('active'); }

    function copyToClipboard(text, btn) {
      var done = function() {
        var orig = btn.textContent;
        btn.textContent = '✅ 已复制';
        setTimeout(function() { btn.textContent = orig; }, 1500);
      };
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(text).then(done, function() { fallbackCopy(text, done); });
      } else {
        fallbackCopy(text, done);
      }
    }
    function fallbackCopy(text, done) {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand('copy'); done(); } catch(e) { toast('复制失败，请手动复制', 'error'); }
      document.body.removeChild(ta);
    }

    // ==================== 历史记录 ====================

    async function loadHistory() {
      try {
        var list = await api('GET', '/history/exams');
        var el = document.getElementById('historyList');
        if (!list || !list.length) {
          el.innerHTML = '<div class="empty-state"><div class="icon">📭</div><p>暂无历史考试</p><p style="font-size:12px;margin-top:4px;">发布考试并有人作答后会在此显示</p></div>';
          return;
        }
        var html = '';
        for (var i = 0; i < list.length; i++) {
          var ex = list[i];
          html += '<div class="exam-item" onclick="showExamDetail(' + ex.id + ',\'' + escHtml(ex.title).replace(/'/g, "\\'") + '\')">' +
            '<div class="exam-info">' +
              '<div class="exam-title">' + escHtml(ex.title) + '</div>' +
              '<div class="exam-meta">' +
                '发布时间：' + new Date(ex.createdAt).toLocaleString() + ' · ' +
                '参与 ' + (ex.submittedCount || 0) + ' 人 · ' +
                '均分 ' + (ex.avgScore != null ? ex.avgScore.toFixed(1) : '-') + ' · ' +
                '合格率 ' + (ex.passRate != null ? Math.round(ex.passRate*100)+'%' : '-') +
              '</div>' +
            '</div>' +
            '<div class="exam-action"><span class="exam-status status-submitted">查看详情</span></div>' +
          '</div>';
        }
        el.innerHTML = html;
      } catch(e) {
        console.error('[Admin] Load history error:', e);
        document.getElementById('historyList').innerHTML = '<div class="empty-state" style="color:var(--danger);">❌ 加载失败：' + escHtml(e.message) + '</div>';
      }
    }

    async function showExamDetail(examId, title) {
      currentExamIdForExport = examId;
      document.getElementById('detailTitle').textContent = '📊 ' + title + ' - 成绩明细';
      try {
        var results = await api('GET', '/history/exams/' + examId + '/results');
        var tbody = document.getElementById('detailBody');
        if (!results || !results.length) {
          tbody.innerHTML = '<tr><td colspan="4" class="empty-state">暂无成绩数据</td></tr>';
        } else {
          var html = '';
          for (var i = 0; i < results.length; i++) {
            var r = results[i];
            html += '<tr>' +
              '<td>' + (i + 1) + '</td>' +
              '<td>' + escHtml(r.displayName || r.username) + '</td>' +
              '<td><strong>' + r.score + ' / ' + (r.totalScore || '-') + '</strong></td>' +
              '<td>' + (r.submittedAt ? new Date(r.submittedAt).toLocaleString() : '-') + '</td>' +
            '</tr>';
          }
          tbody.innerHTML = html;
        }
        document.getElementById('historyDetail').style.display = '';
      } catch(e) { toast(e.message, 'error'); }
    }

    async function exportScores() {
      if (!currentExamIdForExport) return;
      try {
        var token = getToken();
        var res = await fetch(API + '/history/exams/' + currentExamIdForExport + '/export', {
          headers: { 'Authorization': 'Bearer ' + token }
        });
        if (!res.ok) throw new Error('导出失败');
        var blob = await res.blob();
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = '考试成绩_' + currentExamIdForExport + '_' + Date.now() + '.csv';
        a.click();
        URL.revokeObjectURL(url);
        toast('导出成功', 'success');
      } catch(e) { toast(e.message, 'error'); }
    }

    // ---- 工具 ----
    function escHtml(s) {
      if (!s) return '';
      var d = document.createElement('div');
      d.textContent = s;
      return d.innerHTML;
    }
  

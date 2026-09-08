// ============================================
// ExamSystem 前端 - 共享逻辑
// ============================================

// 同源取 API 基址：本机用 localhost，局域网用服务端内网 IP，自动适配
const API = window.location.origin + '/api';

// ---- 工具 ----
const $ = s => document.querySelector(s);
const $$ = s => document.querySelectorAll(s);

function getToken() { return localStorage.getItem('token'); }
function getUser() {
  try { return JSON.parse(localStorage.getItem('user') || '{}'); }
  catch { return {}; }
}
function setAuth(token, user) {
  localStorage.setItem('token', token);
  localStorage.setItem('user', JSON.stringify(user));
}
function clearAuth() {
  localStorage.removeItem('token');
  localStorage.removeItem('user');
}

async function api(method, path, body) {
  const opts = {
    method,
    headers: { 'Content-Type': 'application/json' },
  };
  const t = getToken();
  if (t) opts.headers['Authorization'] = `Bearer ${t}`;
  if (body !== undefined) opts.body = JSON.stringify(body);
  const res = await fetch(`${API}${path}`, opts);
  const data = await res.json();
  if (!res.ok) throw new Error(data.message || data.title || `HTTP ${res.status}`);
  return data;
}

function toast(msg, type = 'info') {
  const el = document.createElement('div');
  el.className = `toast toast-${type}`;
  el.textContent = msg;
  document.body.appendChild(el);
  setTimeout(() => el.classList.add('show'), 10);
  setTimeout(() => { el.classList.remove('show'); setTimeout(() => el.remove(), 300); }, 2500);
}

function formatTime(seconds) {
  const m = Math.floor(seconds / 60).toString().padStart(2, '0');
  const s = (seconds % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

// ---- 登录/登出 ----
async function doLogin(username, password) {
  const data = await api('POST', '/auth/login', { username, password });
  // 后端返回扁平结构：{ Token, UserId, DisplayName, Role, IsAdmin }
  const user = {
    id: data.userId || data.UserId,
    displayName: data.displayName || data.DisplayName,
    username: username,
    role: data.role || data.Role,
    isAdmin: data.isAdmin || data.IsAdmin
  };
  setAuth(data.token || data.Token, user);
  return user;
}

function logout() {
  clearAuth();
  if (rankingConn) { rankingConn.stop(); rankingConn = null; }
  location.href = 'index.html';
}

// ---- SignalR 实时排名 ----
let rankingConn = null;

function connectRanking(examId, onRankingUpdate) {
  if (rankingConn) return;
  // 使用 SignalR JS 客户端（从 CDN 加载或内联实现）
  // 这里用原生 WebSocket + 自定义协议连接 /hubs/ranking
  const token = getToken();
  // 同源 WebSocket：跟随页面协议(http->ws / https->wss)与主机，避免写死 localhost
  const wsProto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
  const url = `${wsProto}//${window.location.host}/hubs/ranking?access_token=${encodeURIComponent(token)}`;
  
  // 简易 SignalR WebSocket 协议握手
  rankingConn = new WebSocket(url);
  
  rankingConn.onopen = () => {
    console.log('[SignalR] Connected to ranking hub');
    // SignalR 握手
    rankingConn.send('{"protocol":"json","version":1}\x1e');
    // 加入考试组
    const invokeMsg = JSON.stringify({
      type: 1, // Invocation
      target: 'JoinExam',
      arguments: [examId],
      invocationId: `${Date.now()}`
    }) + '\x1e';
    rankingConn.send(invokeMsg);
  };

  rankingConn.onmessage = (event) => {
    const messages = event.data.split('\x1e').filter(m => m.trim());
    for (const msg of messages) {
      if (!msg) continue;
      try {
        const parsed = JSON.parse(msg);
        // 处理 RankingUpdated 回调
        if (parsed.type === 1 && parsed.target === 'RankingUpdate' && parsed.arguments?.[0]) {
          onRankingUpdate(parsed.arguments[0]);
        }
      } catch {}
    }
  };

  rankingConn.onclose = () => {
    console.log('[SignalR] Disconnected');
    rankingConn = null;
  };

  rankingConn.onerror = (err) => {
    console.error('[SignalR] Error', err);
  };
}

// ---- 题型显示 ----
const TYPE_LABELS = { Single: '单选题', Multiple: '多选题', Judge: '判断题' };
const TYPE_CLASS = { Single: 'type-single', Multiple: 'type-multiple', Judge: 'type-judge' };

function renderQuestion(q, index, answers = {}) {
  const typeLabel = TYPE_LABELS[q.type] || q.type;
  const selected = answers[q.id] || '';
  
  let optionsHtml = '';
  if (q.type === 'Judge') {
    optionsHtml = `
      <label class="option${selected==='A'?' selected':''}"><input type="radio" name="q_${q.id}" value="A" ${selected==='A'?'checked':''}> A. 正确</label>
      <label class="option${selected==='B'?' selected':''}"><input type="radio" name="q_${q.id}" value="B" ${selected==='B'?'checked':''}> B. 错误</label>`;
  } else {
    const optSrc = (q.options && typeof q.options === 'object')
      ? { A: q.options.A, B: q.options.B, C: q.options.C, D: q.options.D }
      : { A: q.optionA, B: q.optionB, C: q.optionC, D: q.optionD };
    const opts = [
      { k: 'A', v: optSrc.A },
      { k: 'B', v: optSrc.B },
      { k: 'C', v: optSrc.C },
      { k: 'D', v: optSrc.D },
    ];
    const inputType = q.type === 'Multiple' ? 'checkbox' : 'radio';
    optionsHtml = opts.map(o =>
      `<label class="option${selected.includes(o.k)?' selected':''}">
        <input type="${inputType}" name="q_${q.id}" value="${o.k}" ${selected.includes(o.k)?(inputType==='checkbox'?'checked':'checked'):''}>
        ${o.k}. ${o.v}
      </label>`
    ).join('');
  }

  return `
    <div class="question-card ${TYPE_CLASS[q.type]}" data-id="${q.id}" data-type="${q.type}">
      <div class="q-header">
        <span class="q-num">第 ${index} 题</span>
        <span class="q-type">${typeLabel}</span>
        <span class="q-score">${q.score || '?'} 分</span>
      </div>
      <div class="q-content">${q.content}</div>
      <div class="q-options">${optionsHtml}</div>
    </div>`;
}

function collectAnswers() {
  const answers = {};
  $$('.question-card').forEach(card => {
    const id = card.dataset.id;
    const type = card.dataset.type;
    if (type === 'Multiple') {
      const checked = [...card.querySelectorAll('input:checked')].map(el => el.value);
      answers[id] = checked.sort().join('');
    } else {
      const checked = card.querySelector('input:checked');
      answers[id] = checked ? checked.value : '';
    }
  });
  return answers;
}

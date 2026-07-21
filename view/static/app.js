// 切换 OPC 面板（通用版，支持动态任意数量）
document.querySelectorAll('.menu-item').forEach(item => {
    item.addEventListener('click', () => {
        const target = item.dataset.target;
        // 切换菜单激活状态
        document.querySelectorAll('.menu-item').forEach(i => i.classList.remove('active'));
        item.classList.add('active');
        // 切换信息面板
        document.querySelectorAll('.info-panel').forEach(panel => panel.style.display = 'none');
        document.getElementById(`info-${target}`).style.display = 'block';
    });
});

// ==================== 日志模块（WebSocket 实时推送） ====================
const logContainer = document.getElementById('log-container');
const wsStatusEl = document.getElementById('ws-status');
let userScrolledUp = false;
let ws = null;
let wsReconnectTimer = null;

// 监听滚动事件，判断用户是否在底部
logContainer.addEventListener('scroll', () => {
    const threshold = 30;
    const atBottom = logContainer.scrollHeight - logContainer.scrollTop - logContainer.clientHeight < threshold;
    userScrolledUp = !atBottom;
    const btn = document.getElementById('scroll-bottom-btn');
    if (btn) btn.style.display = userScrolledUp ? 'block' : 'none';
});

function scrollToBottom() {
    logContainer.scrollTop = logContainer.scrollHeight;
    userScrolledUp = false;
    const btn = document.getElementById('scroll-bottom-btn');
    if (btn) btn.style.display = 'none';
}

function appendLog(line) {
    const div = document.createElement('div');
    div.className = 'log-item';
    div.textContent = line;
    logContainer.appendChild(div);
    // 限制最多 600 条
    while (logContainer.children.length > 600) {
        logContainer.removeChild(logContainer.firstChild);
    }
    if (!userScrolledUp) {
        logContainer.scrollTop = logContainer.scrollHeight;
    }
}

function updateWsStatus(connected) {
    if (!wsStatusEl) return;
    if (connected) {
        wsStatusEl.textContent = '已连接';
        wsStatusEl.className = 'ws-badge ws-connected';
    } else {
        wsStatusEl.textContent = '已断开';
        wsStatusEl.className = 'ws-badge ws-disconnected';
    }
}

function connectLogWs() {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const clientId = 'c_' + Date.now().toString(36) + '_' + Math.random().toString(36).substr(2, 8);
    const wsUrl = `${protocol}//${location.host}/ws/logs?clientId=${clientId}`;

    ws = new WebSocket(wsUrl);

    ws.onopen = () => {
        console.log('[WS] 日志连接已建立');
        updateWsStatus(true);
        if (wsReconnectTimer) {
            clearTimeout(wsReconnectTimer);
            wsReconnectTimer = null;
        }
    };

    ws.onmessage = (event) => {
        appendLog(event.data);
    };

    ws.onclose = () => {
        console.log('[WS] 日志连接已断开，3秒后重连...');
        updateWsStatus(false);
        wsReconnectTimer = setTimeout(() => reconnectLogWs(clientId), 3000);
    };

    ws.onerror = (err) => {
        console.error('[WS] 日志连接错误:', err);
        ws.close();
    };
}

function reconnectLogWs(clientId) {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${location.host}/ws/logs?clientId=${clientId}`;

    ws = new WebSocket(wsUrl);

    ws.onopen = () => {
        console.log('[WS] 日志重连已建立');
        updateWsStatus(true);
        if (wsReconnectTimer) {
            clearTimeout(wsReconnectTimer);
            wsReconnectTimer = null;
        }
    };

    ws.onmessage = (event) => {
        appendLog(event.data);
    };

    ws.onclose = () => {
        console.log('[WS] 日志连接已断开，3秒后重连...');
        updateWsStatus(false);
        wsReconnectTimer = setTimeout(() => reconnectLogWs(clientId), 3000);
    };

    ws.onerror = (err) => {
        console.error('[WS] 日志连接错误:', err);
        ws.close();
    };
}

connectLogWs();

// ==================== 性能监控模块 ====================
function fetchPerformance() {
    fetch('/api/performance')
        .then(res => res.json())
        .then(data => {
            // 内存 / 资源
            setText('perf-memory', `${data.memory.workingSetMb} MB`);
            setText('perf-private', `${data.memory.privateMb} MB`);
            setText('perf-gc-heap', `${data.memory.gcHeapMb} MB`);
            setText('perf-gc-gen0', data.memory.gcGen0);
            setText('perf-gc-gen1', data.memory.gcGen1);
            setText('perf-gc-gen2', data.memory.gcGen2);
            setText('perf-handles', data.memory.handleCount);
            // CPU & 线程
            setText('perf-cpu', `${data.cpu.percent}%`);
            setText('perf-threads', data.cpu.threadCount);
            setText('perf-uptime', data.cpu.uptime);
            // 网络（本进程）
            setText('perf-net-total', data.network.total);
            setText('perf-net-established', data.network.established);
            // Web 应用层流量（累计）
            if (data.network.webTraffic) {
                setText('perf-web-in', data.network.webTraffic.bytesInStr);
                setText('perf-web-out', data.network.webTraffic.bytesOutStr);
                setText('perf-web-reqs', data.network.webTraffic.requests);
            }
            // 流量权限提示
            const hintEl = document.getElementById('traffic-hint');
            if (hintEl) {
                if (data.network.statsChecked && !data.network.statsAvailable) {
                    hintEl.textContent = '⚠ 流量统计需以管理员身份运行程序';
                    hintEl.style.display = 'block';
                } else {
                    hintEl.style.display = 'none';
                }
            }
            // 渲染连接明细表
            const tbody = document.querySelector('#conn-table tbody');
            if (tbody) {
                tbody.innerHTML = '';
                (data.network.connections || []).forEach(c => {
                    const tr = document.createElement('tr');
                    const state = c.state || 'UNKNOWN';
                    tr.innerHTML = `<td>${c.local}</td><td>${c.remote}</td><td><span class="conn-state state-${state.toLowerCase()}">${state}</span></td><td class="traffic-cell">${c.bytesInStr}</td><td class="traffic-cell">${c.bytesOutStr}</td>`;
                    tbody.appendChild(tr);
                });
            }
        })
        .catch(err => console.error('获取性能数据失败:', err));
}

function setText(id, val) {
    const el = document.getElementById(id);
    if (el) el.textContent = val;
}

setInterval(fetchPerformance, 5000);
fetchPerformance();

// ==================== OPC 状态轮询 ====================
setInterval(async () => {
    try {
        const res = await fetch('/api/status');
        const status = await res.json();

        for (const key of Object.keys(status)) {
            const s = status[key];
            const ipEl = document.getElementById(`ip-${key}`);
            const progidEl = document.getElementById(`progid-${key}`);
            const runningEl = document.getElementById(`running-${key}`);
            const threadEl = document.getElementById(`thread-${key}`);
            const statusEl = document.getElementById(`status-${key}`);

            if (ipEl) ipEl.textContent = s.ip;
            if (progidEl) progidEl.textContent = s.progid;
            if (runningEl) runningEl.textContent = s.running ? '运行中' : '已断开';
            if (threadEl) threadEl.textContent = `${s.threadId}次`;

            if (statusEl) {
                statusEl.textContent = s.running ? '在线' : '离线';
                statusEl.className = `status ${s.running ? 'online' : 'offline'}`;
            }
        }
    } catch (err) {
        console.error('获取状态失败:', err);
    }
}, 10000);

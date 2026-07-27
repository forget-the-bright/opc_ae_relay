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

// ==================== 流式渲染器（xterm 思路：内存环形缓冲 + 定时整体重绘） ====================
// 原理：消息只写入 JS 数组（ring buffer），定时器每 tick 从缓冲区一次性重建 innerHTML，
//       全程零 appendChild/removeChild，每 tick 仅一次 DOM 写入 + 一次 scrollTop 设置，
//       彻底消除高频追加节点导致的布局抖动。
function createStreamRenderer(container, btnId, itemClass, maxItems, intervalMs) {
    const buffer = [];
    let dirty = false;          // 是否有未渲染的新消息
    let atBottom = true;        // 用户是否跟随底部
    let lastWheelTime = 0;      // 最近一次滚轮时间（防止渲染 tick 抢滚动）

    function escapeHtml(s) {
        return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function render() {
        if (!dirty) return;     // 没有新消息 → 不碰 DOM，用户阅读完全静止
        dirty = false;

        const wasAtBottom = atBottom;
        const savedScrollTop = container.scrollTop;
        const prevScrollHeight = container.scrollHeight;

        // 从缓冲区整体重建（唯一一次 DOM 写入）
        let html = '';
        for (let i = 0; i < buffer.length; i++) {
            html += '<div class="' + itemClass + '">' + escapeHtml(buffer[i]) + '</div>';
        }
        container.innerHTML = html;

        if (wasAtBottom && Date.now() - lastWheelTime > 200) {
            // 跟随底部模式（滚轮操作后 200ms 内不抢）
            container.scrollTop = container.scrollHeight;
        } else {
            // 阅读模式：补偿顶部被裁剪的高度差，画面真正纹丝不动
            // （缓冲区满后新消息会 splice 掉顶部旧条目，同位置内容会上移）
            const heightDelta = container.scrollHeight - prevScrollHeight;
            container.scrollTop = savedScrollTop + heightDelta;
        }
    }

    container.addEventListener('scroll', () => {
        const threshold = 30;
        atBottom = container.scrollHeight - container.scrollTop - container.clientHeight < threshold;
        const btn = document.getElementById(btnId);
        if (btn) btn.style.display = atBottom ? 'none' : 'block';
    });

    // 记录滚轮操作时间，渲染 tick 不与其冲突
    container.addEventListener('wheel', () => { lastWheelTime = Date.now(); }, { passive: true });

    setInterval(render, intervalMs);

    return {
        push: function (msg) {
            buffer.push(msg);
            if (buffer.length > maxItems) buffer.splice(0, buffer.length - maxItems);
            dirty = true;
        },
        scrollToBottom: function () {
            container.scrollTop = container.scrollHeight;
            atBottom = true;
            lastWheelTime = 0;
            const btn = document.getElementById(btnId);
            if (btn) btn.style.display = 'none';
        }
    };
}

// ==================== 日志模块（WebSocket 实时推送） ====================
const logContainer = document.getElementById('log-container');
const wsStatusEl = document.getElementById('ws-status');
const logRenderer = createStreamRenderer(logContainer, 'scroll-bottom-btn', 'log-item', 600, 2000);
let ws = null;
let wsReconnectTimer = null;

function scrollToBottom() {
    logRenderer.scrollToBottom();
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
        logRenderer.push(event.data);
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
        logRenderer.push(event.data);
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

// ==================== 告警模块（WebSocket 实时推送） ====================
const alarmContainer = document.getElementById('log-container-alarm');
const wsStatusAlarmEl = document.getElementById('ws-status-alarm');
const alarmRenderer = createStreamRenderer(alarmContainer, 'scroll-bottom-btn-alarm', 'log-item alarm-item', 600, 300);
let alarmWs = null;
let alarmWsReconnectTimer = null;

function scrollToBottomAlarm() {
    alarmRenderer.scrollToBottom();
}

function updateAlarmWsStatus(connected) {
    if (!wsStatusAlarmEl) return;
    if (connected) {
        wsStatusAlarmEl.textContent = '已连接';
        wsStatusAlarmEl.className = 'ws-badge ws-connected';
    } else {
        wsStatusAlarmEl.textContent = '已断开';
        wsStatusAlarmEl.className = 'ws-badge ws-disconnected';
    }
}

function connectAlarmWs() {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const clientId = 'alarm_' + Date.now().toString(36) + '_' + Math.random().toString(36).substr(2, 8);
    const wsUrl = `${protocol}//${location.host}/ws/alarms?clientId=${clientId}`;

    alarmWs = new WebSocket(wsUrl);

    alarmWs.onopen = () => {
        console.log('[WS] 告警连接已建立');
        updateAlarmWsStatus(true);
        if (alarmWsReconnectTimer) {
            clearTimeout(alarmWsReconnectTimer);
            alarmWsReconnectTimer = null;
        }
    };

    alarmWs.onmessage = (event) => {
        alarmRenderer.push(event.data);
    };

    alarmWs.onclose = () => {
        console.log('[WS] 告警连接已断开，3秒后重连...');
        updateAlarmWsStatus(false);
        alarmWsReconnectTimer = setTimeout(() => reconnectAlarmWs(clientId), 3000);
    };

    alarmWs.onerror = (err) => {
        console.error('[WS] 告警连接错误:', err);
        alarmWs.close();
    };
}

function reconnectAlarmWs(clientId) {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${location.host}/ws/alarms?clientId=${clientId}`;

    alarmWs = new WebSocket(wsUrl);

    alarmWs.onopen = () => {
        console.log('[WS] 告警重连已建立');
        updateAlarmWsStatus(true);
        if (alarmWsReconnectTimer) {
            clearTimeout(alarmWsReconnectTimer);
            alarmWsReconnectTimer = null;
        }
    };

    alarmWs.onmessage = (event) => {
        alarmRenderer.push(event.data);
    };

    alarmWs.onclose = () => {
        console.log('[WS] 告警连接已断开，3秒后重连...');
        updateAlarmWsStatus(false);
        alarmWsReconnectTimer = setTimeout(() => reconnectAlarmWs(clientId), 3000);
    };

    alarmWs.onerror = (err) => {
        console.error('[WS] 告警连接错误:', err);
        alarmWs.close();
    };
}

connectAlarmWs();

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
                    tr.innerHTML = `<td>${c.local}</td><td>${c.remote}</td><td><span class="conn-state state-${state.toLowerCase()}">${state}</span></td><td class="traffic-cell">${c.bytesInStr}</td><td class="traffic-cell">${c.bytesOutStr}</td><td class="traffic-cell">${c.lastConnectTimeStr || '-'}</td><td class="traffic-cell">${c.lastCloseTimeStr || '-'}</td>`;
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

// ==================== 连接表格高度拖拽 ====================
(function () {
    const wrap = document.getElementById('conn-table-wrap');
    const handle = document.getElementById('conn-table-resize-handle');
    if (!wrap || !handle) return;

    let startY = 0, startH = 0, dragging = false;

    handle.addEventListener('mousedown', e => {
        dragging = true;
        startY = e.clientY;
        startH = wrap.offsetHeight;
        document.body.style.cursor = 'row-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault();
    });

    document.addEventListener('mousemove', e => {
        if (!dragging) return;
        const newH = Math.max(100, startH + (e.clientY - startY));
        wrap.style.maxHeight = newH + 'px';
        wrap.style.height = newH + 'px';
    });

    document.addEventListener('mouseup', () => {
        if (!dragging) return;
        dragging = false;
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
    });
})();

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

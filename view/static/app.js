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

// 加载日志
function fetchLogs() {
    fetch('/api/logs')
        .then(res => res.json())
        .then(logs => {
            const logContainer = document.getElementById('log-container');
            //logContainer.innerHTML = ''; // 清空旧日志
            logs.forEach(line => {
                const div = document.createElement('div');
                div.className = 'log-item';
                div.textContent = line;
                logContainer.appendChild(div);
            });
            // 限制最多 100 条
            while (logContainer.children.length > 100) {
                logContainer.removeChild(logContainer.firstChild);
            }
            logContainer.scrollTop = logContainer.scrollHeight;
        });
}
setInterval(fetchLogs, 500);
fetchLogs();

// 动态刷新所有 OPC 状态（通用版，自动识别 opc1/opc2/...）
setInterval(async () => {
    try {
        const res = await fetch('/api/status');
        const status = await res.json();

        // 遍历 status 里的所有 opc 节点：opc1, opc2, opc3...
        for (const key of Object.keys(status)) {
            const s = status[key];
            console.log(s);
            console.log(key);
            // 更新面板信息
            const ipEl = document.getElementById(`ip-${key}`);
            const progidEl = document.getElementById(`progid-${key}`);
            const runningEl = document.getElementById(`running-${key}`);
            const threadEl = document.getElementById(`thread-${key}`);
            const statusEl = document.getElementById(`status-${key}`);

            if (ipEl) ipEl.textContent = s.ip;
            if (progidEl) progidEl.textContent = s.progid;
            if (runningEl) runningEl.textContent = s.running ? '运行中' : '已断开';
            if (threadEl) threadEl.textContent = s.threadId;

            // 更新左侧状态指示灯
            if (statusEl) {
                statusEl.textContent = s.running ? '在线' : '离线';
                statusEl.className = `status ${s.running ? 'online' : 'offline'}`;
            }
        }
    } catch (err) {
        console.error('获取状态失败:', err);
    }
}, 5000);
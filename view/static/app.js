// 切换 OPC 面板
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
function fetchLogs() {
    fetch('/api/logs')
        .then(res => res.json())
        .then(logs => {
            const logContainer = document.getElementById('log-container');
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
// 每 500ms 拉一次
setInterval(fetchLogs, 500);
fetchLogs(); // 首次拉取

// 定时获取状态信息（每 5 秒）
setInterval(async () => {
    try {
        const res = await fetch('/api/status');
        const status = await res.json();
        // 更新 OPC1 状态
        document.getElementById('opc1-ip').textContent = status.opc1.ip;
        document.getElementById('opc1-progid').textContent = status.opc1.progid;
        document.getElementById('opc1-running').textContent = status.opc1.running ? '运行中' : '已断开';
        document.getElementById('opc1-thread').textContent = status.opc1.threadId;
        document.getElementById('status-opc1').textContent = status.opc1.running ? '在线' : '离线';
        document.getElementById('status-opc1').className = `status ${status.opc1.running ? 'online' : 'offline'}`;

        // 更新 OPC2 状态
        document.getElementById('opc2-ip').textContent = status.opc2.ip;
        document.getElementById('opc2-progid').textContent = status.opc2.progid;
        document.getElementById('opc2-running').textContent = status.opc2.running ? '运行中' : '已断开';
        document.getElementById('opc2-thread').textContent = status.opc2.threadId;
        document.getElementById('status-opc2').textContent = status.opc2.running ? '在线' : '离线';
        document.getElementById('status-opc2').className = `status ${status.opc2.running ? 'online' : 'offline'}`;
    } catch (err) {
        console.error('获取状态失败:', err);
    }
}, 5000);
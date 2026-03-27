let systemInfoLoaded = false;
let changelogLoaded = false;
let systemInfoInterval = null;
let lastAvatarUrl = null; // 记录上次头像URL，避免重复请求

function initWelcomePage() {
    loadWelcomeData();

    if (systemInfoInterval) {
        clearInterval(systemInfoInterval);
    }
    systemInfoInterval = setInterval(function() {
        loadSystemInfo();
    }, 1000);
}

function loadWelcomeData() {
    loadSystemInfo();
    loadChangelog();
}

function loadSystemInfo() {
    if (typeof sendStandardMessage === 'function') {
        sendStandardMessage('get_system_info', {});
    }
}

function loadChangelog() {
    if (typeof sendStandardMessage === 'function') {
        sendStandardMessage('get_changelog', {});
    }
}

function handleSystemInfo(data) {
    if (!data) return;

    systemInfoLoaded = true;

    // 检查是否所有欢迎页数据都加载完成
    checkWelcomeDataLoaded();

    var memoryPercent = data.memoryPercent || 0;
    var cpuPercent = data.cpuPercent || 0;

    updateProgressRing('memory', memoryPercent);
    updateProgressRing('cpu', cpuPercent);

    var elements = {
        version: document.getElementById('welcome-version'),
        os: document.getElementById('info-os'),
        runtime: document.getElementById('info-runtime'),
        uptime: document.getElementById('info-uptime'),
        memory: document.getElementById('info-memory'),
        threads: document.getElementById('info-threads'),
        cpu: document.getElementById('info-cpu'),
        users: document.getElementById('info-users'),
        groups: document.getElementById('info-groups'),
        plugins: document.getElementById('info-plugins'),
        llm: document.getElementById('info-llm')
    };

    if (elements.version) elements.version.textContent = data.currentVersion || '-';
    if (elements.os) elements.os.textContent = data.osVersion || '-';
    if (elements.runtime) elements.runtime.textContent = data.dotnetVersion || '-';
    if (elements.uptime) elements.uptime.textContent = data.uptimeFormatted || formatUptimeClient(data.uptime);
    if (elements.memory) elements.memory.textContent = data.memoryUsageFormatted || '-';
    if (elements.threads) elements.threads.textContent = data.threadCount || '0';
    if (elements.cpu) elements.cpu.textContent = (data.processorCount || 0) + ' 核心';
    if (elements.users) elements.users.textContent = data.totalUsers || '0';
    if (elements.groups) elements.groups.textContent = data.totalGroups || '0';
    if (elements.plugins) elements.plugins.textContent = (data.runningPlugins || '0') + ' / ' + (data.totalPlugins || '0');
    if (elements.llm) elements.llm.textContent = data.llmModel || '-';

    // 更新协议端信息
    updateProtocolInfo(data.protocolInfo);
}

function updateProtocolInfo(protocolInfo) {
    if (!protocolInfo) return;

    var statusEl = document.getElementById('protocol-status');
    var nicknameEl = document.getElementById('protocol-nickname');
    var typeEl = document.getElementById('protocol-type');
    var userIdEl = document.getElementById('protocol-userid');
    var avatarImg = document.getElementById('protocol-avatar-img');
    var avatarPlaceholder = document.getElementById('protocol-avatar-placeholder');
    var infoBox = document.querySelector('.protocol-info-box');

    if (statusEl) {
        statusEl.className = 'protocol-status' + (protocolInfo.isConnected ? '' : ' disconnected');
    }

    if (infoBox) {
        infoBox.className = 'protocol-info-box' + (protocolInfo.isConnected ? '' : ' disconnected');
    }

    if (nicknameEl) {
        nicknameEl.textContent = protocolInfo.nickname || 'Unknown';
    }

    if (typeEl) {
        typeEl.textContent = protocolInfo.protocolType || 'Unknown';
    }

    if (userIdEl) {
        userIdEl.textContent = protocolInfo.userId ? 'QQ: ' + protocolInfo.userId : '';
    }

    // 头像处理 - 通过后端代理获取
    // 只在头像URL变化时才更新，避免每秒重复请求
    if (avatarImg && avatarPlaceholder) {
        var newAvatarUrl = protocolInfo.avatarUrl;
        if (newAvatarUrl && newAvatarUrl !== lastAvatarUrl) {
            // 获取key参数
            var urlParams = new URLSearchParams(window.location.search);
            var key = urlParams.get('key') || '';
            // 使用后端代理接口，添加key参数
            var proxyUrl = '/api/proxy?action=proxy-image&url=' + encodeURIComponent(newAvatarUrl) + '&key=' + encodeURIComponent(key);
            avatarImg.src = proxyUrl;
            avatarImg.style.display = 'block';
            avatarPlaceholder.style.display = 'none';
            lastAvatarUrl = newAvatarUrl;
        } else if (!newAvatarUrl) {
            avatarImg.style.display = 'none';
            avatarPlaceholder.style.display = 'flex';
            lastAvatarUrl = null;
        }
    }
}

function updateProgressRing(type, percent) {
    var ring = document.querySelector('.' + type + '-ring');
    var valueEl = document.getElementById(type + '-percent');

    if (!ring) return;

    var circumference = 2 * Math.PI * 50;
    var offset = circumference - (percent / 100) * circumference;

    ring.style.strokeDashoffset = offset;

    if (valueEl) {
        valueEl.textContent = percent.toFixed(1) + '%';
    }

    if (percent > 80) {
        ring.style.stroke = '#e74c3c';
    } else if (percent > 60) {
        ring.style.stroke = '#f39c12';
    } else {
        if (type === 'memory') {
            ring.style.stroke = 'var(--primary-color)';
        } else {
            ring.style.stroke = 'var(--secondary-color)';
        }
    }
}

function handleChangelog(data) {
    changelogLoaded = true;

    // 检查是否所有欢迎页数据都加载完成
    checkWelcomeDataLoaded();

    var container = document.getElementById('changelog-content');
    if (!container) return;

    if (!data || data === 'Changelog not found' || data === 'Error loading changelog') {
        container.innerHTML = '<p style="color: var(--text-secondary); text-align: center; padding: 20px;">暂无更新日志</p>';
        return;
    }

    var lines = data.split('\n');
    var html = '';

    for (var i = 0; i < lines.length; i++) {
        var line = lines[i].trim();
        if (!line) continue;

        if (line.match(/^v\d+\.\d+\.\d+/) || line.match(/^v\d+\.\d+/)) {
            html += '<div class="changelog-version">' + escapeHtml(line) + '</div>';
        } else {
            html += '<div class="changelog-item">' + escapeHtml(line) + '</div>';
        }
    }

    container.innerHTML = html || '<p style="color: var(--text-secondary); text-align: center; padding: 20px;">暂无更新日志</p>';
}

function formatUptimeClient(seconds) {
    if (!seconds || isNaN(seconds)) return '0秒';

    var uptime = parseFloat(seconds);
    var days = Math.floor(uptime / 86400);
    var hours = Math.floor((uptime % 86400) / 3600);
    var minutes = Math.floor((uptime % 3600) / 60);
    var secs = Math.floor(uptime % 60);

    if (days >= 1) {
        return days + '天 ' + hours + '小时 ' + minutes + '分钟';
    } else if (hours >= 1) {
        return hours + '小时 ' + minutes + '分钟 ' + secs + '秒';
    } else if (minutes >= 1) {
        return minutes + '分钟 ' + secs + '秒';
    } else {
        return secs + '秒';
    }
}

function escapeHtml(text) {
    var div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// 检查欢迎页数据是否加载完成
function checkWelcomeDataLoaded() {
    if (systemInfoLoaded && changelogLoaded) {
        // 通知主页面欢迎页数据加载完成
        if (typeof onWelcomePageLoaded === 'function') {
            onWelcomePageLoaded();
        }
    }
}

function stopWelcomeAutoRefresh() {
    if (systemInfoInterval) {
        clearInterval(systemInfoInterval);
        systemInfoInterval = null;
    }
}

// 重置欢迎页状态（切换协议端后调用）
function resetWelcomePageState() {
    systemInfoLoaded = false;
    changelogLoaded = false;
    lastAvatarUrl = null;
}

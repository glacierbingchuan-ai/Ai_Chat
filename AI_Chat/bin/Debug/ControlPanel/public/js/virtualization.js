let virtualizationData = null;
let selectedVirtPluginId = null;
let pendingVirtAction = null;

function initVirtualization() {
    refreshVirtualizationData();
}

function refreshVirtualizationData() {
    const container = document.getElementById('virt-plugins-container');
    container.innerHTML = `
        <div class="loading">
            <div class="loading-spinner"></div>
            <div>加载虚拟化数据中...</div>
        </div>
    `;

    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('get_virtualization_data', {});
    } else {
        container.innerHTML = `
            <div class="error-message">
                <p>WebSocket 未连接，无法获取虚拟化数据</p>
            </div>
        `;
    }
}

function handleVirtualizationData(data) {
    virtualizationData = data;
    
    const plugins = data.plugins || [];
    const config = data.config || {};

    let totalRegistry = 0;
    let totalFiles = 0;
    let totalBlocked = 0;

    plugins.forEach(p => {
        totalRegistry += (p.registryEntries || []).length;
        totalFiles += (p.fileEntries || []).length;
        if (p.statistics) {
            totalBlocked += (p.statistics.processAccessBlocked || 0) + (p.statistics.fileBlockedWrites || 0);
        }
    });

    document.getElementById('virt-plugins-count').textContent = plugins.length;
    document.getElementById('virt-registry-count').textContent = totalRegistry;
    document.getElementById('virt-files-count').textContent = totalFiles;
    document.getElementById('virt-blocked-count').textContent = totalBlocked;

    renderVirtPluginsList(plugins);
    renderVirtAdvancedPlugins(plugins);
}

function renderVirtPluginsList(plugins) {
    const container = document.getElementById('virt-plugins-container');

    if (plugins.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">🛡️</div>
                <p>暂无插件</p>
                <p class="empty-subtitle">加载的插件将显示在这里</p>
            </div>
        `;
        return;
    }

    let html = '<div class="virt-plugins-list">';

    plugins.forEach(plugin => {
        const stats = plugin.statistics || {};
        const regCount = (plugin.registryEntries || []).length;
        const fileCount = (plugin.fileEntries || []).length;
        const blockedCount = (stats.processAccessBlocked || 0) + (stats.fileBlockedWrites || 0);
        const supportSandbox = plugin.supportSandbox !== false;

        html += `
            <div class="virt-plugin-item ${selectedVirtPluginId === plugin.pluginId ? 'selected' : ''}" 
                 onclick="selectVirtPlugin('${escapeHtml(plugin.pluginId)}')">
                <div class="virt-plugin-item-header">
                    <span class="virt-plugin-name">${escapeHtml(plugin.pluginId)} ${!supportSandbox ? '<span style="color: #ffc107; font-size: 0.75rem;">⚠️</span>' : ''}</span>
                    <span class="virt-status-badge ${plugin.isVirtualizationEnabled ? 'protected' : 'unprotected'}">
                        ${plugin.isVirtualizationEnabled ? '受保护' : '未保护'}
                    </span>
                </div>
                <div class="virt-plugin-item-stats">
                    <span title="虚拟注册表">📋 ${regCount}</span>
                    <span title="虚拟文件">📁 ${fileCount}</span>
                    <span title="已拦截">🚫 ${blockedCount}</span>
                </div>
            </div>
        `;
    });

    html += '</div>';
    container.innerHTML = html;
}

function renderVirtAdvancedPlugins(plugins) {
    const container = document.getElementById('virt-advanced-plugins');

    if (plugins.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <p>暂无插件</p>
            </div>
        `;
        return;
    }

    let html = '';

    plugins.forEach(plugin => {
        const stats = plugin.statistics || {};
        const regCount = (plugin.registryEntries || []).length;
        const fileCount = (plugin.fileEntries || []).length;
        const supportSandbox = plugin.supportSandbox !== false;

        html += `
            <div class="virt-advanced-plugin-card">
                <div class="virt-advanced-plugin-info">
                    <h4>${escapeHtml(plugin.pluginId)} ${!supportSandbox ? '<span style="color: #ffc107; font-size: 0.85rem; margin-left: 8px;">⚠️ 不支持沙箱</span>' : ''}</h4>
                    <p>虚拟注册表: ${regCount} 项 | 虚拟文件: ${fileCount} 个 | 状态: ${plugin.isVirtualizationEnabled ? '受保护' : '未保护'}</p>
                </div>
                <div class="virt-advanced-plugin-actions">
                    ${!supportSandbox && !plugin.isVirtualizationEnabled ? 
                        `<button class="btn btn-sm btn-secondary" disabled title="该插件不支持沙箱运行">不支持虚拟化</button>` :
                        `<button class="btn btn-sm ${plugin.isVirtualizationEnabled ? 'btn-danger' : 'btn-success'}" 
                                onclick="showVirtWarningModal('${escapeHtml(plugin.pluginId)}', 'toggle', ${!plugin.isVirtualizationEnabled})">
                            ${plugin.isVirtualizationEnabled ? '禁用虚拟化' : '启用虚拟化'}
                        </button>`
                    }
                    <button class="btn btn-danger btn-sm" 
                            onclick="showVirtWarningModal('${escapeHtml(plugin.pluginId)}', 'clear')">
                        清除数据
                    </button>
                </div>
            </div>
        `;
    });

    container.innerHTML = html;
}

let pendingSandboxPluginId = null;

function showSandboxWarningModal(pluginId, pluginName) {
    pendingSandboxPluginId = pluginId;
    const content = document.getElementById('sandbox-warning-content');
    content.innerHTML = `
        <div style="text-align: center;">
            <div style="font-size: 3rem; margin-bottom: 15px;">⚠️</div>
            <h4 style="margin-bottom: 15px; color: #ffc107;">插件不支持沙箱运行</h4>
            <p style="color: var(--text-secondary); margin-bottom: 15px;">
                插件 <strong>${escapeHtml(pluginName)}</strong> 标记为不支持沙箱运行。
            </p>
            <div style="background: rgba(220, 53, 69, 0.1); border: 1px solid rgba(220, 53, 69, 0.3); border-radius: 8px; padding: 15px; margin-bottom: 15px;">
                <p style="color: #ffc107; margin: 0; font-weight: 500;">⚠️ 风险提示</p>
                <ul style="margin: 10px 0 0 0; padding-left: 20px; color: var(--text-secondary); text-align: left;">
                    <li>此插件无法在沙箱环境中运行</li>
                    <li>插件将直接访问真实系统文件和注册表</li>
                    <li>请确保您完全信任此插件</li>
                </ul>
            </div>
            <p style="color: var(--text-secondary);">
                是否仍要开启此插件？
            </p>
        </div>
    `;
    
    document.getElementById('sandbox-warning-modal').style.display = 'flex';
}

function closeSandboxWarningModal() {
    document.getElementById('sandbox-warning-modal').style.display = 'none';
    pendingSandboxPluginId = null;
}

function confirmSandboxWarning() {
    if (pendingSandboxPluginId) {
        // 发送确认消息给后端
        if (ws && ws.readyState === WebSocket.OPEN) {
            sendStandardMessage('approve_plugin', { pluginId: pendingSandboxPluginId });
            showToast('正在加载插件...', 'info');
        } else {
            showToast('WebSocket 未连接', 'error');
        }
    }
    closeSandboxWarningModal();
}

function selectVirtPlugin(pluginId) {
    selectedVirtPluginId = pluginId;
    
    document.querySelectorAll('.virt-plugin-item').forEach(item => {
        item.classList.remove('selected');
    });
    event.currentTarget.classList.add('selected');

    document.getElementById('virt-detail-placeholder').style.display = 'none';
    document.getElementById('virt-detail-content').style.display = 'flex';

    renderVirtRegistry(pluginId);
    renderVirtFiles(pluginId);
    renderVirtStats(pluginId);
    renderVirtActivities(pluginId);
}

function switchVirtDetailTab(tabName) {
    document.querySelectorAll('.virt-detail-tab').forEach(tab => {
        tab.classList.remove('active');
        if (tab.dataset.tab === tabName) {
            tab.classList.add('active');
        }
    });

    document.querySelectorAll('.virt-detail-pane').forEach(pane => {
        pane.classList.remove('active');
    });
    document.getElementById(tabName + '-detail').classList.add('active');
}

function switchVirtTab(tabName) {
    const mainContent = document.querySelector('.virt-main-content');
    const advancedContent = document.getElementById('virt-advanced');
    
    if (tabName === 'virt-advanced') {
        mainContent.style.display = 'none';
        advancedContent.style.display = 'block';
    } else {
        mainContent.style.display = 'flex';
        advancedContent.style.display = 'none';
    }
}

function renderVirtRegistry(pluginId) {
    const container = document.getElementById('virt-registry-container');
    
    if (!virtualizationData) {
        container.innerHTML = '<div class="empty-state"><p>请先加载数据</p></div>';
        return;
    }

    const plugin = virtualizationData.plugins.find(p => p.pluginId === pluginId);
    const entries = plugin?.registryEntries || [];

    if (entries.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">📋</div>
                <p>该插件没有虚拟注册表数据</p>
            </div>
        `;
        return;
    }

    let html = '<div class="virt-registry-list">';
    html += '<table class="virt-table">';
    html += '<thead><tr><th>键路径</th><th>值名称</th><th>值</th><th>类型</th><th>操作</th></tr></thead>';
    html += '<tbody>';

    entries.forEach((entry, index) => {
        const displayValue = entry.value !== null ? 
            (typeof entry.value === 'object' ? JSON.stringify(entry.value) : String(entry.value)) : 
            '(空)';
        
        html += `
            <tr>
                <td title="${escapeHtml(entry.keyPath)}">${escapeHtml(truncateText(entry.keyPath, 40))}</td>
                <td>${escapeHtml(entry.valueName || '(默认)')}</td>
                <td title="${escapeHtml(displayValue)}">${escapeHtml(truncateText(displayValue, 30))}</td>
                <td>${escapeHtml(entry.valueKind || 'String')}</td>
                <td>
                    <button class="btn btn-danger btn-sm" onclick="deleteVirtRegistryKey('${escapeHtml(pluginId)}', '${escapeHtml(entry.keyPath)}')">
                        删除
                    </button>
                </td>
            </tr>
        `;
    });

    html += '</tbody></table></div>';
    container.innerHTML = html;
}

function renderVirtFiles(pluginId) {
    const container = document.getElementById('virt-files-container');
    
    if (!virtualizationData) {
        container.innerHTML = '<div class="empty-state"><p>请先加载数据</p></div>';
        return;
    }

    const plugin = virtualizationData.plugins.find(p => p.pluginId === pluginId);
    const entries = plugin?.fileEntries || [];

    if (entries.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">📁</div>
                <p>该插件没有虚拟文件数据</p>
            </div>
        `;
        return;
    }

    let html = '<div class="virt-files-list">';
    html += '<table class="virt-table">';
    html += '<thead><tr><th>虚拟路径</th><th>实际路径</th><th>大小</th><th>类型</th><th>状态</th><th>操作</th></tr></thead>';
    html += '<tbody>';

    entries.forEach(entry => {
        const sizeStr = entry.size ? formatBytes(entry.size) : '-';
        const typeStr = entry.isDirectory ? '目录' : '文件';
        const statusStr = entry.isDeleted ? '已删除' : '正常';
        const statusClass = entry.isDeleted ? 'deleted' : 'active';
        
        html += `
            <tr>
                <td title="${escapeHtml(entry.virtualPath)}">${escapeHtml(truncateText(entry.virtualPath, 35))}</td>
                <td title="${escapeHtml(entry.realPath)}">${escapeHtml(truncateText(entry.realPath, 35))}</td>
                <td>${sizeStr}</td>
                <td>${typeStr}</td>
                <td><span class="virt-status-badge ${statusClass}">${statusStr}</span></td>
                <td>
                    <button class="btn btn-danger btn-sm" onclick="deleteVirtFile('${escapeHtml(pluginId)}', '${escapeHtml(entry.virtualPath)}')">
                        删除
                    </button>
                </td>
            </tr>
        `;
    });

    html += '</tbody></table></div>';
    container.innerHTML = html;
}

function renderVirtStats(pluginId) {
    const container = document.getElementById('virt-stats-container');
    
    if (!virtualizationData) {
        container.innerHTML = '<div class="empty-state"><p>请先加载数据</p></div>';
        return;
    }

    const plugin = virtualizationData.plugins.find(p => p.pluginId === pluginId);
    const stats = plugin?.statistics || {};

    if (!stats || Object.keys(stats).length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">📊</div>
                <p>该插件没有统计信息</p>
            </div>
        `;
        return;
    }

    let html = '<div class="virt-stats-grid">';

    const statItems = [
        { label: '注册表读取', value: stats.registryReads || 0, icon: '📖' },
        { label: '注册表写入', value: stats.registryWrites || 0, icon: '✏️' },
        { label: '虚拟注册表写入', value: stats.registryVirtualWrites || 0, icon: '📝' },
        { label: '文件读取', value: stats.fileReads || 0, icon: '📄' },
        { label: '文件写入', value: stats.fileWrites || 0, icon: '💾' },
        { label: '虚拟文件写入', value: stats.fileVirtualWrites || 0, icon: '📁' },
        { label: '已拦截文件写入', value: stats.fileBlockedWrites || 0, icon: '🚫' },
        { label: '进程访问尝试', value: stats.processAccessAttempts || 0, icon: '🔍' },
        { label: '已拦截进程访问', value: stats.processAccessBlocked || 0, icon: '🔒' },
        { label: '最后活动时间', value: stats.lastActivity ? new Date(stats.lastActivity).toLocaleString() : '无', icon: '🕐' }
    ];

    statItems.forEach(item => {
        html += `
            <div class="virt-stat-box">
                <span class="virt-stat-icon">${item.icon}</span>
                <div class="virt-stat-info">
                    <span class="virt-stat-label">${item.label}</span>
                    <span class="virt-stat-value">${item.value}</span>
                </div>
            </div>
        `;
    });

    html += '</div>';
    container.innerHTML = html;
}

function renderVirtActivities(pluginId) {
    const container = document.getElementById('virt-activities-container');
    
    if (!virtualizationData) {
        container.innerHTML = '<div class="empty-state"><p>请先加载数据</p></div>';
        return;
    }

    const plugin = virtualizationData.plugins.find(p => p.pluginId === pluginId);
    const activities = plugin?.activityRecords || [];

    if (activities.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">📝</div>
                <p>该插件没有行为记录</p>
                <p class="empty-subtitle">插件运行时的操作将记录在这里</p>
            </div>
        `;
        return;
    }

    let html = '<div class="virt-activities-list">';
    html += '<table class="virt-table">';
    html += '<thead><tr><th>时间</th><th>类型</th><th>类别</th><th>目标</th><th>详情</th><th>状态</th></tr></thead>';
    html += '<tbody>';

    activities.forEach(activity => {
        const timeStr = new Date(activity.timestamp).toLocaleString();
        const typeIcon = activity.activityType === 'Read' ? '📖' : 
                        activity.activityType === 'Write' ? '✏️' : 
                        activity.activityType === 'Delete' ? '🗑️' : 
                        activity.activityType === 'Create' ? '➕' : 
                        activity.activityType === 'Start' ? '▶️' : 
                        activity.activityType === 'Kill' ? '💀' : 
                        activity.activityType === 'Access' ? '🔍' : '❓';
        const categoryIcon = activity.category === 'File' ? '📁' : 
                            activity.category === 'Registry' ? '📋' : 
                            activity.category === 'Process' ? '⚙️' : 
                            activity.category === 'Directory' ? '📂' : '❓';
        const statusBadge = activity.isBlocked ? 
            '<span class="virt-status-badge blocked">已拦截</span>' :
            (activity.isVirtualized ? 
                '<span class="virt-status-badge virtualized">虚拟化</span>' : 
                '<span class="virt-status-badge real">真实</span>');
        
        html += `
            <tr>
                <td>${timeStr}</td>
                <td>${typeIcon} ${escapeHtml(activity.activityType)}</td>
                <td>${categoryIcon} ${escapeHtml(activity.category)}</td>
                <td title="${escapeHtml(activity.target)}">${escapeHtml(truncateText(activity.target, 30))}</td>
                <td title="${escapeHtml(activity.detail)}">${escapeHtml(truncateText(activity.detail || '-', 20))}</td>
                <td>${statusBadge}</td>
            </tr>
        `;
    });

    html += '</tbody></table></div>';
    container.innerHTML = html;
}

function showVirtWarningModal(pluginId, action, enabled) {
    pendingVirtAction = { pluginId, action, enabled };
    
    const content = document.getElementById('virt-warning-content');
    
    if (action === 'toggle') {
        if (enabled) {
            content.innerHTML = `
                <div style="text-align: center;">
                    <div style="font-size: 3rem; margin-bottom: 15px;">🔓</div>
                    <h4 style="margin-bottom: 15px;">确认启用虚拟化？</h4>
                    <p style="color: var(--text-secondary); margin-bottom: 15px;">
                        您即将为插件 <strong>${escapeHtml(pluginId)}</strong> 启用虚拟化保护。
                    </p>
                    <p style="color: var(--text-secondary);">
                        启用后，该插件的所有文件和注册表操作将被隔离到虚拟环境中，不会影响真实系统。
                    </p>
                </div>
            `;
        } else {
            content.innerHTML = `
                <div style="text-align: center;">
                    <div style="font-size: 3rem; margin-bottom: 15px;">⚠️</div>
                    <h4 style="margin-bottom: 15px; color: #ffc107;">危险操作：禁用虚拟化</h4>
                    <p style="color: var(--text-secondary); margin-bottom: 15px;">
                        您即将为插件 <strong>${escapeHtml(pluginId)}</strong> 禁用虚拟化保护。
                    </p>
                    <div style="background: rgba(220, 53, 69, 0.1); border: 1px solid rgba(220, 53, 69, 0.3); border-radius: 8px; padding: 15px; margin-bottom: 15px;">
                        <p style="color: #ffc107; margin: 0; font-weight: 500;">⚠️ 警告：可能导致以下风险</p>
                        <ul style="margin: 10px 0 0 0; padding-left: 20px; color: var(--text-secondary); text-align: left;">
                            <li>插件可以直接修改真实系统文件</li>
                            <li>插件可以修改真实注册表</li>
                            <li>恶意插件可能破坏系统或窃取数据</li>
                            <li>无法撤销此操作带来的后果</li>
                        </ul>
                    </div>
                    <p style="color: #dc3545; font-weight: 500;">
                        仅在您完全信任此插件的情况下继续！
                    </p>
                </div>
            `;
        }
    } else if (action === 'clear') {
        content.innerHTML = `
            <div style="text-align: center;">
                <div style="font-size: 3rem; margin-bottom: 15px;">🗑️</div>
                <h4 style="margin-bottom: 15px; color: #ffc107;">确认清除虚拟化数据？</h4>
                <p style="color: var(--text-secondary); margin-bottom: 15px;">
                    您即将清除插件 <strong>${escapeHtml(pluginId)}</strong> 的所有虚拟化数据。
                </p>
                <div style="background: rgba(220, 53, 69, 0.1); border: 1px solid rgba(220, 53, 69, 0.3); border-radius: 8px; padding: 15px; margin-bottom: 15px;">
                    <p style="color: #ffc107; margin: 0; font-weight: 500;">⚠️ 注意</p>
                    <ul style="margin: 10px 0 0 0; padding-left: 20px; color: var(--text-secondary); text-align: left;">
                        <li>所有虚拟注册表项将被删除</li>
                        <li>所有虚拟文件将被删除</li>
                        <li>此操作无法撤销</li>
                    </ul>
                </div>
            </div>
        `;
    }
    
    document.getElementById('virt-warning-modal').style.display = 'flex';
}

function closeVirtWarningModal() {
    document.getElementById('virt-warning-modal').style.display = 'none';
    pendingVirtAction = null;
}

function confirmVirtAction() {
    if (!pendingVirtAction) return;
    
    const { pluginId, action, enabled } = pendingVirtAction;
    
    if (action === 'toggle') {
        if (ws && ws.readyState === WebSocket.OPEN) {
            sendStandardMessage('toggle_virtualization', { pluginId: pluginId, enabled: enabled });
            showToast(`正在${enabled ? '启用' : '禁用'}虚拟化...`, 'info');
        }
    } else if (action === 'clear') {
        if (ws && ws.readyState === WebSocket.OPEN) {
            sendStandardMessage('clear_plugin_virtualization', { pluginId: pluginId });
            showToast('正在清除数据...', 'info');
        }
    }
    
    closeVirtWarningModal();
}

function toggleVirtPlugin(pluginId, enabled) {
    showVirtWarningModal(pluginId, 'toggle', enabled);
}

function clearVirtPluginData(pluginId) {
    showVirtWarningModal(pluginId, 'clear', false);
}

function deleteVirtRegistryKey(pluginId, keyPath) {
    if (confirm(`确定要删除虚拟注册表键 ${keyPath} 吗？`)) {
        if (ws && ws.readyState === WebSocket.OPEN) {
            sendStandardMessage('delete_virtual_registry_key', { pluginId: pluginId, keyPath: keyPath });
            showToast('正在删除...', 'info');
        }
    }
}

function deleteVirtFile(pluginId, virtualPath) {
    if (confirm(`确定要删除虚拟文件 ${virtualPath} 吗？`)) {
        if (ws && ws.readyState === WebSocket.OPEN) {
            sendStandardMessage('delete_virtual_file', { pluginId: pluginId, virtualPath: virtualPath });
            showToast('正在删除...', 'info');
        }
    }
}

function handleVirtualizationMessage(type, data) {
    switch (type) {
        case 'virtualization_data':
            handleVirtualizationData(data);
            break;
        case 'plugin_virtualization_data':
            break;
        case 'virtualization_cleared':
            showToast(`插件 ${data.pluginId} 的虚拟化数据已清除`, 'success');
            refreshVirtualizationData();
            break;
        case 'virtualization_toggled':
            showToast(`插件 ${data.pluginId} 虚拟化已${data.enabled ? '启用' : '禁用'}`, 'success');
            refreshVirtualizationData();
            break;
        case 'virtual_registry_deleted':
        case 'virtual_file_deleted':
            showToast(data.success ? '删除成功' : '删除失败', data.success ? 'success' : 'error');
            if (selectedVirtPluginId) {
                refreshVirtualizationData();
            }
            break;
        case 'virtualization_error':
            showToast(data.error || '虚拟化操作失败', 'error');
            break;
        case 'plugin_sandbox_warning':
            showSandboxWarningModal(data.pluginId, data.pluginName);
            break;
    }
}

function formatBytes(bytes) {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function truncateText(text, maxLength) {
    if (!text) return '';
    return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
}

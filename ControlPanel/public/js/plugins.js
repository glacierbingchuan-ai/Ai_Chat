// 插件管理器前端代码 - 优化版

// ==================== 状态管理 ====================
const PluginManagerState = {
    plugins: [],
    currentPluginId: null,
    currentConfig: {},
    
    setPlugins(plugins) {
        this.plugins = plugins || [];
        this.updateStats();
    },
    
    updateStats() {
        const total = this.plugins.length;
        const running = this.plugins.filter(p => p.State === 'Running').length;
        const stopped = this.plugins.filter(p => p.State === 'Stopped' || p.State === 'Initialized').length;
        
        const totalEl = document.getElementById('total-plugins-count');
        const runningEl = document.getElementById('running-plugins-count');
        const stoppedEl = document.getElementById('stopped-plugins-count');
        
        if (totalEl) totalEl.textContent = total;
        if (runningEl) runningEl.textContent = running;
        if (stoppedEl) stoppedEl.textContent = stopped;
    }
};

// 保持向后兼容
let currentPlugins = [];
let currentPluginId = null;
let currentPluginConfig = {};

// ==================== WebSocket 工具函数 ====================

/**
 * 检查 WebSocket 是否已连接
 */
function isWebSocketReady() {
    return ws && ws.readyState === WebSocket.OPEN;
}

/**
 * 发送 WebSocket 消息（带状态检查）
 * @param {string} type - 消息类型
 * @param {object} data - 消息数据
 * @param {function} onError - 错误回调
 * @returns {string|null} 消息ID或null
 */
function sendPluginMessage(type, data, onError) {
    if (!isWebSocketReady()) {
        console.error('WebSocket not connected, state:', ws ? ws.readyState : 'null');
        if (onError) onError('WebSocket 未连接');
        return null;
    }
    return sendStandardMessage(type, data);
}

/**
 * 显示加载状态
 */
function showLoading(containerId, message = '加载中...') {
    const container = document.getElementById(containerId);
    if (container) {
        container.innerHTML = `
            <div class="loading">
                <div class="loading-spinner"></div>
                <div>${message}</div>
            </div>
        `;
    }
}

/**
 * 显示错误状态
 */
function showError(containerId, message) {
    const container = document.getElementById(containerId);
    if (container) {
        container.innerHTML = `
            <div class="error-message">
                <p>${message}</p>
            </div>
        `;
    }
}

// ==================== 初始化 ====================

function initPluginManager() {
    refreshPluginsList();
}

// 刷新插件列表
function refreshPluginsList() {
    showLoading('plugins-container', '加载插件列表中...');
    
    sendPluginMessage('get_plugins', {}, (error) => {
        showError('plugins-container', `WebSocket 未连接: ${error}`);
    });
}

// 处理插件列表消息
function handlePluginsList(data) {
    // 更新状态管理
    PluginManagerState.setPlugins(data.Plugins);
    
    // 保持向后兼容
    currentPlugins = data.Plugins || [];

    const container = document.getElementById('plugins-container');
    
    if (currentPlugins.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">🔌</div>
                <p>暂无已加载的插件</p>
                <p class="empty-subtitle">点击"加载插件"按钮添加新插件</p>
            </div>
        `;
        return;
    }

    let html = '<div class="plugins-grid">';
    
    currentPlugins.forEach(plugin => {
        const stateClass = getPluginStateClass(plugin.State);
        const stateIcon = getPluginStateIcon(plugin.State);
        
        html += `
            <div class="plugin-card ${stateClass}" data-plugin-id="${plugin.Id}">
                <div class="plugin-header">
                    <div class="plugin-icon">${stateIcon}</div>
                    <div class="plugin-info">
                        <h3 class="plugin-name">${escapeHtml(plugin.Name)}</h3>
                        <span class="plugin-version">v${plugin.Version}</span>
                    </div>
                    <div class="plugin-state-badge ${stateClass}">${plugin.State}</div>
                </div>
                
                <div class="plugin-body">
                    <p class="plugin-description">${escapeHtml(plugin.Description || '暂无描述')}</p>
                    <div class="plugin-meta">
                        <span class="plugin-author">👤 ${escapeHtml(plugin.Author || 'Unknown')}</span>
                        <span class="plugin-priority">⚡ 优先级: ${plugin.Priority}</span>
                    </div>

                </div>
                
                <div class="plugin-actions">
                    ${plugin.State === 'Running' ? `
                        <button class="btn btn-warning btn-sm" onclick="stopPlugin('${plugin.Id}')">
                            ⏹️ 停止
                        </button>
                    ` : `
                        <button class="btn btn-success btn-sm" onclick="startPlugin('${plugin.Id}')">
                            ▶️ 启动
                        </button>
                    `}
                    <button class="btn btn-primary btn-sm" onclick="showPluginManage('${plugin.Id}')">
                        🎮 管理
                    </button>
                    <button class="btn btn-info btn-sm" onclick="showPluginReadme('${plugin.Id}')">
                        📖 自述
                    </button>
                    <button class="btn btn-info btn-sm" onclick="showPluginPermissions('${plugin.Id}')">
                        🔒 权限
                    </button>
                    <button class="btn btn-info btn-sm" onclick="reloadPlugin('${plugin.Id}')">
                        🔄 重载
                    </button>
                    <button class="btn btn-danger btn-sm" onclick="unloadPlugin('${plugin.Id}')">
                        🗑️ 卸载
                    </button>
                </div>
            </div>
        `;
    });
    
    html += '</div>';
    container.innerHTML = html;
}

// 获取插件状态样式类
function getPluginStateClass(state) {
    switch (state) {
        case 'Running': return 'state-running';
        case 'Stopped': return 'state-stopped';
        case 'Initialized': return 'state-initialized';
        case 'Error': return 'state-error';
        default: return 'state-unknown';
    }
}

// 获取插件状态图标
function getPluginStateIcon(state) {
    switch (state) {
        case 'Running': return '✅';
        case 'Stopped': return '⏹️';
        case 'Initialized': return '⚡';
        case 'Error': return '❌';
        default: return '❓';
    }
}

// 启动插件
function startPlugin(pluginId) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('start_plugin', { pluginId: pluginId });
        showToast(`正在启动插件 ${pluginId}...`, 'info');
    }
}

// 停止插件
function stopPlugin(pluginId) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('stop_plugin', { pluginId: pluginId });
        showToast(`正在停止插件 ${pluginId}...`, 'info');
    }
}

// 重新加载插件
function reloadPlugin(pluginId) {
    if (confirm(`确定要重新加载插件 ${pluginId} 吗？`)) {
        if (ws && ws.readyState === WebSocket.OPEN) {
            sendStandardMessage('reload_plugin', { pluginId: pluginId });
            showToast(`正在重新加载插件 ${pluginId}...`, 'info');
        }
    }
}

// 卸载插件
function unloadPlugin(pluginId) {
    if (confirm(`确定要卸载插件 ${pluginId} 吗？此操作不可恢复。`)) {
        if (ws && ws.readyState === WebSocket.OPEN) {
            sendStandardMessage('unload_plugin', { pluginId: pluginId });
            showToast(`正在卸载插件 ${pluginId}...`, 'info');
        }
    }
}

// 显示插件管理弹窗（合并命令和配置）
function showPluginManage(pluginId) {
    currentPluginId = pluginId;
    currentPluginConfig = {};
    document.getElementById('plugin-manage-modal').style.display = 'flex';

    // 默认显示命令标签页
    switchPluginTab('commands');

    // 加载命令列表
    const commandsContainer = document.getElementById('plugin-manage-commands-container');
    commandsContainer.innerHTML = `
        <div class="loading">
            <div class="loading-spinner"></div>
            <div>加载命令列表中...</div>
        </div>
    `;

    // 加载配置
    const configContainer = document.getElementById('plugin-manage-config-container');
    configContainer.innerHTML = `
        <div class="loading">
            <div class="loading-spinner"></div>
            <div>加载配置中...</div>
        </div>
    `;

    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('get_plugin_commands', { pluginId: pluginId });
        sendStandardMessage('get_plugin_config', { pluginId: pluginId });
    }
}

// 切换标签页
function switchPluginTab(tabName) {
    // 更新标签按钮状态
    document.querySelectorAll('.plugin-tab').forEach(tab => {
        tab.classList.remove('active');
        if (tab.dataset.tab === tabName) {
            tab.classList.add('active');
        }
    });

    // 更新内容区域显示
    document.querySelectorAll('.plugin-tab-content').forEach(content => {
        content.classList.remove('active');
    });
    document.getElementById(`tab-${tabName}`).classList.add('active');

    // 显示/隐藏保存配置按钮
    const saveBtn = document.getElementById('save-config-btn');
    if (tabName === 'config') {
        saveBtn.style.display = 'inline-block';
    } else {
        saveBtn.style.display = 'none';
    }
}

// 处理插件命令列表（用于管理弹窗）
function handlePluginCommands(data) {
    const container = document.getElementById('plugin-manage-commands-container');
    const commands = data.Commands || [];

    if (commands.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <p>该插件没有可用命令</p>
            </div>
        `;
        return;
    }

    let html = '<div class="plugin-commands-list">';

    commands.forEach(cmd => {
        html += `
            <div class="plugin-command-item">
                <div class="command-info">
                    <h4>${escapeHtml(cmd.Name)}</h4>
                    <p>${escapeHtml(cmd.Description || '暂无描述')}</p>
                    ${cmd.Usage ? `<small class="command-usage">用法: ${escapeHtml(cmd.Usage)}</small>` : ''}
                </div>
                <button class="btn btn-primary btn-sm" onclick="executePluginManageCommand('${cmd.Name}')">
                    执行
                </button>
            </div>
        `;
    });

    html += '</div>';
    container.innerHTML = html;
}

// 当前正在执行的命令
let currentCommandName = null;
let currentCommandParams = [];

// 执行插件命令（在管理弹窗中）
function executePluginManageCommand(commandName) {
    if (!currentPluginId) return;

    // 所有命令使用通用参数输入
    showCommandParamModal(commandName, `执行命令: ${commandName}`, [
        { name: 'params', label: '参数 (格式: key1=value1,key2=value2)', type: 'text', required: false }
    ]);
}

// 显示命令参数输入弹窗
function showCommandParamModal(commandName, description, params) {
    currentCommandName = commandName;
    currentCommandParams = params;

    document.getElementById('command-param-title').textContent = `执行: ${commandName}`;
    document.getElementById('command-param-description').textContent = description;

    const inputsContainer = document.getElementById('command-param-inputs');
    let html = '';

    params.forEach(param => {
        const inputId = `cmd-param-${param.name}`;

        if (param.type === 'textarea') {
            html += `
                <div class="form-group">
                    <label for="${inputId}">${param.label}${param.required ? ' *' : ''}</label>
                    <textarea id="${inputId}" class="form-control" rows="4" ${param.required ? 'required' : ''}></textarea>
                </div>
            `;
        } else if (param.type === 'number') {
            html += `
                <div class="form-group">
                    <label for="${inputId}">${param.label}${param.required ? ' *' : ''}</label>
                    <input type="number" id="${inputId}" class="form-control"
                        ${param.min !== undefined ? `min="${param.min}"` : ''}
                        ${param.max !== undefined ? `max="${param.max}"` : ''}
                        ${param.step !== undefined ? `step="${param.step}"` : ''}
                        ${param.required ? 'required' : ''}>
                </div>
            `;
        } else {
            html += `
                <div class="form-group">
                    <label for="${inputId}">${param.label}${param.required ? ' *' : ''}</label>
                    <input type="text" id="${inputId}" class="form-control" ${param.required ? 'required' : ''}>
                </div>
            `;
        }
    });

    inputsContainer.innerHTML = html;
    document.getElementById('plugin-command-param-modal').style.display = 'flex';
}

// 关闭命令参数弹窗
function closeCommandParamModal() {
    document.getElementById('plugin-command-param-modal').style.display = 'none';
    currentCommandName = null;
    currentCommandParams = [];
}

// 执行带参数的命令
function executeCommandWithParams() {
    if (!currentPluginId || !currentCommandName) return;

    const parameters = {};

    // 收集参数值
    for (const param of currentCommandParams) {
        const input = document.getElementById(`cmd-param-${param.name}`);
        if (!input) continue;

        const value = input.value.trim();

        // 验证必填项
        if (param.required && !value) {
            showToast(`${param.label} 不能为空`, 'error');
            input.focus();
            return;
        }

        // 特殊处理通用参数输入
        if (param.name === 'params' && value) {
            // 解析 key1=value1,key2=value2 格式
            value.split(',').forEach(pair => {
                const [key, val] = pair.split('=');
                if (key && val) {
                    parameters[key.trim()] = val.trim();
                }
            });
        } else {
            parameters[param.name] = value;
        }
    }

    // 发送命令
    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('execute_plugin_command', {
            pluginId: currentPluginId,
            command: currentCommandName,
            parameters: parameters
        });
        showToast(`正在执行命令 ${currentCommandName}...`, 'info');
    }

    closeCommandParamModal();
}

// 处理插件配置（用于管理弹窗）
function handlePluginManageConfig(data) {
    const container = document.getElementById('plugin-manage-config-container');
    
    // 兼容不同的数据结构
    const config = data.Configuration || data.configuration || {};
    currentPluginConfig = config;

    if (Object.keys(config).length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <p>该插件没有可配置项</p>
            </div>
        `;
        return;
    }

    let html = '<div class="plugin-config-form">';

    for (const [key, value] of Object.entries(config)) {
        const inputId = `manage-config-${key}`;
        let inputHtml = '';

        if (typeof value === 'boolean') {
            inputHtml = `
                <select id="${inputId}" class="form-control">
                    <option value="true" ${value ? 'selected' : ''}>是</option>
                    <option value="false" ${!value ? 'selected' : ''}>否</option>
                </select>
            `;
        } else if (typeof value === 'number') {
            inputHtml = `<input type="number" id="${inputId}" value="${value}" class="form-control">`;
        } else if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
            // MAP类型（对象/字典）使用特殊编辑器
            inputHtml = renderMapEditor(inputId, value);
        } else if (Array.isArray(value)) {
            // 数组类型
            inputHtml = `<textarea id="${inputId}" class="form-control" rows="3" placeholder="JSON数组格式">${escapeHtml(JSON.stringify(value))}</textarea>`;
        } else {
            inputHtml = `<input type="text" id="${inputId}" value="${escapeHtml(value?.toString() || '')}" class="form-control">`;
        }

        html += `
            <div class="form-group">
                <label for="${inputId}">${escapeHtml(key)}</label>
                ${inputHtml}
            </div>
        `;
    }

    html += '</div>';
    container.innerHTML = html;
    
    // 为MAP编辑器绑定事件
    bindMapEditorEvents();
}

// 渲染MAP类型编辑器
function renderMapEditor(inputId, mapData) {
    const entries = Object.entries(mapData);
    let rowsHtml = '';
    
    entries.forEach(([k, v], index) => {
        rowsHtml += renderMapRow(inputId, k, v, index);
    });
    
    return `
        <div class="map-editor" id="${inputId}-container" data-map-id="${inputId}">
            <div class="map-editor-header">
                <span class="map-editor-label">键值对配置 (${entries.length} 项)</span>
                <button type="button" class="btn btn-primary btn-sm" onclick="addMapRow('${inputId}')">+ 添加</button>
            </div>
            <div class="map-editor-rows" id="${inputId}-rows">
                ${rowsHtml}
            </div>
            <input type="hidden" id="${inputId}" value="${escapeHtml(JSON.stringify(mapData))}">
        </div>
    `;
}

// 渲染单行MAP编辑器
function renderMapRow(inputId, key, value, index) {
    const valueType = getValueType(value);
    const valueStr = formatValueForDisplay(value, valueType);
    
    return `
        <div class="map-editor-row" data-index="${index}">
            <input type="text" class="map-editor-key" value="${escapeHtml(key)}" placeholder="键" oninput="updateMapHiddenInput('${inputId}')">
            <select class="map-editor-type" onchange="onMapTypeChange('${inputId}', this)">
                <option value="string" ${valueType === 'string' ? 'selected' : ''}>字符串</option>
                <option value="number" ${valueType === 'number' ? 'selected' : ''}>数字</option>
                <option value="boolean" ${valueType === 'boolean' ? 'selected' : ''}>布尔值</option>
            </select>
            <input type="text" class="map-editor-value" value="${escapeHtml(valueStr)}" placeholder="值" oninput="updateMapHiddenInput('${inputId}')">
            <button type="button" class="btn btn-danger btn-sm map-editor-delete" onclick="removeMapRow('${inputId}', this)">删除</button>
        </div>
    `;
}

// 获取值的类型
function getValueType(value) {
    if (typeof value === 'boolean') return 'boolean';
    if (typeof value === 'number') return 'number';
    return 'string';
}

// 格式化值用于显示
function formatValueForDisplay(value, type) {
    if (type === 'boolean') {
        return value ? 'true' : 'false';
    }
    return String(value);
}

// 类型改变时处理
function onMapTypeChange(inputId, selectElement) {
    const row = selectElement.closest('.map-editor-row');
    const valueInput = row.querySelector('.map-editor-value');
    const newType = selectElement.value;
    const currentValue = valueInput.value;
    
    // 尝试转换当前值为新类型
    let convertedValue = currentValue;
    if (newType === 'boolean') {
        convertedValue = currentValue === 'true' || currentValue === '1' ? 'true' : 'false';
    } else if (newType === 'number') {
        const num = parseFloat(currentValue);
        convertedValue = isNaN(num) ? '0' : String(num);
    }
    
    valueInput.value = convertedValue;
    updateMapHiddenInput(inputId);
}

// 绑定MAP编辑器事件
function bindMapEditorEvents() {
    document.querySelectorAll('.map-editor').forEach(editor => {
        const mapId = editor.dataset.mapId;
        const container = document.getElementById(`${mapId}-container`);
        
        // 使用事件委托监听所有输入变化
        container.addEventListener('input', (e) => {
            if (e.target.classList.contains('map-editor-key') || 
                e.target.classList.contains('map-editor-value')) {
                updateMapHiddenInput(mapId);
            }
        });
        
        // 初始更新一次隐藏输入框
        updateMapHiddenInput(mapId);
    });
}

// 添加MAP行
function addMapRow(inputId) {
    const rowsContainer = document.getElementById(`${inputId}-rows`);
    const index = rowsContainer.children.length;
    
    // 使用 renderMapRow 渲染新行，默认类型为字符串
    const rowHtml = renderMapRow(inputId, '', '', index);
    
    rowsContainer.insertAdjacentHTML('beforeend', rowHtml);
    updateMapHiddenInput(inputId);
}

// 删除MAP行
function removeMapRow(inputId, btnElement) {
    const rowsContainer = document.getElementById(`${inputId}-rows`);
    const row = btnElement.closest('.map-editor-row');
    if (row) {
        row.remove();
        // 重新索引所有行
        rowsContainer.querySelectorAll('.map-editor-row').forEach((r, i) => {
            r.dataset.index = i;
        });
        updateMapHiddenInput(inputId);
    }
}

// 更新MAP隐藏输入框的值
function updateMapHiddenInput(inputId) {
    const rowsContainer = document.getElementById(`${inputId}-rows`);
    const hiddenInput = document.getElementById(inputId);
    const mapData = {};
    
    rowsContainer.querySelectorAll('.map-editor-row').forEach(row => {
        const key = row.querySelector('.map-editor-key').value.trim();
        const valueStr = row.querySelector('.map-editor-value').value;
        const typeSelect = row.querySelector('.map-editor-type');
        const valueType = typeSelect ? typeSelect.value : 'string';
        
        if (key) {
            // 根据类型选择器解析值
            let value;
            switch (valueType) {
                case 'boolean':
                    value = valueStr === 'true';
                    break;
                case 'number':
                    const num = parseFloat(valueStr);
                    value = isNaN(num) ? 0 : num;
                    break;
                case 'string':
                default:
                    value = valueStr;
                    break;
            }
            mapData[key] = value;
        }
    });
    
    hiddenInput.value = JSON.stringify(mapData);
    
    // 更新计数显示
    const container = document.getElementById(`${inputId}-container`);
    const label = container?.querySelector('.map-editor-label');
    if (label) {
        const count = Object.keys(mapData).length;
        label.textContent = `键值对配置 (${count} 项)`;
    }
}

// 保存插件配置（在管理弹窗中）
function savePluginManageConfig() {
    if (!currentPluginId) return;

    // 首先强制更新所有MAP编辑器的隐藏输入框
    document.querySelectorAll('.map-editor').forEach(editor => {
        const mapId = editor.dataset.mapId;
        updateMapHiddenInput(mapId);
    });

    const newConfig = {};
    for (const key of Object.keys(currentPluginConfig)) {
        const input = document.getElementById(`manage-config-${key}`);
        if (input) {
            let value = input.value;
            const originalValue = currentPluginConfig[key];
            
            // 根据原始类型转换值
            if (typeof originalValue === 'boolean') {
                value = value === 'true';
            } else if (typeof originalValue === 'number') {
                value = parseFloat(value);
            } else if (typeof originalValue === 'object' && originalValue !== null && !Array.isArray(originalValue)) {
                // MAP类型 - 解析JSON
                try {
                    value = JSON.parse(value);
                } catch (e) {
                    showToast(`配置项 "${key}" 格式错误，请检查JSON格式: ${e.message}`, 'error');
                    console.error(`JSON parse error for ${key}:`, value, e);
                    return;
                }
            } else if (Array.isArray(originalValue)) {
                // 数组类型 - 解析JSON
                try {
                    value = JSON.parse(value);
                } catch (e) {
                    showToast(`配置项 "${key}" 格式错误，请使用有效的JSON数组格式: ${e.message}`, 'error');
                    console.error(`JSON parse error for ${key}:`, value, e);
                    return;
                }
            }
            newConfig[key] = value;
        }
    }

    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('set_plugin_config', {
            pluginId: currentPluginId,
            configuration: newConfig
        });
        showToast('正在保存配置...', 'info');
    }
}

// 关闭插件管理弹窗
function closePluginManageModal() {
    document.getElementById('plugin-manage-modal').style.display = 'none';
    currentPluginId = null;
    currentPluginConfig = {};
}

// 显示插件自述
function showPluginReadme(pluginId) {
    currentPluginId = pluginId;
    document.getElementById('plugin-readme-modal').style.display = 'flex';

    const container = document.getElementById('plugin-readme-content');
    container.innerHTML = `
        <div class="loading">
            <div class="loading-spinner"></div>
            <div>加载自述文档中...</div>
        </div>
    `;

    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('get_plugin_readme', { pluginId: pluginId });
    }
}

// 处理插件自述
function handlePluginReadme(data) {
    const container = document.getElementById('plugin-readme-content');
    const readme = data.Readme || '<p>该插件没有提供自述文档</p>';

    container.innerHTML = `
        <div class="plugin-readme-content">
            ${readme}
        </div>
    `;
}

// 关闭插件自述模态框
function closePluginReadmeModal() {
    document.getElementById('plugin-readme-modal').style.display = 'none';
    currentPluginId = null;
}

// 显示插件权限
function showPluginPermissions(pluginId) {
    currentPluginId = pluginId;
    document.getElementById('plugin-permissions-modal').style.display = 'flex';

    const container = document.getElementById('plugin-permissions-content');
    container.innerHTML = `
        <div class="loading">
            <div class="loading-spinner"></div>
            <div>加载权限列表中...</div>
        </div>
    `;

    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('get_plugin_permissions', { pluginId: pluginId });
    }
}

// 处理插件权限
function handlePluginPermissions(data) {
    const container = document.getElementById('plugin-permissions-content');
    const systemPermissions = data.SystemPermissions || [];
    const declaredPermissions = data.DeclaredPermissions || [];

    let html = '<div class="plugin-permissions-list">';

    // 系统识别的权限
    html += '<div class="permissions-section">';
    html += '<h3>🔒 系统自动识别的权限</h3>';
    if (systemPermissions.length === 0) {
        html += '<p class="no-permissions">无</p>';
    } else {
        html += '<ul class="permissions-list system-permissions">';
        systemPermissions.forEach(perm => {
            html += `<li class="permission-item">${escapeHtml(perm)}</li>`;
        });
        html += '</ul>';
    }
    html += '</div>';

    // 插件自述的权限
    html += '<div class="permissions-section">';
    html += '<h3>📝 插件自述的权限</h3>';
    if (declaredPermissions.length === 0) {
        html += '<p class="no-permissions">该插件没有声明额外权限</p>';
    } else {
        html += '<ul class="permissions-list declared-permissions">';
        declaredPermissions.forEach(perm => {
            html += `<li class="permission-item">${escapeHtml(perm)}</li>`;
        });
        html += '</ul>';
    }
    html += '</div>';

    html += '</div>';
    container.innerHTML = html;
}

// 关闭插件权限模态框
function closePluginPermissionsModal() {
    document.getElementById('plugin-permissions-modal').style.display = 'none';
    currentPluginId = null;
}

// 显示加载插件模态框
function showLoadPluginModal() {
    document.getElementById('load-plugin-modal').style.display = 'flex';
    document.getElementById('plugin-file-path').value = '';
    initDropZone();
}

// 关闭加载插件模态框
function closeLoadPluginModal() {
    document.getElementById('load-plugin-modal').style.display = 'none';
}

// 初始化拖放区域
function initDropZone() {
    const dropZone = document.getElementById('plugin-drop-zone');
    const fileInput = document.getElementById('plugin-file-input');
    const filePathInput = document.getElementById('plugin-file-path');
    
    if (!dropZone) return;
    
    // 点击区域选择文件
    dropZone.addEventListener('click', () => {
        fileInput.click();
    });
    
    // 文件选择处理
    fileInput.addEventListener('change', (e) => {
        const file = e.target.files[0];
        if (file) {
            handlePluginFile(file);
        }
    });
    
    // 拖放事件
    dropZone.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.add('drag-over');
    });
    
    dropZone.addEventListener('dragleave', (e) => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.remove('drag-over');
    });
    
    dropZone.addEventListener('drop', (e) => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.remove('drag-over');
        
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            const file = files[0];
            if (file.name.endsWith('.dll')) {
                handlePluginFile(file);
            } else {
                showToast('请选择 DLL 文件', 'error');
            }
        }
    });
}

// 处理插件文件
function handlePluginFile(file) {
    const filePathInput = document.getElementById('plugin-file-path');
    
    // 使用 FileReader 读取文件为 ArrayBuffer
    const reader = new FileReader();
    reader.onload = (e) => {
        // 保存文件内容到全局变量，供后续使用
        window.selectedPluginFile = {
            name: file.name,
            content: e.target.result  // ArrayBuffer
        };
        filePathInput.value = file.name;
        showToast(`已选择文件: ${file.name}，点击加载按钮上传`, 'success');
    };
    reader.readAsArrayBuffer(file);
}

// 从文件加载插件
function loadPluginFromFile() {
    if (!window.selectedPluginFile) {
        showToast('请先选择插件文件', 'error');
        return;
    }
    
    const fileName = window.selectedPluginFile.name;
    const arrayBuffer = window.selectedPluginFile.content;
    
    // 将 ArrayBuffer 转换为 Base64
    const bytes = new Uint8Array(arrayBuffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    const base64Content = btoa(binary);
    
    if (ws && ws.readyState === WebSocket.OPEN) {
        sendStandardMessage('upload_and_load_plugin', { 
            fileName: fileName,
            fileContent: base64Content
        });
        showToast(`正在上传并加载插件: ${fileName}...`, 'info');
        closeLoadPluginModal();
        // 清空选择的文件
        window.selectedPluginFile = null;
        document.getElementById('plugin-file-path').value = '';
    }
}

// 处理插件操作结果
function handlePluginOperationResult(data) {
    // 兼容大小写（后端可能返回 Success 或 success）
    const success = data.success !== undefined ? data.success : data.Success;
    const message = data.message || data.Message;
    
    if (success) {
        showToast(message || '操作成功', 'success');
        // 刷新插件列表
        setTimeout(refreshPluginsList, 500);
    } else {
        showToast(message || '操作失败', 'error');
    }
}

// 当前命令结果（用于复制）
let currentCommandResult = '';

// 处理命令执行结果
function handlePluginCommandResult(data) {
    const resultContent = document.getElementById('command-result-content');

    if (data.Success) {
        const resultStr = typeof data.Result === 'object' ?
            JSON.stringify(data.Result, null, 2) : String(data.Result);
        currentCommandResult = resultStr;

        // 格式化显示结果
        let html = '<div class="command-result-success">';
        html += '<div class="result-header">✅ 执行成功</div>';
        
        // 检查是否是MAP类型结果（包含Type和Data字段）
        if (data.Result && typeof data.Result === 'object' && data.Result.Type === 'Map' && data.Result.Data) {
            html += formatMapResult(data.Result.Data);
        } else {
            html += '<pre class="result-content">';
            html += escapeHtml(resultStr);
            html += '</pre>';
        }
        
        html += '</div>';

        resultContent.innerHTML = html;
    } else {
        currentCommandResult = data.Message || '执行失败';

        let html = '<div class="command-result-error">';
        html += '<div class="result-header">❌ 执行失败</div>';
        html += '<div class="result-message">';
        html += escapeHtml(data.Message || '未知错误');
        html += '</div>';
        html += '</div>';

        resultContent.innerHTML = html;
    }

    // 显示结果弹窗
    document.getElementById('plugin-command-result-modal').style.display = 'flex';
}

// 格式化MAP类型结果显示为表格
function formatMapResult(mapData) {
    if (!mapData || typeof mapData !== 'object') {
        return '<pre class="result-content">' + escapeHtml(String(mapData)) + '</pre>';
    }
    
    const entries = Object.entries(mapData);
    if (entries.length === 0) {
        return '<div class="result-content"><p>（空MAP）</p></div>';
    }
    
    let html = '<div class="map-result-container">';
    html += '<table class="map-result-table">';
    html += '<thead><tr><th>键 (Key)</th><th>值 (Value)</th></tr></thead>';
    html += '<tbody>';
    
    for (const [key, value] of entries) {
        const valueStr = typeof value === 'object' ? JSON.stringify(value, null, 2) : String(value);
        const valueClass = typeof value === 'number' ? 'value-number' : 
                          typeof value === 'boolean' ? 'value-boolean' : 'value-string';
        
        html += '<tr>';
        html += '<td class="map-key">' + escapeHtml(key) + '</td>';
        html += '<td class="map-value ' + valueClass + '">' + escapeHtml(valueStr) + '</td>';
        html += '</tr>';
    }
    
    html += '</tbody></table>';
    html += '</div>';
    
    return html;
}

// 关闭命令结果弹窗
function closeCommandResultModal() {
    document.getElementById('plugin-command-result-modal').style.display = 'none';
    currentCommandResult = '';
}

// 复制命令结果
function copyCommandResult() {
    if (!currentCommandResult) return;

    navigator.clipboard.writeText(currentCommandResult).then(() => {
        showToast('结果已复制到剪贴板', 'success');
    }).catch(() => {
        showToast('复制失败', 'error');
    });
}

// HTML 转义
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// 处理 WebSocket 消息
function handlePluginWebSocketMessage(type, data) {
    console.log('handlePluginWebSocketMessage:', type, data);
    switch (type) {
        case 'plugins_list':
            handlePluginsList(data);
            break;
        case 'plugin_config':
            handlePluginManageConfig(data);
            break;
        case 'plugin_commands':
            handlePluginCommands(data);
            break;
        case 'plugin_started':
        case 'plugin_stopped':
        case 'plugin_reloaded':
        case 'plugin_unloaded':
        case 'plugin_config_updated':
            handlePluginOperationResult(data);
            break;
        case 'plugin_start_failed':
            handlePluginOperationResult(data);
            break;
        case 'plugin_loaded_from_file':
            handlePluginOperationResult(data);
            // 同时通知插件市场下载流程
            if (typeof window.handlePluginMarketLoadedFromFile === 'function') {
                window.handlePluginMarketLoadedFromFile(data);
            }
            break;
        case 'plugin_command_result':
            handlePluginCommandResult(data);
            break;
        case 'plugin_readme':
            handlePluginReadme(data);
            break;
        case 'plugin_permissions':
            handlePluginPermissions(data);
            break;
        case 'plugin_error':
            showToast(data.Message || '插件操作失败', 'error');
            break;
    }
}

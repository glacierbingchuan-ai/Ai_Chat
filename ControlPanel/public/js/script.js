        console.log(`_____ _               _____ _____ ______ _____  
  / ____| |        /\\   / ____|_   _|  ____|  __ \\ 
 | |  __| |       /  \\ | |      | | | |__  | |__) |
 | | |_ | |      / /\\ \\| |      | | |  __| |  _  / 
 | |__| | |____ / ____ \\ |____ _| |_| |____| | \\ \\ 
  \\_____|______/_/    \\_\\_____|_____|______|_|  \\_\\
                                                   `);
        // Global variables
        let ws = null;
        let config = {};
        let userConfig = {};
        let llmStatusInterval = null;
        let lastLlmStatus = 'offline'; // Track last LLM status
        let loadingStartTime = null;
        let loadingWarningTimer = null;
        let quoteLoaded = false;
        let backgroundLoaded = false;
        let wsConnected = false;
        let authFailed = false; // Flag to indicate authentication failure
        let selectedUserId = 0; // Current selected user ID
        let allowedUsers = []; // List of allowed users
        let allowedGroups = []; // List of allowed groups

        // Initialize application
        function init() {
            // Record loading start time
            loadingStartTime = Date.now();
            
            // Set timer to show warning after 5 seconds
            loadingWarningTimer = setTimeout(() => {
                const warningElement = document.getElementById('loading-warning');
                if (warningElement) {
                    warningElement.style.display = 'block';
                }
            }, 5000);
            
            // Check if background image is loaded
            checkBackgroundLoad();
            
            connectWebSocket();
            fetchQuote();
            
            // Start snow animation
            startSnowAnimation();
        }

        // Snow animation
        function startSnowAnimation() {
            const container = document.getElementById('snow-container');
            if (!container) return;
            
            const containerWidth = window.innerWidth;
            const containerHeight = window.innerHeight;
            const flakeCount = 80; // 雪花数量

            // 清空容器
            container.innerHTML = '';

            // 创建雪花
            for (let i = 0; i < flakeCount; i++) {
                const flake = document.createElement('div');
                flake.className = 'snow-flake';

                // 随机设置雪花属性
                const size = Math.random() * 5 + 2; // 大小：2-7px
                const left = Math.random() * containerWidth; // 水平位置
                const delay = Math.random() * 10; // 延迟：0-10s
                const duration = Math.random() * 2 + 2; // 持续时间：2-4s

                // 设置样式
                flake.style.width = `${size}px`;
                flake.style.height = `${size}px`;
                flake.style.left = `${left}px`;
                flake.style.animationDelay = `${delay}s`;
                flake.style.animationDuration = `${duration}s`;

                // 添加到容器
                container.appendChild(flake);
            }
        }

        // Available background images
        const backgroundImages = [
            '/css/image/image_1.jpg',
            '/css/image/image_2.jpg',
            '/css/image/image_3.jpg',
            '/css/image/image_4.jpg',
            '/css/image/image_5.jpg'
        ];

        // Get random background image
        function getRandomBackgroundImage() {
            const randomIndex = Math.floor(Math.random() * backgroundImages.length);
            return backgroundImages[randomIndex];
        }

        // Set random background image
        function setRandomBackground() {
            const randomImage = getRandomBackgroundImage();
            // Create a style element to set the background image
            const styleId = 'dynamic-background-style';
            let styleElement = document.getElementById(styleId);
            if (!styleElement) {
                styleElement = document.createElement('style');
                styleElement.id = styleId;
                document.head.appendChild(styleElement);
            }
            styleElement.textContent = `
                body::before {
                    background-image: url('${randomImage}') !important;
                }
            `;
            return randomImage;
        }

        // Check if background image is loaded
        function checkBackgroundLoad() {
            const bgImageUrl = setRandomBackground();
            const img = new Image();
            img.onload = function() {
                backgroundLoaded = true;
                checkIfAllLoaded();
            };
            img.onerror = function() {
                // Background image failed to load, but still proceed
                backgroundLoaded = true;
                checkIfAllLoaded();
            };
            img.src = bgImageUrl;
        }

        // Check if all resources are loaded
        function checkIfAllLoaded() {
            if (quoteLoaded && backgroundLoaded && wsConnected) {
                hideLoadingScreen();
            }
        }

        let sentences = [];
        let quoteInterval = null;
        let typingInterval = null;
        
        // Fetch quotes from local file
        function fetchQuote() {
            fetch('css/Sentence.txt')
                .then(response => response.text())
                .then(data => {
                    sentences = data.trim().split('\n').filter(s => s.trim() !== '');
                    if (sentences.length > 0) {
                        showRandomQuote();
                        // Start interval after first quote is shown
                        if (!quoteInterval) {
                            startQuoteInterval();
                        }
                    } else {
                        document.getElementById('quote-text').textContent = 'Sentence.txt 文件为空';
                    }
                    quoteLoaded = true;
                    checkIfAllLoaded();
                })
                .catch(error => {
                    console.error('Error fetching quotes:', error);
                    document.getElementById('quote-text').textContent = '读取句子文件失败';
                    quoteLoaded = true; // Still mark as loaded even if there's an error
                    checkIfAllLoaded();
                });
        }
        
        // Show random quote with typing effect
        function showRandomQuote() {
            if (sentences.length > 0) {
                const randomIndex = Math.floor(Math.random() * sentences.length);
                const randomQuote = sentences[randomIndex];
                typeQuote(randomQuote);
            }
        }
        
        // Type quote with typing effect
        function typeQuote(quote) {
            const quoteElement = document.getElementById('quote-text');
            
            // Clear any existing typing interval
            if (typingInterval) {
                clearInterval(typingInterval);
            }
            
            // Fade out current text
            quoteElement.style.opacity = '0';
            quoteElement.style.transition = 'opacity 0.3s ease';
            
            setTimeout(() => {
                // Clear content after fade out
                quoteElement.textContent = '';
                let index = 0;
                
                // Fade in new text
                quoteElement.style.opacity = '1';
                
                typingInterval = setInterval(() => {
                    if (index < quote.length) {
                        quoteElement.textContent += quote.charAt(index);
                        index++;
                    } else {
                        clearInterval(typingInterval);
                        typingInterval = null;
                    }
                }, 50); // 50ms per character
            }, 300); // Wait for fade out to complete
        }
        
        // Start quote interval
        function startQuoteInterval() {
            if (quoteInterval) {
                clearInterval(quoteInterval);
            }
            
            // Change quote every 20 seconds after typing completes
            quoteInterval = setInterval(showRandomQuote, 20 * 1000);
        }

        // Hide loading screen
        function hideLoadingScreen() {
            // 如果版本已过期，保持加载屏幕显示
            if (isVersionNotAllowed) {
                return;
            }
            
            // Clear loading warning timer
            if (loadingWarningTimer) {
                clearTimeout(loadingWarningTimer);
                loadingWarningTimer = null;
            }
            
            const loadingScreen = document.getElementById('loading-screen');
            if (loadingScreen) {
                loadingScreen.style.opacity = '0';
                loadingScreen.style.visibility = 'hidden';
            }
            
            // Start fireworks animation after loading completes
            if (typeof Fireworks !== 'undefined') {
                Fireworks.start(5000);
            }
        }

        // Show loading screen
        function showLoadingScreen() {
            // Reset loading start time
            loadingStartTime = Date.now();
            
            // Clear existing warning timer if any
            if (loadingWarningTimer) {
                clearTimeout(loadingWarningTimer);
                loadingWarningTimer = null;
            }
            
            // Set new timer to show warning after 5 seconds
            loadingWarningTimer = setTimeout(() => {
                const warningElement = document.getElementById('loading-warning');
                if (warningElement) {
                    warningElement.style.display = 'block';
                }
            }, 5000);
            
            const loadingScreen = document.getElementById('loading-screen');
            if (loadingScreen) {
                loadingScreen.style.opacity = '1';
                loadingScreen.style.visibility = 'visible';
            }
        }

        // Check LLM status immediately when WebSocket connects
        function checkLlmStatusOnConnect() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('get_llm_status');
                // Start periodic LLM status checks after first check
                startLlmStatusTimer();
            }
        }

        // Get access key from URL parameters
        function getAccessKey() {
            const urlParams = new URLSearchParams(window.location.search);
            return urlParams.get('key');
        }

        // Connect to WebSocket server
        function connectWebSocket() {
            const key = getAccessKey();
            if (!key) {
                showToast('Missing access key, please use the correct link to access the control panel', 'error');
                return;
            }
            const wsUrl = `ws://localhost:8080/ws?key=${key}`;
            ws = new WebSocket(wsUrl);

            ws.onopen = function() {
                wsConnected = true;
                checkIfAllLoaded();
                // Check LLM status immediately after connection
                checkLlmStatusOnConnect();
            };

            ws.onmessage = function(event) {
                try {
                    const message = JSON.parse(event.data);
                    handleWebSocketMessage(message);
                } catch (error) {
                    console.error('Error parsing WebSocket message:', error);
                }
            };

            ws.onclose = function() {
                // Only show toast and try to reconnect if authentication didn't fail
                if (!authFailed) {
                    showToast('WebSocket 连接已断开', 'error');
                    // Show loading screen when connection is lost
                    showLoadingScreen();
                    // Reconnect after 3 seconds
                    setTimeout(connectWebSocket, 3000);
                }
            };

            ws.onerror = function(error) {
                // Only show toast and loading screen if authentication didn't fail
                if (!authFailed) {
                    showToast('WebSocket 连接错误', 'error');
                    // Show loading screen on error
                    showLoadingScreen();
                }
            };
        }



        // Create standard message for sending
        function createStandardMessage(type, data = null) {
            return {
                type: type,
                data: data,
                timestamp: new Date().toISOString(),
                id: 'frontend_' + Math.random().toString(36).substr(2, 9)
            };
        }

        // Send standard message
        function sendStandardMessage(type, data = null) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                const message = createStandardMessage(type, data);
                ws.send(JSON.stringify(message));
                return message.id;
            }
            return null;
        }

        // Send message (alias for sendStandardMessage)
        function sendMessage(message) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify(message));
                return true;
            }
            console.warn('WebSocket is not connected');
            return false;
        }

        // Handle initial data
        function handleInitData(data) {
            // Update logs
            updateLogs(data.logs);

            // Update config
            config = data.config;
            updateConfigForm();

            // 注意：EULA弹窗现在由页面加载时的API检查控制
            // 这里保留逻辑作为备用，但通常不会触发
            if (config.isFirstRun || !config.eulaAccepted) {
                // 只有在弹窗未显示时才显示
                var modal = document.getElementById('eula-modal');
                if (modal && modal.style.display !== 'flex') {
                    showEulaModal();
                }
            }

            // Update user config if available
            if (data.userConfig) {
                userConfig = data.userConfig;
                updateUserConfigForm();
            }

            // Update allowed users and groups
            if (config.allowedUserIds) {
                allowedUsers = config.allowedUserIds;
            }
            if (config.allowedGroupIds) {
                allowedGroups = config.allowedGroupIds;
            }
            updateUserSelector(allowedUsers, allowedGroups);
            updateAllowedUsersList();

            // Update selected user
            if (data.selectedUserId) {
                const previousUserId = selectedUserId;
                selectedUserId = data.selectedUserId;
                const selector = document.getElementById('user-selector');
                if (selector) {
                    selector.value = selectedUserId;
                }
                updateUserSettingsVisibility();
                // Update user list display after selectedUserId is set
                updateUserSelector(allowedUsers, allowedGroups);

                // Show success toast only when switching users (not on initial load)
                if (previousUserId !== 0 && previousUserId !== selectedUserId) {
                    showToast(`已切换到用户 ${selectedUserId}`, 'success');
                }
            } else if (!allowedUsers || allowedUsers.length === 0) {
                // No users at all, just update UI without showing add user modal
                selectedUserId = 0;
                updateUserSettingsVisibility();
                updateRoleCardsUserDisplay();
                updateChatHistoryUserDisplay();
            }

            // Update scheduled events
            updateEvents(data.scheduledEvents);

            // Update stats
            updateStats(data.stats);
            updateCurrentUserStats(data.stats);

            // Update chat history (if available)
            if (data.chatHistory && Array.isArray(data.chatHistory)) {
                handleChatHistory(data.chatHistory);
            }
        }

        // Show EULA modal
        function showEulaModal() {
            console.log('showEulaModal called');
            const modal = document.getElementById('eula-modal');
            if (modal) {
                modal.style.display = 'flex';
                console.log('EULA modal shown');
            } else {
                console.warn('EULA modal element not found');
            }
        }

        // Close EULA modal
        function closeEulaModal() {
            console.log('closeEulaModal called');
            const modal = document.getElementById('eula-modal');
            if (modal) {
                modal.style.display = 'none';
                console.log('EULA modal closed');
            } else {
                console.warn('EULA modal element not found');
            }
        }

        // Accept EULA
        function acceptEula() {
            console.log('acceptEula called');

            // Update config
            config.isFirstRun = false;
            config.eulaAccepted = true;

            // 获取key参数
            var urlParams = new URLSearchParams(window.location.search);
            var accessKey = urlParams.get('key');

            // 构建带key的URL
            var url = '/api/accept-eula';
            if (accessKey) {
                url += '?key=' + encodeURIComponent(accessKey);
            }

            // Send update to server via API
            fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            }).then(function(response) {
                console.log('EULA accepted on server');
            }).catch(function(error) {
                console.error('Error accepting EULA:', error);
            });

            // 关闭弹窗
            closeEulaModal();

            // 显示提示
            showToast('已接受使用协议，欢迎使用！', 'success');

            // 加载统计脚本
            loadUstatScript();
        }

        // 加载统计脚本
        function loadUstatScript() {
            // 检查脚本是否已加载
            var existingScript = document.querySelector('script[src*="ustat.js"]');
            if (existingScript) {
                return; // 已加载，不再重复加载
            }
            var script = document.createElement('script');
            script.async = true;
            script.src = 'https://019c84ae-b2ff-7e69-b21a-fc3c8ef42308.spst2.com/ustat.js';
            document.body.appendChild(script);
        }

        // Reject EULA and exit
        function rejectEula() {
            console.log('rejectEula called');

            if (confirm('您必须接受使用协议才能继续使用本软件。确定要退出吗？')) {
                console.log('User confirmed exit');

                // Send message to server to shutdown
                var sent = sendMessage({
                    type: 'reject_eula',
                    data: {}
                });
                console.log('reject_eula message sent:', sent);

                // Close the page
                window.close();

                // If window.close() doesn't work, show a message after a short delay
                setTimeout(function() {
                    document.body.innerHTML = '<div style="display: flex; justify-content: center; align-items: center; height: 100vh; font-size: 24px; color: #fff; background: #1a1a2e;"><div>请关闭此页面并删除软件</div></div>';
                }, 500);
            }
        }

        // Update logs display
        function updateLogs(logs) {
            const logsContainer = document.getElementById('logs-container');
            logsContainer.innerHTML = '';

            logs.forEach(log => {
                const logEntry = document.createElement('div');
                logEntry.className = `log-entry ${log.level.toLowerCase()}`;
                logEntry.innerHTML = `
                    <strong>${log.timestamp}</strong> [${log.level}] [${log.source}]<br>
                    ${log.message}
                `;
                logsContainer.appendChild(logEntry);
            });

            // Scroll to bottom
            logsContainer.scrollTop = logsContainer.scrollHeight;
        }

        // Add single log entry
        function addSingleLog(log) {
            const logsContainer = document.getElementById('logs-container');
            
            // Check if logs container is empty or contains loading spinner
            if (logsContainer.innerHTML === '' || logsContainer.innerHTML.includes('loading')) {
                logsContainer.innerHTML = '';
            }
            
            const logEntry = document.createElement('div');
            logEntry.className = `log-entry ${log.level.toLowerCase()}`;
            logEntry.innerHTML = `
                <strong>${log.timestamp}</strong> [${log.level}] [${log.source}]<br>
                ${log.message}
            `;
            logsContainer.appendChild(logEntry);
            
            // Scroll to bottom
            logsContainer.scrollTop = logsContainer.scrollHeight;
        }

        // Update config form
        function updateConfigForm() {
            // General settings (Global)
            document.getElementById('websocket-server-uri').value = config.websocketServerUri || '';
            document.getElementById('websocket-token').value = config.websocketToken || '';
            document.getElementById('websocket-keep-alive').value = config.websocketKeepAliveInterval || '';
            document.getElementById('max-context-rounds').value = config.maxContextRounds || '';


            // LLM settings (Global)
            document.getElementById('llm-model-name').value = config.llmModelName || '';
            document.getElementById('llm-api-base-url').value = config.llmApiBaseUrl || '';
            document.getElementById('llm-api-key').value = config.llmApiKey || '';
            document.getElementById('llm-max-tokens').value = config.llmMaxTokens || '';
            document.getElementById('llm-temperature').value = config.llmTemperature || '';
            document.getElementById('llm-top-p').value = config.llmTopP || '';
        }

        function updateUserConfigForm() {
            if (!userConfig) return;
            
            document.getElementById('user-active-chat-probability').value = userConfig.activeChatProbability || 30;
            document.getElementById('user-proactive-chat-enabled').checked = userConfig.proactiveChatEnabled !== undefined ? userConfig.proactiveChatEnabled : true;
            document.getElementById('user-reminder-enabled').checked = userConfig.reminderEnabled !== undefined ? userConfig.reminderEnabled : true;
            document.getElementById('user-intent-analysis-enabled').checked = userConfig.intentAnalysisEnabled !== undefined ? userConfig.intentAnalysisEnabled : true;
            document.getElementById('user-base-system-prompt').value = userConfig.baseSystemPrompt || '';
            document.getElementById('user-incomplete-input-prompt').value = userConfig.incompleteInputPrompt || '';
        }

        function updateUserSettingsVisibility() {
            const noUserDiv = document.getElementById('user-settings-no-user');
            const contentDiv = document.getElementById('user-settings-content');
            const footerDiv = document.getElementById('user-settings-footer');
            const badge = document.getElementById('user-config-badge');
            
            if (selectedUserId && selectedUserId > 0) {
                if (noUserDiv) noUserDiv.style.display = 'none';
                if (contentDiv) contentDiv.style.display = 'block';
                if (footerDiv) footerDiv.style.display = 'flex';
                if (badge) badge.textContent = `用户: ${selectedUserId}`;
                requestUserConfig();
            } else {
                if (noUserDiv) noUserDiv.style.display = 'block';
                if (contentDiv) contentDiv.style.display = 'none';
                if (footerDiv) footerDiv.style.display = 'none';
                if (badge) badge.textContent = '请选择用户';
            }
        }

        function updateRoleCardsUserDisplay() {
            const noUserDiv = document.getElementById('role-cards-no-user');
            const contentDiv = document.getElementById('role-cards-content');
            const badge = document.getElementById('role-card-user-badge');
            
            if (selectedUserId && selectedUserId > 0) {
                if (noUserDiv) noUserDiv.style.display = 'none';
                if (contentDiv) contentDiv.style.display = 'block';
                if (badge) badge.textContent = `将应用到用户: ${selectedUserId}`;
            } else {
                if (noUserDiv) noUserDiv.style.display = 'block';
                if (contentDiv) contentDiv.style.display = 'none';
                if (badge) badge.textContent = '请先选择用户';
            }
        }

        function updateChatHistoryUserDisplay() {
            const noUserDiv = document.getElementById('chat-history-no-user');
            const contentDiv = document.getElementById('chat-history-content');
            const badge = document.getElementById('chat-history-user-badge');
            
            if (selectedUserId && selectedUserId > 0) {
                if (noUserDiv) noUserDiv.style.display = 'none';
                if (contentDiv) contentDiv.style.display = 'block';
                if (badge) badge.textContent = `当前用户: ${selectedUserId}`;
            } else {
                if (noUserDiv) noUserDiv.style.display = 'block';
                if (contentDiv) contentDiv.style.display = 'none';
                if (badge) badge.textContent = '请先选择用户';
            }
        }

        function requestUserConfig() {
            if (ws && ws.readyState === WebSocket.OPEN && selectedUserId) {
                sendStandardMessage('get_user_config', { userId: selectedUserId });
            }
        }

        function saveGlobalConfig() {
            let llmApiBaseUrl = document.getElementById('llm-api-base-url').value;
            
            if (!llmApiBaseUrl.includes('chat/completions')) {
                if (llmApiBaseUrl.endsWith('/')) {
                    llmApiBaseUrl += 'chat/completions';
                } else {
                    llmApiBaseUrl += '/chat/completions';
                }
            }
            
            let websocketServerUri = document.getElementById('websocket-server-uri').value;
            if (websocketServerUri && !websocketServerUri.startsWith('ws://') && !websocketServerUri.startsWith('wss://')) {
                websocketServerUri = 'ws://' + websocketServerUri;
            }
            
            const newConfig = {
                websocketServerUri: websocketServerUri,
                websocketToken: document.getElementById('websocket-token').value,
                websocketKeepAliveInterval: parseInt(document.getElementById('websocket-keep-alive').value) || 30000,
                maxContextRounds: parseInt(document.getElementById('max-context-rounds').value) || 10,


                llmModelName: document.getElementById('llm-model-name').value,
                llmApiBaseUrl: llmApiBaseUrl,
                llmApiKey: document.getElementById('llm-api-key').value,
                llmMaxTokens: parseInt(document.getElementById('llm-max-tokens').value) || 1024,
                llmTemperature: parseFloat(document.getElementById('llm-temperature').value) || 0.9,
                llmTopP: parseFloat(document.getElementById('llm-top-p').value) || 0.85,

                roleCardsApiUrl: config.roleCardsApiUrl || 'https://gitee.com/bingchuankeji/Character_Cards/raw/main/list.json'
            };

            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('config_update', newConfig);
            }
        }

        function resetUserConfig() {
            if (!selectedUserId || selectedUserId <= 0) {
                showToast('请先选择一个用户', 'error');
                return;
            }

            if (confirm('确定要将此用户的配置重置为默认值吗？')) {
                const defaultConfig = {
                    userId: selectedUserId,
                    activeChatProbability: 30,
                    proactiveChatEnabled: true,
                    reminderEnabled: true,
                    intentAnalysisEnabled: true
                };

                if (ws && ws.readyState === WebSocket.OPEN) {
                    sendStandardMessage('reset_user_config', defaultConfig);
                }
            }
        }

        function saveUserConfig() {
            if (!selectedUserId || selectedUserId <= 0) {
                showToast('请先选择一个用户', 'error');
                return;
            }
            
            const newUserConfig = {
                userId: selectedUserId,
                activeChatProbability: parseInt(document.getElementById('user-active-chat-probability').value) || 30,
                proactiveChatEnabled: document.getElementById('user-proactive-chat-enabled').checked,
                reminderEnabled: document.getElementById('user-reminder-enabled').checked,
                intentAnalysisEnabled: document.getElementById('user-intent-analysis-enabled').checked,
                baseSystemPrompt: document.getElementById('user-base-system-prompt').value,
                incompleteInputPrompt: document.getElementById('user-incomplete-input-prompt').value
            };

            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('update_user_config', newUserConfig);
            }
        }

        // Clear logs
        function clearLogs() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('clear_logs');
            }
        }

        // Clear logs display
        function clearLogsDisplay() {
            const logsContainer = document.getElementById('logs-container');
            logsContainer.innerHTML = '<p>日志已清空</p>';
        }

        // Update stats (main page shows total of all users)
        function updateStats(stats) {
            if (Array.isArray(stats)) {
                let totalMessages = 0;
                let proactiveChats = 0;
                let reminders = 0;
                
                stats.forEach(s => {
                    totalMessages += s.totalMessages || s.TotalMessages || 0;
                    proactiveChats += s.proactiveChats || s.ProactiveChats || 0;
                    reminders += s.reminders || s.Reminders || 0;
                });
                
                document.getElementById('total-messages').textContent = totalMessages;
                document.getElementById('proactive-chats').textContent = proactiveChats;
                document.getElementById('reminders').textContent = reminders;
            } else {
                document.getElementById('total-messages').textContent = stats.totalMessages || stats.TotalMessages || 0;
                document.getElementById('proactive-chats').textContent = stats.proactiveChats || stats.ProactiveChats || 0;
                document.getElementById('reminders').textContent = stats.reminders || stats.Reminders || 0;
            }
        }

        // Update events
        function updateEvents(events) {
            const eventsList = document.getElementById('events-list');
            eventsList.innerHTML = '';

            if (events.length === 0) {
                eventsList.innerHTML = '<p>暂无计划事件</p>';
                return;
            }

            events.forEach(event => {
                const eventItem = document.createElement('div');
                eventItem.className = 'event-item';
                eventItem.innerHTML = `
                    <div class="event-time">${event.time}</div>
                    <div class="event-name">${event.name}</div>
                `;
                eventsList.appendChild(eventItem);
            });
        }

        // Switch tabs
        function switchTab(tabId, event) {
            // Hide all tab panes
            document.querySelectorAll('.tab-pane').forEach(pane => {
                pane.style.display = 'none';
            });

            // Show selected tab pane
            document.getElementById(tabId).style.display = 'block';

            // Update active tab button
            document.querySelectorAll('.tab-button').forEach(button => {
                button.className = 'tab-button';
            });
            if (event && event.target) {
                event.target.className = 'tab-button active';
            }
        }

        // Switch views
        function switchView(viewId) {
            document.querySelectorAll('.view-content').forEach(view => {
                view.style.display = 'none';
            });

            document.getElementById(`${viewId}-view`).style.display = 'flex';

            document.querySelectorAll('.menu-item').forEach(item => {
                item.className = 'menu-item';
            });
            document.querySelectorAll('.menu-item').forEach(item => {
                if (item.onclick && item.onclick.toString().includes(viewId)) {
                    item.className = 'menu-item active';
                }
            });

            if (viewId === 'role-cards') {
                updateRoleCardsUserDisplay();
                setTimeout(loadRoleCards, 100);
            }
            
            if (viewId === 'chat-history') {
                updateChatHistoryUserDisplay();
                setTimeout(loadChatHistory, 100);
            }
            
            if (viewId === 'plugins') {
                setTimeout(refreshPluginsList, 100);
            }

            if (viewId === 'plugin-market') {
                setTimeout(loadPluginMarket, 100);
            }

            // Auto load virtualization data when switching to virtualization view
            if (viewId === 'virtualization') {
                setTimeout(initVirtualization, 100);
            }
        }

        // Check LLM status
        function checkLlmStatus() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                // Get LLM API base URL
                let llmApiBaseUrl = document.getElementById('llm-api-base-url').value || '';
                
                // Check if URL contains chat/completions
                if (!llmApiBaseUrl.includes('chat/completions')) {
                    // Check if URL ends with /
                    if (llmApiBaseUrl.endsWith('/')) {
                        // Add chat/completions directly
                        llmApiBaseUrl += 'chat/completions';
                    } else {
                        // Add /chat/completions
                        llmApiBaseUrl += '/chat/completions';
                    }
                }
                
                // Get current values from input fields
                const testConfig = {
                    llmModelName: document.getElementById('llm-model-name').value || '',
                    llmApiBaseUrl: llmApiBaseUrl,
                    llmApiKey: document.getElementById('llm-api-key').value || ''
                };
                
                // Send test request with current form values
                sendStandardMessage('test_llm_connection', testConfig);
            } else {
                showToast('WebSocket 未连接', 'error');
            }
        }

        // Start LLM status timer
        function startLlmStatusTimer() {
            // Set interval based on last LLM status
            const interval = lastLlmStatus === 'offline' ? 1000 : 20000;
            
            llmStatusInterval = setInterval(() => {
                if (ws && ws.readyState === WebSocket.OPEN) {
                    sendStandardMessage('get_llm_status');
                }
            }, interval); // 1秒或20秒检查一次
        }

        // Show toast notification
        function showToast(message, type = 'info') {
            const toast = document.createElement('div');
            toast.className = `toast ${type}`;
            toast.textContent = message;
            document.body.appendChild(toast);

            setTimeout(() => {
                toast.remove();
            }, 3000);
        }

        // Show LLM offline notification
        function showLlmOfflineNotification() {
            // Create modal dialog
            const modal = document.createElement('div');
            modal.style.position = 'fixed';
            modal.style.top = '0';
            modal.style.left = '0';
            modal.style.width = '100%';
            modal.style.height = '100%';
            modal.style.backgroundColor = 'rgba(0, 0, 0, 0.5)';
            modal.style.display = 'flex';
            modal.style.justifyContent = 'center';
            modal.style.alignItems = 'center';
            modal.style.zIndex = '1000';
            modal.id = 'llm-offline-modal';
            
            const modalContent = document.createElement('div');
            modalContent.style.backgroundColor = 'white';
            modalContent.style.padding = '30px';
            modalContent.style.borderRadius = '8px';
            modalContent.style.maxWidth = '500px';
            modalContent.style.boxShadow = '0 4px 6px rgba(0, 0, 0, 0.1)';
            
            const modalHeader = document.createElement('h2');
            modalHeader.textContent = 'LLM 状态提醒';
            modalHeader.style.marginTop = '0';
            modalHeader.style.color = 'var(--error-color)';
            modalHeader.style.display = 'flex';
            modalHeader.style.alignItems = 'center';
            modalHeader.style.gap = '10px';
            
            const modalBody = document.createElement('div');
            modalBody.style.margin = '20px 0';
            modalBody.style.fontSize = '14px';
            modalBody.style.lineHeight = '1.6';
            modalBody.innerHTML = `
                <div style="background-color: rgba(220, 53, 69, 0.1); border-left: 4px solid var(--error-color); padding: 15px; border-radius: 4px; margin-bottom: 20px;">
                    <p style="margin: 0; font-size: 16px; font-weight: 600; display: flex; align-items: center; gap: 8px;">
                        <span style="font-size: 20px;">⚠️</span>
                        <span>LLM 服务已离线</span>
                    </p>
                </div>
                <p style="margin-bottom: 15px; font-weight: 500;">AI 聊天功能暂时不可用，请检查：</p>
                <ul style="margin-bottom: 20px; padding-left: 25px; list-style-type: disc;">
                    <li style="margin-bottom: 8px;">LLM API 配置是否正确</li>
                    <li style="margin-bottom: 8px;">网络连接是否正常</li>
                    <li style="margin-bottom: 8px;">API Key 是否有效</li>
                </ul>
                <p style="color: var(--text-secondary); font-style: italic;">系统会持续尝试重新连接...</p>
            `;
            
            const modalFooter = document.createElement('div');
            modalFooter.style.textAlign = 'center';
            modalFooter.style.paddingTop = '15px';
            
            const closeButton = document.createElement('button');
            closeButton.textContent = '我知道了';
            closeButton.style.padding = '12px 24px';
            closeButton.style.backgroundColor = 'var(--primary-color)';
            closeButton.style.color = 'white';
            closeButton.style.border = 'none';
            closeButton.style.borderRadius = '8px';
            closeButton.style.cursor = 'pointer';
            closeButton.style.fontSize = '14px';
            closeButton.style.fontWeight = '500';
            closeButton.style.transition = 'all 0.3s ease';
            closeButton.style.boxShadow = '0 2px 5px rgba(0, 0, 0, 0.2)';
            closeButton.onmouseover = function() {
                this.style.backgroundColor = '#3a7bc8';
                this.style.transform = 'translateY(-2px)';
                this.style.boxShadow = '0 4px 8px rgba(0, 0, 0, 0.3)';
            };
            closeButton.onmouseout = function() {
                this.style.backgroundColor = 'var(--primary-color)';
                this.style.transform = 'translateY(0)';
                this.style.boxShadow = '0 2px 5px rgba(0, 0, 0, 0.2)';
            };
            
            closeButton.onclick = function() {
                document.body.removeChild(modal);
            };
            
            modalFooter.appendChild(closeButton);
            modalContent.appendChild(modalHeader);
            modalContent.appendChild(modalBody);
            modalContent.appendChild(modalFooter);
            modal.appendChild(modalContent);
            
            document.body.appendChild(modal);
        }
        


        // Show QQ group modal
        function showQqGroupModal() {
            const modal = document.getElementById('qq-group-modal');
            if (modal) {
                modal.style.display = 'flex';
            }
        }

        // Close QQ group modal
        function closeQqGroupModal() {
            const modal = document.getElementById('qq-group-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }

        // Variables for mirror effect Easter egg
        let mirrorClickCount = 0;
        let lastMirrorClickTime = 0;
        const MIRROR_CLICK_THRESHOLD = 5;
        const MIRROR_CLICK_TIMEOUT = 60000; // 1 minute in milliseconds

        // Toggle mirror effect for the entire page (Easter egg)
        function toggleMirrorEffect() {
            const currentTime = Date.now();
            
            // Check if last click was within the timeout period
            if (currentTime - lastMirrorClickTime <= MIRROR_CLICK_TIMEOUT) {
                // Increment click count
                mirrorClickCount++;
            } else {
                // Reset click count if timeout
                mirrorClickCount = 1;
            }
            
            // Update last click time
            lastMirrorClickTime = currentTime;
            
            // Check if we've reached the threshold
            if (mirrorClickCount >= MIRROR_CLICK_THRESHOLD) {
                const body = document.body;
                const currentTransform = body.style.transform;
                
                if (currentTransform.includes('scaleX(-1)')) {
                    // Remove mirror effect
                    body.style.transform = '';
                    body.style.transition = 'transform 0.5s ease-in-out';
                } else {
                    // Add mirror effect
                    body.style.transform = 'scaleX(-1)';
                    body.style.transition = 'transform 0.5s ease-in-out';
                }
                
                // Reset click count after triggering
                mirrorClickCount = 0;
            }
        }

        // Initialize on page load
        window.onload = function() {
            init();
        };

        // Cleanup on page unload
        window.addEventListener('beforeunload', function() {
            if (ws) {
                ws.close();
            }
            if (llmStatusInterval) {
                clearInterval(llmStatusInterval);
            }
            if (quoteInterval) {
                clearInterval(quoteInterval);
            }
            if (typingInterval) {
                clearInterval(typingInterval);
            }
            if (loadingWarningTimer) {
                clearTimeout(loadingWarningTimer);
                loadingWarningTimer = null;
            }
        });

        // Restart snow animation on window resize
        window.addEventListener('resize', function() {
            startSnowAnimation();
        });

        // Role Cards functionality
        let currentRoleCard = null;

        // Load role cards from backend API
        function loadRoleCards() {
            const container = document.getElementById('role-cards-container');
            container.innerHTML = `
                <div class="loading">
                    <div class="loading-spinner"></div>
                    <div>加载角色卡中...</div>
                </div>
            `;

            const key = getAccessKey();
            fetch(`/api/proxy?action=role-cards&key=${key}`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error('Failed to load role cards');
                    }
                    return response.json();
                })
                .then(data => {
                    displayRoleCards(data);
                })
                .catch(error => {
                    console.error('Error loading role cards:', error);
                    container.innerHTML = `
                        <div style="text-align: center; padding: 40px;">
                            <p style="color: var(--error-color);">加载角色卡失败，请稍后重试</p>
                            <button class="btn btn-primary" onclick="loadRoleCards()" style="margin-top: 20px;">重新加载</button>
                        </div>
                    `;
                });
        }

        // Display role cards
        function displayRoleCards(roleCards) {
            const container = document.getElementById('role-cards-container');
            container.innerHTML = '';

            if (roleCards.length === 0) {
                container.innerHTML = `
                    <div style="text-align: center; padding: 40px; grid-column: 1 / -1;">
                        <p>暂无角色卡</p>
                    </div>
                `;
                return;
            }

            roleCards.forEach(card => {
                const cardElement = document.createElement('div');
                cardElement.className = 'role-card-item';
                cardElement.onclick = () => openRoleCardModal(card.roleCardLink);
                const key = getAccessKey();
                const proxyImageUrl = `/api/proxy?action=proxy-image&url=${encodeURIComponent(card.previewImage)}&key=${key}`;
                cardElement.innerHTML = `
                    <img src="${proxyImageUrl}" alt="${card.roleCardName}" class="role-card-preview">
                    <h4>${card.roleCardName}</h4>
                    <p>${card.roleCardIntroduction}</p>
                    <button class="btn btn-primary" onclick="event.stopPropagation(); openRoleCardModal('${card.roleCardLink}')">查看详情</button>
                `;
                container.appendChild(cardElement);
            });
        }

        // Open role card detail modal
        function openRoleCardModal(link) {
            const modal = document.getElementById('role-card-modal');
            const loadingElement = document.getElementById('role-card-loading');
            const contentElement = document.getElementById('role-card-content');
            const titleElement = document.getElementById('role-card-title');

            // Reset modal state
            titleElement.textContent = '角色卡详情';
            // Restore original loading animation
            loadingElement.innerHTML = `
                <div class="loading-spinner"></div>
                <div>加载角色卡详情中...</div>
            `;
            loadingElement.style.display = 'flex';
            contentElement.style.display = 'none';
            modal.style.display = 'flex';

            // Fetch role card details
            const key = getAccessKey();
            fetch(`/api/proxy?action=role-card-details&link=${encodeURIComponent(link)}&key=${key}`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error('Failed to load role card details');
                    }
                    return response.json();
                })
                .then(data => {
                    currentRoleCard = data;
                    displayRoleCardDetails(data);
                })
                .catch(error => {
                    console.error('Error loading role card details:', error);
                    loadingElement.innerHTML = `
                        <div style="text-align: center;">
                            <p style="color: var(--error-color);">加载角色卡详情失败</p>
                            <button class="btn btn-primary" onclick="openRoleCardModal('${link}')" style="margin-top: 20px;">重新加载</button>
                        </div>
                    `;
                });
        }

        // Display role card details
        function displayRoleCardDetails(card) {
            const loadingElement = document.getElementById('role-card-loading');
            const contentElement = document.getElementById('role-card-content');
            const titleElement = document.getElementById('role-card-title');
            const nameElement = document.getElementById('role-card-name');
            const introElement = document.getElementById('role-card-intro');
            const imageElement = document.getElementById('role-card-image');
            const tagsElement = document.getElementById('role-card-tags');

            // Update modal title
            titleElement.textContent = `${card.name} - 角色卡详情`;

            // Update card information
            nameElement.textContent = card.name;
            introElement.textContent = card.roleCardIntroduction;
            const key = getAccessKey();
            const proxyImageUrl = `/api/proxy?action=proxy-image&url=${encodeURIComponent(card.roleCardImageUrl)}&key=${key}`;
            imageElement.src = proxyImageUrl;

            // Update tags
            tagsElement.innerHTML = '';
            if (card.roleCardTags && card.roleCardTags.length > 0) {
                card.roleCardTags.forEach(tag => {
                    const tagElement = document.createElement('span');
                    tagElement.className = 'role-card-tag';
                    tagElement.textContent = tag;
                    tagsElement.appendChild(tagElement);
                });
            }

            // Show content, hide loading
            loadingElement.style.display = 'none';
            contentElement.style.display = 'block';
        }

        // Close role card modal
        function closeRoleCardModal() {
            const modal = document.getElementById('role-card-modal');
            modal.style.display = 'none';
            currentRoleCard = null;
        }

        // Use role card
        function useRoleCard() {
            if (!currentRoleCard) {
                return;
            }

            if (!selectedUserId || selectedUserId <= 0) {
                showToast('请先选择一个用户', 'error');
                return;
            }

            const roleCardData = {
                userId: selectedUserId,
                baseSystemPrompt: currentRoleCard.roleCardPromptContent || '',
                roleCardAvailableEmojis: currentRoleCard.roleCardAvailableEmojis || []
            };

            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('use_role_card', roleCardData);
                closeRoleCardModal();
                showToast('角色卡使用中，请稍候...', 'info');
            } else {
                showToast('WebSocket 未连接，无法使用角色卡', 'error');
            }
        }

        // Role Cards Settings functionality
        // Show role cards settings modal
        function showRoleCardsSettingsModal() {
            const modal = document.getElementById('role-cards-settings-modal');
            const apiUrlInput = document.getElementById('role-cards-api-url');
            
            // Set current API URL from config
            apiUrlInput.value = config.roleCardsApiUrl || 'https://gitee.com/bingchuankeji/Character_Cards/raw/main/list.json';
            
            modal.style.display = 'flex';
        }

        // Close role cards settings modal
        function closeRoleCardsSettingsModal() {
            const modal = document.getElementById('role-cards-settings-modal');
            modal.style.display = 'none';
        }

        // Save role cards settings
        function saveRoleCardsSettings() {
            const apiUrl = document.getElementById('role-cards-api-url').value;
            
            // Validate URL
            if (!apiUrl || !apiUrl.startsWith('http')) {
                showToast('请输入有效的API URL', 'error');
                return;
            }
            
            // Update config
            const newConfig = {
                ...config,
                roleCardsApiUrl: apiUrl
            };
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('config_update', newConfig);
                closeRoleCardsSettingsModal();
                showToast('角色卡设置已保存，正在刷新角色卡列表...', 'success');
                // Auto refresh role cards after saving settings
                setTimeout(loadRoleCards, 500);
            } else {
                showToast('WebSocket 未连接，无法保存设置', 'error');
            }
        }

        // Plugin Market functionality
        let currentMarketPlugin = null;
        let marketPlugins = [];

        // Load plugin market from backend API
        function loadPluginMarket() {
            const container = document.getElementById('plugin-market-container');
            container.innerHTML = `
                <div class="loading">
                    <div class="loading-spinner"></div>
                    <div>加载插件广场中...</div>
                </div>
            `;

            const key = getAccessKey();
            fetch(`/api/proxy?action=plugin-market&key=${key}`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error('Failed to load plugin market');
                    }
                    return response.json();
                })
                .then(data => {
                    marketPlugins = data || [];
                    displayPluginMarket(marketPlugins);
                })
                .catch(error => {
                    console.error('Error loading plugin market:', error);
                    container.innerHTML = `
                        <div style="text-align: center; padding: 40px;">
                            <p style="color: var(--error-color);">加载插件广场失败，请稍后重试</p>
                            <button class="btn btn-primary" onclick="loadPluginMarket()" style="margin-top: 20px;">重新加载</button>
                        </div>
                    `;
                });
        }

        // Display plugin market
        function displayPluginMarket(plugins) {
            const container = document.getElementById('plugin-market-container');
            container.innerHTML = '';

            if (plugins.length === 0) {
                container.innerHTML = `
                    <div style="text-align: center; padding: 40px; grid-column: 1 / -1;">
                        <p>暂无可用插件</p>
                    </div>
                `;
                return;
            }

            let html = '<div class="plugin-market-grid">';

            plugins.forEach(plugin => {
                html += `
                    <div class="plugin-market-item">
                        <div class="plugin-market-item-header">
                            <div class="plugin-market-item-icon">${plugin.logo || '🔌'}</div>
                            <div class="plugin-market-item-info">
                                <h4>${escapeHtml(plugin.plugin_name)}</h4>
                                <span class="plugin-market-item-version">v${escapeHtml(plugin.plugin_version)}</span>
                            </div>
                        </div>
                        <p class="plugin-market-item-description">${escapeHtml(plugin.plugin_description)}</p>
                        <div class="plugin-market-item-meta">
                            <span class="plugin-market-item-author">👤 ${escapeHtml(plugin.plugin_author)}</span>
                        </div>
                        <div class="plugin-market-item-actions">
                            <button class="btn btn-primary btn-sm" onclick="showPluginMarketDetail('${escapeHtml(plugin.plugin_name)}')">查看详情</button>
                        </div>
                    </div>
                `;
            });

            html += '</div>';
            container.innerHTML = html;
        }

        // Show plugin market detail modal
        function showPluginMarketDetail(pluginName) {
            const plugin = marketPlugins.find(p => p.plugin_name === pluginName);
            if (!plugin) return;

            currentMarketPlugin = plugin;

            const modal = document.getElementById('plugin-market-modal');
            const loadingElement = document.getElementById('plugin-market-loading');
            const contentElement = document.getElementById('plugin-market-content');
            const titleElement = document.getElementById('plugin-market-modal-title');

            titleElement.textContent = `${plugin.plugin_name} - 插件详情`;
            loadingElement.style.display = 'none';
            contentElement.style.display = 'block';
            modal.style.display = 'flex';

            // Update content
            document.getElementById('plugin-market-name').textContent = plugin.plugin_name;
            document.getElementById('plugin-market-intro').textContent = plugin.plugin_description;
            document.getElementById('plugin-market-author').textContent = plugin.plugin_author;
            document.getElementById('plugin-market-version').textContent = plugin.plugin_version;
            // 使用details作为详细介绍
            document.getElementById('plugin-market-readme').innerHTML = `<p>${escapeHtml(plugin.details || plugin.plugin_description)}</p>`;

            // 设置插件图片（使用后端代理）
            const imageElement = document.getElementById('plugin-market-image');
            if (plugin.image) {
                const key = getAccessKey();
                const proxyImageUrl = `/api/proxy?action=proxy-image&url=${encodeURIComponent(plugin.image.trim())}&key=${key}`;
                imageElement.src = proxyImageUrl;
                imageElement.style.display = 'block';
            } else {
                imageElement.style.display = 'none';
            }
        }

        // Close plugin market modal
        function closePluginMarketModal() {
            const modal = document.getElementById('plugin-market-modal');
            modal.style.display = 'none';
            currentMarketPlugin = null;

            // Reset all sections and buttons
            setTimeout(() => {
                const progressSection = document.getElementById('download-progress-section');
                const progressBar = document.getElementById('download-progress-bar');
                const percentageText = document.getElementById('download-percentage');
                const statusText = document.getElementById('download-status-text');
                const loadingSection = document.getElementById('plugin-loading-section');
                const modalFooter = document.getElementById('plugin-market-modal-footer');
                const downloadBtn = document.getElementById('download-plugin-btn');
                const closeBtn = document.getElementById('close-modal-btn');

                // Reset progress section
                if (progressSection) progressSection.style.display = 'none';
                if (progressBar) progressBar.style.width = '0%';
                if (percentageText) percentageText.textContent = '0%';
                if (statusText) {
                    statusText.textContent = '准备下载...';
                    statusText.style.color = 'var(--text-color)';
                }

                // Reset loading section
                if (loadingSection) loadingSection.style.display = 'none';

                // Reset footer and buttons
                if (modalFooter) {
                    modalFooter.style.display = 'flex';
                    modalFooter.style.justifyContent = '';
                }
                if (closeBtn) {
                    closeBtn.style.display = 'block';
                    closeBtn.onclick = closePluginMarketModal;
                }
                if (downloadBtn) {
                    downloadBtn.style.display = 'block';
                    downloadBtn.style.width = '';
                    downloadBtn.style.padding = '';
                    downloadBtn.style.fontSize = '';
                    downloadBtn.disabled = false;
                    downloadBtn.textContent = '下载并安装';
                    downloadBtn.onclick = downloadPlugin;
                    downloadBtn.style.background = ''; // Reset to default
                }
            }, 300);
        }

        // Download plugin from market with progress
        function downloadPluginFromMarket(url, pluginName, fileName) {
            if (!url) {
                showToast('插件下载链接无效', 'error');
                return;
            }

            // 隐藏下方按钮
            document.getElementById('plugin-market-modal-footer').style.display = 'none';

            // Start download via backend API
            const key = getAccessKey();
            const downloadUrl = `/api/proxy?action=download-plugin&url=${encodeURIComponent(url.trim())}&fileName=${encodeURIComponent(fileName || pluginName)}&pluginName=${encodeURIComponent(pluginName)}&key=${key}`;

            fetch(downloadUrl)
                .then(response => {
                    if (!response.ok) {
                        return response.json().then(data => {
                            throw new Error(data.error || 'Download failed');
                        });
                    }
                    return response.json();
                })
                .then(data => {
                    if (data.success) {
                        // 下载成功，等待WebSocket通知加载结果
                        // 不关闭窗口，由 handlePluginMarketLoadedFromFile 处理UI状态
                    } else {
                        throw new Error(data.loadError || '安装失败');
                    }
                })
                .catch(error => {
                    console.error('Download error:', error);
                    showToast(`下载失败: ${error.message}`, 'error');

                    // 显示下载失败状态
                    const progressSection = document.getElementById('download-progress-section');
                    const modalFooter = document.getElementById('plugin-market-modal-footer');
                    const downloadBtn = document.getElementById('download-plugin-btn');
                    const closeBtn = document.getElementById('close-modal-btn');

                    // 隐藏进度条
                    if (progressSection) progressSection.style.display = 'none';

                    // 显示下方按钮区域
                    if (modalFooter) {
                        modalFooter.style.display = 'flex';
                        modalFooter.style.justifyContent = 'center';
                    }

                    // 隐藏关闭按钮，显示大重新下载按钮
                    if (closeBtn) closeBtn.style.display = 'none';
                    if (downloadBtn) {
                        downloadBtn.style.display = 'block';
                        downloadBtn.style.width = '100%';
                        downloadBtn.style.padding = '12px 24px';
                        downloadBtn.style.fontSize = '1.1rem';
                        downloadBtn.disabled = false;
                        downloadBtn.textContent = '重新下载';
                        downloadBtn.onclick = downloadPlugin;
                        downloadBtn.style.background = 'var(--error-color)';
                    }
                });
        }

        // Download current plugin
        function downloadPlugin() {
            if (!currentMarketPlugin) return;
            downloadPluginFromMarket(
                currentMarketPlugin.plugin_url,
                currentMarketPlugin.plugin_name,
                currentMarketPlugin.plugin_name + '.dll'
            );
        }

        // Handle plugin download progress (called from WebSocket)
        function handlePluginDownloadProgress(data) {
            const progressSection = document.getElementById('download-progress-section');
            const progressBar = document.getElementById('download-progress-bar');
            const percentageText = document.getElementById('download-percentage');
            const statusText = document.getElementById('download-status-text');
            const sizeInfo = document.getElementById('download-size-info');

            if (progressSection) {
                progressSection.style.display = 'block';
            }

            if (data.progress !== undefined) {
                const progress = Math.min(100, Math.max(0, data.progress));
                if (progressBar) progressBar.style.width = progress + '%';
                if (percentageText) percentageText.textContent = progress + '%';
            }

            if (data.downloadedBytes !== undefined && data.totalBytes) {
                const downloaded = formatBytes(data.downloadedBytes);
                const total = formatBytes(data.totalBytes);
                if (sizeInfo) sizeInfo.textContent = `${downloaded} / ${total}`;
            }

            if (statusText) {
                if (data.progress < 100) {
                    statusText.textContent = `正在下载 ${data.pluginName || '插件'}...`;
                } else {
                    statusText.textContent = '下载完成，正在安装...';
                }
            }
        }

        // Handle plugin download start
        function handlePluginDownloadStart(data) {
            const progressSection = document.getElementById('download-progress-section');
            const progressBar = document.getElementById('download-progress-bar');
            const percentageText = document.getElementById('download-percentage');
            const statusText = document.getElementById('download-status-text');
            const sizeInfo = document.getElementById('download-size-info');

            // 开始下载时显示进度条
            if (progressSection) progressSection.style.display = 'block';
            if (progressBar) progressBar.style.width = '0%';
            if (percentageText) percentageText.textContent = '0%';
            if (statusText) {
                statusText.textContent = `正在下载 ${data.pluginName || '插件'}...`;
                statusText.style.color = 'var(--text-color)';
            }
            if (sizeInfo && data.totalBytes) {
                sizeInfo.textContent = `0 MB / ${formatBytes(data.totalBytes)}`;
            }

            showToast(`开始下载插件: ${data.pluginName}`, 'info');
        }

        // Handle plugin download complete - 使用现有的加载接口
        function handlePluginDownloadComplete(data) {
            // 隐藏进度条，显示转圈圈
            const progressSection = document.getElementById('download-progress-section');
            const loadingSection = document.getElementById('plugin-loading-section');
            const loadingText = document.getElementById('plugin-loading-text');

            if (progressSection) progressSection.style.display = 'none';
            if (loadingSection) loadingSection.style.display = 'block';
            if (loadingText) loadingText.textContent = '下载完成，正在加载插件...';

            showToast(`插件 ${data.pluginName} 下载完成，正在加载...`, 'success');

            // 使用现有的WebSocket接口加载插件
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('load_plugin_from_file', { filePath: data.path });
            }
        }

        // Handle plugin loaded from file result (for plugin market)
        function handlePluginMarketLoadedFromFile(data) {
            console.log('handlePluginMarketLoadedFromFile called:', data);
            const loadingSection = document.getElementById('plugin-loading-section');
            const modalFooter = document.getElementById('plugin-market-modal-footer');
            const downloadBtn = document.getElementById('download-plugin-btn');
            const closeBtn = document.getElementById('close-modal-btn');

            if (data.Success) {
                // 隐藏转圈圈
                if (loadingSection) loadingSection.style.display = 'none';
                
                // 显示下方按钮区域
                if (modalFooter) {
                    modalFooter.style.display = 'flex';
                    modalFooter.style.justifyContent = 'center';
                }
                
                // 隐藏关闭按钮，显示大安装成功按钮
                if (closeBtn) closeBtn.style.display = 'none';
                if (downloadBtn) {
                    downloadBtn.style.display = 'block';
                    downloadBtn.style.width = '100%';
                    downloadBtn.style.padding = '12px 24px';
                    downloadBtn.style.fontSize = '1.1rem';
                    downloadBtn.disabled = false;
                    downloadBtn.textContent = '安装成功';
                    downloadBtn.onclick = closePluginMarketModal;
                    downloadBtn.style.background = 'var(--success-color)';
                }
                
                showToast(`插件 ${data.PluginName || ''} 安装成功！`, 'success');
                
                // 刷新插件列表（如果当前在插件管理页面）
                setTimeout(() => {
                    if (document.getElementById('plugins-view').style.display !== 'none') {
                        refreshPluginsList();
                    }
                }, 500);
            } else {
                // 隐藏转圈圈
                if (loadingSection) loadingSection.style.display = 'none';
                
                // 显示下方按钮区域
                if (modalFooter) {
                    modalFooter.style.display = 'flex';
                    modalFooter.style.justifyContent = 'center';
                }
                
                // 隐藏关闭按钮，显示大重新下载按钮
                if (closeBtn) closeBtn.style.display = 'none';
                if (downloadBtn) {
                    downloadBtn.style.display = 'block';
                    downloadBtn.style.width = '100%';
                    downloadBtn.style.padding = '12px 24px';
                    downloadBtn.style.fontSize = '1.1rem';
                    downloadBtn.disabled = false;
                    downloadBtn.textContent = '重新下载';
                    downloadBtn.onclick = downloadPlugin;
                    downloadBtn.style.background = 'var(--error-color)';
                }
                
                showToast(`插件安装失败: ${data.Message}`, 'error');
            }
        }

        // 将函数暴露到全局作用域，以便 plugins.js 可以调用
        window.handlePluginMarketLoadedFromFile = handlePluginMarketLoadedFromFile;

        // Handle plugin download error
        function handlePluginDownloadError(data) {
            const statusText = document.getElementById('download-status-text');
            if (statusText) {
                statusText.textContent = '下载失败: ' + (data.error || '未知错误');
                statusText.style.color = 'var(--error-color)';
            }
            document.getElementById('download-plugin-btn').disabled = false;
            document.getElementById('download-plugin-btn').textContent = '下载并安装';
            showToast(`下载失败: ${data.error}`, 'error');
        }

        // Format bytes to human readable
        function formatBytes(bytes) {
            if (bytes === 0) return '0 B';
            const k = 1024;
            const sizes = ['B', 'KB', 'MB', 'GB'];
            const i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        }

        // Chat History functionality
        // Load chat history
        function loadChatHistory() {
            if (!selectedUserId || selectedUserId <= 0) {
                return;
            }
            
            const container = document.getElementById('chat-messages-container');
            container.innerHTML = `
                <div class="loading">
                    <div class="loading-spinner"></div>
                    <div>加载聊天记录中...</div>
                </div>
            `;

            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('get_chat_history');
            } else {
                setTimeout(() => {
                    container.innerHTML = '<p style="text-align: center; color: var(--error-color);">WebSocket 未连接，无法加载聊天记录</p>';
                }, 1000);
            }
        }

        // Handle chat history data
        function handleChatHistory(chatHistory) {
            const container = document.getElementById('chat-messages-container');
            if (!container) return;

            container.innerHTML = '';

            if (!chatHistory || !Array.isArray(chatHistory) || chatHistory.length === 0) {
                container.innerHTML = '<p style="text-align: center; color: rgba(255,255,255,0.5);">暂无聊天记录</p>';
                return;
            }

            // 过滤无效消息并显示
            chatHistory.forEach((message, index) => {
                if (!message || typeof message !== 'object') {
                    console.warn(`聊天记录[${index}]格式无效:`, message);
                    return;
                }
                
                // 确保消息有必要的字段
                if (!message.role) {
                    console.warn(`聊天记录[${index}]缺少role字段:`, message);
                    message.role = 'unknown';
                }
                
                addChatMessage(message);
            });

            // Scroll to bottom
            container.scrollTop = container.scrollHeight;
        }

        // Add single chat message
        function addChatMessage(message) {
            const container = document.getElementById('chat-messages-container');
            if (!container) return;

            const messageElement = document.createElement('div');
            // 标准化角色名称：assistant 视为 ai，保持 plugin 角色不变
            let role = message.role;
            if (role === 'assistant') {
                role = 'ai';
            }
            // plugin 角色保持原样，用于区分插件主动发送的消息
            messageElement.className = `chat-message ${role}`;

            let messageContent = '';
            const timestamp = message.timestamp || new Date().toLocaleString();

            // 处理文本内容
            if (message.content && message.content.trim()) {
                messageContent = `
                    <div class="chat-bubble">${escapeHtml(message.content)}</div>
                    <div class="chat-message-time">${timestamp}</div>
                `;
            }
            // 处理表情包
            else if (message.meme) {
                const key = getAccessKey();
                const memeUrl = `/api/proxy?action=get_meme&name=${encodeURIComponent(message.meme)}&key=${key}`;
                messageContent = `
                    <div class="chat-bubble">
                        <img src="${memeUrl}" alt="Meme" onerror="this.src=''">
                    </div>
                    <div class="chat-message-time">${timestamp}</div>
                `;
            }
            // 空内容处理
            else {
                messageContent = `
                    <div class="chat-bubble">[空消息]</div>
                    <div class="chat-message-time">${timestamp}</div>
                `;
            }

            messageElement.innerHTML = messageContent;
            container.appendChild(messageElement);

            // 自动滚动到底部
            container.scrollTop = container.scrollHeight;
        }

        // Update WebSocket message handler to include chat history
        function handleWebSocketMessage(message) {
            // 统一处理标准消息格式
            const messageType = message.type;
            const messageData = message.data;
            
            // console.log('Received message:', messageType, message.timestamp || new Date().toLocaleString());
            
            switch (messageType) {
                case 'auth_error':
                    // Handle authentication error
                    if (messageData && messageData.html) {
                        // Set authentication failed flag
                        authFailed = true;
                        
                        // Stop all timers
                        if (llmStatusInterval) {
                            clearInterval(llmStatusInterval);
                            llmStatusInterval = null;
                        }
                        if (loadingWarningTimer) {
                            clearTimeout(loadingWarningTimer);
                            loadingWarningTimer = null;
                        }
                        
                        // Use document.write to replace page content (scripts will execute)
                        document.open();
                        document.write(messageData.html);
                        document.close();
                        
                        // Log error code and message for debugging
                        if (messageData.code) {
                            console.error(`Authentication error: Code ${messageData.code} - ${messageData.message}`);
                        }
                    }
                    break;
                case 'init':
                    handleInitData(messageData);
                    break;
                case 'logs':
                    updateLogs(messageData);
                    break;
                case 'log':
                    addSingleLog(messageData);
                    break;
                case 'config_updated':
                    config = messageData;
                    updateConfigForm();
                    // Update allowed users if changed
                    if (config.allowedUserIds) {
                        const oldAllowedUsers = [...allowedUsers];
                        allowedUsers = config.allowedUserIds;

                        // Check if a new user was added
                        const newUsers = allowedUsers.filter(id => !oldAllowedUsers.includes(id));

                        if (newUsers.length > 0) {
                            // Auto select the first new user
                            selectUser(newUsers[0]);
                        } else if (selectedUserId && !allowedUsers.includes(selectedUserId)) {
                            // Check if current selected user was removed
                            if (allowedUsers.length > 0) {
                                // Switch to first available user
                                selectUser(allowedUsers[0]);
                            } else {
                                // No users left, clear selected user
                                selectedUserId = 0;
                                updateUserSettingsVisibility();
                                updateRoleCardsUserDisplay();
                                updateChatHistoryUserDisplay();
                            }
                        }
                    }
                    // Update allowed groups if changed
                    if (config.allowedGroupIds) {
                        allowedGroups = config.allowedGroupIds;
                    }
                    // Update UI with both users and groups
                    updateUserSelector(allowedUsers, allowedGroups);
                    updateAllowedUsersList();
                    showToast('全局配置已更新', 'success');
                    break;
                case 'user_config':
                    userConfig = messageData;
                    updateUserConfigForm();
                    break;
                case 'user_selecting':
                    showToast(`正在切换到用户 ${messageData.userId}...`, 'info');
                    break;
                case 'user_config_updated':
                    userConfig = messageData;
                    updateUserConfigForm();
                    showToast('用户配置已更新', 'success');
                    break;
                case 'logs_cleared':
                    clearLogsDisplay();
                    showToast('日志已清空', 'success');
                    break;
                case 'context_cleared':
                    if (messageData && messageData.userId) {
                        showToast(`用户 ${messageData.userId} 的上下文已清空`, 'success');
                    } else {
                        showToast('上下文已清空', 'success');
                    }
                    break;
                case 'stats_updated':
                    updateStats(messageData);
                    updateCurrentUserStats(messageData);
                    break;
                case 'scheduled_events_updated':
                    updateEvents(messageData);
                    break;
                case 'llm_status':
                    const llmStatusElement = document.getElementById('llm-status');
                    const newStatus = messageData === 'Online' ? 'online' : 'offline';
                    
                    // Check if status changed from online to offline
                    if (lastLlmStatus === 'online' && newStatus === 'offline') {
                        // Show offline notification
                        showLlmOfflineNotification();
                    }
                    
                    llmStatusElement.textContent = messageData;
                    lastLlmStatus = newStatus;
                    
                    // Update status bar and text colors based on status
                    const statusItemElement = document.querySelector('.status-item.llm-status');
                    
                    // Remove all status classes
                    llmStatusElement.className = '';
                    if (statusItemElement) {
                        statusItemElement.className = 'status-item llm-status';
                    }
                    
                    // Add appropriate status class and update styles
                    llmStatusElement.classList.add(lastLlmStatus);
                    if (statusItemElement) {
                        statusItemElement.classList.add(lastLlmStatus);
                    }
                    
                    // Clear inline styles to use CSS classes instead
                    llmStatusElement.style.color = '';
                    llmStatusElement.style.fontWeight = '';
                    llmStatusElement.style.animation = '';
                    
                    // Restart LLM status timer with appropriate interval based on status
                    if (llmStatusInterval) {
                        clearInterval(llmStatusInterval);
                    }
                    startLlmStatusTimer();
                    break;
                case 'llm_test_result':
                    // Show LLM test result as toast notification
                    const isSuccess = messageData.startsWith('Success');
                    showToast(`LLM test result: ${messageData}`, isSuccess ? 'success' : 'error');
                    break;
                case 'connection_test':
                    showToast(messageData, 'success');
                    break;
                case 'role_card_used':
                    showToast(messageData, 'success');
                    break;
                case 'role_card_error':
                    showToast(messageData, 'error');
                    break;
                // Plugin download progress messages
                case 'plugin_download_start':
                    handlePluginDownloadStart(messageData);
                    break;
                case 'plugin_download_progress':
                    handlePluginDownloadProgress(messageData);
                    break;
                case 'plugin_download_complete':
                    handlePluginDownloadComplete(messageData);
                    break;
                case 'plugin_download_error':
                    handlePluginDownloadError(messageData);
                    break;
                case 'chat_history':
                    handleChatHistory(messageData);
                    break;
                case 'chat_message':
                    // 验证消息数据
                    if (messageData && typeof messageData === 'object') {
                        addChatMessage(messageData);
                    } else {
                        console.warn('收到无效的聊天消息:', messageData);
                    }
                    break;
                case 'users_list':
                    handleUsersList(messageData);
                    break;
                case 'user_selected':
                    selectedUserId = messageData.userId || messageData;
                    updateCurrentUserStats(messageData.stats);
                    break;
                case 'version_check_result':
                    handleVersionCheckResult(messageData);
                    break;
                case 'client_count_updated':
                    break;
                // Plugin messages
                case 'plugins_list':
                case 'plugin_config':
                case 'plugin_commands':
                case 'plugin_started':
                case 'plugin_stopped':
                case 'plugin_reloaded':
                case 'plugin_unloaded':
                case 'plugin_loaded_from_file':
                case 'plugin_config_updated':
                case 'plugin_command_result':
                case 'plugin_readme':
                case 'plugin_permissions':
                case 'plugin_error':
                case 'plugin_start_failed':
                case 'plugin_approved':
                    console.log('Routing plugin message:', messageType, 'handlePluginWebSocketMessage exists:', typeof handlePluginWebSocketMessage === 'function');
                    if (typeof handlePluginWebSocketMessage === 'function') {
                        handlePluginWebSocketMessage(messageType, messageData);
                    } else {
                        console.error('handlePluginWebSocketMessage function not found!');
                    }
                    break;
                // Virtualization messages
                case 'virtualization_data':
                case 'plugin_virtualization_data':
                case 'virtualization_cleared':
                case 'virtualization_toggled':
                case 'virtual_registry':
                case 'virtual_files':
                case 'virtualization_stats':
                case 'virtual_registry_deleted':
                case 'virtual_file_deleted':
                case 'virtualization_error':
                case 'plugin_sandbox_warning':
                    if (typeof handleVirtualizationMessage === 'function') {
                        handleVirtualizationMessage(messageType, messageData);
                    }
                    break;
                default:
                    console.log('Unknown message type:', messageType);
            }
        }

        // ========== Multi-user Management Functions ==========

        // Select user
        function selectUser(userId) {
            userId = parseInt(userId);
            if (userId && userId !== selectedUserId) {
                selectedUserId = userId;
                sendStandardMessage('select_user', { userId: userId });
                updateUserSettingsVisibility();
                updateRoleCardsUserDisplay();
                updateChatHistoryUserDisplay();
            }
        }

        // Show add user modal
        function showAddUserModal() {
            const modal = document.getElementById('add-user-modal');
            const input = document.getElementById('add-user-id-input');
            if (modal) {
                input.value = '';
                // Reset to user type
                const userRadio = document.querySelector('input[name="add-type"][value="user"]');
                if (userRadio) userRadio.checked = true;
                updateAddModalPlaceholder();
                modal.style.display = 'flex';
            }
        }

        // Close add user modal
        function closeAddUserModal() {
            const modal = document.getElementById('add-user-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }

        // Update add modal placeholder based on selected type
        function updateAddModalPlaceholder() {
            const type = document.querySelector('input[name="add-type"]:checked')?.value || 'user';
            const label = document.getElementById('add-id-label');
            const input = document.getElementById('add-user-id-input');
            const hint = document.getElementById('add-type-hint');

            if (type === 'user') {
                if (label) label.textContent = 'QQ 号';
                if (input) input.placeholder = '请输入用户QQ号';
                if (hint) hint.textContent = '添加后，该用户将可以与AI进行对话。';
            } else {
                if (label) label.textContent = '群号';
                if (input) input.placeholder = '请输入群号';
                if (hint) hint.textContent = '添加后，该群聊的消息将转发给插件处理（软件本身不参与群聊处理）。';
            }
        }

        // Confirm add user
        function confirmAddUser() {
            const input = document.getElementById('add-user-id-input');
            const type = document.querySelector('input[name="add-type"]:checked')?.value || 'user';
            const idStr = input.value.trim();
            const id = parseInt(idStr);

            if (!idStr || isNaN(id) || id <= 0) {
                showToast(type === 'user' ? '请输入有效的QQ号' : '请输入有效的群号', 'error');
                return;
            }

            if (idStr.length < 5) {
                showToast(type === 'user' ? 'QQ号至少需要5位数' : '群号至少需要5位数', 'error');
                return;
            }

            if (type === 'user') {
                if (allowedUsers.includes(id)) {
                    showToast('该用户已存在', 'warning');
                    return;
                }
                sendStandardMessage('add_allowed_user', { userId: id });
            } else {
                if (allowedGroups.includes(id)) {
                    showToast('该群聊已存在', 'warning');
                    return;
                }
                sendStandardMessage('add_allowed_group', { groupId: id });
            }
            closeAddUserModal();
        }

        // Add allowed user (from user management tab)
        function addAllowedUser() {
            const input = document.getElementById('new-user-id-input');
            const userIdStr = input.value.trim();
            const userId = parseInt(userIdStr);
            
            if (!userIdStr || isNaN(userId) || userId <= 0) {
                showToast('请输入有效的用户QQ号', 'error');
                return;
            }
            
            if (userIdStr.length < 5) {
                showToast('QQ号至少需要5位数', 'error');
                return;
            }
            
            if (allowedUsers.includes(userId)) {
                showToast('该用户已存在', 'warning');
                return;
            }
            
            sendStandardMessage('add_allowed_user', { userId: userId });
            input.value = '';
        }

        // Remove allowed user
        function removeAllowedUser(userId) {
            showDeleteUserModal(userId);
        }

        // Toggle user list visibility
        function toggleUserList() {
            const dropdown = document.getElementById('user-dropdown');
            if (dropdown) {
                dropdown.classList.toggle('show');
            }
        }

        // Close dropdown when clicking outside
        document.addEventListener('click', function(e) {
            const dropdown = document.getElementById('user-dropdown');
            const userSection = document.querySelector('.user-selector-section');
            if (dropdown && userSection && !userSection.contains(e.target)) {
                dropdown.classList.remove('show');
            }
        });

        // Show clear context modal
        function showClearContextModal() {
            const modal = document.getElementById('clear-context-modal');
            const selector = document.getElementById('clear-context-user-selector');
            if (modal && selector) {
                selector.innerHTML = '<option value="">请选择用户...</option>';
                allowedUsers.forEach(userId => {
                    const option = document.createElement('option');
                    option.value = userId;
                    option.textContent = `用户 ${userId}`;
                    if (userId === selectedUserId) {
                        option.selected = true;
                    }
                    selector.appendChild(option);
                });
                modal.style.display = 'flex';
            }
        }

        // Close clear context modal
        function closeClearContextModal() {
            const modal = document.getElementById('clear-context-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }

        // Confirm clear context
        function confirmClearContext() {
            const selector = document.getElementById('clear-context-user-selector');
            const userId = parseInt(selector.value);
            
            if (!userId || userId <= 0) {
                showToast('请选择要清空上下文的用户', 'error');
                return;
            }
            
            if (confirm(`确定要清空用户 ${userId} 的上下文吗？此操作不可恢复！`)) {
                sendStandardMessage('clear_context_for_user', { userId: userId });
                closeClearContextModal();
            }
        }

        // Clear context (legacy - for current user)
        function clearContext() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('clear_context');
            } else {
                showToast('WebSocket 未连接', 'error');
            }
        }

        // Update user list in sidebar
        function updateUserSelector(users, groups) {
            const container = document.getElementById('user-list');
            const currentUserName = document.getElementById('current-user-name');
            const currentAvatar = document.getElementById('current-avatar');
            if (!container) return;

            allowedUsers = users || [];
            allowedGroups = groups || [];

            // Update current user display
            if (currentUserName) {
                if (selectedUserId && allowedUsers.includes(selectedUserId)) {
                    currentUserName.textContent = `用户 ${selectedUserId}`;
                    if (currentAvatar) currentAvatar.textContent = '👤';
                } else {
                    currentUserName.textContent = '选择用户';
                    if (currentAvatar) currentAvatar.textContent = '👤';
                }
            }

            const totalCount = allowedUsers.length + allowedGroups.length;
            if (totalCount === 0) {
                container.innerHTML = '<p class="user-list-empty">暂无用户和群聊</p>';
                return;
            }

            container.innerHTML = '';

            // Add section header for users if there are users
            if (allowedUsers.length > 0) {
                const userHeader = document.createElement('div');
                userHeader.className = 'list-section-header';
                userHeader.innerHTML = '<span style="font-size: 0.75rem; color: var(--text-secondary); padding: 8px 12px; display: block;">👤 用户</span>';
                container.appendChild(userHeader);

                allowedUsers.forEach(userId => {
                    const item = document.createElement('div');
                    item.className = 'user-item' + (userId === selectedUserId ? ' active' : '');
                    item.innerHTML = `
                        <div class="user-info" onclick="selectUserAndClose(${userId})">
                            <span class="user-type-icon">👤</span>
                            <span class="user-name">${userId}</span>
                        </div>
                        <button class="btn-delete" onclick="event.stopPropagation(); deleteUserFromList(${userId}, 'user')" title="删除">删除</button>
                    `;
                    container.appendChild(item);
                });
            }

            // Add section header for groups if there are groups
            if (allowedGroups.length > 0) {
                const groupHeader = document.createElement('div');
                groupHeader.className = 'list-section-header';
                groupHeader.innerHTML = '<span style="font-size: 0.75rem; color: var(--text-secondary); padding: 8px 12px; display: block; margin-top: 8px;">👥 群聊</span>';
                container.appendChild(groupHeader);

                allowedGroups.forEach(groupId => {
                    const item = document.createElement('div');
                    item.className = 'user-item group-item';
                    item.innerHTML = `
                        <div class="user-info" onclick="handleGroupClick(${groupId})">
                            <span class="user-type-icon">👥</span>
                            <span class="user-name">${groupId}</span>
                        </div>
                        <button class="btn-delete" onclick="event.stopPropagation(); deleteUserFromList(${groupId}, 'group')" title="删除">删除</button>
                    `;
                    container.appendChild(item);
                });
            }
        }

        // Handle group click - show info modal
        function handleGroupClick(groupId) {
            const modal = document.getElementById('group-info-modal');
            if (modal) {
                modal.style.display = 'flex';
            }
        }

        // Close group info modal
        function closeGroupInfoModal() {
            const modal = document.getElementById('group-info-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }

        // Select user and close dropdown
        function selectUserAndClose(userId) {
            selectUser(userId);
            const dropdown = document.getElementById('user-dropdown');
            if (dropdown) {
                dropdown.classList.remove('show');
            }
        }

        // Global variable to store the ID and type to delete
        let deleteTargetId = 0;
        let deleteTargetType = 'user';

        // Show delete user modal
        function showDeleteUserModal(userId, type = 'user') {
            deleteTargetId = userId;
            deleteTargetType = type;
            const warningTitle = document.getElementById('delete-user-warning-title');
            if (warningTitle) {
                const typeText = type === 'user' ? '用户' : '群聊';
                warningTitle.textContent = `确定要删除${typeText} ${userId} 吗？`;
            }
            const modal = document.getElementById('delete-user-modal');
            if (modal) {
                modal.style.display = 'flex';
            }
        }

        // Close delete user modal
        function closeDeleteUserModal() {
            deleteTargetId = 0;
            deleteTargetType = 'user';
            const modal = document.getElementById('delete-user-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }

        // Confirm delete user
        function confirmDeleteUser() {
            if (deleteTargetId > 0) {
                if (deleteTargetType === 'user') {
                    sendStandardMessage('remove_allowed_user', { userId: deleteTargetId });
                } else {
                    sendStandardMessage('remove_allowed_group', { groupId: deleteTargetId });
                }
                closeDeleteUserModal();
            }
        }

        // Delete user/group from sidebar list
        function deleteUserFromList(id, type = 'user') {
            showDeleteUserModal(id, type);
        }

        // Update allowed users list in user management tab
        function updateAllowedUsersList() {
            const container = document.getElementById('allowed-users-list');
            if (!container) return;
            
            if (allowedUsers.length === 0) {
                container.innerHTML = '<p style="text-align: center; color: var(--text-secondary);">暂无允许的用户</p>';
                return;
            }
            
            container.innerHTML = '';
            allowedUsers.forEach(userId => {
                const item = document.createElement('div');
                item.className = 'allowed-user-item';
                item.innerHTML = `
                    <div class="user-info">
                        <span class="user-avatar">👤</span>
                        <span class="user-id">${userId}</span>
                    </div>
                    <div class="user-actions">
                        <button class="btn btn-sm btn-secondary" onclick="selectUser(${userId})">选择</button>
                        <button class="btn btn-sm btn-danger" onclick="removeAllowedUser(${userId})">移除</button>
                    </div>
                `;
                container.appendChild(item);
            });
        }

        // Update current user stats in sidebar
        function updateCurrentUserStats(stats) {
            const container = document.getElementById('current-user-stats');
            if (!container) return;
            
            if (selectedUserId > 0 && stats) {
                let userStats = null;
                
                // If stats is an array, find the current user's stats
                if (Array.isArray(stats)) {
                    userStats = stats.find(s => (s.userId || s.UserId) === selectedUserId);
                } else {
                    userStats = stats;
                }
                
                if (userStats) {
                    container.style.display = 'flex';
                    document.getElementById('user-stat-messages').textContent = userStats.totalMessages || userStats.TotalMessages || 0;
                    document.getElementById('user-stat-proactive').textContent = userStats.proactiveChats || userStats.ProactiveChats || 0;
                    document.getElementById('user-stat-reminders').textContent = userStats.reminders || userStats.Reminders || 0;
                } else {
                    container.style.display = 'none';
                }
            } else {
                container.style.display = 'none';
            }
        }

        // Handle users list message
        function handleUsersList(data) {
            if (data && Array.isArray(data.users)) {
                allowedUsers = data.users.map(u => u.userId || u);
            } else if (Array.isArray(data)) {
                // Backward compatibility
                allowedUsers = data.map(u => u.userId || u);
            }
            if (data && Array.isArray(data.groups)) {
                allowedGroups = data.groups.map(g => g.groupId || g);
            }
            updateUserSelector(allowedUsers, allowedGroups);
            updateAllowedUsersList();
        }

        // ========== Version Check Functions ==========
        
        function handleVersionCheckResult(data) {
            if (!data) return;
            
            const { hasUpdate, isVersionAllowed, currentVersion, latestVersion, minimumAllowedVersion, updateContent, updateUrl } = data;
            
            console.log('版本检查结果:', data);
            
            if (updateUrl) {
                currentUpdateUrl = updateUrl;
            }
            
            if (!isVersionAllowed) {
                showVersionNotAllowedModal(currentVersion, minimumAllowedVersion, updateContent);
            } else if (hasUpdate) {
                showUpdateAvailableModal(currentVersion, latestVersion, updateContent);
            }
        }
        
        let isVersionNotAllowed = false;
        let currentUpdateUrl = 'https://gitee.com/bingchuankeji/Ai_Chat';
        
        function showVersionNotAllowedModal(currentVersion, minimumVersion, updateContent) {
            isVersionNotAllowed = true;
            const modal = document.getElementById('version-not-allowed-modal');
            const contentList = document.getElementById('version-not-allowed-content');
            const currentVersionEl = document.getElementById('version-not-allowed-current');
            const minimumVersionEl = document.getElementById('version-not-allowed-minimum');
            
            if (currentVersionEl) currentVersionEl.textContent = currentVersion;
            if (minimumVersionEl) minimumVersionEl.textContent = minimumVersion;
            
            if (contentList && updateContent && updateContent.length > 0) {
                contentList.innerHTML = updateContent.map(item => `<li>${item}</li>`).join('');
            } else if (contentList) {
                contentList.innerHTML = '<li>请更新到最新版本以继续使用</li>';
            }
            
            if (modal) {
                modal.style.display = 'flex';
                // 禁止点击外部关闭
                modal.onclick = function(e) {
                    if (e.target === modal) {
                        e.stopPropagation();
                        return false;
                    }
                };
            }
        }
        
        function showUpdateAvailableModal(currentVersion, latestVersion, updateContent) {
            const modal = document.getElementById('update-available-modal');
            const contentList = document.getElementById('update-available-content');
            const currentVersionEl = document.getElementById('update-available-current');
            const latestVersionEl = document.getElementById('update-available-latest');
            
            if (currentVersionEl) currentVersionEl.textContent = currentVersion;
            if (latestVersionEl) latestVersionEl.textContent = latestVersion;
            
            if (contentList && updateContent && updateContent.length > 0) {
                contentList.innerHTML = updateContent.map(item => `<li>${item}</li>`).join('');
            } else if (contentList) {
                contentList.innerHTML = '<li>新版本已可用</li>';
            }
            
            if (modal) {
                modal.style.display = 'flex';
            }
        }
        
        function closeVersionNotAllowedModal() {
            const modal = document.getElementById('version-not-allowed-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }
        
        function closeUpdateAvailableModal() {
            const modal = document.getElementById('update-available-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }
        
        function confirmVersionExit() {
            sendStandardMessage('confirm_version_exit');
            closeVersionNotAllowedModal();
            showToast('正在退出应用程序...', 'info');
        }
        
        function goToUpdateUrl() {
            if (currentUpdateUrl) {
                window.open(currentUpdateUrl, '_blank');
            }
        }
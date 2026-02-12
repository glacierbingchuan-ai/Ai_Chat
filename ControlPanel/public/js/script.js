        // Global variables
        let ws = null;
        let config = {};
        let llmStatusInterval = null;
        let lastLlmStatus = 'offline'; // Track last LLM status
        let loadingStartTime = null;
        let loadingWarningTimer = null;
        let quoteLoaded = false;
        let backgroundLoaded = false;
        let wsConnected = false;
        let authFailed = false; // Flag to indicate authentication failure

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

        // Check if background image is loaded
        function checkBackgroundLoad() {
            const bgImageUrl = '/css/image.jpg';
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

        // Handle initial data
        function handleInitData(data) {
            // Update logs
            updateLogs(data.logs);
            
            // Update config
            config = data.config;
            updateConfigForm();
            
            // Update scheduled events
            updateEvents(data.scheduledEvents);
            
            // Update stats
            updateStats(data.stats);
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
            // General settings
            document.getElementById('websocket-server-uri').value = config.websocketServerUri || '';
            document.getElementById('websocket-token').value = config.websocketToken || '';
            document.getElementById('websocket-keep-alive').value = config.websocketKeepAliveInterval || '';
            document.getElementById('max-context-rounds').value = config.maxContextRounds || '';
            document.getElementById('target-user-id').value = config.targetUserId || '';
            document.getElementById('active-chat-probability').value = config.activeChatProbability || '';
            document.getElementById('min-safe-delay').value = config.minSafeDelay || '';
            document.getElementById('proactive-chat-enabled').checked = config.proactiveChatEnabled !== undefined ? config.proactiveChatEnabled : false;
            document.getElementById('reminder-enabled').checked = config.reminderEnabled !== undefined ? config.reminderEnabled : false;
            document.getElementById('reinforcement-enabled').checked = config.reinforcementEnabled !== undefined ? config.reinforcementEnabled : false;
            document.getElementById('intent-analysis-enabled').checked = config.intentAnalysisEnabled !== undefined ? config.intentAnalysisEnabled : true;

            // LLM settings
            document.getElementById('llm-model-name').value = config.llmModelName || '';
            document.getElementById('llm-api-base-url').value = config.llmApiBaseUrl || '';
            document.getElementById('llm-api-key').value = config.llmApiKey || '';
            document.getElementById('llm-max-tokens').value = config.llmMaxTokens || '';
            document.getElementById('llm-temperature').value = config.llmTemperature || '';
            document.getElementById('llm-top-p').value = config.llmTopP || '';

            // Prompt settings
            document.getElementById('base-system-prompt').value = config.baseSystemPrompt || '';
            document.getElementById('incomplete-input-prompt').value = config.incompleteInputPrompt || '';
            document.getElementById('reinforcement-prompt').value = config.reinforcementPrompt || '';
        }

        // Save configuration
        function saveConfig() {
            // Get LLM API base URL
            let llmApiBaseUrl = document.getElementById('llm-api-base-url').value;
            
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
            
            // Get WebSocket server URI and ensure it starts with ws://
            let websocketServerUri = document.getElementById('websocket-server-uri').value;
            if (websocketServerUri && !websocketServerUri.startsWith('ws://') && !websocketServerUri.startsWith('wss://')) {
                websocketServerUri = 'ws://' + websocketServerUri;
            }
            
            const newConfig = {
                // General settings
                websocketServerUri: websocketServerUri,
                websocketToken: document.getElementById('websocket-token').value,
                websocketKeepAliveInterval: parseInt(document.getElementById('websocket-keep-alive').value) || 30000,
                maxContextRounds: parseInt(document.getElementById('max-context-rounds').value) || 10,
                targetUserId: parseInt(document.getElementById('target-user-id').value) || 0,
                activeChatProbability: parseInt(document.getElementById('active-chat-probability').value) || 30,
                minSafeDelay: parseInt(document.getElementById('min-safe-delay').value) || 1200,
                proactiveChatEnabled: document.getElementById('proactive-chat-enabled').checked,
                reminderEnabled: document.getElementById('reminder-enabled').checked,
                reinforcementEnabled: document.getElementById('reinforcement-enabled').checked,
                intentAnalysisEnabled: document.getElementById('intent-analysis-enabled').checked,

                // LLM settings
                llmModelName: document.getElementById('llm-model-name').value,
                llmApiBaseUrl: llmApiBaseUrl,
                llmApiKey: document.getElementById('llm-api-key').value,
                llmMaxTokens: parseInt(document.getElementById('llm-max-tokens').value) || 1024,
                llmTemperature: parseFloat(document.getElementById('llm-temperature').value) || 0.9,
                llmTopP: parseFloat(document.getElementById('llm-top-p').value) || 0.85,

                // Prompt settings
                baseSystemPrompt: document.getElementById('base-system-prompt').value,
                incompleteInputPrompt: document.getElementById('incomplete-input-prompt').value,
                reinforcementPrompt: document.getElementById('reinforcement-prompt').value,
                
                // Role Cards settings
                roleCardsApiUrl: config.roleCardsApiUrl || 'https://gitee.com/bingchuankeji/Character_Cards/raw/main/list.json'
            };

            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('config_update', newConfig);
            }
        }

        // Clear logs
        function clearLogs() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('clear_logs');
            }
        }

        // Clear context
        function clearContext() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('clear_context');
            } else {
                showToast('WebSocket 未连接', 'error');
            }
        }

        // Clear logs display
        function clearLogsDisplay() {
            const logsContainer = document.getElementById('logs-container');
            logsContainer.innerHTML = '<p>日志已清空</p>';
        }

        // Update stats
        function updateStats(stats) {
            document.getElementById('total-messages').textContent = stats.totalMessages || 0;
            document.getElementById('proactive-chats').textContent = stats.proactiveChats || 0;
            document.getElementById('reminders').textContent = stats.reminders || 0;
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
            // Hide all view contents
            document.querySelectorAll('.view-content').forEach(view => {
                view.style.display = 'none';
            });

            // Show selected view content
            document.getElementById(`${viewId}-view`).style.display = 'block';

            // Update active menu item
            document.querySelectorAll('.menu-item').forEach(item => {
                item.className = 'menu-item';
            });
            document.querySelectorAll('.menu-item').forEach(item => {
                if (item.onclick && item.onclick.toString().includes(viewId)) {
                    item.className = 'menu-item active';
                }
            });

            // Auto load role cards when switching to role cards view
            if (viewId === 'role-cards') {
                setTimeout(loadRoleCards, 100);
            }
            
            // Auto load chat history when switching to chat history view
            if (viewId === 'chat-history') {
                setTimeout(loadChatHistory, 100);
            }
            
            // Auto load contributors when switching to about view
            if (viewId === 'about') {
                setTimeout(loadContributors, 100);
            }
        }

        // Test connection
        function testConnection() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('test_connection');
            } else {
                showToast('WebSocket 未连接', 'error');
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
            // Show QQ group modal after 2 seconds
            setTimeout(showQqGroupModal, 2000);
        };

        // Cleanup on page unload
        window.onunload = function() {
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
        };

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

            // 准备角色卡数据，包括表情包信息
            const roleCardData = {
                baseSystemPrompt: currentRoleCard.roleCardPromptContent || '',
                reinforcementPrompt: currentRoleCard.roleCardEnhancedPromptContent || '',
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

        // Load contributors list
        function loadContributors() {
            const container = document.getElementById('contributors-list');
            container.innerHTML = `
                <div class="loading">
                    <div class="loading-spinner"></div>
                    <div>加载贡献者中...</div>
                </div>
            `;

            fetch('css/contributors/list.txt')
                .then(response => {
                    if (!response.ok) {
                        throw new Error('Failed to load contributors');
                    }
                    return response.text();
                })
                .then(data => {
                    displayContributors(data);
                })
                .catch(error => {
                    console.error('Error loading contributors:', error);
                    container.innerHTML = `
                        <div style="text-align: center; padding: 20px;">
                            <p style="color: var(--error-color);">加载贡献者失败</p>
                            <button class="btn btn-primary" onclick="loadContributors()" style="margin-top: 10px;">重新加载</button>
                        </div>
                    `;
                });
        }

        // Display contributors
        function displayContributors(data) {
            const container = document.getElementById('contributors-list');
            container.innerHTML = '';

            const contributors = data.trim().split('\n').filter(line => line.trim() !== '');

            if (contributors.length === 0) {
                container.innerHTML = '<p style="text-align: center;">暂无贡献者</p>';
                return;
            }

            contributors.forEach(line => {
                const parts = line.split('|');
                if (parts.length === 2) {
                    const qqInfo = parts[0].split(':');
                    if (qqInfo.length === 2) {
                        const qq = qqInfo[1];
                        const name = parts[1];
                        const avatarUrl = `css/contributors/${qq}.jpg`;

                        const contributorElement = document.createElement('div');
                        contributorElement.className = 'contributor-item';
                        contributorElement.innerHTML = `
                            <div class="contributor-avatar">
                                <img src="${avatarUrl}" alt="${name}" onerror="this.src='css/glacier.jpg';">
                            </div>
                            <div class="contributor-info">
                                <h4>${name}</h4>
                                <p>QQ: ${qq}</p>
                            </div>
                        `;
                        container.appendChild(contributorElement);
                    }
                }
            });
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

        // Chat History functionality
        // Load chat history
        function loadChatHistory() {
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
            container.innerHTML = '';

            if (!chatHistory || chatHistory.length === 0) {
                container.innerHTML = '<p style="text-align: center;">暂无聊天记录</p>';
                return;
            }

            chatHistory.forEach(message => {
                addChatMessage(message);
            });

            // Scroll to bottom
            container.scrollTop = container.scrollHeight;
        }

        // Add single chat message
        function addChatMessage(message) {
            const container = document.getElementById('chat-messages-container');
            const messageElement = document.createElement('div');
            messageElement.className = `chat-message ${message.role}`;

            let messageContent = '';
            if (message.content) {
                messageContent = `
                    <div class="chat-bubble">${message.content}</div>
                    <div class="chat-message-time">${message.timestamp}</div>
                `;
            } else if (message.meme) {
                const key = getAccessKey();
                const memeUrl = `/api/proxy?action=get_meme&name=${encodeURIComponent(message.meme)}&key=${key}`;
                messageContent = `
                    <div class="chat-bubble">
                        <img src="${memeUrl}" alt="Meme">
                    </div>
                    <div class="chat-message-time">${message.timestamp}</div>
                `;
            }

            messageElement.innerHTML = messageContent;
            container.appendChild(messageElement);
        }

        // Update WebSocket message handler to include chat history
        function handleWebSocketMessage(message) {
            // 统一处理标准消息格式
            const messageType = message.type;
            const messageData = message.data;
            
            console.log('Received message:', messageType, message.timestamp || new Date().toLocaleString());
            
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
                        
                        // Clear all elements from the page
                        document.body.innerHTML = '';
                        
                        // Replace entire page content with unauthorized HTML
                        document.documentElement.innerHTML = messageData.html;
                        
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
                    showToast('配置已更新', 'success');
                    break;
                case 'logs_cleared':
                    clearLogsDisplay();
                    showToast('日志已清空', 'success');
                    break;
                case 'context_cleared':
                    showToast('上下文已清空', 'success');
                    break;
                case 'stats_updated':
                    updateStats(messageData);
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
                case 'chat_history':
                    handleChatHistory(messageData);
                    break;
                case 'chat_message':
                    addChatMessage(messageData);
                    // Scroll to bottom
                    const container = document.getElementById('chat-messages-container');
                    if (container) {
                        container.scrollTop = container.scrollHeight;
                    }
                    break;
                default:
                    console.log('Unknown message type:', messageType);
            }
        }


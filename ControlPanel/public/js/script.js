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

        // Handle WebSocket messages
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
                default:
                    console.log('Unknown message type:', messageType);
            }
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
            const newConfig = {
                // General settings
                websocketServerUri: document.getElementById('websocket-server-uri').value,
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
                llmApiBaseUrl: document.getElementById('llm-api-base-url').value,
                llmApiKey: document.getElementById('llm-api-key').value,
                llmMaxTokens: parseInt(document.getElementById('llm-max-tokens').value) || 1024,
                llmTemperature: parseFloat(document.getElementById('llm-temperature').value) || 0.9,
                llmTopP: parseFloat(document.getElementById('llm-top-p').value) || 0.85,

                // Prompt settings
                baseSystemPrompt: document.getElementById('base-system-prompt').value,
                incompleteInputPrompt: document.getElementById('incomplete-input-prompt').value,
                reinforcementPrompt: document.getElementById('reinforcement-prompt').value,
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
                // Get current values from input fields
                const testConfig = {
                    llmModelName: document.getElementById('llm-model-name').value || '',
                    llmApiBaseUrl: document.getElementById('llm-api-base-url').value || '',
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
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
        let vectorDbPageSize = 20; // 每页显示条数
        let vectorDbCurrentPage = 1; // 当前页码
        let vectorDbAllEntries = []; // 所有向量条目
        let protocolConnected = false; // 协议端连接状态
        let protocolServerUri = localStorage.getItem('protocolServerUri') || ''; // 保存的协议端地址
        let protocolToken = localStorage.getItem('protocolToken') || ''; // 保存的token
        let protocolKeepAlive = parseInt(localStorage.getItem('protocolKeepAlive')) || 30000; // 保存的心跳间隔
        let initDataLoaded = false; // init 数据是否加载完成
        let welcomePageLoaded = false; // 欢迎页数据是否加载完成
        let fireworksPlayed = false; // 烟花是否已播放过
        let initialConnectionAttempted = false; // 初始连接尝试是否已完成
        let initialDataRequested = false; // 是否已请求过初始数据，防止重复请求

        // WebSocket 消息追踪
        const pendingRequests = new Map(); // 存储待处理的请求
        const REQUEST_TIMEOUT = 30000; // 请求超时时间 30秒

        // ========== 工具函数 ==========

        /**
         * DOM 操作工具
         */
        const DOM = {
            get(id) {
                return document.getElementById(id);
            },
            show(id, display = 'block') {
                const el = document.getElementById(id);
                if (el) el.style.display = display;
            },
            hide(id) {
                const el = document.getElementById(id);
                if (el) el.style.display = 'none';
            },
            setText(id, text) {
                const el = document.getElementById(id);
                if (el) el.textContent = text;
            },
            setHtml(id, html) {
                const el = document.getElementById(id);
                if (el) el.innerHTML = html;
            },
            setValue(id, value) {
                const el = document.getElementById(id);
                if (el) el.value = value;
            },
            addClass(id, className) {
                const el = document.getElementById(id);
                if (el) el.classList.add(className);
            },
            removeClass(id, className) {
                const el = document.getElementById(id);
                if (el) el.classList.remove(className);
            },
            on(id, event, handler) {
                const el = document.getElementById(id);
                if (el) el.addEventListener(event, handler);
            }
        };

        /**
         * 防抖函数
         */
        function debounce(fn, delay) {
            let timer = null;
            return function(...args) {
                clearTimeout(timer);
                timer = setTimeout(() => fn.apply(this, args), delay);
            };
        }

        /**
         * 节流函数
         */
        function throttle(fn, limit) {
            let inThrottle = false;
            return function(...args) {
                if (!inThrottle) {
                    fn.apply(this, args);
                    inThrottle = true;
                    setTimeout(() => inThrottle = false, limit);
                }
            };
        }

        /**
         * 请求初始数据（防止重复请求）
         */
        function requestInitialData() {
            if (initialDataRequested) {
                console.log('Initial data already requested, skipping...');
                return;
            }
            initialDataRequested = true;
            console.log('Requesting initial data...');
            sendStandardMessage('get_initial_data', {});
        }

        /**
         * HTML 转义
         */
        function escapeHtml(text) {
            if (!text) return '';
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }

        /**
         * 安全的 WebSocket 消息发送
         */
        function sendMessageSafe(type, data = null, callback = null) {
            if (!ws || ws.readyState !== WebSocket.OPEN) {
                console.warn('[WebSocket] Not connected, cannot send:', type);
                if (callback && typeof callback === 'function') {
                    callback(new Error('WebSocket not connected'), null);
                }
                return null;
            }
            return sendStandardMessage(type, data, callback);
        }

        /**
         * Modal 管理器
         */
        const Modal = {
            show(modalId) {
                const modal = document.getElementById(modalId);
                if (modal) modal.style.display = 'flex';
            },
            hide(modalId) {
                const modal = document.getElementById(modalId);
                if (modal) modal.style.display = 'none';
            }
        };

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
            
            // Add enter key listener for vector search
            setTimeout(() => {
                const searchInput = document.getElementById('vector-search-input');
                if (searchInput) {
                    searchInput.addEventListener('keypress', function(e) {
                        if (e.key === 'Enter') {
                            searchVectors();
                        }
                    });
                }
                // Load vector DB settings
                loadVectorDbSettings();
            }, 500);
            
            // 添加聊天窗口滚动监听（用于分页加载历史消息）
            setupChatScrollListener();
            
            // 绑定 radio button 事件监听器
            setupRadioButtonListeners();
        }
        
        // 设置聊天窗口滚动监听
        function setupChatScrollListener() {
            setTimeout(() => {
                const container = document.getElementById('chat-messages-container');
                if (container) {
                    container.addEventListener('scroll', function() {
                        // 当滚动到顶部附近时加载更多（添加防抖）
                        if (container.scrollTop < 50) {
                            const now = Date.now();
                            // 至少间隔 500ms 才能再次触发加载，避免无限循环
                            if (now - chatHistoryState.lastLoadTime > 500) {
                                chatHistoryState.lastLoadTime = now;
                                loadMoreChatHistory();
                            }
                        }
                    });
                }
            }, 1000); // 延迟 1 秒确保 DOM 已加载
        }

        // 绑定 radio button 事件监听器
        function setupRadioButtonListeners() {
            setTimeout(() => {
                // 上下文模式切换
                const vectorRadio = document.getElementById('context-mode-vector');
                const summarizationRadio = document.getElementById('context-mode-summarization');
                if (vectorRadio) {
                    vectorRadio.addEventListener('change', toggleContextMode);
                }
                if (summarizationRadio) {
                    summarizationRadio.addEventListener('change', toggleContextMode);
                }
                
                // Embedding 来源切换
                const localEmbeddingRadio = document.getElementById('embedding-source-local');
                const apiEmbeddingRadio = document.getElementById('embedding-source-api');
                if (localEmbeddingRadio) {
                    localEmbeddingRadio.addEventListener('change', toggleEmbeddingSource);
                }
                if (apiEmbeddingRadio) {
                    apiEmbeddingRadio.addEventListener('change', toggleEmbeddingSource);
                }
            }, 100); // 延迟确保 DOM 已渲染
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
                // 注意：加载屏幕在协议端连接状态确定后再隐藏
                // 协议端连接状态检查在 handleInitData 中处理
            }
        }

        // 检查协议端连接状态
        function checkProtocolConnectionStatus() {
            if (!protocolConnected) {
                // 如果未连接协议端，显示连接配置页面
                showProtocolConnectionScreen();
            } else {
                // 已连接，显示切换按钮
                showSwitchProtocolButton();
            }
        }

        // 显示协议端连接配置页面
        // autoStartScan: 是否自动启动扫描（仅在初始连接尝试完成后才应为true）
        function showProtocolConnectionScreen(autoStartScan = true) {
            const screen = document.getElementById('protocol-connection-screen');
            if (screen) {
                const serverUri = config.websocketServerUri || protocolServerUri || '';
                const token = config.websocketToken || protocolToken || '';
                const keepAlive = config.websocketKeepAliveInterval || protocolKeepAlive || 30000;

                const uriInput = document.getElementById('protocol-server-uri');
                const tokenInput = document.getElementById('protocol-token');
                const keepAliveInput = document.getElementById('protocol-keep-alive');

                if (uriInput) uriInput.value = serverUri;
                if (tokenInput) tokenInput.value = token;
                if (keepAliveInput) keepAliveInput.value = keepAlive;

                screen.style.display = 'flex';
                
                // 只有在允许自动启动扫描时才通知后端开始扫描
                if (autoStartScan) {
                    notifyStartScan();
                }
            }
            hideProtocolConnectionError();
            showProtocolConnectionStatus(false);
            hideSwitchProtocolButton();
        }

        // 隐藏协议端连接配置页面
        function hideProtocolConnectionScreen() {
            const screen = document.getElementById('protocol-connection-screen');
            if (screen) {
                screen.style.display = 'none';
            }
        }

        // 显示切换协议端按钮
        function showSwitchProtocolButton() {
            const btn = document.getElementById('switch-protocol-btn');
            if (btn) {
                btn.style.display = 'flex';
            }
        }

        // 隐藏切换协议端按钮
        function hideSwitchProtocolButton() {
            const btn = document.getElementById('switch-protocol-btn');
            if (btn) {
                btn.style.display = 'none';
            }
        }

        // 快速选择协议端地址
        function setQuickOption(uri) {
            const uriInput = document.getElementById('protocol-server-uri');
            if (uriInput) {
                uriInput.value = uri;
                uriInput.focus();
            }
        }

        // 切换密码可见性
        function togglePasswordVisibility(inputId, button) {
            const input = document.getElementById(inputId);
            const eyeIcon = button.querySelector('.eye-icon');
            if (input) {
                if (input.type === 'password') {
                    input.type = 'text';
                    if (eyeIcon) eyeIcon.textContent = '🙈';
                } else {
                    input.type = 'password';
                    if (eyeIcon) eyeIcon.textContent = '👁️';
                }
            }
        }

        // 扫描相关变量
        let isScanning = false;
        let foundServices = new Map();
        let scanRequested = false; // 防止重复请求

        // 通知后端开始扫描（页面打开时调用）
        async function notifyStartScan() {
            // 防止重复发送
            if (scanRequested) return;
            scanRequested = true;
            
            // 尝试发送，如果失败则重试
            let retries = 0;
            const maxRetries = 5;
            
            const trySend = () => {
                if (ws && ws.readyState === WebSocket.OPEN) {
                    ws.send(JSON.stringify({ type: 'start_scan' }));
                    console.log('Scan started');
                    return true;
                }
                return false;
            };
            
            // 立即尝试
            if (trySend()) return;
            
            // 延迟重试
            const retryInterval = setInterval(() => {
                retries++;
                if (trySend() || retries >= maxRetries) {
                    clearInterval(retryInterval);
                    if (retries >= maxRetries) {
                        console.warn('Failed to start scan after', maxRetries, 'retries');
                    }
                }
            }, 500);
        }

        // 更新扫描UI状态
        function updateScanUI(scanning) {
            isScanning = scanning;
            const scanStatus = document.getElementById('scan-status');
            const statusIndicator = document.getElementById('scan-status-indicator');
            
            if (scanStatus) {
                scanStatus.style.display = scanning ? 'flex' : 'none';
            }
            
            if (statusIndicator) {
                if (scanning) {
                    statusIndicator.classList.remove('stopped');
                    statusIndicator.innerHTML = '<span class="scan-dot"></span><span>扫描中</span>';
                } else {
                    statusIndicator.classList.add('stopped');
                    statusIndicator.innerHTML = '<span class="scan-dot"></span><span>已停止</span>';
                }
            }
        }

        // 处理发现的服务（实时推送）
        function handleServiceFound(result) {
            console.log('handleServiceFound called:', result);
            // C#返回的是大写属性名，需要适配
            const address = result.Address || result.address;
            const name = result.Name || result.name;
            
            if (!result || !address) {
                console.warn('Invalid result:', result);
                return;
            }
            
            // 避免重复添加
            if (foundServices.has(address)) {
                console.log('Service already exists:', address);
                return;
            }
            foundServices.set(address, result);
            
            const resultsContainer = document.getElementById('scan-results');
            console.log('resultsContainer:', resultsContainer);
            if (!resultsContainer) {
                console.warn('scan-results container not found');
                return;
            }
            
            // 移除空状态提示
            const emptyTip = resultsContainer.querySelector('.scan-results-empty');
            if (emptyTip) {
                emptyTip.remove();
            }
            
            const item = document.createElement('div');
            item.className = 'scan-result-item';
            item.onclick = () => selectScanResult(address);
            
            item.innerHTML = `
                <div class="result-icon">🤖</div>
                <div class="result-info">
                    <div class="result-name">${name || 'OneBot服务'}</div>
                    <div class="result-address">${address}</div>
                </div>
                <div class="result-action">选择</div>
            `;
            
            resultsContainer.appendChild(item);
            console.log('Service added to UI:', address);
        }

        // 处理扫描状态变化
        function handleScanStateChanged(data) {
            updateScanUI(data.isScanning);
        }

        // 选择扫描结果
        function selectScanResult(address) {
            const uriInput = document.getElementById('protocol-server-uri');
            if (uriInput) {
                uriInput.value = address;
                uriInput.focus();
            }
        }

        // 连接到协议端
        async function connectToProtocol() {
            const serverUri = document.getElementById('protocol-server-uri').value.trim();
            const token = document.getElementById('protocol-token').value.trim();
            const keepAlive = parseInt(document.getElementById('protocol-keep-alive').value) || 30000;

            if (!serverUri) {
                showProtocolConnectionError('请输入协议端地址');
                return;
            }

            // 验证地址格式
            let uri = serverUri;
            if (!uri.startsWith('ws://') && !uri.startsWith('wss://')) {
                uri = 'ws://' + uri;
            }

            // 显示连接中状态
            showProtocolConnectionStatus(true);
            hideProtocolConnectionError();

            try {
                // 发送连接请求到后端
                const result = await sendProtocolConnectRequest({
                    serverUri: uri,
                    token: token,
                    keepAliveInterval: keepAlive
                });

                if (result.success) {
                    // 保存配置
                    protocolServerUri = uri;
                    protocolToken = token;
                    protocolKeepAlive = keepAlive;
                    localStorage.setItem('protocolServerUri', protocolServerUri);
                    localStorage.setItem('protocolToken', protocolToken);
                    localStorage.setItem('protocolKeepAlive', protocolKeepAlive.toString());

                    protocolConnected = true;
                    hideProtocolConnectionScreen();
                    showSwitchProtocolButton();
                    showToast('协议端连接成功', 'success');
                    // 连接成功后，重新加载所有数据
                    reloadAllData();
                } else {
                    showProtocolConnectionError(result.message || '连接失败，请检查地址和配置');
                }
            } catch (error) {
                showProtocolConnectionError('连接请求失败: ' + error.message);
            } finally {
                showProtocolConnectionStatus(false);
            }
        }

        // 发送协议端连接请求
        function sendProtocolConnectRequest(data) {
            return new Promise((resolve, reject) => {
                sendStandardMessage('connect_protocol', data, (error, response) => {
                    if (error) {
                        reject(error);
                    } else {
                        resolve(response);
                    }
                });
            });
        }

        // 显示连接状态
        function showProtocolConnectionStatus(show) {
            const statusEl = document.getElementById('protocol-connection-status');
            if (statusEl) {
                statusEl.style.display = show ? 'flex' : 'none';
            }
            // 禁用/启用连接按钮
            const btn = document.querySelector('.btn-connect');
            if (btn) {
                btn.disabled = show;
            }
        }

        // 显示连接错误
        function showProtocolConnectionError(message) {
            const errorEl = document.getElementById('protocol-connection-error');
            if (errorEl) {
                errorEl.querySelector('.error-text').textContent = message;
                errorEl.style.display = 'flex';
            }
        }

        // 隐藏连接错误
        function hideProtocolConnectionError() {
            const errorEl = document.getElementById('protocol-connection-error');
            if (errorEl) {
                errorEl.style.display = 'none';
            }
        }

        // 切换协议端
        let isSwitchingProtocol = false; // 防止重复点击

        function switchProtocolServer() {
            if (isSwitchingProtocol) {
                return;
            }
            if (confirm('确定要断开当前协议端连接并切换到新的协议端吗？')) {
                isSwitchingProtocol = true;

                // 设置超时，确保 isSwitchingProtocol 会被重置
                const timeoutId = setTimeout(() => {
                    isSwitchingProtocol = false;
                    protocolConnected = false;
                    showProtocolConnectionScreen();
                    showToast('断开请求超时，请重试', 'warning');
                }, 5000);

                // 发送断开连接请求
                const messageId = sendStandardMessage('disconnect_protocol', {}, (error, response) => {
                    clearTimeout(timeoutId);
                    isSwitchingProtocol = false;
                    // 无论断开是否成功，都显示配置页面
                    protocolConnected = false;
                    showProtocolConnectionScreen();
                    showToast('已断开协议端连接，请配置新的协议端', 'info');
                });

                // 如果 messageId 为 null，说明发送失败
                if (!messageId) {
                    clearTimeout(timeoutId);
                    isSwitchingProtocol = false;
                    protocolConnected = false;
                    showProtocolConnectionScreen();
                    showToast('发送断开请求失败', 'error');
                }
            }
        }

        // 处理协议端断开事件
        function handleProtocolDisconnected() {
            protocolConnected = false;
            showProtocolConnectionScreen();
            showToast('协议端连接已断开，请重新配置', 'warning');
            // 停止欢迎页自动刷新
            if (typeof stopWelcomeAutoRefresh === 'function') {
                stopWelcomeAutoRefresh();
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
                // 使用 display: none 完全隐藏，而不是仅仅依赖 opacity
                loadingScreen.style.display = 'none';
                loadingScreen.style.opacity = '0';
                loadingScreen.style.visibility = 'hidden';
            }

            // Start fireworks animation after loading completes (only once)
            if (typeof Fireworks !== 'undefined' && !fireworksPlayed) {
                Fireworks.start();
                fireworksPlayed = true;
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
                // 先重置 display，再设置 opacity 和 visibility
                loadingScreen.style.display = 'flex';
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
                // 注意：不在这里发送 get_initial_data，等待 initial_connection_attempt_completed 事件
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

        // Send standard message with optional callback for response
        function sendStandardMessage(type, data = null, callback = null) {
            // 如果页面正在卸载，不发送新请求
            if (window.isPageUnloading) {
                console.warn('[WebSocket] Page is unloading, skipping message:', type);
                if (callback && typeof callback === 'function') {
                    callback(new Error('Page is unloading'), null);
                }
                return null;
            }

            if (ws && ws.readyState === WebSocket.OPEN) {
                const message = createStandardMessage(type, data);
                ws.send(JSON.stringify(message));
                
                // 如果有回调函数，存储待处理请求
                if (callback && typeof callback === 'function') {
                    const timeoutId = setTimeout(() => {
                        if (pendingRequests.has(message.id)) {
                            pendingRequests.delete(message.id);
                            callback(new Error('Request timeout'), null);
                        }
                    }, REQUEST_TIMEOUT);
                    
                    pendingRequests.set(message.id, {
                        callback: callback,
                        timeoutId: timeoutId,
                        timestamp: Date.now()
                    });
                }
                
                return message.id;
            }
            if (callback && typeof callback === 'function') {
                callback(new Error('WebSocket not connected'), null);
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

            // Load vector DB settings from config
            loadVectorDbSettings();

            // Initialize local embedding status from init data
            if (data.localEmbeddingStatus) {
                updateLocalEmbeddingStatusUI(data.localEmbeddingStatus.modelExists);
            }

            // Initialize welcome page data
            if (typeof initWelcomePage === 'function') {
                initWelcomePage();
            }

            // 使用 init 消息中的协议端连接状态
            if (data.protocolStatus) {
                protocolConnected = data.protocolStatus.isConnected || false;
                initialConnectionAttempted = data.protocolStatus.initialConnectionAttempted || false;

                // 正常情况下，此时 initialConnectionAttempted 应该为 true
                // 因为前端是在收到 initial_connection_attempt_completed 事件后才请求 get_initial_data
                // 但为了兼容性，仍然检查这个标志
                if (!initialConnectionAttempted) {
                    console.warn('Received init data before initial connection completed, this should not happen');
                    // 仍然继续处理，但可能显示连接配置页面
                }
                
                if (protocolConnected) {
                    // 协议端已连接，初始化主界面
                    initializeMainUI();
                    showSwitchProtocolButton();
                } else {
                    // 协议端未连接，显示配置页面并启动扫描
                    // 无论是否有配置，只要连接失败就启动扫描，帮助用户快速重新连接
                    console.log('Protocol connection failed, showing config screen with auto-scan');
                    showProtocolConnectionScreen(true);
                }
            } else {
                // 兼容旧版本，如果没有 protocolStatus，显示配置页面（不自动扫描）
                showProtocolConnectionScreen(false);
            }

            // 标记 init 数据加载完成
            initDataLoaded = true;
            // 检查是否所有数据都加载完成
            checkAllDataLoaded();
            
            // 如果协议端未连接，隐藏加载屏幕（在 showProtocolConnectionScreen 之后）
            if (!protocolConnected && initDataLoaded && welcomePageLoaded) {
                hideLoadingScreen();
            }
        }

        // 初始化主界面（在协议端连接完成后调用）
        function initializeMainUI() {
            // 显示主界面内容
            const mainContent = document.querySelector('.main-content');
            if (mainContent) {
                mainContent.style.display = 'flex';
            }
            // 可以在这里添加其他初始化逻辑
        }

        // 重新加载所有数据（切换协议端后调用）
        function reloadAllData() {
            // 重置加载标志
            initDataLoaded = false;
            welcomePageLoaded = false;
            fireworksPlayed = false;
            // 重置 welcome.js 的加载状态
            if (typeof resetWelcomePageState === 'function') {
                resetWelcomePageState();
            }
            // 重新发送 init 请求获取最新数据
            // 注意：init 响应会在 handleWebSocketMessage 中自动处理，不需要回调
            sendStandardMessage('get_initial_data', {});
        }

        // 检查所有数据是否加载完成，完成后隐藏加载屏幕
        function checkAllDataLoaded() {
            if (initDataLoaded && welcomePageLoaded) {
                hideLoadingScreen();
            }
        }

        // 欢迎页数据加载完成回调（由 welcome.js 调用）
        function onWelcomePageLoaded() {
            welcomePageLoaded = true;
            checkAllDataLoaded();
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
            if (!logsContainer) {
                console.warn('[Logs] Container not found');
                return;
            }
            logsContainer.innerHTML = '';

            if (!Array.isArray(logs)) {
                console.warn('[Logs] Invalid logs data');
                return;
            }

            logs.forEach(log => {
                if (!log) return;
                const logEntry = document.createElement('div');
                logEntry.className = `log-entry ${(log.level || 'info').toLowerCase()}`;
                logEntry.innerHTML = `
                    <strong>${log.timestamp || '-'}</strong> [${log.level || 'INFO'}] [${log.source || '-'}]<br>
                    ${log.message || ''}
                `;
                logsContainer.appendChild(logEntry);
            });

            // Scroll to bottom
            logsContainer.scrollTop = logsContainer.scrollHeight;
        }

        // Add single log entry
        function addSingleLog(log) {
            if (!log) return;

            const logsContainer = document.getElementById('logs-container');
            if (!logsContainer) {
                console.warn('[Logs] Container not found');
                return;
            }

            // Check if logs container is empty or contains loading spinner
            if (logsContainer.innerHTML === '' || logsContainer.innerHTML.includes('loading')) {
                logsContainer.innerHTML = '';
            }

            const logEntry = document.createElement('div');
            logEntry.className = `log-entry ${(log.level || 'info').toLowerCase()}`;
            logEntry.innerHTML = `
                <strong>${log.timestamp || '-'}</strong> [${log.level || 'INFO'}] [${log.source || '-'}]<br>
                ${log.message || ''}
            `;
            logsContainer.appendChild(logEntry);

            // Scroll to bottom
            logsContainer.scrollTop = logsContainer.scrollHeight;
        }

        // Update config form
        function updateConfigForm() {
            // General settings (Global)
            document.getElementById('max-context-rounds').value = config.maxContextRounds || '';
            document.getElementById('rate-limit-time-window').value = config.rateLimitTimeWindow || 60;
            document.getElementById('rate-limit-max-requests').value = config.rateLimitMaxRequests || 10;


            // LLM settings (Global)
            document.getElementById('llm-model-name').value = config.llmModelName || '';
            document.getElementById('llm-api-base-url').value = config.llmApiBaseUrl || '';
            document.getElementById('llm-api-key').value = config.llmApiKey || '';
            document.getElementById('llm-max-tokens').value = config.llmMaxTokens || '';
            document.getElementById('llm-temperature').value = config.llmTemperature || '';
            document.getElementById('llm-top-p').value = config.llmTopP || '';
            
            // Context management settings (Global) - 默认使用对话压缩
            const useVectorContext = config.useVectorContext === true;
            const useContextSummarization = config.useContextSummarization !== false;
            const useLocalEmbedding = config.useLocalEmbedding || false;
            
            // 直接设置显示状态，不触发 onchange 事件（先设置UI，再设置radio状态）
            const embeddingSection = document.getElementById('embedding-settings-section');
            if (embeddingSection) embeddingSection.style.display = useVectorContext ? 'block' : 'none';
            
            if (useVectorContext) {
                const localSettings = document.getElementById('embedding-local-settings');
                const apiSettings = document.getElementById('embedding-api-settings');
                if (localSettings) localSettings.style.display = useLocalEmbedding ? 'block' : 'none';
                if (apiSettings) apiSettings.style.display = useLocalEmbedding ? 'none' : 'block';
            }
            
            // 获取 radio button 元素
            const vectorRadio = document.getElementById('context-mode-vector');
            const summarizationRadio = document.getElementById('context-mode-summarization');
            const localEmbeddingRadio = document.getElementById('embedding-source-local');
            const apiEmbeddingRadio = document.getElementById('embedding-source-api');
            
            // 设置 radio button 状态（使用 addEventListener 后，设置 checked 不会触发事件）
            if (useVectorContext) {
                vectorRadio.checked = true;
            } else {
                summarizationRadio.checked = true;
            }
            
            if (useLocalEmbedding) {
                localEmbeddingRadio.checked = true;
            } else {
                apiEmbeddingRadio.checked = true;
            }

            // Embedding settings (Global)
            document.getElementById('embedding-model-name').value = config.embeddingModelName || '';
            document.getElementById('embedding-api-base-url').value = config.embeddingApiBaseUrl || '';
            document.getElementById('embedding-api-key').value = config.embeddingApiKey || '';
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

            const newConfig = {
                maxContextRounds: parseInt(document.getElementById('max-context-rounds').value) || 10,
                rateLimitTimeWindow: parseInt(document.getElementById('rate-limit-time-window').value) || 60,
                rateLimitMaxRequests: parseInt(document.getElementById('rate-limit-max-requests').value) || 10,

                llmModelName: document.getElementById('llm-model-name').value,
                llmApiBaseUrl: llmApiBaseUrl,
                llmApiKey: document.getElementById('llm-api-key').value,
                llmMaxTokens: parseInt(document.getElementById('llm-max-tokens').value) || 1024,
                llmTemperature: parseFloat(document.getElementById('llm-temperature').value) || 0.9,
                llmTopP: parseFloat(document.getElementById('llm-top-p').value) || 0.85,
                
                embeddingModelName: document.getElementById('embedding-model-name').value,
                embeddingApiBaseUrl: document.getElementById('embedding-api-base-url').value,
                embeddingApiKey: document.getElementById('embedding-api-key').value,

                useVectorContext: document.getElementById('context-mode-vector').checked,
                useContextSummarization: document.getElementById('context-mode-summarization').checked,
                useLocalEmbedding: document.getElementById('embedding-source-local').checked,

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

        function updateQueueStatus(queueCount) {
            const queueStatusElement = document.getElementById('queue-status');
            const queueStatusTextElement = document.getElementById('queue-status-text');
            
            if (queueCount > 0) {
                queueStatusElement.style.display = 'flex';
                queueStatusTextElement.textContent = `当前有 ${queueCount} 条请求，正在排队...`;
            } else {
                queueStatusElement.style.display = 'none';
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

        // Toggle context mode - 控制 Embedding 设置的显示/隐藏
        function toggleContextMode() {
            const isVectorMode = document.getElementById('context-mode-vector').checked;
            const embeddingSection = document.getElementById('embedding-settings-section');
            if (embeddingSection) {
                embeddingSection.style.display = isVectorMode ? 'block' : 'none';
            }
            // 只在向量模式下，且选择了本地嵌入时才检查状态
            if (isVectorMode) {
                const isLocal = document.getElementById('embedding-source-local')?.checked;
                if (isLocal) {
                    checkLocalEmbeddingStatus();
                }
            }
        }

        // 切换 Embedding 来源（API/本地）
        function toggleEmbeddingSource() {
            const isLocal = document.getElementById('embedding-source-local').checked;
            const apiSettings = document.getElementById('embedding-api-settings');
            const localSettings = document.getElementById('embedding-local-settings');
            
            if (apiSettings) apiSettings.style.display = isLocal ? 'none' : 'block';
            if (localSettings) localSettings.style.display = isLocal ? 'block' : 'none';
            
            if (isLocal) {
                checkLocalEmbeddingStatus();
            }
        }

        // 检查本地 Embedding 模型状态
        function checkLocalEmbeddingStatus() {
            if (!document.getElementById('embedding-source-local')?.checked) return;
            
            const statusIcon = document.getElementById('local-model-status-icon');
            const statusMessage = document.getElementById('local-model-status-message');
            const initSection = document.getElementById('local-model-init-section');
            const downloadSection = document.getElementById('local-model-download-section');
            
            if (statusIcon) statusIcon.textContent = '⏳';
            if (statusMessage) statusMessage.textContent = '检查中...';
            if (initSection) initSection.style.display = 'none';
            if (downloadSection) downloadSection.style.display = 'none';
            
            if (!ws || ws.readyState !== WebSocket.OPEN) {
                if (statusIcon) statusIcon.textContent = '❌';
                if (statusMessage) statusMessage.textContent = '连接未就绪';
                return;
            }
            
            sendStandardMessage('check_local_embedding_status', null, (error, response) => {
                if (error) {
                    if (statusIcon) statusIcon.textContent = '❌';
                    if (statusMessage) statusMessage.textContent = '检查失败: ' + error.message;
                    return;
                }
                
                if (response.exists) {
                    if (statusIcon) statusIcon.textContent = '✅';
                    if (statusMessage) statusMessage.textContent = '模型已就绪';
                    if (initSection) initSection.style.display = 'none';
                } else {
                    if (statusIcon) statusIcon.textContent = '⚠️';
                    if (statusMessage) statusMessage.textContent = '模型未找到';
                    if (initSection) initSection.style.display = 'block';
                }
            });
        }

        // 更新本地 Embedding 状态 UI（从初始化数据）
        function updateLocalEmbeddingStatusUI(modelExists) {
            const statusIcon = document.getElementById('local-model-status-icon');
            const statusMessage = document.getElementById('local-model-status-message');
            const initSection = document.getElementById('local-model-init-section');
            const downloadSection = document.getElementById('local-model-download-section');
            
            if (modelExists) {
                if (statusIcon) statusIcon.textContent = '✅';
                if (statusMessage) statusMessage.textContent = '模型已就绪';
                if (initSection) initSection.style.display = 'none';
                if (downloadSection) downloadSection.style.display = 'none';
            } else {
                if (statusIcon) statusIcon.textContent = '⚠️';
                if (statusMessage) statusMessage.textContent = '模型未找到';
                if (initSection) initSection.style.display = 'block';
                if (downloadSection) downloadSection.style.display = 'none';
            }
        }

        // 初始化本地 Embedding 模型（下载）
        function initLocalEmbeddingModel() {
            const btn = document.getElementById('init-local-model-btn');
            const initSection = document.getElementById('local-model-init-section');
            const downloadSection = document.getElementById('local-model-download-section');
            
            if (btn) btn.disabled = true;
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('init_local_embedding_model', null, (error, response) => {
                    if (error) {
                        showToast('初始化失败: ' + error.message, 'error');
                        if (btn) btn.disabled = false;
                        return;
                    }
                    
                    if (response.success) {
                        if (initSection) initSection.style.display = 'none';
                        if (downloadSection) downloadSection.style.display = 'block';
                        showToast('开始下载模型...', 'info');
                    } else {
                        showToast('初始化失败: ' + (response.message || '未知错误'), 'error');
                        if (btn) btn.disabled = false;
                    }
                });
            }
        }

        // 更新本地模型下载进度
        function updateLocalModelProgress(data) {
            const progressBar = document.getElementById('local-model-progress-bar');
            const percentage = document.getElementById('download-percentage');
            const statusText = document.getElementById('download-status-text');
            const sizeInfo = document.getElementById('download-size-info');
            const downloadSection = document.getElementById('local-model-download-section');
            const initSection = document.getElementById('local-model-init-section');
            const statusIcon = document.getElementById('local-model-status-icon');
            const statusMessage = document.getElementById('local-model-status-message');
            
            if (data.status === 'downloading') {
                if (downloadSection) downloadSection.style.display = 'block';
                if (initSection) initSection.style.display = 'none';
                if (progressBar) progressBar.style.width = data.progress + '%';
                if (percentage) percentage.textContent = data.progress + '%';
                if (statusText) statusText.textContent = '正在下载...';
                if (sizeInfo) sizeInfo.textContent = `${data.downloadedMB} MB / ${data.totalMB} MB`;
            } else if (data.status === 'completed') {
                if (progressBar) progressBar.style.width = '100%';
                if (percentage) percentage.textContent = '100%';
                if (statusText) statusText.textContent = '下载完成';
                if (statusIcon) statusIcon.textContent = '✅';
                if (statusMessage) statusMessage.textContent = '模型已就绪';
                if (downloadSection) downloadSection.style.display = 'none';
                showToast('模型下载完成！', 'success');
            } else if (data.status === 'error') {
                if (statusText) statusText.textContent = '下载失败';
                showToast('下载失败: ' + data.message, 'error');
                if (initSection) initSection.style.display = 'block';
                if (downloadSection) downloadSection.style.display = 'none';
                const btn = document.getElementById('init-local-model-btn');
                if (btn) btn.disabled = false;
            }
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

            if (viewId === 'vector-db') {
                updateVectorDBUserDisplay();
                setTimeout(loadVectorEntries, 100);
            }

            if (viewId === 'welcome') {
                setTimeout(function() {
                    if (typeof initWelcomePage === 'function') {
                        initWelcomePage();
                    }
                }, 100);
            } else {
                if (typeof stopWelcomeAutoRefresh === 'function') {
                    stopWelcomeAutoRefresh();
                }
            }
        }

        // Check LLM status
        function checkLlmStatus() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                // Get LLM API base URL
                let llmApiBaseUrl = document.getElementById('llm-api-base-url').value || '';
                
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
            window.isPageUnloading = true;

            if (ws && ws.readyState === WebSocket.OPEN) {
                try {
                    ws.close();
                } catch (e) {
                }
            } else if (ws) {
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

        // Chat History functionality with pagination
        // 聊天历史状态管理
        let chatHistoryState = {
            messages: [],           // 当前显示的消息列表
            oldestMessageId: null,  // 最早消息的ID（用于分页）
            isLoading: false,       // 是否正在加载
            hasMore: true,          // 是否还有更多消息
            limit: 20,              // 每次加载数量
            isFirstLoad: true,      // 是否是首次加载
            lastLoadTime: 0         // 上次加载时间（用于防抖）
        };

        // Load chat history (initial load or load more)
        function loadChatHistory() {
            if (!selectedUserId || selectedUserId <= 0) {
                return;
            }
            
            // 重置状态
            chatHistoryState = {
                messages: [],
                oldestMessageId: null,
                isLoading: false,
                hasMore: true,
                limit: 20,
                isFirstLoad: true,
                lastLoadTime: 0
            };
            
            const container = document.getElementById('chat-messages-container');
            container.innerHTML = `
                <div class="loading">
                    <div class="loading-spinner"></div>
                    <div>加载聊天记录中...</div>
                </div>
            `;

            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('get_chat_history', {
                    limit: chatHistoryState.limit
                });
            } else {
                setTimeout(() => {
                    container.innerHTML = '<p style="text-align: center; color: var(--error-color);">WebSocket 未连接，无法加载聊天记录</p>';
                }, 1000);
            }
        }

        // Load more chat history (pagination)
        function loadMoreChatHistory() {
            if (!selectedUserId || selectedUserId <= 0 || chatHistoryState.isLoading || !chatHistoryState.hasMore) {
                return;
            }
            
            chatHistoryState.isLoading = true;
            
            // 显示加载指示器
            showLoadingMoreIndicator();
            
            // 获取当前显示的消息中最早的一条（用于查询更早的消息）
            const oldestMessage = chatHistoryState.messages.length > 0 
                ? chatHistoryState.messages[0] 
                : null;
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                const requestData = {
                    limit: chatHistoryState.limit
                };
                
                // 如果有最早的消息，使用它的时间戳来查询更早的消息
                if (oldestMessage && oldestMessage.id) {
                    requestData.beforeId = oldestMessage.id;
                }
                
                sendStandardMessage('get_chat_history', requestData);
            }
        }

        // Show loading more indicator
        function showLoadingMoreIndicator() {
            const container = document.getElementById('chat-messages-container');
            if (!container) return;
            
            // 在顶部添加加载指示器
            const indicator = document.createElement('div');
            indicator.id = 'loading-more-indicator';
            indicator.className = 'loading-more';
            indicator.innerHTML = `
                <div class="loading-spinner-small"></div>
                <span>加载更多...</span>
            `;
            indicator.style.cssText = `
                text-align: center;
                padding: 10px;
                color: rgba(255,255,255,0.6);
                font-size: 12px;
                display: flex;
                align-items: center;
                justify-content: center;
                gap: 8px;
            `;
            
            container.insertBefore(indicator, container.firstChild);
        }

        // Hide loading more indicator
        function hideLoadingMoreIndicator() {
            const indicator = document.getElementById('loading-more-indicator');
            if (indicator) {
                indicator.remove();
            }
        }

        // Handle chat history data (with pagination support)
        function handleChatHistory(chatHistoryData) {
            const container = document.getElementById('chat-messages-container');
            if (!container) return;
            
            // 处理新旧格式兼容
            let messages, hasMore, totalCount;
            
            if (Array.isArray(chatHistoryData)) {
                // 旧格式：直接是消息数组
                messages = chatHistoryData;
                hasMore = false;
                totalCount = messages.length;
            } else {
                // 新格式：包含分页信息的对象
                messages = chatHistoryData.messages || [];
                hasMore = chatHistoryData.hasMore || false;
                totalCount = chatHistoryData.totalCount || messages.length;
            }
            
            chatHistoryState.hasMore = hasMore;
            chatHistoryState.isLoading = false;
            hideLoadingMoreIndicator();

            // 过滤无效消息
            const validMessages = messages.filter((message, index) => {
                if (!message || typeof message !== 'object') {
                    console.warn(`聊天记录[${index}]格式无效:`, message);
                    return false;
                }
                if (!message.role) {
                    console.warn(`聊天记录[${index}]缺少role字段:`, message);
                    message.role = 'unknown';
                }
                return true;
            });

            if (chatHistoryState.isFirstLoad) {
                // 首次加载：清空容器并显示消息
                container.innerHTML = '';
                
                if (validMessages.length === 0) {
                    container.innerHTML = '<p style="text-align: center; color: rgba(255,255,255,0.5);">暂无聊天记录</p>';
                    chatHistoryState.isFirstLoad = false;
                    return;
                }
                
                // 添加消息到容器
                validMessages.forEach(message => {
                    addChatMessage(message, false); // false = 不自动滚动
                });
                
                // 滚动到底部
                container.scrollTop = container.scrollHeight;
                
                // 更新状态
                chatHistoryState.messages = [...validMessages];
                if (validMessages.length > 0) {
                    chatHistoryState.oldestMessageId = validMessages[0].id;
                }
                chatHistoryState.isFirstLoad = false;
                
            } else {
                // 加载更多：在顶部插入消息
                if (validMessages.length === 0) {
                    showToast('没有更多历史消息了', 'info');
                    return;
                }
                
                // 记录当前滚动位置
                const oldHeight = container.scrollHeight;
                const oldScrollTop = container.scrollTop;
                
                // 在顶部插入消息（保持时间顺序）
                validMessages.forEach((message, index) => {
                    const messageElement = createChatMessageElement(message);
                    container.insertBefore(messageElement, container.children[index] || container.firstChild);
                });
                
                // 调整滚动位置，保持用户视角不变
                const newHeight = container.scrollHeight;
                container.scrollTop = oldScrollTop + (newHeight - oldHeight);
                
                // 更新状态
                chatHistoryState.messages = [...validMessages, ...chatHistoryState.messages];
                if (validMessages.length > 0) {
                    chatHistoryState.oldestMessageId = validMessages[0].id;
                }
            }
            
            // 如果没有更多消息，显示提示
            if (!chatHistoryState.hasMore && !document.getElementById('no-more-messages')) {
                const noMoreIndicator = document.createElement('div');
                noMoreIndicator.id = 'no-more-messages';
                noMoreIndicator.style.cssText = `
                    text-align: center;
                    padding: 15px;
                    color: rgba(255,255,255,0.4);
                    font-size: 12px;
                    border-top: 1px solid rgba(255,255,255,0.1);
                    margin-top: 10px;
                `;
                noMoreIndicator.textContent = '—— 没有更多历史消息 ——';
                container.insertBefore(noMoreIndicator, container.firstChild);
            }
        }

        /**
         * 处理消息内容中的 [IMG:文件名] 标记，转换为图片标签
         */
        function processImageTags(content) {
            if (!content) return '';

            // 匹配 [IMG:文件名] 格式
            const imgRegex = /\[IMG:([^\]]+)\]/g;
            const key = getAccessKey();

            // 将内容分割成文本和图片标记
            const parts = [];
            let lastIndex = 0;
            let match;

            while ((match = imgRegex.exec(content)) !== null) {
                // 添加图片前的文本
                if (match.index > lastIndex) {
                    const text = content.substring(lastIndex, match.index);
                    if (text.trim()) {
                        parts.push({ type: 'text', content: text });
                    }
                }

                // 添加图片
                const fileName = match[1].trim();
                const imageUrl = `/api/proxy?action=get_image&filename=${encodeURIComponent(fileName)}&userId=${selectedUserId}&key=${key}`;
                parts.push({ type: 'image', src: imageUrl, fileName: fileName });

                lastIndex = match.index + match[0].length;
            }

            // 添加最后一段文本
            if (lastIndex < content.length) {
                const text = content.substring(lastIndex);
                if (text.trim()) {
                    parts.push({ type: 'text', content: text });
                }
            }

            // 如果没有匹配到任何 IMG 标记，直接返回转义后的文本
            if (parts.length === 0) {
                return escapeHtml(content);
            }

            // 构建 HTML
            let html = '';
            parts.forEach(part => {
                if (part.type === 'text') {
                    html += escapeHtml(part.content);
                } else if (part.type === 'image') {
                    html += `<img src="${part.src}" alt="${escapeHtml(part.fileName)}" class="chat-image" onerror="this.style.display='none'; this.nextElementSibling.style.display='block';" style="max-width: 100%; border-radius: 8px; margin: 4px 0;">
                    <div style="display:none; color: #ff6b6b; font-size: 12px;">[图片加载失败: ${escapeHtml(part.fileName)}]</div>`;
                }
            });

            return html;
        }

        // Create chat message element (without appending to container)
        function createChatMessageElement(message) {
            const messageElement = document.createElement('div');
            // 标准化角色名称：assistant 视为 ai，保持 plugin 角色不变
            let role = message.role;
            if (role === 'assistant') {
                role = 'ai';
            }
            // plugin 角色保持原样，用于区分插件主动发送的消息
            messageElement.className = `chat-message ${role}`;
            messageElement.dataset.messageId = message.id; // 保存消息ID用于分页

            let messageContent = '';
            const timestamp = message.timestamp || new Date().toLocaleString();

            // 处理文本内容（包含图片标记）
            if (message.content && message.content.trim()) {
                const processedContent = processImageTags(message.content);
                messageContent = `
                    <div class="chat-bubble">${processedContent}</div>
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
            return messageElement;
        }

        // Add single chat message
        // autoScroll: 是否自动滚动到底部（默认true）
        function addChatMessage(message, autoScroll = true) {
            const container = document.getElementById('chat-messages-container');
            if (!container) return;

            // 如果是新消息（不是历史消息），需要更新状态
            if (chatHistoryState && !chatHistoryState.isFirstLoad) {
                chatHistoryState.messages.push(message);
            }

            const messageElement = createChatMessageElement(message);
            container.appendChild(messageElement);

            // 自动滚动到底部
            if (autoScroll) {
                container.scrollTop = container.scrollHeight;
            }
        }

        // Update WebSocket message handler to include chat history
        function handleWebSocketMessage(message) {
            // 统一处理标准消息格式
            const messageType = message.type;
            const messageData = message.data;
            const replyTo = message.replyTo;
            
            // 处理带 replyTo 的响应消息
            let callbackHandled = false;
            if (replyTo && pendingRequests.has(replyTo)) {
                const requestInfo = pendingRequests.get(replyTo);
                clearTimeout(requestInfo.timeoutId);
                pendingRequests.delete(replyTo);

                // 调用回调函数
                if (requestInfo.callback && typeof requestInfo.callback === 'function') {
                    requestInfo.callback(null, messageData);
                    callbackHandled = true;
                    // 对于 init 消息，继续处理以更新 UI
                    if (messageType !== 'init') {
                        return; // 回调处理完成，不需要继续 switch 处理
                    }
                }
            }
            
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
                                updateVectorDBUserDisplay();
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
                case 'config_update_error':
                    showToast('配置保存失败: ' + (messageData.message || '未知错误'), 'error');
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
                    showToast(`${messageData}`, 'success');
                    break;
                case 'role_card_error':
                    showToast(messageData, 'error');
                    break;
                // vector_entries 现在通过回调处理，不再通过全局消息处理
                // case 'vector_entries':
                //     displayVectorEntries(messageData);
                //     break;
                case 'vector_search_results':
                    displayVectorSearchResults(messageData);
                    break;
                case 'vector_entry_deleted':
                    showToast('向量条目已删除', 'success');
                    loadVectorEntries();
                    break;
                case 'vectors_cleared':
                    showToast('向量数据库已清空', 'success');
                    loadVectorEntries();
                    break;
                case 'vector_entries_updated':
                    loadVectorEntries();
                    break;
                // Local embedding model messages
                case 'local_embedding_init':
                    // 初始化响应，不需要特殊处理，只是确认收到
                    break;
                case 'local_embedding_status':
                    // 状态检查响应，不需要特殊处理
                    break;
                case 'local_embedding_download_progress':
                    updateLocalModelProgress(messageData);
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
                case 'queue_status':
                    updateQueueStatus(messageData);
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
                case 'system_info':
                    if (typeof handleSystemInfo === 'function') {
                        handleSystemInfo(messageData);
                    }
                    break;
                case 'changelog':
                    if (typeof handleChangelog === 'function') {
                        handleChangelog(messageData);
                    }
                    break;
                case 'protocol_connected':
                    protocolConnected = true;
                    // 清空扫描结果并重置扫描请求标志
                    foundServices.clear();
                    scanRequested = false; // 重置，允许下次重新扫描
                    const resultsContainer = document.getElementById('scan-results');
                    if (resultsContainer) {
                        resultsContainer.innerHTML = '';
                    }
                    hideProtocolConnectionScreen();
                    showSwitchProtocolButton();
                    showToast('协议端连接成功', 'success');
                    // 连接成功后，重新加载所有数据
                    reloadAllData();
                    break;
                case 'protocol_disconnected':
                    handleProtocolDisconnected();
                    break;
                case 'protocol_connection_error':
                    showProtocolConnectionError(messageData?.message || '协议端连接失败');
                    break;
                case 'service_found':
                    console.log('Received service_found:', messageData);
                    handleServiceFound(messageData);
                    break;
                case 'scan_state_changed':
                    handleScanStateChanged(messageData);
                    break;
                case 'scan_started':
                    // 扫描已开始
                    break;
                case 'scan_stopped':
                    // 扫描已停止
                    break;
                case 'scan_error':
                    showToast(messageData?.message || '扫描失败', 'error');
                    break;
                case 'protocol_status_changed':
                    // 协议端连接状态变化（用于处理页面刷新后的状态同步）
                    console.log('Protocol status changed:', messageData);
                    if (messageData && messageData.initialConnectionAttempted && !initialDataRequested) {
                        // 如果初始连接已完成且还没请求过初始数据，立即请求
                        requestInitialData();
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
                updateVectorDBUserDisplay();
                // 聊天记录不会自动返回，需要用户进入聊天记录页面时单独请求
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
            
            if (confirm(`确定要清空用户 ${userId} 的上下文和向量数据库吗？此操作不可恢复！`)) {
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

        // ========== Vector Database Management Functions ==========
        
        function updateVectorDBUserDisplay() {
            const noUserDiv = document.getElementById('vector-db-no-user');
            const contentDiv = document.getElementById('vector-db-content');
            const badge = document.getElementById('vector-db-user-badge');
            
            if (selectedUserId && selectedUserId > 0) {
                if (noUserDiv) noUserDiv.style.display = 'none';
                if (contentDiv) contentDiv.style.display = 'block';
                if (badge) badge.textContent = `当前用户: ${selectedUserId}`;
                loadVectorEntries();
            } else {
                if (noUserDiv) noUserDiv.style.display = 'block';
                if (contentDiv) contentDiv.style.display = 'none';
                if (badge) badge.textContent = '请先选择用户';
            }
        }

        // 向量数据库总条目数（从后端获取）
        let vectorDbTotalCount = 0;

        function loadVectorEntries() {
            const container = document.getElementById('vector-entries-container');
            if (!container) return;
            
            container.innerHTML = `
                <div class="loading">
                    <div class="loading-spinner"></div>
                    <div>加载向量数据中...</div>
                </div>
            `;
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                // 传递分页参数，使用回调处理响应
                sendStandardMessage('get_vector_entries', { 
                    userId: selectedUserId,
                    page: vectorDbCurrentPage,
                    pageSize: vectorDbPageSize
                }, (error, responseData) => {
                    if (error) {
                        console.error('Error loading vector entries:', error);
                        container.innerHTML = `
                            <div class="empty-state">
                                <div class="empty-state-icon">❌</div>
                                <div class="empty-state-text">加载失败</div>
                                <div class="empty-state-hint">${error.message}</div>
                            </div>
                        `;
                        return;
                    }
                    // 直接处理返回的数据
                    if (responseData) {
                        displayVectorEntries(responseData);
                    }
                });
            }
        }

        function displayVectorEntries(data) {
            // 处理新的分页数据格式
            if (data && typeof data === 'object') {
                vectorDbAllEntries = data.entries || [];
                vectorDbTotalCount = data.totalCount || 0;
                // 如果后端返回了页码，使用后端返回的
                if (data.page) {
                    vectorDbCurrentPage = data.page;
                }
            } else {
                // 兼容旧格式（直接是数组）
                vectorDbAllEntries = data || [];
                vectorDbTotalCount = vectorDbAllEntries.length;
            }
            renderCurrentPage();
        }

        function renderCurrentPage() {
            const container = document.getElementById('vector-entries-container');
            const countElement = document.getElementById('vector-count');
            const paginationContainer = document.getElementById('pagination-container');

            if (!container) return;

            if (countElement) {
                countElement.textContent = vectorDbTotalCount;
            }

            if (!vectorDbAllEntries || vectorDbAllEntries.length === 0) {
                container.innerHTML = `
                    <div class="empty-state">
                        <div class="empty-state-icon">📭</div>
                        <div class="empty-state-text">暂无向量数据</div>
                        <div class="empty-state-hint">该用户还没有存储任何向量数据</div>
                    </div>
                `;
                if (paginationContainer) paginationContainer.style.display = 'none';
                return;
            }

            // 使用后端返回的总数计算总页数
            const totalPages = Math.ceil(vectorDbTotalCount / vectorDbPageSize);
            console.log('[VECTOR_DB] Total entries:', vectorDbTotalCount, 'Page size:', vectorDbPageSize, 'Total pages:', totalPages, 'Current page:', vectorDbCurrentPage);
            
            // 后端已经分页，直接使用返回的数据
            const currentPageEntries = vectorDbAllEntries;

            container.innerHTML = '';

            currentPageEntries.forEach((entry, index) => {
                if (!entry) {
                    console.warn('[VECTOR_DB] Skipping invalid entry at index:', index);
                    return;
                }

                const entryElement = document.createElement('div');
                entryElement.className = 'vector-entry-card';

                const entryId = (entry && (entry.id || entry.Id)) || '';
                const entryRole = (entry && (entry.role || entry.Role)) || 'unknown';
                const entryContent = (entry && (entry.content || entry.Content)) || '';
                const entryTimestamp = (entry && (entry.timestamp || entry.Timestamp)) || Date.now();

                let idDisplay = 'N/A';
                if (entryId && typeof entryId === 'string' && entryId.length > 0) {
                    idDisplay = entryId.length > 8 ? entryId.substring(0, 8) + '...' : entryId;
                }

                let deleteButton = '';
                if (entryId && typeof entryId === 'string' && entryId.length > 0) {
                    deleteButton = `<button class="btn btn-sm btn-danger" onclick="deleteVectorEntry('${entryId.replace(/'/g, "\\'")}')">删除</button>`;
                }

                let timeDisplay = '';
                try {
                    timeDisplay = new Date(entryTimestamp).toLocaleString();
                } catch (e) {
                    timeDisplay = 'Invalid Date';
                }

                entryElement.innerHTML = `
                    <div class="vector-entry-header">
                        <span class="vector-entry-role ${entryRole}">${escapeHtml(entryRole)}</span>
                        <span class="vector-entry-time">${timeDisplay}</span>
                    </div>
                    <div class="vector-entry-content">${escapeHtml(entryContent)}</div>
                    <div class="vector-entry-footer">
                        <span class="vector-entry-id">ID: ${idDisplay}</span>
                        ${deleteButton}
                    </div>
                `;
                container.appendChild(entryElement);
            });

            updatePagination(totalPages);
        }

        function updatePagination(totalPages) {
            const paginationContainer = document.getElementById('pagination-container');
            const paginationInfo = document.getElementById('pagination-info');
            const prevBtn = document.getElementById('prev-page-btn');
            const nextBtn = document.getElementById('next-page-btn');

            console.log('[VECTOR_DB] updatePagination called, totalPages:', totalPages);

            if (!paginationContainer) {
                console.log('[VECTOR_DB] paginationContainer not found');
                return;
            }

            if (totalPages <= 1) {
                console.log('[VECTOR_DB] Hiding pagination, totalPages <= 1');
                paginationContainer.style.display = 'none';
                return;
            }
            
            console.log('[VECTOR_DB] Showing pagination');
            paginationContainer.style.display = 'block';
            
            if (paginationInfo) {
                paginationInfo.textContent = `第 ${vectorDbCurrentPage} 页 / 共 ${totalPages} 页`;
            }
            
            if (prevBtn) {
                prevBtn.disabled = vectorDbCurrentPage <= 1;
                prevBtn.style.opacity = vectorDbCurrentPage <= 1 ? '0.5' : '1';
                prevBtn.style.cursor = vectorDbCurrentPage <= 1 ? 'not-allowed' : 'pointer';
            }
            
            if (nextBtn) {
                const shouldDisable = vectorDbCurrentPage >= totalPages;
                console.log('[VECTOR_DB] Next button - currentPage:', vectorDbCurrentPage, 'totalPages:', totalPages, 'disabled:', shouldDisable);
                nextBtn.disabled = shouldDisable;
                nextBtn.style.opacity = shouldDisable ? '0.5' : '1';
                nextBtn.style.cursor = shouldDisable ? 'not-allowed' : 'pointer';
            }
        }

        function goToPage(page) {
            const totalPages = Math.ceil(vectorDbTotalCount / vectorDbPageSize);
            if (page < 1 || page > totalPages) return;
            
            vectorDbCurrentPage = page;
            // 重新从后端加载数据
            loadVectorEntries();
        }

        function updateThresholdDisplay() {
            const thresholdInput = document.getElementById('similarity-threshold');
            const thresholdDisplay = document.getElementById('threshold-display');
            if (thresholdInput && thresholdDisplay) {
                const value = parseFloat(thresholdInput.value);
                thresholdDisplay.textContent = value.toFixed(2);
            }
        }

        function searchVectors() {
            const queryInput = document.getElementById('vector-search-input');
            const query = queryInput ? queryInput.value.trim() : '';
            const threshold = vectorDbSettings.similarityThreshold;
            const topK = vectorDbSettings.topK || 10;
            
            console.log('[VECTOR_SEARCH] Searching for:', query, 'Threshold:', threshold, 'TopK:', topK, 'User ID:', selectedUserId);
            
            if (!query) {
                loadVectorEntries();
                return;
            }
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('search_vectors', { query: query, topK: topK, threshold: threshold, userId: selectedUserId });
            } else {
                console.warn('[VECTOR_SEARCH] WebSocket not connected');
            }
        }

        function displayVectorSearchResults(results) {
            console.log('[VECTOR_SEARCH] Received results:', results);
            vectorDbAllEntries = results || [];
            vectorDbCurrentPage = 1;
            renderCurrentPage();
        }

        function deleteVectorEntry(id) {
            if (!confirm('确定要删除这条向量数据吗？')) {
                return;
            }
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('delete_vector_entry', { id: id, userId: selectedUserId });
            }
        }

        function showClearVectorsModal() {
            const modal = document.getElementById('clear-vectors-modal');
            if (modal) {
                modal.style.display = 'flex';
            }
        }

        function closeClearVectorsModal() {
            const modal = document.getElementById('clear-vectors-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }

        function confirmClearVectors() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('clear_vectors', { userId: selectedUserId });
            }
            closeClearVectorsModal();
        }

        function showRegenerateVectorsModal() {
            const modal = document.getElementById('regenerate-vectors-modal');
            if (modal) {
                modal.style.display = 'flex';
            }
        }

        function closeRegenerateVectorsModal() {
            const modal = document.getElementById('regenerate-vectors-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }

        function confirmRegenerateVectors() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('regenerate_vectors', { userId: selectedUserId });
            }
            closeRegenerateVectorsModal();
        }

        let vectorDbSettings = {
            similarityThreshold: 0.2,
            topK: 10
        };

        function loadVectorDbSettings() {
            const saved = localStorage.getItem('vectorDbSettings');
            if (saved) {
                try {
                    const parsed = JSON.parse(saved);
                    vectorDbSettings = {
                        similarityThreshold: parsed.similarityThreshold !== undefined ? parsed.similarityThreshold : 0.2,
                        topK: parsed.topK !== undefined ? parsed.topK : 10
                    };
                } catch (e) {
                    console.error('[VECTOR_DB] Failed to load settings:', e);
                    vectorDbSettings = { similarityThreshold: 0.2, topK: 10 };
                }
            } else if (config) {
                vectorDbSettings = {
                    similarityThreshold: config.vectorDbSimilarityThreshold !== undefined ? config.vectorDbSimilarityThreshold : 0.2,
                    topK: config.vectorDbTopK !== undefined ? config.vectorDbTopK : 10
                };
            } else {
                vectorDbSettings = { similarityThreshold: 0.2, topK: 10 };
            }
            applyVectorDbSettings();
        }

        function applyVectorDbSettings() {
            const thresholdInput = document.getElementById('similarity-threshold');
            const thresholdDisplay = document.getElementById('threshold-display');
            const topKInput = document.getElementById('vector-db-topk');

            if (thresholdInput && thresholdDisplay) {
                thresholdInput.value = vectorDbSettings.similarityThreshold || 0.2;
                thresholdDisplay.textContent = parseFloat(vectorDbSettings.similarityThreshold || 0.2).toFixed(2);
            }
            if (topKInput) {
                topKInput.value = vectorDbSettings.topK || 10;
            }
        }

        function showVectorDbSettingsModal() {
            loadVectorDbSettings();
            const modal = document.getElementById('vector-db-settings-modal');
            if (modal) {
                modal.style.display = 'flex';
            }
        }

        function closeVectorDbSettingsModal() {
            const modal = document.getElementById('vector-db-settings-modal');
            if (modal) {
                modal.style.display = 'none';
            }
        }

        function saveVectorDbSettings() {
            const thresholdInput = document.getElementById('similarity-threshold');
            const topKInput = document.getElementById('vector-db-topk');

            if (thresholdInput) {
                vectorDbSettings.similarityThreshold = parseFloat(thresholdInput.value);
            }
            if (topKInput) {
                let topK = parseInt(topKInput.value);
                if (topK < 1) topK = 1;
                if (topK > 50) topK = 50;
                vectorDbSettings.topK = topK;
            }

            localStorage.setItem('vectorDbSettings', JSON.stringify(vectorDbSettings));
            
            applyVectorDbSettings();

            closeVectorDbSettingsModal();
            console.log('[VECTOR_DB] Settings saved:', vectorDbSettings);
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendStandardMessage('save_vector_db_settings', {
                    similarityThreshold: vectorDbSettings.similarityThreshold,
                    topK: vectorDbSettings.topK
                });
                showToast('向量数据库设置已保存', 'success');
            }
        }

        function escapeHtml(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }
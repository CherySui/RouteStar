document.addEventListener('DOMContentLoaded', () => {
    const addAppBtn = document.getElementById('add-app-btn');
    const homeEmpty = document.getElementById('home-empty');
    const homeApp = document.getElementById('home-app');
    const homeAppTitle = document.getElementById('home-app-title');
    const homeDesc = document.getElementById('home-app-desc');
    const homeBgVideo = document.getElementById('home-bg-video');
    const globalBgImage = document.getElementById('global-bg-image');
    const launchAppBtn = document.getElementById('launch-app-btn');

    // 启动器默认背景
    const DEFAULT_BG = 'http://launcher.local/img/First_menu_bg.png';
    function showDefaultBg() {
        if (homeBgVideo) { homeBgVideo.style.display = 'none'; homeBgVideo.src = ''; }
        if (globalBgImage) globalBgImage.style.backgroundImage = `url("${DEFAULT_BG}")`;
    }

    // Tools page nodes
    const mainLogo = document.getElementById('main-logo');
    const toolsPage = document.getElementById('tools-page');
    const sortableAppList = document.getElementById('sortable-app-list');
    let isToolsPageOpen = false;

    function closeToolsPage() {
        isToolsPageOpen = false;
        if (toolsPage) toolsPage.style.display = 'none';

        if (currentAppPath && (appOrder.includes(currentAppPath) || hoyoOrder.includes(currentAppPath))) {
            if (homeApp) homeApp.style.display = 'flex';
        } else {
            if (homeEmpty) homeEmpty.style.display = 'flex';
        }
    }

    function toggleToolsPage() {
        isToolsPageOpen = !isToolsPageOpen;
        if (isToolsPageOpen) {
            if (toolsPage) toolsPage.style.display = 'flex';
            if (homeApp) homeApp.style.display = 'none';
            if (homeEmpty) homeEmpty.style.display = 'none';
            renderSortableList();
            prepareTimeTrackingDropdown();

            // 如果当前没有激活的 tab，自动跳转到抽卡记录页
            const activeSidebar = toolsPage.querySelector('.sidebar-item.active');
            if (!activeSidebar) {
                const firstItem = toolsPage.querySelector('.sidebar-item[data-tab="tool-hoyo-gacha"]');
                if (firstItem) firstItem.click();
            }
        } else {
            closeToolsPage();
        }
    }

    // Modal nodes
    const settingsBtn = document.getElementById('app-settings-btn');
    const settingsModal = document.getElementById('settings-modal');
    const closeModalBtn = document.getElementById('close-modal-btn');
    const saveSettingsBtn = document.getElementById('save-settings-btn');
    const clearBgBtn = document.getElementById('clear-bg-btn');

    // Form nodes
    const inputName = document.getElementById('setting-app-name');
    const inputDesc = document.getElementById('setting-app-desc');
    const btnPickBgImg = document.getElementById('btn-pick-bg-img');
    const btnPickBgVid = document.getElementById('btn-pick-bg-vid');
    const inputUseVideo = document.getElementById('setting-use-video');

    let appRegistry = {}; // path -> {name, desc, bgImg, bgVid, useVideo, base64Icon}
    let appOrder = []; // 按顺序存储应用的 path
    let hoyoOrder = []; // 用户已添加的米游游戏列表（初始为空，按需添加）
    let currentAppPath = "";
    let gamePaths = { genshin: '', starrail: '', zzz: '', hyy: '', h3: '' };

    // 预定义米游配置 (图标指向本地 icon/ 目录)
    const HOYO_PRESETS = {
        'Hoyo_Genshin': { name: '原神', desc: '在七种元素交汇的大陆——「提瓦特」，每个人都可以成为神。', icon: 'http://launcher.local/icon/gi.png', bgImg: 'http://launcher.local/icon/gi.png' },
        'Hoyo_StarRail': { name: '崩坏：星穹铁道', desc: '愿此行，终抵群星。', icon: 'http://launcher.local/icon/hsr.png', bgImg: 'http://launcher.local/icon/hsr.png' },
        'Hoyo_ZZZ': { name: '绝区零', desc: '新艾利都，欢迎你的到来。', icon: 'http://launcher.local/icon/zzz.png', bgImg: 'http://launcher.local/icon/zzz.png' },
        'Hoyo_HYY': { name: '崩坏：因缘精灵', desc: '新的旅程，等待开启。', icon: 'http://launcher.local/icon/hyy.png', bgImg: 'http://launcher.local/icon/hyy.png' },
        'Hoyo_H3': { name: '崩坏3', desc: '终将到来的律者，终将落幕的终焉。', icon: 'http://launcher.local/icon/h3.png', bgImg: 'http://launcher.local/icon/h3.png' }
    };

    // 初始化/同步 hoyoOrder 中已有游戏的 appRegistry 条目
    // 注意：只同步已在 hoyoOrder 中的游戏，不自动添加新游戏
    function initHoyoApps() {
        hoyoOrder.forEach(id => {
            if (!appRegistry[id] && HOYO_PRESETS[id]) {
                const p = HOYO_PRESETS[id];
                appRegistry[id] = {
                    name: p.name,
                    desc: p.desc,
                    localIcon: p.icon,
                    bgImg: p.bgImg || '',
                    isHoyo: true,
                    processName: getHoyoProcessName(id),
                    usageSeconds: 0
                };
            } else if (appRegistry[id] && HOYO_PRESETS[id]) {
                // 已有条目：如果用户未修改过 bgImg（为空），自动补充预设背景
                if (!appRegistry[id].bgImg && HOYO_PRESETS[id].bgImg) {
                    appRegistry[id].bgImg = HOYO_PRESETS[id].bgImg;
                }
                // 自动补齐缺失的进程名
                if (!appRegistry[id].processName) {
                    appRegistry[id].processName = getHoyoProcessName(id);
                }
            }
        });
    }

    // 添加一个米游游戏到用户的列表
    function addHoyoGame(id) {
        if (hoyoOrder.includes(id)) return; // 已存在
        const p = HOYO_PRESETS[id];
        if (!p) return;
        hoyoOrder.push(id);
        if (!appRegistry[id]) {
            appRegistry[id] = {
                name: p.name,
                desc: p.desc,
                localIcon: p.icon,
                bgImg: p.bgImg || '',  // 使用预设背景（图标充当默认背景）
                isHoyo: true,
                processName: getHoyoProcessName(id),
                usageSeconds: 0
            };
        }
        saveConfig();
        renderAppListTopBar();
        // 自动切换到刚添加的游戏
        currentAppPath = id;
        renderAppPage(id);
    }

    // 米游游戏选择下拉面板
    function closeAddHoyoPanel() {
        const modal = document.getElementById('add-hoyo-modal');
        const backdrop = document.getElementById('add-hoyo-backdrop');
        if (modal) modal.style.display = 'none';
        if (backdrop) backdrop.style.display = 'none';
    }

    function showAddHoyoGameModal() {
        const allAdded = Object.keys(HOYO_PRESETS).every(id => hoyoOrder.includes(id));
        if (allAdded) return; // 全部已添加，+按鈕没动静

        const modal = document.getElementById('add-hoyo-modal');
        const backdrop = document.getElementById('add-hoyo-backdrop');
        if (!modal) return;

        // 刷新卡片状态（适配新类名 .hoyo-drop-card）
        Object.keys(HOYO_PRESETS).forEach(id => {
            const card = modal.querySelector(`[data-game-id="${id}"]`);
            if (!card) return;
            const isAdded = hoyoOrder.includes(id);
            card.classList.toggle('game-card-added', isAdded);
            const btn = card.querySelector('.game-card-add-btn');
            if (btn) {
                btn.textContent = isAdded ? '已添加' : '添加';
                btn.disabled = isAdded;
            }
        });

        if (backdrop) backdrop.style.display = 'block';
        modal.style.display = 'block';
    }

    function parseLocalPath(url) {
        if (!url) return "未设置";
        // Convert virtual drive URL back to real local path for display
        const match = url.match(/^http:\/\/drive-([a-z])\.local\/(.*)$/i);
        if (match) {
            return `${match[1].toUpperCase()}:/${decodeURIComponent(match[2])}`;
        }
        return url;
    }

    function formatUsageTime(seconds) {
        if (!seconds) return '0 h 0 min 0 s';
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = seconds % 60;
        return `${h} h ${m} min ${s} s`;
    }

    function saveConfig() {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({
                type: 'save_config',
                data: { apps: appRegistry, order: appOrder, hoyoOrder: hoyoOrder, gamePaths: gamePaths }
            });
        }
    }

    // 重绘顶栏的图标列表
    // 米哈游官方游戏识别列表 (根据 gameBiz 或特定标识)
    const HOYO_APP_IDS = ['hk4e', 'hkrpg', 'nap', 'hkrpg_cn', 'hk4e_cn', 'nap_cn', 'hkrpg_global', 'hk4e_global', 'nap_global'];

    function renderAppListTopBar() {
        const hoyoContainer = document.getElementById('hoyo-app-list');
        const customContainer = document.getElementById('custom-app-list');
        if (!hoyoContainer || !customContainer) return;

        // 清空现有项
        hoyoContainer.innerHTML = '';
        customContainer.querySelectorAll('.app-item').forEach(el => el.remove());

        // 1. 渲染米哈游官方游戏
        hoyoOrder.forEach(id => {
            const cfg = appRegistry[id];
            if (!cfg) return;
            const newApp = document.createElement('div');
            newApp.className = 'app-item';
            newApp.addEventListener('click', () => {
                closeToolsPage();
                currentAppPath = id;
                renderAppPage(id);
            });
            // 如果是预设，使用 localIcon；如果是动态添加，使用 base64
            const iconSrc = cfg.localIcon ? cfg.localIcon : `data:image/png;base64,${cfg.base64Icon}`;
            newApp.innerHTML = `<img src="${iconSrc}" class="slot-icon">`;
            hoyoContainer.appendChild(newApp);
        });

        // 2. 渲染自添加应用
        appOrder.forEach(path => {
            const cfg = appRegistry[path];
            if (!cfg || cfg.isHoyo) return; // 排除官方游戏，防止重复

            const newApp = document.createElement('div');
            newApp.className = 'app-item';
            newApp.addEventListener('click', () => {
                closeToolsPage();
                currentAppPath = path;
                renderAppPage(path);
            });
            newApp.innerHTML = `<img src="data:image/png;base64,${cfg.base64Icon}" class="slot-icon">`;
            customContainer.insertBefore(newApp, addAppBtn);
        });

        // 3. 无内容时折叠 hoyo 列表容器，避免 flex gap 产生多余空白
        hoyoContainer.style.display = hoyoOrder.length === 0 ? 'none' : '';
    }

    function renderAppPage(path, skipTransition = false) {
        if (!appRegistry[path]) return;
        const config = appRegistry[path];

        // 触发黑幕过渡动画 (仅当不跳过时)
        if (!skipTransition) {
            const curtain = document.getElementById('transition-curtain');
            if (curtain) {
                curtain.classList.remove('curtain-fade');
                void curtain.offsetWidth; // 触发重绘
                curtain.classList.add('curtain-fade');
            }
        }

        homeEmpty.style.display = 'none';
        homeApp.style.display = 'block';

        homeAppTitle.textContent = config.name;
        homeDesc.textContent = config.desc;

        if (config.useVideo && config.bgVid) {
            homeBgVideo.src = config.bgVid;
            homeBgVideo.style.display = 'block';
            globalBgImage.style.backgroundImage = 'none';
        } else if (config.bgImg) {
            homeBgVideo.style.display = 'none';
            homeBgVideo.src = '';
            globalBgImage.style.backgroundImage = `url("${config.bgImg}")`;
        } else {
            homeBgVideo.style.display = 'none';
            homeBgVideo.src = '';
            globalBgImage.style.backgroundImage = 'none';
        }

        const uTimeSpan = document.getElementById('home-usage-time');
        if (uTimeSpan) {
            uTimeSpan.textContent = formatUsageTime(config.usageSeconds || 0);
        }

        // 米游游戏：根据是否有游戏路径切换按钮文字
        if (config.isHoyo) {
            const hasPath = getHoyoGamePath(path);
            launchAppBtn.textContent = hasPath ? '开始游戏' : '安装游戏';
        } else {
            launchAppBtn.textContent = '启动';
        }

        renderTodos(path);
    }

    // 各米游游戏的 exe 文件名与进程名对照表
    const HOYO_EXE_INFO = {
        'Hoyo_Genshin': { exe: 'GenshinImpact.exe', process: 'GenshinImpact' },
        'Hoyo_StarRail': { exe: 'StarRail.exe', process: 'StarRail' },
        'Hoyo_ZZZ': { exe: 'ZenlessZoneZero.exe', process: 'ZenlessZoneZero' },
        'Hoyo_HYY': { exe: 'YinYuan.exe', process: 'YinYuan' },
        'Hoyo_H3': { exe: 'BH3.exe', process: 'BH3' }
    };

    // 根据 Hoyo 预设 ID 查找对应游戏文件夹路径
    function getHoyoFolderPath(id) {
        if (id === 'Hoyo_Genshin') return gamePaths.genshin || '';
        if (id === 'Hoyo_StarRail') return gamePaths.starrail || '';
        if (id === 'Hoyo_ZZZ') return gamePaths.zzz || '';
        if (id === 'Hoyo_HYY') return gamePaths.hyy || '';
        if (id === 'Hoyo_H3') return gamePaths.h3 || '';
        return '';
    }

    // 根据 Hoyo 预设 ID 返回完整的游戏 .exe 路径（文件夹 + exe 名）
    // C# 的 launch_app 要求 path 必须是可访问的文件路径
    function getHoyoGamePath(id) {
        const folder = getHoyoFolderPath(id);
        if (!folder) return '';
        const info = HOYO_EXE_INFO[id];
        if (!info) return folder; // 未知游戏退化为文件夹
        // 兼容 Windows 路径分隔符
        const sep = folder.endsWith('\\') || folder.endsWith('/') ? '' : '\\';
        return folder + sep + info.exe;
    }

    // 获取游戏的进程名（用于时长统计）
    function getHoyoProcessName(id) {
        const info = HOYO_EXE_INFO[id];
        return info ? info.process : '';
    }

    function renderTodos(path) {
        const todoList = document.getElementById('todo-list');
        if (!todoList) return;
        todoList.innerHTML = '';
        const config = appRegistry[path];
        if (!config) return;
        const todos = config.todos || [];

        if (todos.length === 0) {
            todoList.innerHTML = '<p class="empty-state">暂无待办</p>';
            return;
        }

        todos.forEach((todoText, index) => {
            const item = document.createElement('div');
            item.className = 'todo-item';

            const spanText = document.createElement('span');
            spanText.className = 'todo-text';
            spanText.textContent = todoText;
            spanText.title = todoText;

            const spanCheck = document.createElement('span');
            spanCheck.className = 'material-symbols-outlined todo-check';
            spanCheck.textContent = 'check';
            spanCheck.setAttribute('data-idx', index);

            item.appendChild(spanText);
            item.appendChild(spanCheck);
            todoList.appendChild(item);
        });

        todoList.querySelectorAll('.todo-check').forEach(btn => {
            btn.addEventListener('click', function () {
                const idx = parseInt(this.getAttribute('data-idx'));
                todos.splice(idx, 1);
                config.todos = todos;
                saveConfig();
                renderTodos(path);
            });
        });
    }

    const todoInput = document.getElementById('todo-input-field');
    const todoAddBtn = document.getElementById('btn-add-todo');

    if (todoAddBtn && todoInput) {
        todoAddBtn.addEventListener('click', () => {
            if (!currentAppPath || !appRegistry[currentAppPath]) return;
            const val = todoInput.value.trim();
            if (!val) return;

            const config = appRegistry[currentAppPath];
            if (!config.todos) config.todos = [];
            config.todos.push(val);
            saveConfig();

            todoInput.value = '';
            renderTodos(currentAppPath);
        });

        todoInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                todoAddBtn.click();
            }
        });
    }

    if (addAppBtn) {
        addAppBtn.addEventListener('click', () => {
            closeToolsPage();
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({ type: 'open_file_picker' });
            }
        });
    }

    const addHoyoBtn = document.getElementById('add-hoyo-game-btn');
    if (addHoyoBtn) {
        addHoyoBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const modal = document.getElementById('add-hoyo-modal');
            if (modal && modal.style.display !== 'none') {
                closeAddHoyoPanel(); // 已开则关闭（toggle）
            } else {
                showAddHoyoGameModal();
            }
        });
    }

    // 点击透明幕关闭面板
    const hoyoBackdrop = document.getElementById('add-hoyo-backdrop');
    if (hoyoBackdrop) {
        hoyoBackdrop.addEventListener('click', closeAddHoyoPanel);
    }

    // 鼠标滚轮 → 水平惯性缓动滚动（指数 ease-out）
    const hoyoCards = document.querySelector('.hoyo-dropdown-cards');
    if (hoyoCards) {
        let targetX = 0;
        let rafId = null;

        const animateScroll = () => {
            const diff = targetX - hoyoCards.scrollLeft;
            if (Math.abs(diff) < 0.5) {
                hoyoCards.scrollLeft = targetX;
                rafId = null;
                return;
            }
            // 每帧向目标靠近 16%，形成自然减速
            hoyoCards.scrollLeft += diff * 0.16;
            rafId = requestAnimationFrame(animateScroll);
        };

        hoyoCards.addEventListener('wheel', (e) => {
            if (e.deltaY === 0) return;
            e.preventDefault();
            // 累积目标位置，并钳位到合法范围
            const maxScroll = hoyoCards.scrollWidth - hoyoCards.clientWidth;
            targetX = Math.max(0, Math.min(maxScroll, targetX + e.deltaY * 0.9));
            // 只在没有动画进行时才启动新的 rAF 循环
            if (!rafId) rafId = requestAnimationFrame(animateScroll);
        }, { passive: false });
    }

    // 游戏卡片点击添加
    document.querySelectorAll('.game-card-add-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const id = btn.closest('[data-game-id]')?.getAttribute('data-game-id');
            if (!id) return;
            addHoyoGame(id);
            closeAddHoyoPanel();
        });
    });

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (event) => {
            const data = event.data;
            if (!data) return;

            if (data.type === 'load_config') {
                const loaded = data.data;
                if (!loaded) {
                    // 全新启动，hoyoOrder 保持空，不预填充
                    renderAppListTopBar();
                    showDefaultBg();
                    return;
                }

                if (loaded.apps) appRegistry = loaded.apps;
                if (loaded.order) appOrder = loaded.order;
                // 兼容旧配置：如果存在已保存的 hoyoOrder 就用它，否则保持空
                if (loaded.hoyoOrder) hoyoOrder = loaded.hoyoOrder;
                if (loaded.gamePaths) {
                    gamePaths = loaded.gamePaths;
                    updateGamePathUI();
                }

                // 同步已添加游戏的 appRegistry 条目
                initHoyoApps();

                renderAppListTopBar();

                // 如果有已添加的游戏，默认选中第一个；否则显示空页+默认背景
                if (!currentAppPath) {
                    if (hoyoOrder.length > 0) {
                        currentAppPath = hoyoOrder[0];
                        renderAppPage(currentAppPath);
                    } else if (appOrder.length > 0) {
                        currentAppPath = appOrder[0];
                        renderAppPage(currentAppPath);
                    } else {
                        showDefaultBg();
                    }
                }
            }
            else if (data.type === 'folder_selected') {
                const targetId = data.tag;
                if (!targetId) return;

                // 处理 Hoyo 路径 Modal 的回传 (tag 以 'hoyo:' 开头)
                if (targetId.startsWith('hoyo:')) {
                    const pathKey = targetId.replace('hoyo:', '');
                    gamePaths[pathKey] = data.path;
                    saveConfig();
                    // 如果 modal 还开着，更新显示
                    const display = document.getElementById('hoyo-path-display');
                    if (display) {
                        display.textContent = data.path;
                        display.title = data.path;
                    }
                    return;
                }

                // 偏好设置页面的文本框回传
                const input = document.getElementById(targetId);
                if (input) {
                    input.value = data.path;
                    if (targetId === 'path-genshin') gamePaths.genshin = data.path;
                    if (targetId === 'path-starrail') gamePaths.starrail = data.path;
                    if (targetId === 'path-zzz') gamePaths.zzz = data.path;
                    saveConfig();
                }
            }
            else if (data.type === 'app_selected') {
                if (data.base64Icon) {
                    if (!appRegistry[data.path]) {
                        appRegistry[data.path] = {
                            name: data.appName,
                            desc: '暂无简介',
                            bgImg: '',
                            bgVid: '',
                            useVideo: true,
                            base64Icon: data.base64Icon,
                            todos: [],
                            processName: '',
                            usageSeconds: 0
                        };
                        appOrder.push(data.path);
                        saveConfig();
                        renderAppListTopBar();
                    }

                    // 模拟点击刚刚添加的或已存在的应用
                    const allItems = addAppBtn.parentNode.querySelectorAll('.app-item');
                    const idx = appOrder.indexOf(data.path);
                    if (idx >= 0 && idx < allItems.length) {
                        allItems[idx].click();
                    }
                }
            }
            else if (data.type === 'media_picked') {
                const targetPath = data.appPath;
                if (!appRegistry[targetPath]) return;

                let parsedPath = parseLocalPath(data.path);

                if (data.mediaType === 'video') {
                    appRegistry[targetPath].bgVid = data.path;
                    // 更新标准 modal 的标签
                    const lb = document.getElementById('label-bg-vid-path');
                    if (lb) { lb.textContent = parsedPath; lb.title = parsedPath; }
                    // 更新米游 modal 的标签（如果米游 modal 开着）
                    const hoyoLb = document.getElementById('hoyo-bg-vid-display');
                    if (hoyoLb) { hoyoLb.textContent = parsedPath; hoyoLb.title = parsedPath; }
                } else {
                    appRegistry[targetPath].bgImg = data.path;
                    const lb = document.getElementById('label-bg-img-path');
                    if (lb) { lb.textContent = parsedPath; lb.title = parsedPath; }
                    const hoyoLb = document.getElementById('hoyo-bg-img-display');
                    if (hoyoLb) { hoyoLb.textContent = parsedPath; hoyoLb.title = parsedPath; }
                }
                saveConfig();
                // 实时刷新当前页面的背景
                if (currentAppPath === targetPath) {
                    renderAppPage(targetPath, true);
                }

                // 按钮反馈（仅标准 modal 场景有效）
                const btn = data.mediaType === 'video' ? btnPickBgVid : btnPickBgImg;
                if (btn) {
                    const originalText = btn.textContent;
                    btn.textContent = '已加载 ✓';
                    btn.style.color = '#34d399';
                    setTimeout(() => { btn.textContent = originalText; btn.style.color = ''; }, 2000);
                }
            }
            else if (data.type === 'update_time_tick') {
                const p = data.appKey || data.path;
                if (p && appRegistry[p]) {
                    appRegistry[p].usageSeconds = (appRegistry[p].usageSeconds || 0) + 1;
                    saveConfig();
                    if (currentAppPath === p) {
                        const uTimeSpan = document.getElementById('home-usage-time');
                        if (uTimeSpan) uTimeSpan.textContent = formatUsageTime(appRegistry[p].usageSeconds);
                    }
                }
            }
        });
    }

    if (launchAppBtn) {
        launchAppBtn.addEventListener('click', () => {
            if (!currentAppPath || !window.chrome || !window.chrome.webview) return;
            const cfg = appRegistry[currentAppPath];

            if (cfg.isHoyo) {
                const gamePath = getHoyoGamePath(currentAppPath);
                if (!gamePath) {
                    // 未配置路径 → 按钮提示（无 alert，保持原生感）
                    const orig = launchAppBtn.textContent;
                    launchAppBtn.textContent = '请先设置游戏路径';
                    setTimeout(() => launchAppBtn.textContent = orig, 2000);
                    return;
                }
                window.chrome.webview.postMessage({
                    type: 'launch_app',
                    path: gamePath,
                    appKey: currentAppPath,
                    // 进程名优先从预设表取，保证时长统计准确
                    processName: getHoyoProcessName(currentAppPath)
                });
            } else {
                window.chrome.webview.postMessage({
                    type: 'launch_app',
                    path: currentAppPath,
                    appKey: currentAppPath,
                    processName: cfg.processName || ''
                });
            }
        });
    }

    if (settingsBtn) {
        settingsBtn.addEventListener('click', () => {
            if (!currentAppPath || !appRegistry[currentAppPath]) return;
            const config = appRegistry[currentAppPath];

            if (config.isHoyo) {
                // 米游游戏：显示专属的游戏路径选择 modal
                showHoyoPathModal(currentAppPath);
            } else {
                // 自定义应用：显示标准高级 modal
                showAppSettingsModal(currentAppPath);
            }
        });
    }

    // ======================================================
    // NEW: PREMIUM MODAL FOR CUSTOM APPS
    // ======================================================
    function showAppSettingsModal(id) {
        const cfg = appRegistry[id];
        if (!cfg) return;

        const usage = formatUsageTime(cfg.usageSeconds || 0);
        const modal = document.getElementById('settings-modal');
        const card = modal.querySelector('.modal-card');
        const origHTML = card.innerHTML;

        card.classList.add('hoyo-pro');
        card.innerHTML = `
            <div class="hoyo-modal-sidebar">
                <div class="hoyo-sidebar-title">应用设置</div>
                <div class="hoyo-nav-item active" data-hoyo-tab="app-base">基本信息</div>
                <div class="hoyo-nav-item" data-hoyo-tab="app-custom">个性化</div>
                <div class="hoyo-nav-item" data-hoyo-tab="app-manage">管理</div>
                <div style="flex:1"></div>
                <div class="hoyo-nav-item" id="app-modal-close-btn" style="opacity:0.6">返回主界面</div>
            </div>

            <div class="hoyo-modal-main">
                <!-- Tab 1: 基本信息 -->
                <div id="hoyo-tab-app-base" class="hoyo-tab-pane active">
                    <div class="hoyo-content-header">
                        <h2>基本信息</h2>
                    </div>

                    <div class="hoyo-card">
                        <div class="hoyo-game-info">
                            <img src="${cfg.localIcon ? cfg.localIcon : `data:image/png;base64,${cfg.base64Icon}`}" class="hoyo-game-logo">
                            <div class="hoyo-game-meta">
                                <div class="hoyo-game-name">${cfg.name}</div>
                                <div class="hoyo-game-stats">
                                    <span>累计运行: ${usage}</span>
                                </div>
                            </div>
                        </div>
                    </div>

                    <div class="hoyo-card">
                        <div class="hoyo-card-title">应用名称</div>
                        <input type="text" id="input-app-name" class="modern-input" value="${cfg.name}" style="width:100%; box-sizing:border-box;">
                        
                        <div class="hoyo-card-title" style="margin-top:20px;">应用简介</div>
                        <textarea id="input-app-desc" class="modern-textarea" style="width:100%; box-sizing:border-box;">${cfg.desc || ''}</textarea>
                    </div>
                </div>

                <!-- Tab 2: 个性化 -->
                <div id="hoyo-tab-app-custom" class="hoyo-tab-pane">
                    <div class="hoyo-content-header">
                        <h2>个性化设置</h2>
                    </div>
                    
                    <div class="hoyo-card">
                        <div class="hoyo-card-title">静态背景图</div>
                        <div class="file-input-group">
                            <span id="label-bg-img-path" class="file-path-display">${parseLocalPath(cfg.bgImg) || '未设置'}</span>
                            <button class="file-btn" id="btn-pick-bg-img-pro">选择</button>
                        </div>
                    </div>

                    <div class="hoyo-card">
                        <div class="hoyo-card-title">视频动态背景</div>
                        <div class="file-input-group">
                            <span id="label-bg-vid-path" class="file-path-display">${parseLocalPath(cfg.bgVid) || '未设置'}</span>
                            <button class="file-btn" id="btn-pick-bg-vid-pro">选择</button>
                        </div>
                    </div>

                    <div class="hoyo-card" style="display:flex; justify-content:space-between; align-items:center;">
                        <div>
                            <div style="font-weight:600; font-size:0.95rem;">优先使用视频背景</div>
                        </div>
                        <label class="switch">
                            <input type="checkbox" id="check-use-video-pro" ${cfg.useVideo !== false ? 'checked' : ''}>
                            <span class="slider round"></span>
                        </label>
                    </div>
                </div>

                <!-- Tab 3: 管理 -->
                <div id="hoyo-tab-app-manage" class="hoyo-tab-pane">
                    <div class="hoyo-content-header">
                        <h2>应用管理</h2>
                    </div>
                    
                    <div class="hoyo-card">
                        <div class="hoyo-card-title">程序路径</div>
                        <div class="hoyo-path-box" title="${id}">${id}</div>
                        <button class="hoyo-action-btn" id="btn-app-open-dir" style="margin-top:12px;">
                            <span class="material-symbols-outlined">folder_open</span>打开程序所在目录
                        </button>
                    </div>

                    <div class="hoyo-card" style="border-color:rgba(248, 113, 113, 0.2)">
                        <div style="color:#f87171; font-weight:600; margin-bottom:8px;">从启动器移除</div>
                        <button id="btn-app-remove-pro" class="hoyo-action-btn hoyo-btn-danger" style="width:100%; justify-content:center;">确认移除</button>
                    </div>
                </div>
            </div>
        `;

        modal.style.display = 'flex';

        // --- 逻辑绑定 ---
        const closeAppModal = () => {
            // 在关闭前保存名称和简介
            cfg.name = document.getElementById('input-app-name').value;
            cfg.desc = document.getElementById('input-app-desc').value;
            saveConfig();

            card.classList.remove('hoyo-pro');
            card.innerHTML = origHTML;
            modal.style.display = 'none';
            renderAppListTopBar();
            renderAppPage(id);
            reattachModalListeners();
        };

        document.getElementById('app-modal-close-btn').addEventListener('click', closeAppModal);

        // 标签切换
        const navItems = card.querySelectorAll('.hoyo-nav-item[data-hoyo-tab]');
        const panes = card.querySelectorAll('.hoyo-tab-pane');
        navItems.forEach(item => {
            item.addEventListener('click', () => {
                const target = item.getAttribute('data-hoyo-tab');
                navItems.forEach(n => n.classList.toggle('active', n === item));
                panes.forEach(p => p.classList.toggle('active', p.id === `hoyo-tab-${target}`));
            });
        });

        // 背景选择
        document.getElementById('btn-pick-bg-img-pro').addEventListener('click', () => {
            window.chrome.webview.postMessage({ type: 'pick_media', mediaType: 'image', appPath: id });
        });
        document.getElementById('btn-pick-bg-vid-pro').addEventListener('click', () => {
            window.chrome.webview.postMessage({ type: 'pick_media', mediaType: 'video', appPath: id });
        });
        document.getElementById('check-use-video-pro').addEventListener('change', (e) => {
            cfg.useVideo = e.target.checked;
            saveConfig();
            renderAppPage(id, true);
        });

        // 打开目录 (利用程序完整路径，C# 端需要处理文件路径转目录路径)
        document.getElementById('btn-app-open-dir').addEventListener('click', () => {
            // 简单的处理：去掉文件名得到目录
            let dir = id;
            if (dir.includes('\\')) dir = dir.substring(0, dir.lastIndexOf('\\'));
            window.chrome.webview.postMessage({ type: 'open_folder', path: dir });
        });

        // 移除
        document.getElementById('btn-app-remove-pro').addEventListener('click', () => {
            const idx = appOrder.indexOf(id);
            if (idx !== -1) appOrder.splice(idx, 1);
            delete appRegistry[id];
            saveConfig();

            card.classList.remove('hoyo-pro');
            card.innerHTML = origHTML;
            modal.style.display = 'none';

            currentAppPath = hoyoOrder[0] || appOrder[0] || '';
            renderAppListTopBar();
            if (currentAppPath) renderAppPage(currentAppPath);
            else showDefaultBg();
            reattachModalListeners();
        });
    }

    // ======================================================
    // NEW: HOYO-STYLE PREMIUM SETTINGS MODAL
    // ======================================================
    function showHoyoPathModal(id) {
        const gameNames = {
            Hoyo_Genshin: '原神',
            Hoyo_StarRail: '崩坏：星穹铁道',
            Hoyo_ZZZ: '绝区零',
            Hoyo_HYY: '崩坏：因缘精灵',
            Hoyo_H3: '崩坏3'
        };
        const pathKeys = { Hoyo_Genshin: 'genshin', Hoyo_StarRail: 'starrail', Hoyo_ZZZ: 'zzz', Hoyo_HYY: 'hyy', Hoyo_H3: 'h3' };

        const gameName = gameNames[id] || '游戏';
        const pathKey = pathKeys[id];
        const curPath = gamePaths[pathKey] || '';
        const cfg = appRegistry[id] || {};
        const usage = formatUsageTime(cfg.usageSeconds || 0);

        const modal = document.getElementById('settings-modal');
        const card = modal.querySelector('.modal-card');

        // 备份原始 HTML 以便关闭时恢复
        const origHTML = card.innerHTML;

        // 注入 Premium 结构
        card.classList.add('hoyo-pro');
        card.innerHTML = `
            <div class="hoyo-modal-sidebar">
                <div class="hoyo-sidebar-title">游戏设置</div>
                <div class="hoyo-nav-item active" data-hoyo-tab="hoyo-base">基本信息</div>
                <div class="hoyo-nav-item" data-hoyo-tab="hoyo-custom">个性化</div>
                <div class="hoyo-nav-item" data-hoyo-tab="hoyo-manage">危险区域</div>
                <div style="flex:1"></div>
                <div class="hoyo-nav-item" id="hoyo-modal-close-btn" style="opacity:0.6">返回主界面</div>
            </div>

            <div class="hoyo-modal-main">
                <!-- Tab 1: 基本信息 -->
                <div id="hoyo-tab-hoyo-base" class="hoyo-tab-pane active">
                    <div class="hoyo-content-header">
                        <h2>基本信息</h2>
                    </div>

                    <div class="hoyo-card">
                        <div class="hoyo-game-info">
                            <img src="${cfg.localIcon}" class="hoyo-game-logo">
                            <div class="hoyo-game-meta">
                                <div class="hoyo-game-name">${gameName}</div>
                                <div class="hoyo-game-stats">
                                    <span>累计运行: ${usage}</span>
                                    <span>状态: ${curPath ? '已安装' : '未配置'}</span>
                                </div>
                            </div>
                            <button id="btn-hoyo-remove-alt" class="hoyo-action-btn hoyo-btn-danger">
                                <span class="material-symbols-outlined">delete</span>从列表移除
                            </button>
                        </div>
                    </div>

                    <div class="hoyo-card">
                        <div class="hoyo-card-title">
                            游戏目录
                            <button class="hoyo-action-btn" id="btn-hoyo-open-folder">
                                <span class="material-symbols-outlined">folder_open</span>打开所在目录
                            </button>
                        </div>
                        <div class="hoyo-path-box" title="${curPath || '尚未指定安装目录'}">${curPath || '尚未指定安装目录'}</div>
                        <div style="display:flex; justify-content:space-between; align-items:center; margin-top:16px;">
                            <div style="font-size:0.85rem; color:rgba(255,255,255,0.4)">请选择含有游戏主程序的文件夹进行定位</div>
                            <button class="hoyo-action-btn" id="btn-hoyo-relocate">
                                <span class="material-symbols-outlined">near_me</span>重新定位
                            </button>
                        </div>
                    </div>
                </div>

                <!-- Tab 2: 个性化 -->
                <div id="hoyo-tab-hoyo-custom" class="hoyo-tab-pane">
                    <div class="hoyo-content-header">
                        <h2>个性化设置</h2>
                    </div>
                    
                    <div class="hoyo-card">
                        <div class="hoyo-card-title">静态背景图</div>
                        <div class="file-input-group">
                            <span id="hoyo-bg-img-display" class="file-path-display">${parseLocalPath(cfg.bgImg) || '未设置'}</span>
                            <button class="file-btn" id="btn-hoyo-pick-img">选择</button>
                        </div>
                    </div>

                    <div class="hoyo-card">
                        <div class="hoyo-card-title">视频动态背景</div>
                        <div class="file-input-group">
                            <span id="hoyo-bg-vid-display" class="file-path-display">${parseLocalPath(cfg.bgVid) || '未设置'}</span>
                            <button class="file-btn" id="btn-hoyo-pick-vid">选择</button>
                        </div>
                    </div>

                    <div class="hoyo-card" style="display:flex; justify-content:space-between; align-items:center;">
                        <div>
                            <div style="font-weight:600; font-size:0.95rem;">优先使用视频背景</div>
                            <div style="font-size:0.8rem; color:rgba(255,255,255,0.4); margin-top:2px;">开启后若存在视频背景则优先播放</div>
                        </div>
                        <label class="switch">
                            <input type="checkbox" id="hoyo-use-video" ${cfg.useVideo !== false ? 'checked' : ''}>
                            <span class="slider round"></span>
                        </label>
                    </div>
                </div>

                <!-- Tab 3: 危险区域 -->
                <div id="hoyo-tab-hoyo-manage" class="hoyo-tab-pane">
                    <div class="hoyo-content-header">
                        <h2>管理与维护</h2>
                    </div>
                    <div class="hoyo-card" style="border-color:rgba(248, 113, 113, 0.2)">
                        <div style="color:#f87171; font-weight:600; margin-bottom:8px;">从启动器移除游戏</div>
                        <p style="font-size:0.85rem; color:rgba(255,255,255,0.4); margin-bottom:16px;">这只会从 RouteStar 的列表中移除该游戏，不会删除您的游戏文件。您可以随时重新添加。</p>
                        <button id="btn-hoyo-remove" class="hoyo-action-btn hoyo-btn-danger" style="width:100%; justify-content:center;">确认移除</button>
                    </div>
                </div>
            </div>
        `;

        modal.style.display = 'flex';

        // --- 内部逻辑绑定 ---

        // 1. 关闭逻辑
        const closeHoyoModal = () => {
            card.classList.remove('hoyo-pro');
            card.innerHTML = origHTML;
            modal.style.display = 'none';
            reattachModalListeners();
        };
        document.getElementById('hoyo-modal-close-btn').addEventListener('click', closeHoyoModal);

        // 2. 标签切换
        const navItems = card.querySelectorAll('.hoyo-nav-item[data-hoyo-tab]');
        const panes = card.querySelectorAll('.hoyo-tab-pane');
        navItems.forEach(item => {
            item.addEventListener('click', () => {
                const target = item.getAttribute('data-hoyo-tab');
                navItems.forEach(n => n.classList.toggle('active', n === item));
                panes.forEach(p => p.classList.toggle('active', p.id === `hoyo-tab-${target}`));
            });
        });

        // 3. 功能按钮：打开文件夹
        document.getElementById('btn-hoyo-open-folder').addEventListener('click', () => {
            if (curPath && window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({ type: 'open_folder', path: curPath });
            }
        });

        // 4. 功能按钮：重新定位
        document.getElementById('btn-hoyo-relocate').addEventListener('click', () => {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({ type: 'open_folder_picker', tag: 'hoyo:' + pathKey });
            }
        });

        // 5. 背景设置
        document.getElementById('btn-hoyo-pick-img').addEventListener('click', () => {
            window.chrome.webview.postMessage({ type: 'pick_media', mediaType: 'image', appPath: id });
        });
        document.getElementById('btn-hoyo-pick-vid').addEventListener('click', () => {
            window.chrome.webview.postMessage({ type: 'pick_media', mediaType: 'video', appPath: id });
        });

        // 6. 视频开关
        document.getElementById('hoyo-use-video').addEventListener('change', (e) => {
            if (appRegistry[id]) {
                appRegistry[id].useVideo = e.target.checked;
                saveConfig();
                renderAppPage(id, true);
            }
        });

        // 7. 移除游戏
        const removeFn = () => {
            const idx = hoyoOrder.indexOf(id);
            if (idx !== -1) hoyoOrder.splice(idx, 1);
            delete appRegistry[id];
            saveConfig();
            closeHoyoModal();
            currentAppPath = hoyoOrder[0] || appOrder[0] || '';
            renderAppListTopBar();
            if (currentAppPath) renderAppPage(currentAppPath);
            else {
                document.getElementById('home-app').style.display = 'none';
                document.getElementById('home-empty').style.display = 'flex';
                showDefaultBg();
            }
        };
        document.getElementById('btn-hoyo-remove').addEventListener('click', removeFn);
        document.getElementById('btn-hoyo-remove-alt').addEventListener('click', removeFn);
    }


    // Hoyo modal 关闭后恢复标准 modal 的所有事件绑定
    function reattachModalListeners() {
        const modal = document.getElementById('settings-modal');

        const cb = document.getElementById('close-modal-btn');
        if (cb) cb.addEventListener('click', () => modal.style.display = 'none');

        const pickImg = document.getElementById('btn-pick-bg-img');
        if (pickImg) pickImg.addEventListener('click', () => {
            if (window.chrome) window.chrome.webview.postMessage({ type: 'pick_media', mediaType: 'image', appPath: currentAppPath });
        });

        const pickVid = document.getElementById('btn-pick-bg-vid');
        if (pickVid) pickVid.addEventListener('click', () => {
            if (window.chrome) window.chrome.webview.postMessage({ type: 'pick_media', mediaType: 'video', appPath: currentAppPath });
        });

        const clearBg = document.getElementById('clear-bg-btn');
        if (clearBg) clearBg.addEventListener('click', () => {
            if (!currentAppPath) return;
            appRegistry[currentAppPath].bgImg = '';
            appRegistry[currentAppPath].bgVid = '';
            const lbImg = document.getElementById('label-bg-img-path');
            const lbVid = document.getElementById('label-bg-vid-path');
            if (lbImg) { lbImg.textContent = '未设置'; lbImg.title = '未设置'; }
            if (lbVid) { lbVid.textContent = '未设置'; lbVid.title = '未设置'; }
            saveConfig();
        });

        const saveSBtn = document.getElementById('save-settings-btn');
        if (saveSBtn) saveSBtn.addEventListener('click', () => {
            if (!currentAppPath) return;
            const config = appRegistry[currentAppPath];
            const nameIn = document.getElementById('setting-app-name');
            const descIn = document.getElementById('setting-app-desc');
            const vidIn = document.getElementById('setting-use-video');
            if (nameIn) config.name = nameIn.value;
            if (descIn) config.desc = descIn.value;
            if (vidIn) config.useVideo = vidIn.checked;
            modal.style.display = 'none';
            saveConfig();
            renderAppPage(currentAppPath);
        });

        const removeAppBtnR = document.getElementById('remove-app-btn');
        if (removeAppBtnR) removeAppBtnR.addEventListener('click', () => {
            if (!currentAppPath) return;
            const idx = appOrder.indexOf(currentAppPath);
            if (idx !== -1) appOrder.splice(idx, 1);
            delete appRegistry[currentAppPath];
            saveConfig();
            modal.style.display = 'none';
            currentAppPath = hoyoOrder[0] || appOrder[0] || '';
            renderAppListTopBar();
            if (currentAppPath) {
                renderAppPage(currentAppPath);
            } else {
                document.getElementById('home-app').style.display = 'none';
                document.getElementById('home-empty').style.display = 'flex';
                showDefaultBg();
            }
        });
    }

    if (closeModalBtn) {
        closeModalBtn.addEventListener('click', () => settingsModal.style.display = 'none');
    }

    if (btnPickBgImg) {
        btnPickBgImg.addEventListener('click', () => {
            if (window.chrome) window.chrome.webview.postMessage({ type: 'pick_media', mediaType: 'image', appPath: currentAppPath });
        });
    }

    if (btnPickBgVid) {
        btnPickBgVid.addEventListener('click', () => {
            if (window.chrome) window.chrome.webview.postMessage({ type: 'pick_media', mediaType: 'video', appPath: currentAppPath });
        });
    }

    if (clearBgBtn) {
        clearBgBtn.addEventListener('click', () => {
            if (!currentAppPath) return;
            appRegistry[currentAppPath].bgImg = '';
            appRegistry[currentAppPath].bgVid = '';

            const lbImg = document.getElementById('label-bg-img-path');
            const lbVid = document.getElementById('label-bg-vid-path');
            if (lbImg) { lbImg.textContent = '未设置'; lbImg.title = '未设置'; }
            if (lbVid) { lbVid.textContent = '未设置'; lbVid.title = '未设置'; }

            // 原生化视觉反馈，摒弃 alert
            const originalText = clearBgBtn.textContent;
            clearBgBtn.textContent = '已清理 ✓';
            clearBgBtn.style.color = '#34d399';
            setTimeout(() => {
                clearBgBtn.textContent = originalText;
                clearBgBtn.style.color = '';
            }, 1500);
        });
    }

    if (saveSettingsBtn) {
        saveSettingsBtn.addEventListener('click', () => {
            if (!currentAppPath) return;
            const config = appRegistry[currentAppPath];
            config.name = inputName.value;
            config.desc = inputDesc.value;
            config.useVideo = inputUseVideo.checked;

            settingsModal.style.display = 'none';
            saveConfig();
            renderAppPage(currentAppPath);
        });
    }

    const removeAppBtn = document.getElementById('remove-app-btn');
    if (removeAppBtn) {
        removeAppBtn.addEventListener('click', () => {
            if (!currentAppPath) return;
            const idx = appOrder.indexOf(currentAppPath);
            if (idx !== -1) appOrder.splice(idx, 1);
            delete appRegistry[currentAppPath];
            saveConfig();
            settingsModal.style.display = 'none';
            currentAppPath = hoyoOrder[0] || appOrder[0] || '';
            renderAppListTopBar();
            if (currentAppPath) {
                renderAppPage(currentAppPath);
            } else {
                document.getElementById('home-app').style.display = 'none';
                document.getElementById('home-empty').style.display = 'flex';
                showDefaultBg();
            }
        });
    }

    if (mainLogo) {
        mainLogo.style.cursor = 'pointer';
        mainLogo.addEventListener('click', () => {
            toggleToolsPage();
        });
        mainLogo.addEventListener('contextmenu', (e) => e.preventDefault());
    }

    function prepareTimeTrackingDropdown() {
        const ddOptions = document.getElementById('time-tracking-options');
        const ddText = document.getElementById('time-tracking-selected-text');
        const ddContainer = document.getElementById('time-tracking-dropdown');
        const pInput = document.getElementById('time-tracking-process-input');

        if (ddContainer) ddContainer.dataset.value = '';

        if (ddOptions && ddText) {
            ddText.textContent = '-- 请选择应用 --';
            ddOptions.innerHTML = '';

            const defaultOpt = document.createElement('div');
            defaultOpt.className = 'dropdown-option-item';
            defaultOpt.textContent = '-- 请选择应用 --';
            defaultOpt.dataset.value = '';
            ddOptions.appendChild(defaultOpt);

            appOrder.forEach(p => {
                const opt = document.createElement('div');
                opt.className = 'dropdown-option-item';
                opt.dataset.value = p;
                opt.textContent = appRegistry[p].name;
                ddOptions.appendChild(opt);
            });
            if (pInput) pInput.value = '';
        }
    }

    // Custom Dropdown logic setup
    const ddHeader = document.getElementById('time-tracking-header');
    const ddOptions = document.getElementById('time-tracking-options');
    const ddContainer = document.getElementById('time-tracking-dropdown');
    const ddText = document.getElementById('time-tracking-selected-text');
    const pInput = document.getElementById('time-tracking-process-input');

    if (ddHeader && ddOptions) {
        ddHeader.addEventListener('click', (e) => {
            e.stopPropagation();
            ddOptions.style.display = ddOptions.style.display === 'none' ? 'block' : 'none';
        });
    }

    if (ddOptions && ddContainer && ddText) {
        ddOptions.addEventListener('click', (e) => {
            if (e.target.classList.contains('dropdown-option-item')) {
                const val = e.target.dataset.value;
                const txt = e.target.textContent;

                ddContainer.dataset.value = val;
                ddText.textContent = txt;
                ddOptions.style.display = 'none';

                if (val && appRegistry[val]) {
                    if (pInput) pInput.value = appRegistry[val].processName || '';
                } else {
                    if (pInput) pInput.value = '';
                }
            }
        });
    }

    // Close dropdown when clicking outside
    document.addEventListener('click', (e) => {
        if (ddContainer && !ddContainer.contains(e.target)) {
            if (ddOptions) ddOptions.style.display = 'none';
        }
    });

    const saveProcessBtn = document.getElementById('save-process-name-btn');
    if (saveProcessBtn) {
        saveProcessBtn.addEventListener('click', () => {
            if (!ddContainer) return;
            const p = ddContainer.dataset.value;
            if (p && appRegistry[p]) {
                appRegistry[p].processName = pInput ? pInput.value.trim() : '';
                saveConfig();

                const fb = document.getElementById('process-save-feedback');
                if (fb) {
                    fb.style.opacity = '1';
                    setTimeout(() => fb.style.opacity = '0', 2000);
                }
            }
        });
    }

    // --- Tips Cycle Logic ---
    const tipText = document.getElementById('tip-text');
    let tipsArray = [];

    if (tipText) {
        fetch('tips/tips.json')
            .then(res => res.json())
            .then(data => {
                if (data && data.length > 0) {
                    tipsArray = data;
                    startTipsCycle();
                }
            })
            .catch(err => console.error('Failed to load tips:', err));
    }

    function startTipsCycle() {
        const showNextTip = () => {
            const randomTip = tipsArray[Math.floor(Math.random() * tipsArray.length)];
            tipText.textContent = randomTip;
            tipText.classList.add('show');

            // Fade out slightly before the next interval
            setTimeout(() => {
                tipText.classList.remove('show');
            }, 4000);
        };

        showNextTip();
        setInterval(showNextTip, 5000); // Trigger every 5s
    }

    // 侧边栏 Tab 切换支持
    const sidebarItems = document.querySelectorAll('.sidebar-item');
    const tabPanes = document.querySelectorAll('.tools-tab-pane');
    sidebarItems.forEach(item => {
        item.addEventListener('click', () => {
            if (item.classList.contains('sidebar-separator')) return;
            sidebarItems.forEach(i => i.classList.remove('active'));
            tabPanes.forEach(pane => pane.classList.remove('active'));
            item.classList.add('active');
            const targetId = 'tab-' + item.getAttribute('data-tab');
            const targetPane = document.getElementById(targetId);
            if (targetPane) targetPane.classList.add('active');

            // 切到抽卡页时，自动加载当前选中游戏的本地缓存
            if (item.getAttribute('data-tab') === 'tool-hoyo-gacha') {
                loadGachaCache(currentGachaBiz);
            }
        });
    });

    // 应用排序的列表重绘逻辑
    function renderSortableList() {
        if (!sortableAppList) return;
        sortableAppList.innerHTML = '';

        // 1. 米哈游官方游戏排序
        const hoyoTitle = document.createElement('div');
        hoyoTitle.innerHTML = '<h4 style="margin: 15px 0 10px; color:rgba(255,255,255,0.4); font-size: 0.72rem; text-transform:uppercase; letter-spacing:1px; border-bottom: 1px solid rgba(255,255,255,0.05); padding-bottom:5px;">米哈游官方游戏</h4>';
        sortableAppList.appendChild(hoyoTitle);

        hoyoOrder.forEach((id, index) => {
            const cfg = appRegistry[id];
            if (!cfg) return;
            sortableAppList.appendChild(createSortableElement(id, cfg, hoyoOrder, true));
        });

        // 2. 自添加应用排序
        const customTitle = document.createElement('div');
        customTitle.innerHTML = '<h4 style="margin: 30px 0 10px; color:rgba(255,255,255,0.4); font-size: 0.72rem; text-transform:uppercase; letter-spacing:1px; border-bottom: 1px solid rgba(255,255,255,0.05); padding-bottom:5px;">自添加应用</h4>';
        sortableAppList.appendChild(customTitle);

        appOrder.forEach((path, index) => {
            const cfg = appRegistry[path];
            if (!cfg || cfg.isHoyo) return;
            sortableAppList.appendChild(createSortableElement(path, cfg, appOrder, false));
        });
    }

    function createSortableElement(id, cfg, orderArray, isHoyo) {
        const div = document.createElement('div');
        div.className = 'sortable-item';
        const iconSrc = cfg.localIcon ? cfg.localIcon : `data:image/png;base64,${cfg.base64Icon}`;

        div.innerHTML = `
            <div class="sortable-item-info">
                <img src="${iconSrc}" class="sortable-item-icon">
                <span class="sortable-item-name">${cfg.name}</span>
            </div>
            <div class="sortable-item-controls">
                <button class="ctrl-btn" data-action="up" title="上移"><span class="material-symbols-outlined">arrow_upward</span></button>
                <button class="ctrl-btn" data-action="down" title="下移"><span class="material-symbols-outlined">arrow_downward</span></button>
                ${!isHoyo ? `<button class="ctrl-btn remove-btn" data-action="del" title="删除"><span class="material-symbols-outlined">delete</span></button>` : ''}
            </div>
        `;

        div.querySelectorAll('.ctrl-btn').forEach(btn => {
            btn.addEventListener('click', function () {
                const action = this.getAttribute('data-action');
                const idx = orderArray.indexOf(id);

                if (action === 'up' && idx > 0) {
                    const temp = orderArray[idx];
                    orderArray[idx] = orderArray[idx - 1];
                    orderArray[idx - 1] = temp;
                } else if (action === 'down' && idx < orderArray.length - 1) {
                    const temp = orderArray[idx];
                    orderArray[idx] = orderArray[idx + 1];
                    orderArray[idx + 1] = temp;
                } else if (action === 'del') {
                    orderArray.splice(idx, 1);
                    delete appRegistry[id];
                    if (currentAppPath === id) currentAppPath = "";
                }

                saveConfig();
                renderSortableList();
                renderAppListTopBar();
            });
        });
        return div;
    }

    // === HoYo 抽卡记录提取逻辑 ===
    const btnExtract = document.getElementById('btn-extract-gacha');
    const gachaStatusPanel = document.getElementById('gacha-status-panel');
    const gachaStatusText = document.getElementById('gacha-status-text');
    const gachaResultContainer = document.getElementById('gacha-result-container');

    // --- Tab 切换 + 自动加载缓存 ---
    let currentGachaBiz = 'hkrpg_cn';
    const gachaTabs = document.querySelectorAll('.gacha-tab');

    function loadGachaCache(biz) {
        window.chrome.webview.postMessage({ type: 'load_gacha_cache', gameBiz: biz });
    }

    gachaTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            gachaTabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            currentGachaBiz = tab.getAttribute('data-biz');
            // 切换游戏后立即读取该游戏的本地缓存
            gachaResultContainer.innerHTML = '';
            loadGachaCache(currentGachaBiz);
        });
    });

    if (btnExtract) {
        btnExtract.addEventListener('click', () => {
            const gameBiz = currentGachaBiz;
            gachaStatusPanel.style.display = 'flex';
            gachaStatusText.textContent = '正在从本地缓存提取 URL...';
            gachaResultContainer.innerHTML = '';

            let manualPath = '';
            if (gameBiz.includes('hk4e')) manualPath = gamePaths.genshin;
            if (gameBiz.includes('hkrpg')) manualPath = gamePaths.starrail;
            if (gameBiz.includes('nap')) manualPath = gamePaths.zzz;

            window.chrome.webview.postMessage({
                type: 'get_gacha_url',
                gameBiz: gameBiz,
                gamePath: manualPath
            });
        });
    }

    // === 偏好设置中的路径配置逻辑 ===
    const browsePathBtns = document.querySelectorAll('[data-action="browse-path"]');
    browsePathBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const targetId = btn.getAttribute('data-target');
            window.chrome.webview.postMessage({
                type: 'open_folder_picker',
                tag: targetId
            });
        });
    });

    function updateGamePathUI() {
        const pGenshin = document.getElementById('path-genshin');
        const pStarRail = document.getElementById('path-starrail');
        const pZZZ = document.getElementById('path-zzz');
        if (pGenshin) pGenshin.value = gamePaths.genshin || '';
        if (pStarRail) pStarRail.value = gamePaths.starrail || '';
        if (pZZZ) pZZZ.value = gamePaths.zzz || '';
    }

    ['path-genshin', 'path-starrail', 'path-zzz'].forEach(id => {
        const input = document.getElementById(id);
        if (input) {
            input.addEventListener('change', () => {
                if (id === 'path-genshin') gamePaths.genshin = input.value;
                if (id === 'path-starrail') gamePaths.starrail = input.value;
                if (id === 'path-zzz') gamePaths.zzz = input.value;
                saveConfig();
            });
        }
    });

    // 处理 C# 后端推送的抽卡消息
    window.chrome.webview.addEventListener('message', async (event) => {
        const msg = event.data;

        if (msg.type === 'gacha_fetch_started') {
            gachaStatusPanel.style.display = 'flex';
            gachaStatusText.textContent = '正在连接服务器...';
        }
        else if (msg.type === 'gacha_fetch_progress') {
            gachaStatusText.textContent = msg.message;
        }
        else if (msg.type === 'gacha_logs_result') {
            if (!msg.success) {
                showGachaError(msg.error || '未知错误');
                return;
            }
            const stats = processGachaLogs(msg.logs);
            renderGachaStats(stats);
            gachaStatusPanel.style.display = 'none';
        }
    });

    function processGachaLogs(logs) {
        logs.sort((a, b) => a.id.localeCompare(b.id));

        const groups = {};
        logs.forEach(item => {
            const key = item.gacha_type;
            if (!groups[key]) groups[key] = { items: [], name: item.gacha_type_name };
            groups[key].items.push(item);
        });

        const stats = [];
        for (const type in groups) {
            const group = groups[type];
            let pity = 0;
            const fiveStars = [];
            let fourStarCount = 0;

            group.items.forEach(item => {
                pity++;
                if (item.rank_type === '5') {
                    fiveStars.push({ name: item.name, pity: pity });
                    pity = 0;
                } else if (item.rank_type === '4') {
                    fourStarCount++;
                }
            });

            stats.push({
                typeName: group.name,
                total: group.items.length,
                pity: pity,
                fiveStars: fiveStars.reverse(),
                fourStarCount: fourStarCount
            });
        }
        return stats;
    }

    function renderGachaStats(stats) {
        gachaResultContainer.innerHTML = '';
        if (stats.length === 0) {
            showGachaError('抓取成功但未发现任何抽卡记录。');
            return;
        }

        stats.forEach((s, idx) => {
            const pityMax = s.typeName.includes('新手') ? 20 : (s.typeName.includes('光锥') || s.typeName.includes('武器') ? 80 : 90);
            const pityPercent = Math.min((s.pity / pityMax) * 100, 100);

            const card = document.createElement('div');
            card.className = 'gacha-pool-card';
            card.style.animationDelay = `${idx * 0.07}s`;

            // 每个五星的颜色和进度条
            function fiveStarBarColor(pity) {
                if (pity <= 35) return { bar: 'rgba(74,222,128,0.18)', fill: '#4ade80', text: '#86efac' };
                if (pity <= 65) return { bar: 'rgba(250,204,21,0.15)', fill: '#facc15', text: '#fde047' };
                return { bar: 'rgba(248,113,113,0.18)', fill: '#f87171', text: '#fca5a5' };
            }

            const fiveStarRows = s.fiveStars.map(f => {
                const c = fiveStarBarColor(f.pity);
                const barW = Math.min((f.pity / 90) * 100, 100).toFixed(1);
                return `
                    <div class="gpc-five-star-row">
                        <div class="gpc-fsr-bar" style="width:0%;background:${c.fill};opacity:0.22" data-w="${barW}"></div>
                        <span class="gpc-five-star-name" style="color:${c.text}">${f.name}</span>
                        <span class="gpc-five-star-pity" style="color:${c.text};opacity:0.75">${f.pity} 抽</span>
                    </div>
                `;
            }).join('');

            card.innerHTML = `
                <div class="gpc-header">
                    <div class="gpc-pool-name">${s.typeName}</div>
                    <div class="gpc-total-pulls">${s.total} 抽</div>
                </div>

                <div class="gpc-pity-block">
                    <div class="gpc-pity-num">${s.pity}</div>
                    <div class="gpc-pity-label">当前保底进度</div>
                </div>

                <div class="gpc-progress-track">
                    <div class="gpc-progress-fill" style="width: 0%" data-target="${pityPercent}"></div>
                </div>

                <div class="gpc-stars-row">
                    <div class="gpc-star-badge gold">
                        <div class="gpc-star-count">${s.fiveStars.length}</div>
                        <div class="gpc-star-label">★★★★★</div>
                    </div>
                    <div class="gpc-star-badge purple">
                        <div class="gpc-star-count">${s.fourStarCount}</div>
                        <div class="gpc-star-label">★★★★</div>
                    </div>
                </div>

                <div class="gpc-divider"></div>
                <div class="gpc-five-star-list">
                    ${fiveStarRows || '<div class="gpc-no-five-star">暂无五星记录</div>'}
                </div>
            `;

            gachaResultContainer.appendChild(card);

            // 两类进度条的入场动画（需等 DOM 插入后触发）
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    // 当前保底进度条
                    const fill = card.querySelector('.gpc-progress-fill');
                    if (fill) fill.style.width = fill.getAttribute('data-target') + '%';

                    // 每个五星条形图
                    card.querySelectorAll('.gpc-fsr-bar').forEach(bar => {
                        bar.style.transition = 'width 0.8s cubic-bezier(0.4,0,0.2,1)';
                        bar.style.width = bar.getAttribute('data-w') + '%';
                    });
                });
            });
        });
    }

    function showGachaError(msg) {
        gachaStatusPanel.style.display = 'none';
        gachaResultContainer.innerHTML = `
            <div class="gacha-error-state">
                <span class="material-symbols-outlined">error</span>
                <p>${msg}</p>
            </div>
        `;
    }
}
);

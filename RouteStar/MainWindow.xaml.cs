using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.Graphics;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace RouteStar
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // ── 虚拟内存策略 P/Invoke ─────────────────────────────────────────
        // 传入 (-1, -1) 告知 OS 允许将所有可换出的物理页立即挪到 Page File
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(
            IntPtr hProcess,
            IntPtr dwMinimumWorkingSetSize,
            IntPtr dwMaximumWorkingSetSize);

        private bool _webViewSuspended = false;
        private CancellationTokenSource? _downloadCts = null;
        private bool _isDownloadPaused = false;
        private string? _currentInstallDir = null;  // 当前正在下载/已暂停的安装目录
        private string? _currentAppKey = null;       // 当前正在下载的 appKey

        public MainWindow()
        {
            InitializeComponent();
            // 核心：无边框沉浸式并保留原生阴影贴靠
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            // 锁定窗口尺寸为 1180x700，禁止最大化和手动拖拽调调艰大小
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(
                WinRT.Interop.WindowNative.GetWindowHandle(this)));

            if (appWindow != null)
            {
                // 使用 OverlappedPresenter 禁用最大化和调调大小
                var presenter = appWindow.Presenter as OverlappedPresenter
                    ?? OverlappedPresenter.Create();
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
                appWindow.SetPresenter(presenter);

                // 计算 DPI 缩放比例，将逻辑像素转换为物理像素
                double scale = 1.0;
                try
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    uint dpiVal = GetDpiForWindow(hwnd);
                    scale = dpiVal / 96.0;
                }
                catch { }

                int targetW = (int)(1180 * scale);
                int targetH = (int)(700 * scale);
                appWindow.Resize(new SizeInt32(targetW, targetH));
            }

            InitializeWebViewAsync();
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private async void InitializeWebViewAsync()
        {
            await LauncherWebView.EnsureCoreWebView2Async();
            
            // 重要：让 WebView2 原生接管 CSS 的 -webkit-app-region: drag 拖拽属性
            LauncherWebView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;

            // 彻底去 Web 化：禁用各种浏览器的默认行为和提示
            LauncherWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            LauncherWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            LauncherWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            LauncherWebView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;
            LauncherWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            
            string baseFolder = AppContext.BaseDirectory;
            string webPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseFolder, "web"));
            
            if (System.IO.Directory.Exists(webPath))
            {
                // 先绑事件（极其重要）
                LauncherWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                // 核心：一劳永逸直接映射电脑的所有物理驱动器为虚拟服务器
                // 这将完美允许 Chromium 内核利用底层 C++ 播放器去自动分片(HTTP 206)抓取本地大体积视频
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        string letter = drive.Name.Substring(0, 1).ToLower();
                        try 
                        {
                            LauncherWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                                $"drive-{letter}.local", drive.RootDirectory.FullName, CoreWebView2HostResourceAccessKind.Allow);
                        } catch { } // 有可能某些保留盘符无法映射
                    }
                }

                LauncherWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "launcher.local", webPath, CoreWebView2HostResourceAccessKind.Allow);
                    
                LauncherWebView.CoreWebView2.NavigationCompleted += async (s, ev) => {
                    // 1. 发送配置数据给 JS
                    string configPath = System.IO.Path.Combine(AppContext.BaseDirectory, "RouteStarConfig.json");
                    string json = System.IO.File.Exists(configPath) ? System.IO.File.ReadAllText(configPath) : "null";
                    string payload = $"{{\"type\":\"load_config\", \"data\": {json}}}";
                    LauncherWebView.CoreWebView2.PostWebMessageAsJson(payload);

                    // 2. 强行等待 0.4 秒，确保 Web 渲染至少已经开始了
                    await System.Threading.Tasks.Task.Delay(400);
                    
                    // 3. 消失
                    HideLoadingMask();
                };

                LauncherWebView.Source = new Uri("http://launcher.local/index.html");

                // 订阅窗口激活状态：失活时挂起 WebView2 并 Trim 宿主内存
                this.Activated += MainWindow_Activated;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Error] Web resources path not found: {webPath}");
            }
        }

        // ── 窗口激活/失活处理 ─────────────────────────────────────────────
        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (LauncherWebView.CoreWebView2 is null) return;

            bool deactivated = args.WindowActivationState == WindowActivationState.Deactivated;

            if (deactivated && !_webViewSuspended)
            {
                try
                {
                    // 步骤 1：通知 JS 停止所有定时器，让 WebView2 进入空闲状态
                    LauncherWebView.CoreWebView2.PostWebMessageAsJson("{\"type\":\"prepare_suspend\"}");

                    // 步骤 2：等待 800ms，确保 JS 已停止所有活动
                    await Task.Delay(800);

                    // 步骤 3：此时 WebView2 应已空闲，可以安全挂起
                    bool suspended = await LauncherWebView.CoreWebView2.TrySuspendAsync();
                    if (suspended)
                    {
                        _webViewSuspended = true;
                        TrimHostProcessMemory();
                        System.Diagnostics.Debug.WriteLine("[MemOpt] WebView2 suspended successfully.");
                    }
                    else
                    {
                        // 挂起失败（概率极低），仍执行 Trim 获得部分收益
                        TrimHostProcessMemory();
                        System.Diagnostics.Debug.WriteLine("[MemOpt] TrySuspendAsync returned false, host trimmed only.");
                    }
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    // 极端情况下的状态异常，跳过本次挂起
                    System.Diagnostics.Debug.WriteLine($"[MemOpt] Suspend skipped: 0x{ex.HResult:X8}");
                }
            }
            else if (!deactivated && _webViewSuspended)
            {
                // 恢复 WebView2 渲染
                LauncherWebView.CoreWebView2.Resume();
                _webViewSuspended = false;

                // 等待 200ms 确保 WebView2 进程完全唤醒后再发消息
                // 否则消息可能在进程还未就绪时被丢弃
                await Task.Delay(200);
                LauncherWebView.CoreWebView2.PostWebMessageAsJson("{\"type\":\"resume_timers\"}");
                System.Diagnostics.Debug.WriteLine("[MemOpt] WebView2 resumed.");
            }
        }

        // ── 主动将 .NET 宿主进程的物理页换入虚拟内存 ─────────────────────
        private static void TrimHostProcessMemory()
        {
            // 步骤 1：强制 .NET GC 收集，释放托管堆已死对象，最大化换出效果
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

            // 步骤 2：通知 OS 将所有可换出物理页立即移入 Page File
            var process = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(process.Handle, (IntPtr)(-1), (IntPtr)(-1));

            System.Diagnostics.Debug.WriteLine("[MemOpt] Host process working set trimmed.");
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // 用 WebMessageAsJson 读取 JS 直接传过来的 Object 对象
                string message = e.WebMessageAsJson;
                if (string.IsNullOrEmpty(message)) return;

                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeEl))
                {
                    string actionType = typeEl.GetString();
                    if (actionType == "open_file_picker")
                    {
                        var picker = new Windows.Storage.Pickers.FileOpenPicker();
                        // 绑定到当前窗口指针
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                        picker.FileTypeFilter.Add(".exe");
                        picker.FileTypeFilter.Add(".lnk");

                        var file = await picker.PickSingleFileAsync();
                        if (file != null)
                        {
                            var icon = System.Drawing.Icon.ExtractAssociatedIcon(file.Path);
                            if (icon != null)
                            {
                                using var bmp = icon.ToBitmap();
                                using var ms = new MemoryStream();
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                string base64 = Convert.ToBase64String(ms.ToArray());

                                var response = new
                                {
                                    type = "app_selected",
                                    base64Icon = base64,
                                    appName = file.DisplayName,
                                    path = file.Path
                                };

                                LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                            }
                        }
                    }
                    else if (actionType == "open_folder_picker")
                    {
                        var picker = new Windows.Storage.Pickers.FolderPicker();
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                        picker.FileTypeFilter.Add("*");
                        var folder = await picker.PickSingleFolderAsync();
                        if (folder != null)
                        {
                            var response = new
                            {
                                type = "folder_selected",
                                path = folder.Path,
                                tag = root.TryGetProperty("tag", out var tagEl) ? tagEl.GetString() : ""
                            };
                            LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                        }
                    }
                    else if (actionType == "pick_install_dir")
                    {
                        var picker = new Windows.Storage.Pickers.FolderPicker();
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                        picker.FileTypeFilter.Add("*");
                        var folder = await picker.PickSingleFolderAsync();
                        if (folder != null)
                        {
                            var response = new
                            {
                                type = "install_dir_picked",
                                path = folder.Path
                            };
                            LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                        }
                    }
                    else if (actionType == "launch_app")
                    {
                        if (root.TryGetProperty("path", out var pathEl))
                        {
                            string targetPath = pathEl.GetString();
                            string appKey = root.TryGetProperty("appKey", out var keyEl) ? keyEl.GetString() : targetPath;

                            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                                {
                                    FileName = targetPath,
                                    UseShellExecute = true
                                });

                                // Check if a specific process name is designated for time tracking
                                if (root.TryGetProperty("processName", out var procEl))
                                {
                                    string procName = procEl.GetString();
                                    if (!string.IsNullOrEmpty(procName))
                                    {
                                        string capturedAppKey = appKey;
                                        _ = Task.Run(async () =>
                                        {
                                            // 等待游戏进程出现（最多 60 秒）
                                            int initRetries = 60;
                                            while (System.Diagnostics.Process.GetProcessesByName(procName).Length == 0 && initRetries > 0)
                                            {
                                                await Task.Delay(1000);
                                                initRetries--;
                                            }

                                            if (System.Diagnostics.Process.GetProcessesByName(procName).Length == 0) return;

                                            // 记录会话开始时间
                                            var sessionStart = DateTime.UtcNow;

                                            // 每 5 秒轮询一次检测进程退出（原来是每秒发一次 IPC，CPU 降低 80%）
                                            while (System.Diagnostics.Process.GetProcessesByName(procName).Length > 0)
                                            {
                                                await Task.Delay(5000);
                                            }

                                            int elapsedSeconds = (int)(DateTime.UtcNow - sessionStart).TotalSeconds;
                                            System.Diagnostics.Debug.WriteLine($"[TimeTrack] Session ended: {capturedAppKey}, +{elapsedSeconds}s");

                                            // 直接写入磁盘，无论 WebView2 是否挂起都能正确保存
                                            try
                                            {
                                                string cfgPath = Path.Combine(AppContext.BaseDirectory, "RouteStarConfig.json");
                                                if (File.Exists(cfgPath))
                                                {
                                                    var node = JsonNode.Parse(File.ReadAllText(cfgPath));
                                                    if (node?["apps"] is JsonObject apps && apps[capturedAppKey] is JsonObject appNode)
                                                    {
                                                        int current = appNode["usageSeconds"]?.GetValue<int>() ?? 0;
                                                        appNode["usageSeconds"] = current + elapsedSeconds;
                                                        File.WriteAllText(cfgPath, node.ToJsonString());
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"[TimeTrack] Config write failed: {ex.Message}");
                                            }

                                            // 若 WebView2 处于活跃状态，通知前端同步 UI 显示
                                            DispatcherQueue.TryEnqueue(() =>
                                            {
                                                if (!_webViewSuspended && LauncherWebView.CoreWebView2 != null)
                                                {
                                                    var payload = new { type = "sync_usage_time", appKey = capturedAppKey, addSeconds = elapsedSeconds };
                                                    LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
                                                }
                                            });
                                        });
                                    }
                                }
                            }
                        }
                    }
                    else if (actionType == "save_config")
                    {
                        if (root.TryGetProperty("data", out var dataEl))
                        {
                            string configData = dataEl.GetRawText();
                            string configPath = Path.Combine(AppContext.BaseDirectory, "RouteStarConfig.json");
                            File.WriteAllText(configPath, configData);
                        }
                    }
                    else if (actionType == "load_gacha_cache")
                    {
                        // 读取本地缓存，直接返回给前端，不调用 API
                        string gameBiz = root.GetProperty("gameBiz").GetString();
                        string cacheFile = Path.Combine(AppContext.BaseDirectory, $"gacha_{gameBiz}.json");
                        var cached = LoadGachaCache(cacheFile);
                        if (cached.Count > 0)
                        {
                            var resp = new { type = "gacha_logs_result", success = true, logs = cached };
                            LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(resp));
                        }
                        // 如果没有缓存则静默，保持空状态占位
                    }
                    else if (actionType == "get_gacha_url")
                    {
                        string gameBiz = root.GetProperty("gameBiz").GetString();
                        string gamePath = root.TryGetProperty("gamePath", out var gpEl) ? gpEl.GetString() : null;
                        string url = await Task.Run(() => ExtractGachaUrl(gameBiz, gamePath));

                        if (string.IsNullOrEmpty(url))
                        {
                            var failResp = new { type = "gacha_logs_result", success = false, error = "未能提取到 URL，请确保您最近在游戏中打开过抽卡记录页面，或在偏好设置中设置游戏路径。" };
                            LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(failResp));
                            return;
                        }

                        // 通知前端开始抓取
                        var startResp = new { type = "gacha_fetch_started" };
                        LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(startResp));

                        // 加载本地缓存
                        string cacheFile = Path.Combine(AppContext.BaseDirectory, $"gacha_{gameBiz}.json");
                        var cachedLogs = LoadGachaCache(cacheFile);

                        // 在 C# 后端执行 HTTP 请求，绕过 CORS
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var newLogs = await FetchAllGachaLogsAsync(gameBiz, url, cachedLogs, (status) =>
                                {
                                    DispatcherQueue.TryEnqueue(() =>
                                    {
                                        var prog = new { type = "gacha_fetch_progress", message = status };
                                        LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(prog));
                                    });
                                });

                                // 合并：新记录在前，去重，按 ID 升序排序
                                var merged = newLogs.Concat(cachedLogs)
                                    .GroupBy(l => l.id)
                                    .Select(g => g.First())
                                    .OrderBy(l => l.id)
                                    .ToList();

                                // 保存到本地缓存
                                SaveGachaCache(cacheFile, merged);

                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var successResp = new { type = "gacha_logs_result", success = true, logs = merged };
                                    LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(successResp));
                                });
                            }
                            catch (Exception ex)
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var errResp = new { type = "gacha_logs_result", success = false, error = ex.Message };
                                    LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(errResp));
                                });
                            }
                        });
                    }
                    else if (actionType == "pick_media")
                    {
                        string type = root.GetProperty("mediaType").GetString();
                        string appPath = root.GetProperty("appPath").GetString();

                        var picker = new Windows.Storage.Pickers.FileOpenPicker();
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                        if (type == "video")
                        {
                            picker.FileTypeFilter.Add(".mp4");
                            picker.FileTypeFilter.Add(".webm");
                        }
                        else
                        {
                            picker.FileTypeFilter.Add(".png");
                            picker.FileTypeFilter.Add(".jpg");
                            picker.FileTypeFilter.Add(".jpeg");
                        }

                        var file = await picker.PickSingleFileAsync();
                        if (file != null)
                        {
                            // 解析路径例如 D:\Videos\bg.mp4 -> http://drive-d.local/Videos/bg.mp4
                            string realPath = file.Path;
                            if (realPath.Length >= 3 && realPath.Substring(1, 2) == ":\\")
                            {
                                string letter = realPath.Substring(0, 1).ToLower();
                                string urlPath = realPath.Substring(3).Replace('\\', '/');

                                string[] segments = urlPath.Split('/');
                                for (int i = 0; i < segments.Length; i++)
                                {
                                    segments[i] = Uri.EscapeDataString(segments[i]);
                                }

                                string finalUrl = $"http://drive-{letter}.local/" + string.Join("/", segments);

                                var response = new
                                {
                                    type = "media_picked",
                                    mediaType = type,
                                    appPath = appPath,
                                    path = finalUrl
                                };
                                LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                            }
                        }
                    }
                    else if (actionType == "open_folder")
                    {
                        if (root.TryGetProperty("path", out var pEl))
                        {
                            string folderPath = pEl.GetString();
                            if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = folderPath,
                                    UseShellExecute = true,
                                    Verb = "open"
                                });
                            }
                        }
                    }
                    else if (actionType == "uninstall_game")
                    {
                        string installDir = root.TryGetProperty("installDir", out var udEl) ? udEl.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(installDir) && System.IO.Directory.Exists(installDir))
                        {
                            // 安全检查：不能删除驱动器根目录
                            string fullPath = Path.GetFullPath(installDir);
                            string? pathRoot = Path.GetPathRoot(fullPath);
                            if (fullPath == pathRoot)
                            {
                                System.Diagnostics.Debug.WriteLine("[Uninstall] 拒绝删除驱动器根目录");
                                return;
                            }
                            // 安全检查：不能删除自己所在的目录
                            string appBase = AppContext.BaseDirectory.TrimEnd('/', '\\');
                            if (appBase.StartsWith(fullPath, StringComparison.OrdinalIgnoreCase))
                            {
                                System.Diagnostics.Debug.WriteLine("[Uninstall] 拒绝删除应用自身所在目录");
                                return;
                            }

                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    System.IO.Directory.Delete(installDir, true);
                                    System.Diagnostics.Debug.WriteLine($"[Uninstall] 已删除目录: {installDir}");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Uninstall] 删除失败: {ex.Message}");
                                }
                            });
                        }
                    }
                    else if (actionType == "get_game_status")
                    {
                        // JS 请求检测指定游戏的安装状态
                        string appKey = root.TryGetProperty("appKey", out var akEl) ? akEl.GetString() ?? "" : "";
                        string installDir = root.TryGetProperty("installDir", out var idEl) ? idEl.GetString() ?? "" : "";

                        _ = Task.Run(async () =>
                        {
                            var status = await GameDownloadService.GetGameStatusAsync(appKey, installDir);
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                var resp = new
                                {
                                    type = "game_status_result",
                                    appKey,
                                    state = status.State.ToString().ToLowerInvariant().Replace("notinstalled", "not_installed").Replace("needsupdate", "needs_update").Replace("predownload", "pre_download"),
                                    localVersion = status.LocalVersion,
                                    latestVersion = status.LatestVersion,
                                    preDownloadVersion = status.PreDownloadVersion,
                                    downloadSizeGB = Math.Round(status.DownloadSizeBytes / 1_073_741_824.0, 2),
                                    decompressedSizeGB = Math.Round(status.DecompressedSizeBytes / 1_073_741_824.0, 2),
                                    useIncrementalPatch = status.UseIncrementalPatch
                                };
                                LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(resp));
                            });
                        });
                    }
                    else if (actionType == "start_install" || actionType == "start_update")
                    {
                        string appKey = root.TryGetProperty("appKey", out var akEl2) ? akEl2.GetString() ?? "" : "";
                        bool isUpdate = actionType == "start_update";
                        string? existingDir = root.TryGetProperty("installDir", out var exDirEl) ? exDirEl.GetString() : null;

                        string? installDir = existingDir;

                        // 如果是暂停后恢复，优先用保存的目录
                        if (_isDownloadPaused && _currentAppKey == appKey && !string.IsNullOrEmpty(_currentInstallDir))
                        {
                            installDir = _currentInstallDir;
                            _isDownloadPaused = false;
                        }
                        // 如果是全新安装，弹出目录选择
                        else if (!isUpdate && string.IsNullOrEmpty(installDir))
                        {
                            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
                            var hwnd2 = WinRT.Interop.WindowNative.GetWindowHandle(this);
                            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd2);
                            folderPicker.FileTypeFilter.Add("*");
                            var folder = await folderPicker.PickSingleFolderAsync();
                            if (folder == null) return; // 用户取消
                            installDir = folder.Path;
                        }

                        if (string.IsNullOrEmpty(installDir)) return;
                        string finalInstallDir = installDir;
                        _currentInstallDir = finalInstallDir;
                        _currentAppKey = appKey;
                        _isDownloadPaused = false;

                        // 先通知 JS 安装目录（用于保存配置）
                        var dirResp = new { type = "install_dir_confirmed", appKey, installDir = finalInstallDir };
                        LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(dirResp));

                        // 启动下载 Task
                        _isDownloadPaused = false;
                        _downloadCts?.Cancel();
                        _downloadCts = new CancellationTokenSource();
                        var ct = _downloadCts.Token;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var pkg = await GameDownloadService.FetchGamePackageAsync(appKey, ct);
                                if (pkg?.main?.major == null) throw new Exception("无法获取安装包信息");

                                string targetVersion = pkg.main.major.version ?? "";

                                // 判断使用增量包还是完整包
                                string? localVer = GameDownloadService.ReadLocalVersion(finalInstallDir);
                                var patch = (isUpdate && localVer != null)
                                    ? pkg.main.patches?.FirstOrDefault(p => p.version == localVer)
                                    : null;

                                var filesToDownload = (patch != null ? patch.game_pkgs : pkg.main.major.game_pkgs) ?? new();
                                long totalBytes = filesToDownload.Sum(f => f.size);

                                string tempDir = Path.Combine(finalInstallDir, "_routestar_tmp");
                                Directory.CreateDirectory(tempDir);

                                long totalDownloaded = 0;
                                for (int i = 0; i < filesToDownload.Count; i++)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    var f = filesToDownload[i];

                                    // 防护：API 返回的 URL 可能为空或非法
                                    if (string.IsNullOrEmpty(f.url))
                                        throw new InvalidDataException($"安装包信息异常：第 {i + 1} 个文件的下载地址为空");
                                    string fileName;
                                    try { fileName = Path.GetFileName(new Uri(f.url).LocalPath); }
                                    catch (UriFormatException) { throw new InvalidDataException($"安装包信息异常：第 {i + 1} 个文件的下载地址格式无效 — {f.url}"); }

                                    string destFile = Path.Combine(tempDir, fileName);

                                    // 下载并报告进度
                                    await GameDownloadService.DownloadFileAsync(f.url!, destFile, (dl, total, speed) =>
                                    {
                                        DispatcherQueue.TryEnqueue(() =>
                                        {
                                            var prog = new
                                            {
                                                type = "download_progress",
                                                appKey,
                                                phase = "downloading",
                                                bytesDownloaded = totalDownloaded + dl,
                                                totalBytes,
                                                speedBytesPerSec = (long)speed,
                                                currentFile = $"{i + 1}/{filesToDownload.Count}: {fileName}"
                                            };
                                            LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(prog));
                                        });
                                    }, ct);

                                    totalDownloaded += f.size;

                                    // MD5 校验
                                    DispatcherQueue.TryEnqueue(() =>
                                    {
                                        var vProg = new { type = "download_progress", appKey, phase = "verifying", currentFile = fileName, bytesDownloaded = totalDownloaded, totalBytes, speedBytesPerSec = 0L };
                                        LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(vProg));
                                    });

                                    if (!string.IsNullOrEmpty(f.md5))
                                    {
                                        bool ok = await GameDownloadService.VerifyMd5Async(destFile, f.md5, ct);
                                        if (!ok) throw new Exception($"MD5 校验失败：{fileName}，请重试。");
                                    }

                                }

                                // Step 2: Extract all files
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var extProg = new { type = "download_progress", appKey, phase = "extracting", currentFile = "准备解压...", bytesDownloaded = totalBytes, totalBytes, speedBytesPerSec = 0L };
                                    LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(extProg));
                                });

                                // 收集所有已下载文件路径（解压前准备）
                                var allDownloadedFiles = filesToDownload
                                    .Select(f => Path.Combine(tempDir, Path.GetFileName(new Uri(f.url!).LocalPath)))
                                    .ToList();

                                var fileGroups = allDownloadedFiles
                                    .GroupBy(f => 
                                    {
                                        if (System.Text.RegularExpressions.Regex.IsMatch(f, @"\.\d{3}$"))
                                            return f.Substring(0, f.LastIndexOf('.'));
                                        return f;
                                    })
                                    .ToList();

                                // 解压速度采样状态
                                var extractStartTime = DateTime.UtcNow;
                                var lastSampleTime   = DateTime.UtcNow;
                                long lastSampleBytes = 0L;
                                long extractSpeed    = 0L; // bytes/s

                                foreach (var group in fileGroups)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    var parts = group.OrderBy(f => f).ToList();
                                    int entryCount = 0;

                                    await GameDownloadService.ExtractZipAsync(parts, finalInstallDir,
                                        (entry, extractedInGroup, totalInGroup) =>
                                    {
                                        entryCount++;

                                        // 每 20 个 entry 采样一次速度
                                        if (entryCount % 20 == 0 || extractedInGroup == totalInGroup)
                                        {
                                            var now = DateTime.UtcNow;
                                            double elapsed = (now - lastSampleTime).TotalSeconds;
                                            if (elapsed >= 0.2)
                                            {
                                                long delta = extractedInGroup - lastSampleBytes;
                                                extractSpeed    = (long)(delta / elapsed);
                                                lastSampleTime  = now;
                                                lastSampleBytes = extractedInGroup;
                                            }

                                            long snapExtracted = extractedInGroup;
                                            long snapTotal     = totalInGroup;
                                            long snapSpeed     = extractSpeed;
                                            DispatcherQueue.TryEnqueue(() =>
                                            {
                                                var eProg = new { type = "download_progress", appKey,
                                                    phase = "extracting", currentFile = entry,
                                                    bytesDownloaded = snapExtracted,
                                                    totalBytes = snapTotal,
                                                    speedBytesPerSec = snapSpeed };
                                                LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(eProg));
                                            });
                                        }
                                    }, ct);
                                }
                                
                                // Clean up temp files
                                try { Directory.Delete(tempDir, true); } catch { }

                                // 写入本地版本信息
                                GameDownloadService.WriteLocalVersion(finalInstallDir, targetVersion);

                                // 自动搜索 exe
                                string? exePath = GameDownloadService.FindGameExe(appKey, finalInstallDir);

                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var done = new { type = "download_progress", appKey, phase = "done", bytesDownloaded = totalBytes, totalBytes, speedBytesPerSec = 0L, currentFile = "", installedVersion = targetVersion, exePath = exePath ?? "" };
                                    LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(done));
                                });
                            }
                            catch (OperationCanceledException)
                            {
                                string finalPhase = _isDownloadPaused ? "paused" : "cancelled";
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var cancelled = new { type = "download_progress", appKey, phase = finalPhase, bytesDownloaded = 0L, totalBytes = 0L, speedBytesPerSec = 0L, currentFile = "" };
                                    LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(cancelled));
                                });
                            }
                            catch (IOException ioEx) when (ioEx.HResult == unchecked((int)0x80070070) || ioEx.Message.Contains("磁盘空间不足") || ioEx.Message.Contains("disk") || ioEx.Message.Contains("space"))
                            {
                                // 磁盘空间不足 — 单独提示，不清空下载状态
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var err = new { type = "download_progress", appKey, phase = "error",
                                        errorMessage = "磁盘空间不足，请清理磁盘后重试。",
                                        bytesDownloaded = 0L, totalBytes = 0L, speedBytesPerSec = 0L, currentFile = "" };
                                    LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(err));
                                });
                            }
                            catch (Exception ex)
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var err = new { type = "download_progress", appKey, phase = "error", errorMessage = ex.Message, bytesDownloaded = 0L, totalBytes = 0L, speedBytesPerSec = 0L, currentFile = "" };
                                    LauncherWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(err));
                                });
                            }
                        });
                    }
                    else if (actionType == "pause_download")
                    {
                        _isDownloadPaused = true;
                        _downloadCts?.Cancel();
                    }
                    else if (actionType == "cancel_download")
                    {
                        _isDownloadPaused = false;
                        _downloadCts?.Cancel();
                        
                        string installDir = root.TryGetProperty("installDir", out var idEl) ? idEl.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(installDir))
                        {
                            string tempDir = Path.Combine(installDir, "_routestar_tmp");
                            if (Directory.Exists(tempDir))
                            {
                                try { Directory.Delete(tempDir, true); } catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebMessageError] {ex.Message}");
            }
        }

        private void HideLoadingMask()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (LoadingMask.Visibility == Visibility.Collapsed) return;

                var fadeAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromSeconds(0.25))
                };

                var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                sb.Children.Add(fadeAnim);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeAnim, LoadingMask);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeAnim, "Opacity");

                sb.Completed += (s, e) =>
                {
                    LoadingMask.Visibility = Visibility.Collapsed;
                    LoadingMask.IsHitTestVisible = false;
                };

                sb.Begin();
            });
        }

        private static readonly System.Net.Http.HttpClient _gachaHttpClient = new System.Net.Http.HttpClient();

        // =========================================================
        // 本地缓存 I/O
        // =========================================================

        private record GachaLogEntry(string id, string uid, string name, string item_type, string rank_type, string gacha_type, string gacha_type_name, string time);

        private List<GachaLogEntry> LoadGachaCache(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<GachaLogEntry>();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<GachaLogEntry>>(json) ?? new List<GachaLogEntry>();
            }
            catch { return new List<GachaLogEntry>(); }
        }

        private void SaveGachaCache(string path, List<GachaLogEntry> logs)
        {
            try
            {
                var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gacha] Failed to save cache: {ex.Message}");
            }
        }

        // =========================================================
        // API 抓取（带增量停止逻辑）
        // =========================================================

        private async Task<List<GachaLogEntry>> FetchAllGachaLogsAsync(
            string gameBiz, string gachaUrl,
            List<GachaLogEntry> cachedLogs,
            Action<string> progressCallback)
        {
            // 根据游戏类型决定 API 端点和卡池列表
            string apiBase;
            int[] gachaTypes;

            if (gameBiz.StartsWith("hk4e"))
            {
                apiBase = gameBiz.Contains("global")
                    ? "https://public-operation-hk4e-sg.hoyoverse.com/gacha_info/api/getGachaLog"
                    : "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog";
                gachaTypes = new[] { 301, 302, 200, 100 }; // 角色UP、武器UP、常驻、新手
            }
            else if (gameBiz.StartsWith("hkrpg"))
            {
                apiBase = gameBiz.Contains("global")
                    ? "https://public-operation-hkrpg-sg.hoyoverse.com/common/gacha_record/api/getGachaLog"
                    : "https://public-operation-hkrpg.mihoyo.com/common/gacha_record/api/getGachaLog";
                gachaTypes = new[] { 1, 2, 11, 12 }; // 常驻、新手、角色UP、光锥UP
            }
            else if (gameBiz.StartsWith("nap"))
            {
                apiBase = gameBiz.Contains("global")
                    ? "https://public-operation-nap-sg.hoyoverse.com/common/gacha_record/api/getGachaLog"
                    : "https://public-operation-nap.mihoyo.com/common/gacha_record/api/getGachaLog";
                gachaTypes = new[] { 1, 2, 3, 5 };
            }
            else throw new Exception($"未知的游戏类型: {gameBiz}");

            // 从 gachaUrl 中提取鉴权参数
            var srcUri = new Uri(gachaUrl);
            // 手动解析 Query String
            var authParams = new Dictionary<string, string>();
            foreach (var part in srcUri.Query.TrimStart('?').Split('&'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2)
                    authParams[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
            }

            // 需要保留的鉴权参数
            var keepParams = new[] { "authkey_ver", "sign_type", "auth_appid", "authkey", "lang", "game_biz", "region" };

            var allLogs = new List<GachaLogEntry>();
            string typeKey = string.Empty;

            foreach (var gachaType in gachaTypes)
            {
                typeKey = gachaType.ToString();
                string endId = "0";
                int page = 1;
                string typeName = GetGachaTypeName(gameBiz, gachaType);
                string maxKnownId = null;
                bool maxKnownIdCalculated = false;
                bool reachedKnown = false;

                while (true)
                {
                    progressCallback($"正在抓取 [{typeName}] 第 {page} 页...");

                    // 构建请求 URL
                    var queryParts = new List<string>();
                    foreach (var key in keepParams)
                    {
                        if (authParams.TryGetValue(key, out var val))
                            queryParts.Add($"{key}={Uri.EscapeDataString(val)}");
                    }
                    queryParts.Add($"gacha_type={gachaType}");
                    queryParts.Add("size=20");
                    queryParts.Add($"end_id={endId}");

                    string requestUrl = $"{apiBase}?{string.Join("&", queryParts)}";
                    System.Diagnostics.Debug.WriteLine($"[Gacha] Fetching: {requestUrl}");

                    var resp = await _gachaHttpClient.GetStringAsync(requestUrl);
                    var json = JsonSerializer.Deserialize<JsonElement>(resp);

                    if (json.GetProperty("retcode").GetInt32() != 0)
                    {
                        string msg = json.GetProperty("message").GetString();
                        throw new Exception($"API 返回错误: {msg}");
                    }

                    var list = json.GetProperty("data").GetProperty("list");
                    int count = list.GetArrayLength();

                    if (count == 0) break;

                    if (!maxKnownIdCalculated)
                    {
                        string currentUid = list[0].TryGetProperty("uid", out var u) ? u.GetString() : "";
                        if (!string.IsNullOrEmpty(currentUid) && cachedLogs != null)
                        {
                            maxKnownId = cachedLogs
                                .Where(l => l.uid == currentUid && l.gacha_type == typeKey)
                                .Select(l => l.id)
                                .OrderByDescending(id => id)
                                .FirstOrDefault();
                        }
                        maxKnownIdCalculated = true;
                    }

                    foreach (var item in list.EnumerateArray())
                    {
                        string itemId = item.GetProperty("id").GetString();

                        // 增量停止：遇到已知最大 ID 则跳出
                        if (maxKnownId != null && string.Compare(itemId, maxKnownId, StringComparison.Ordinal) <= 0)
                        {
                            reachedKnown = true;
                            break;
                        }

                        allLogs.Add(new GachaLogEntry(
                            id: itemId,
                            uid: item.TryGetProperty("uid", out var u) ? u.GetString() : "",
                            name: item.GetProperty("name").GetString(),
                            item_type: item.TryGetProperty("item_type", out var it) ? it.GetString() : "",
                            rank_type: item.GetProperty("rank_type").GetString(),
                            gacha_type: typeKey,
                            gacha_type_name: typeName,
                            time: item.GetProperty("time").GetString()
                        ));
                    }

                    if (reachedKnown || count < 20) break;

                    endId = list[count - 1].GetProperty("id").GetString();
                    page++;

                    // 增加页面间延迟，防止 visit too frequently
                    await Task.Delay(750);
                }

                progressCallback($"[{typeName}] 完成，共获取 {allLogs.Count(l => l.gacha_type == typeKey)} 条新记录");
                
                // 卡池切换间歇休息，进一步降低触发频率限制的风险
                await Task.Delay(1000);
            }

            return allLogs;
        }

        private static string GetGachaTypeName(string gameBiz, int gachaType)
        {
            if (gameBiz.StartsWith("hk4e"))
            {
                return gachaType switch { 301 => "角色活动祈愿", 302 => "武器活动祈愿", 200 => "常驻祈愿", 100 => "新手祈愿", _ => gachaType.ToString() };
            }
            else if (gameBiz.StartsWith("hkrpg"))
            {
                return gachaType switch { 1 => "常驻跃迁", 2 => "新手跃迁", 11 => "角色活动跃迁", 12 => "光锥活动跃迁", _ => gachaType.ToString() };
            }
            else
            {
                return gachaType switch { 1 => "常驻调频", 2 => "新手调频", 3 => "邦布调频", 5 => "独家频段", _ => gachaType.ToString() };
            }
        }

        private string ExtractGachaUrl(string gameBiz, string userGamePath = null)
        {
            try
            {
                // 1. 常见的米哈游缓存根目录 (LocalLow)
                string localLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "miHoYo");
                
                string localLowFolder = "";
                string dataDataPath = ""; // 相对安装目录的路径

                switch (gameBiz)
                {
                    case "hk4e_cn":
                        localLowFolder = "Genshin Impact";
                        dataDataPath = @"YuanShen_Data\webCaches";
                        break;
                    case "hk4e_global":
                        localLowFolder = "Genshin Impact";
                        dataDataPath = @"GenshinImpact_Data\webCaches";
                        break;
                    case "hkrpg_cn":
                    case "hkrpg_global":
                        localLowFolder = "崩坏：星穹铁道";
                        dataDataPath = @"StarRail_Data\webCaches";
                        break;
                    case "nap_cn":
                    case "nap_global":
                        localLowFolder = "ZenlessZoneZero";
                        dataDataPath = @"ZenlessZoneZero_Data\webCaches";
                        break;
                }

                List<string> cacheFiles = new List<string>();

                // A. 搜索安装目录 (用户手动指定)
                if (!string.IsNullOrEmpty(userGamePath) && Directory.Exists(userGamePath))
                {
                    string installWebCache = Path.Combine(userGamePath, dataDataPath);
                    if (Directory.Exists(installWebCache))
                    {
                        // 1. 直接搜索目录下的 data_2 (Starward 逻辑)
                        string directTarget = Path.Combine(installWebCache, @"Cache\Cache_Data\data_2");
                        if (File.Exists(directTarget)) cacheFiles.Add(directTarget);

                        // 2. 搜索版本子目录下的 data_2
                        foreach (var sub in Directory.GetDirectories(installWebCache))
                        {
                            string target = Path.Combine(sub, @"Cache\Cache_Data\data_2");
                            if (File.Exists(target)) cacheFiles.Add(target);
                        }
                    }
                }

                // B. 搜索 LocalLow (默认位置)
                if (!string.IsNullOrEmpty(localLowFolder))
                {
                    string basePath = Path.Combine(localLow, localLowFolder);
                    // 兼容星铁的英文名文件夹
                    if (!Directory.Exists(basePath) && gameBiz.Contains("hkrpg"))
                    {
                        basePath = Path.Combine(localLow, "Honkai: Star Rail");
                    }

                    if (Directory.Exists(basePath))
                    {
                        string webCachePath = Path.Combine(basePath, "webCaches");
                        if (Directory.Exists(webCachePath))
                        {
                            // 递归寻找所有 data_2
                            var files = Directory.GetFiles(webCachePath, "data_2", SearchOption.AllDirectories);
                            cacheFiles.AddRange(files);
                        }
                    }
                }

                if (cacheFiles.Count == 0) return "";

                // 按修改时间排序，取最新的
                var sortedFiles = cacheFiles.Distinct().OrderByDescending(f => File.GetLastWriteTime(f));

                // 准备搜索前缀 (更宽松的匹配)
                string[] prefixes = gameBiz.Contains("global") 
                    ? new[] { "https://gs.hoyoverse.com/", "https://webstatic-sea.hoyoverse.com/", "https://webstatic-sea.mihoyo.com/" }
                    : new[] { "https://webstatic.mihoyo.com/", "https://public-operation" };

                foreach (var file in sortedFiles)
                {
                    try {
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var ms = new MemoryStream();
                        fs.CopyTo(ms);
                        byte[] data = ms.ToArray();
                        var span = new ReadOnlySpan<byte>(data);

                        foreach (var prefix in prefixes)
                        {
                            byte[] prefixBytes = System.Text.Encoding.UTF8.GetBytes(prefix);
                            var prefixSpan = new ReadOnlySpan<byte>(prefixBytes);

                            // 关键：使用 LastIndexOf 取最后一个匹配，与 Starward 完全一致
                            // data_2 文件会积累很多旧 URL，只有最后一个才有有效的 authkey
                            int index = span.LastIndexOf(prefixSpan);
                            if (index >= 0)
                            {
                                // 找结束边界：\0 或 " 或 '
                                var remaining = span[index..];
                                int length = remaining.IndexOfAny((byte)0, (byte)'"', (byte)'\'');
                                if (length < 0) length = remaining.Length;

                                string url = System.Text.Encoding.UTF8.GetString(data, index, length);
                                if (url.Contains("authkey=")) return url;
                            }
                        }
                    } catch { continue; }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GachaError] {ex.Message}");
            }
            return "";
        }

        private int FindBytes(byte[] src, byte[] find, int startIndex = 0)
        {
            for (int i = startIndex; i <= src.Length - find.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < find.Length; j++)
                {
                    if (src[i + j] != find[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}

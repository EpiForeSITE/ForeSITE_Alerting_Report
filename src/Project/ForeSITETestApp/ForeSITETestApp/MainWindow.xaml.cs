// -----------------------------------------------------------------------------
//  Author:      Tao He
//  Email:       tao.he@utah.edu
//  Created:     2025-07-01
//  Description: Dashboard user control logic for ForeSITETestApp (WPF).
// -----------------------------------------------------------------------------

using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;


namespace ForeSITETestApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{

    private Dashboard dashboard;
    private Process? flaskProcess; // Declared as nullable to fix CS8618
    private readonly HttpClient _httpClient;
    private const string SERVER_BASE_URL = "http://127.0.0.1:5001";
    private readonly string connectionString = "Data Source=mydb.sqlite";
    public MainWindow()
    {
        InitializeComponent();
        DBHelper.InitializeDatabase();

        // HTTP不需要SSL处理器
        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri(SERVER_BASE_URL),
            Timeout = TimeSpan.FromSeconds(60)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "NotebookApp/1.0");


        this.dashboard = new Dashboard(this);
        //this.MainContent.Content = this.reporter;
        this.MainContent.Content = this.dashboard;

    }
    // 端口是否被占用（快速探测）
    private async Task<bool> IsPortInUseAsync(int port, string host = "127.0.0.1")
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(800));
            if (completed != connectTask) return false; // 超时当不占用
            return client.Connected;
        }
        catch { return false; }
    }

    // 等待端口关闭（直到超时）
    private async Task<bool> WaitPortClosedAsync(int port, int timeoutSeconds = 8)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!await IsPortInUseAsync(port)) return true;
            await Task.Delay(200);
        }
        return !await IsPortInUseAsync(port);
    }

    // 强制终止占用端口的进程（Windows：netstat -ano | findstr :{port}）
    private async Task KillProcessOnPortAsync(int port)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c netstat -ano | findstr :{port}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string output = await p.StandardOutput.ReadToEndAsync();
            p.WaitForExit(2000);

            var pids = new HashSet<int>();
            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (!line.Contains($":{port}")) continue;

                var cols = Regex.Split(line, @"\s+");
                if (cols.Length >= 5 && int.TryParse(cols[^1], out int pid))
                    pids.Add(pid);
            }

            foreach (var pid in pids)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    proc.Kill(entireProcessTree: true);
                }
                catch { /* 已退出或无权限，忽略 */ }
            }

            await WaitPortClosedAsync(port, timeoutSeconds: 8);
        }
        catch { /* 忽略异常 */ }
    }

    // 优雅关停（POST /shutdown），失败不抛，返回端口是否已关闭
    private async Task<bool> TryGracefulShutdownAsync(string baseUrl, int port)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)); // 短超时
                                                                                  // _httpClient 建议已设置 BaseAddress=SERVER_BASE_URL；若没有，用绝对 URL：
            if (_httpClient?.BaseAddress == null)
                await new HttpClient().PostAsync($"{baseUrl.TrimEnd('/')}/shutdown", null, cts.Token);
            else
                await _httpClient.PostAsync("/shutdown", null, cts.Token);
        }
        catch
        {
            // 忽略：可能服务未响应/未开
        }

        // 给它一点时间优雅退出
        return await WaitPortClosedAsync(port, timeoutSeconds: 8);
    }



    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await StartFlaskAndSendRequestAsync();
    }

    public HttpClient getHttpClient()
    {
        return _httpClient;
    }


    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        if (flaskProcess != null)
        {
            try
            {
                // Attempt graceful shutdown via HTTP POST request
                try
                {
                    HttpResponseMessage response = await _httpClient.PostAsync("http://127.0.0.1:5001/shutdown", null);
                    response.EnsureSuccessStatusCode(); // Throws if response is not successful
                    Console.WriteLine("Shutdown request sent successfully.");
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Failed to send shutdown request: {ex.Message}");
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Shutdown request timed out.");
                }

                // Wait for the process to exit gracefully
                if (!flaskProcess.HasExited)
                {
                    flaskProcess.WaitForExit(3000); // Wait up to 3 seconds for graceful exit
                }

                // If still running, forcefully kill the process and its children
                if (!flaskProcess.HasExited)
                {
                    Console.WriteLine("Process did not exit gracefully. Forcing termination...");
                    KillProcessAndChildren(flaskProcess.Id);
                }
            }
            catch (InvalidOperationException ex)
            {
                // Handle case where HasExited or process access fails
                Console.WriteLine($"InvalidOperationException: {ex.Message}");
                try
                {
                    KillProcessAndChildren(flaskProcess.Id); // Attempt to kill using process ID
                }
                catch (Exception killEx)
                {
                    Console.WriteLine($"Error killing process: {killEx.Message}");
                }
            }
            catch (Exception ex)
            {
                // Handle other unexpected errors
                Console.WriteLine($"Error terminating process: {ex.Message}");
            }
            finally
            {
                // Clean up resources
                flaskProcess.Close();
                flaskProcess = null;
                _httpClient.Dispose(); // Dispose HttpClient
            }
        }
    }

    private void KillProcessAndChildren(int pid)
    {
        try
        {
            // Use taskkill to terminate the process and its child processes
            ProcessStartInfo taskKillInfo = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {pid} /T /F", // /T kills child processes, /F forces termination
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process taskKill = Process.Start(taskKillInfo))
            {
                taskKill.WaitForExit();
                if (taskKill.ExitCode != 0)
                {
                    Console.WriteLine($"taskkill failed with exit code {taskKill.ExitCode}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error killing process tree for PID {pid}: {ex.Message}");
        }
    }

    // Helper function to resolve paths (relative to absolute)
    private string ResolvePath(string baseDirectory, string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return "";

        // Check if path is already absolute
        if (Path.IsPathRooted(configPath))
        {
            return configPath;
        }
        else
        {
            // Convert relative path to absolute path based on base directory
            return Path.Combine(baseDirectory, configPath);
        }
    }

    private async Task StartFlaskAndSendRequestAsync()
    {
        // 若是本地 127.0.0.1:5001，先优雅关停，再强杀兜底，然后再启动
        try
        {
            var baseUrl = _httpClient?.BaseAddress?.ToString() ?? SERVER_BASE_URL; // 取你项目中的配置
            if (!string.IsNullOrEmpty(baseUrl) &&
                baseUrl.StartsWith("http://127.0.0.1:5001", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("🟡 Attempt graceful shutdown of existing Flask on :5001 ...");
                bool closed = await TryGracefulShutdownAsync(baseUrl, 5001);

                if (!closed)
                {
                    Debug.WriteLine("🔴 Graceful shutdown failed or timed out. Force killing processes on :5001 ...");
                    await KillProcessOnPortAsync(5001);
                }
                else
                {
                    Debug.WriteLine("✅ Flask gracefully shut down and port 5001 released.");
                }
            }
        }
        catch { /* 忽略异常，继续启动 */ }



        // Get the current execution directory
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string serverDirectory = Path.Combine(baseDirectory, "Server");
        string configPath = Path.Combine(serverDirectory, "config.json");

        dynamic config = new
        {
            pythonPath = @"epysurv311\python.exe",
            RPath = @"epysurv311\Lib\R",
            serverPath = @"epyflaServer.py",
            activateCommand = @"epysurv311\Scripts\activate.bat",
            envName = "epysurv311"
        };


        try
        {
            if (File.Exists(configPath))
            {
                string jsonContent = File.ReadAllText(configPath);
                config = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonContent) ?? config;
            }
            else
            {
                Debug.WriteLine($"⚠️ config.json not found at {configPath}, using default values.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Failed to read config.json: {ex.Message}, using default values.");
        }

        // Resolve all paths
        string pythonPath = ResolvePath(baseDirectory, (string)config.pythonPath);
        string rPath = ResolvePath(baseDirectory,(string)config.RPath);
        string serverPath = ResolvePath(serverDirectory,(string)config.serverPath);
        string activateCommand = ResolvePath(baseDirectory, (string)config.activateCommand);

        string envName = config.envName;

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string logPath = Path.Combine(documentsPath, "flask_log.txt");
        if (!File.Exists(logPath))
        {
            // Ensure the directory exists before creating the log file
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? documentsPath);
            File.Create(logPath).Close(); // 创建空文件
        }

        // Configure R environment variables
        string rHomePath = rPath;
        string rBinPath = Path.Combine(rHomePath, "bin");

        Debug.WriteLine($"Setting R_HOME to: {rHomePath}");
        Debug.WriteLine($"Adding R bin path to PATH: {rBinPath}");



        string fullCommand = $"call \"{activateCommand}\" {envName} && \"{pythonPath}\" \"{serverPath}\"";
        Debug.WriteLine($"Generated command: {fullCommand}");

        var start = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/C " + fullCommand, // Use /C instead of /K
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(serverPath)
        };

        // Set R environment variables
        start.EnvironmentVariables["R_HOME"] = rHomePath;

        // Get current PATH and add R bin directory
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        start.EnvironmentVariables["PATH"] = currentPath + ";" + rBinPath;

        // Add additional R environment variables for better compatibility
        start.EnvironmentVariables["R_USER"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        start.EnvironmentVariables["R_LIBS_USER"] = Path.Combine(rHomePath, "library");

        // Verify paths before starting
        bool pathsValid = true;

        if (!File.Exists(pythonPath))
        {
            Debug.WriteLine($"Warning: Python executable not found at {pythonPath}");
            pathsValid = false;
        }

        if (!File.Exists(serverPath))
        {
            Debug.WriteLine($"Warning: Server script not found at {serverPath}");
            pathsValid = false;
        }

        if (!Directory.Exists(rHomePath))
        {
            Debug.WriteLine($"Warning: R_HOME directory not found at {rHomePath}");
            Debug.WriteLine("R functionality may not be available.");
        }
        else if (!Directory.Exists(rBinPath))
        {
            Debug.WriteLine($"Warning: R bin directory not found at {rBinPath}");
            Debug.WriteLine("R functionality may not be available.");
        }
        else
        {
            Debug.WriteLine("R environment paths verified successfully.");
        }

        if (!pathsValid)
        {
            Debug.WriteLine("Critical paths are missing. Please check your config.json file.");
            return;
        }

        using (flaskProcess = new Process { StartInfo = start })
        using (StreamWriter logWriter = new StreamWriter(logPath, append: true))
        {
            logWriter.AutoFlush = true;

            flaskProcess.EnableRaisingEvents = true;
            flaskProcess.Exited += (s, e) =>
            {
                logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff}] ❌ Flask process exited unexpectedly.");
                Debug.WriteLine("❌ Flask process exited unexpectedly.");
            };

            flaskProcess.Start();

            // Asynchronously log standard error
            _ = Task.Run(async () =>
            {
                using var errReader = flaskProcess.StandardError;
                string? errLine;
                while ((errLine = await errReader.ReadLineAsync()) != null)
                {
                    logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff}] [ERROR] {errLine}");
                }
            });

            // Read standard output and write to log
            using var reader = flaskProcess.StandardOutput;
            string? output;

           


            while ((output = await reader.ReadLineAsync()) != null)
            {
                logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff}] [INFO] {output}");

                // Print startup success message to console
                if (output.Contains("Running on http://127.0.0.1:5001/"))
                {
                    Debug.WriteLine("✅ Flask started successfully.");
                    break;
                }
            }


        }
    }


}

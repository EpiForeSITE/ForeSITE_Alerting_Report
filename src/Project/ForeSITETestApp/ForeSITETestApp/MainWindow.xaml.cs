// -----------------------------------------------------------------------------
//  Author:      Tao He
//  Email:       tao.he@utah.edu
//  Created:     2025-07-01
//  Description: Dashboard user control logic for ForeSITETestApp (WPF).
// -----------------------------------------------------------------------------

using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;


namespace ForeSITETestApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{

    private Dashboard dashboard;
    private Process? flaskProcess; // Declared as nullable to fix CS8618
    private readonly HttpClient _httpClient;
    private readonly int _serverPort;
    private readonly string _serverToken;
    public static StreamWriter? GlobalLogWriter;
    public MainWindow()
    {
        InitializeComponent();
        AppPaths.EnsureInitialized();
        DBHelper.InitializeDatabase();

        _serverPort = ReserveAvailablePort();
        _serverToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        //Our HTTP doesn't need SSL certificate validation
        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_serverPort}"),
            Timeout = TimeSpan.FromSeconds(60)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ForeSITE/2.0");
        _httpClient.DefaultRequestHeaders.Add("X-ForeSITE-Token", _serverToken);


        this.dashboard = new Dashboard(this);
        //this.MainContent.Content = this.reporter;
        this.MainContent.Content = this.dashboard;

    }

    private static int ReserveAvailablePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
    // check Port is in use
    private async Task<bool> IsPortInUseAsync(int port, string host = "127.0.0.1")
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(800));
            if (completed != connectTask) return false; // time out means not connected
            return client.Connected;
        }
        catch { return false; }
    }

    private static string ClassifyPythonStderrLevel(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return "INFO";

        var text = line.Trim();

        if (text.StartsWith("WARNING: This is a development server.", StringComparison.OrdinalIgnoreCase))
            return "WARN";

        if (text.StartsWith("* Serving Flask app", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("* Debug mode:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("* Running on", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Press CTRL+C to quit", StringComparison.OrdinalIgnoreCase))
            return "INFO";

        if (Regex.IsMatch(text, @"^\d{1,3}(?:\.\d{1,3}){3}\s-\s-\s\[\d{2}/[A-Za-z]{3}/\d{4}\s"))
            return "INFO";

        if (text.StartsWith("Traceback", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ImportError", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ModuleNotFoundError", StringComparison.OrdinalIgnoreCase)
            || text.Contains("RuntimeError", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            return "ERROR";

        if (text.Contains("warning", StringComparison.OrdinalIgnoreCase))
            return "WARN";

        return "INFO";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Screenshot/design QA can render the full shell without starting external runtimes.
        if (Environment.GetEnvironmentVariable("FORESITE_UI_PREVIEW") == "1")
            return;

        if (!EnsureEnvironmentInitialized())
        {
            Close();
            return;
        }

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
                    HttpResponseMessage response = await _httpClient.PostAsync("/shutdown", null);
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Shutdown request sent successfully.");
                    }
                    else
                    {
                        // Some Flask hosting modes return 500 even though shutdown proceeds.
                        Console.WriteLine($"Shutdown request returned {((int)response.StatusCode)}; continuing with local process cleanup.");
                    }
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
                GlobalLogWriter?.Dispose();
                GlobalLogWriter = null;
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

            using Process? taskKill = Process.Start(taskKillInfo);
            if (taskKill == null)
            {
                Console.WriteLine("Failed to start taskkill process.");
                return;
            }

            taskKill.WaitForExit();
            if (taskKill.ExitCode != 0)
            {
                Console.WriteLine($"taskkill failed with exit code {taskKill.ExitCode}");
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

    private string GetConfigPath()
    {
        AppPaths.EnsureInitialized();
        return AppPaths.ConfigPath;
    }

    private static bool IsConfigInitialized(JObject config)
    {
        JToken? initializedToken = config["initialized"] ?? config["Initialized"];
        return initializedToken?.Type == JTokenType.Boolean && initializedToken.Value<bool>();
    }

    private static bool TryGetRHomeFromExePath(string rExePath, out string rHome)
    {
        rHome = string.Empty;
        string? rExeDirectory = Path.GetDirectoryName(rExePath);
        if (string.IsNullOrWhiteSpace(rExeDirectory))
            return false;

        // If user selected ...\bin\R.exe, use parent folder as R_HOME.
        if (string.Equals(Path.GetFileName(rExeDirectory), "bin", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(rExeDirectory);
            if (parent == null)
                return false;

            rHome = parent.FullName;
            return true;
        }

        // Otherwise treat selected exe directory as R_HOME.
        rHome = rExeDirectory;
        return true;
    }

    private string ResolveRHomePath(string baseDirectory, string configuredRPath)
    {
        string resolved = ResolvePath(baseDirectory, configuredRPath);

        // If RPath is an exe path (e.g. ...\bin\R.exe), derive R_HOME from it.
        if (string.Equals(Path.GetExtension(resolved), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetRHomeFromExePath(resolved, out string rHomeFromExe))
                return rHomeFromExe;
        }

        return resolved;
    }

    private bool EnsureEnvironmentInitialized()
    {
        try
        {
            string configPath = GetConfigPath();
            JObject config;

            if (File.Exists(configPath))
            {
                string raw = File.ReadAllText(configPath);
                config = string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw);
            }
            else
            {
                config = new JObject();
            }

            if (IsConfigInitialized(config))
                return true;

            MessageBox.Show(
                "Environment is not initialized. Please select the Python environment folder.",
                "Initial Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            using var pythonDialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select the Python environment folder (it should contain python.exe, Scripts\\activate.bat and Lib\\R)."
            };

            if (pythonDialog.ShowDialog() != WinForms.DialogResult.OK)
                return false;

            string pythonFolder = pythonDialog.SelectedPath;
            string pythonPath = Path.Combine(pythonFolder, "python.exe");
            if (!File.Exists(pythonPath))
            {
                MessageBox.Show("python.exe was not found in the selected folder.", "Initial Setup", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            string rHomePath = Path.Combine(pythonFolder, "Lib", "R");
            if (!Directory.Exists(rHomePath))
            {
                MessageBox.Show("R folder was not found at Lib\\R under the selected Python environment folder.", "Initial Setup", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            string activateCommand = Path.Combine(pythonFolder, "Scripts", "activate.bat");
            if (!File.Exists(activateCommand))
            {
                MessageBox.Show("activate.bat was not found at Scripts\\activate.bat under the selected Python environment folder.", "Initial Setup", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            config["pythonPath"] = pythonPath;
            config["RPath"] = rHomePath;
            config["activateCommand"] = activateCommand;
            config["serverPath"] = "epyflaServer.py";
            config["logPath"] = AppPaths.LogPath;
            config["rUserPath"] = AppPaths.RUserPath;
            config["rLibsUserPath"] = AppPaths.RLibraryPath;
            config["initialized"] = true;
            //config["Initialized"] = true;

            File.WriteAllText(configPath, config.ToString());
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize environment.\n{ex.Message}",
                            "Initial Setup",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
            return false;
        }
    }

    private async Task StartFlaskAndSendRequestAsync()
    {
        // Get the current execution directory
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string serverDirectory = Path.Combine(baseDirectory, "Server");
        string configPath = GetConfigPath();

        JObject config = new JObject
        {
            ["pythonPath"] = @"epysurv311\python.exe",
            ["RPath"] = @"epysurv311\Lib\R",
            ["serverPath"] = @"epyflaServer.py",
            ["activateCommand"] = @"epysurv311\Scripts\activate.bat",
            ["envName"] = "epysurv311",
            ["logPath"] = AppPaths.LogPath,
            ["rUserPath"] = AppPaths.RUserPath,
            ["rLibsUserPath"] = AppPaths.RLibraryPath
        };


        try
        {
            if (File.Exists(configPath))
            {
                string jsonContent = File.ReadAllText(configPath);
                if (!string.IsNullOrWhiteSpace(jsonContent))
                {
                    var loaded = JsonConvert.DeserializeObject<JObject>(jsonContent);
                    if (loaded != null)
                    {
                        config.Merge(loaded, new JsonMergeSettings
                        {
                            MergeArrayHandling = MergeArrayHandling.Replace
                        });
                    }
                }
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
        string pythonPath = ResolvePath(baseDirectory, config.Value<string>("pythonPath") ?? string.Empty);
        string rPath = ResolveRHomePath(baseDirectory, config.Value<string>("RPath") ?? string.Empty);
        string serverPath = ResolvePath(serverDirectory, config.Value<string>("serverPath") ?? string.Empty);

        string logPath = ResolvePath(baseDirectory, config.Value<string>("logPath") ?? AppPaths.LogPath);
        if (!File.Exists(logPath))
        {
            // Ensure the directory exists before creating the log file
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? serverDirectory);
            File.Create(logPath).Close(); // 
        }

        // Configure R environment variables
        string rHomePath = rPath;
        string rBinPath = Path.Combine(rHomePath, "bin");

        Debug.WriteLine($"Setting R_HOME to: {rHomePath}");
        Debug.WriteLine($"Adding R bin path to PATH: {rBinPath}");



        var start = new ProcessStartInfo
        {
            FileName = pythonPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(serverPath)
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(serverPath);

        // Set R environment variables
        start.EnvironmentVariables["R_HOME"] = rHomePath;

        // Build PATH with Python/Conda runtime directories first so native modules
        // (for example _ssl.pyd -> libssl/libcrypto) can load correctly.
        string pythonDirectory = Path.GetDirectoryName(pythonPath) ?? string.Empty;
        string pythonDlls = Path.Combine(pythonDirectory, "DLLs");
        string pythonScripts = Path.Combine(pythonDirectory, "Scripts");
        string condaLibraryBin = Path.Combine(pythonDirectory, "Library", "bin");
        string condaLibraryUsrBin = Path.Combine(pythonDirectory, "Library", "usr", "bin");
        string condaLibraryMingwBin = Path.Combine(pythonDirectory, "Library", "mingw-w64", "bin");
        string condaBin = Path.Combine(pythonDirectory, "bin");
        string rBinX64Path = Path.Combine(rBinPath, "x64");

        var pathParts = new List<string>();
        foreach (var candidate in new[] { condaLibraryBin, condaLibraryUsrBin, condaLibraryMingwBin, pythonDlls, pythonScripts, pythonDirectory, condaBin, rBinPath, rBinX64Path })
        {
            if (Directory.Exists(candidate))
                pathParts.Add(candidate);
        }

        // Keep system PATH to avoid breaking Windows built-in tooling resolution
        // (e.g. commands indirectly used by dependencies during startup).
        string inheritedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(inheritedPath))
        {
            pathParts.Add(inheritedPath);
        }
        start.EnvironmentVariables["PATH"] = string.Join(";", pathParts);
        start.EnvironmentVariables["RPY2_CFFI_MODE"] = "ABI";
        start.EnvironmentVariables["FORESITE_PORT"] = _serverPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.EnvironmentVariables["FORESITE_API_TOKEN"] = _serverToken;
        start.EnvironmentVariables["FORESITE_DATABASE_PATH"] = AppPaths.DatabasePath;
        start.EnvironmentVariables["FORESITE_CONFIG_PATH"] = AppPaths.ConfigPath;
        start.EnvironmentVariables["FORESITE_DATA_DIR"] = AppPaths.UserDataDirectory;

        // Add additional R environment variables from config.
        string rUserPath = ResolvePath(baseDirectory, config.Value<string>("rUserPath") ?? AppPaths.RUserPath);
        string rLibsUserPath = ResolvePath(baseDirectory, config.Value<string>("rLibsUserPath") ?? AppPaths.RLibraryPath);
        Directory.CreateDirectory(rUserPath);
        Directory.CreateDirectory(rLibsUserPath);
        start.EnvironmentVariables["R_USER"] = rUserPath;
        start.EnvironmentVariables["R_LIBS_USER"] = rLibsUserPath;

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

        flaskProcess = new Process { StartInfo = start };
        var runningProcess = flaskProcess;

        GlobalLogWriter = new StreamWriter(logPath, append: true);
        GlobalLogWriter.AutoFlush = true;

        runningProcess.EnableRaisingEvents = true;
        runningProcess.Exited += (s, e) =>
        {
            GlobalLogWriter?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff}] ❌ Flask process exited unexpectedly.");
            Debug.WriteLine("❌ Flask process exited unexpectedly.");
        };

        runningProcess.Start();
        var earlyStdErr = new StringBuilder();

        // Asynchronously log standard error
        _ = Task.Run(async () =>
        {
            using var errReader = runningProcess.StandardError;
            string? errLine;
            while ((errLine = await errReader.ReadLineAsync()) != null)
            {
                if (earlyStdErr.Length < 4096)
                {
                    if (earlyStdErr.Length > 0) earlyStdErr.AppendLine();
                    earlyStdErr.Append(errLine);
                }
                var level = ClassifyPythonStderrLevel(errLine);
                GlobalLogWriter?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff}] [{level}] {errLine}");
            }
        });

        // Asynchronously log standard output
        _ = Task.Run(async () =>
        {
            using var reader = runningProcess.StandardOutput;
            string? output;
            while ((output = await reader.ReadLineAsync()) != null)
            {
                GlobalLogWriter?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff}] [INFO] {output}");
            }
        });

        // Wait until the API port is actually ready before UI actions use the server.
        bool ready = false;
        for (int i = 0; i < 60; i++)
        {
            if (runningProcess.HasExited)
            {
                string details = earlyStdErr.Length > 0 ? $" Stderr: {earlyStdErr}" : string.Empty;
                throw new InvalidOperationException($"Python server exited early with code {runningProcess.ExitCode}.{details}");
            }

            if (await IsPortInUseAsync(_serverPort))
            {
                ready = true;
                break;
            }

            await Task.Delay(250);
        }

        if (!ready)
        {
            throw new TimeoutException($"Python server did not become ready on 127.0.0.1:{_serverPort} within timeout.");
        }

        Debug.WriteLine($"✅ Flask started successfully and port {_serverPort} is ready.");
    }


}

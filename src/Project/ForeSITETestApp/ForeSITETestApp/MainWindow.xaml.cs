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

    private async Task StartFlaskAndSendRequestAsync()
    {
        // Get the current execution directory
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        string configPath = Path.Combine(baseDirectory, "config.json");

        dynamic config = new
        {
            pythonPath = @"C:\Program Files (x86)\ForeSITEAlertingReport\epysurv-env\python.exe",
            scriptPath = @"C:\Program Files (x86)\ForeSITEAlertingReport\epyflaServer.py",
            logPath = @"",
            activateCommand = @"C:\Program Files (x86)\ForeSITEAlertingReport\epysurv-env\Scripts\activate.bat",
            envName = "epysurv-dev"
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
                Console.WriteLine($"⚠️ config.json not found at {configPath}, using default values.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to read config.json: {ex.Message}, using default values.");
        }


        string pythonPath = Path.Combine(baseDirectory, (string)config.pythonPath);
        string scriptPath = Path.Combine(baseDirectory, (string)config.scriptPath);

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string logPath = Path.Combine(documentsPath, "flask_log.txt");
        if (!File.Exists(logPath))
        {
            // Ensure the directory exists before creating the log file
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? documentsPath);
            File.Create(logPath).Close(); // 创建空文件
        }

        string activateCommand = Path.Combine(baseDirectory, (string)config.activateCommand);
        string envName = config.envName;


        //for development
        //string condaRoot = @"C:\Users\taohe\miniconda3";

        //string scriptPath = @"C:\Users\taohe\Documents\PyProjects\epyflaServer.py";
        //string logPath = @"C:\Users\taohe\Documents\PyProjects\flask_log.txt";
        //string activateCommand = Path.Combine(condaRoot, "condabin", "activate.bat");
        //string pythonPath = Path.Combine(condaRoot, "envs", envName, "python.exe");

        string fullCommand = $"call \"{activateCommand}\" {envName} && \"{pythonPath}\" \"{scriptPath}\"";
        Console.WriteLine($"Generated command: {fullCommand}");

        var start = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/C " + fullCommand, // Use /C instead of /K
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)
        };

        using (flaskProcess = new Process { StartInfo = start })
        using (StreamWriter logWriter = new StreamWriter(logPath, append: true))
        {
            logWriter.AutoFlush = true;

            flaskProcess.EnableRaisingEvents = true;
            flaskProcess.Exited += (s, e) =>
            {
                logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff}] ❌ Flask process exited unexpectedly.");
                Console.WriteLine("❌ Flask process exited unexpectedly.");
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
                    Console.WriteLine("✅ Flask started successfully.");
                    break;
                }
            }


        }
    }


}

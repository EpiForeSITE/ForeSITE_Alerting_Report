// -----------------------------------------------------------------------------
//  Author:      Tao He
//  Email:       tao.he@utah.edu
//  Created:     2025-07-01
//  Description: Dashboard user control logic for ForeSITETestApp (WPF).
// -----------------------------------------------------------------------------

using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
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
    private readonly string connectionString = "Data Source=mydb.sqlite";
    public MainWindow()
    {
        InitializeComponent();
        //InitializeDatabase();
        
        this.dashboard = new Dashboard(this);
        //this.MainContent.Content = this.reporter;
        this.MainContent.Content = this.dashboard;

    }

    private void InitializeDatabase()
    {
        // Create or connect to SQLite database
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            // Create a table
            var command = connection.CreateCommand();
            command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Email TEXT
                    )";
            command.ExecuteNonQuery();

            // Insert sample data
            command.CommandText = "INSERT INTO Users (Name, Email) VALUES ($name, $email)";
            command.Parameters.AddWithValue("$name", "John Doe");
            command.Parameters.AddWithValue("$email", "john@example.com");
            command.ExecuteNonQuery();

            // Query data
            command.CommandText = "SELECT * FROM Users";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                   // MessageBox.Show($"User: {reader["Name"]}, Email: {reader["Email"]}");
                }
            }
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await StartFlaskAndSendRequestAsync();
    }


    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (flaskProcess != null && !flaskProcess.HasExited)
        {
            flaskProcess.Kill();
        }
    }

    private async Task StartFlaskAndSendRequestAsync()
    {
        string condaRoot = @"C:\Users\taohe\miniconda3";
        string envName = "epysurv-dev";
        string scriptPath = @"C:\Users\taohe\Documents\PyProjects\epyflaServer.py";
        string logPath = @"C:\Users\taohe\Documents\PyProjects\flask_log.txt";

        string activateCommand = Path.Combine(condaRoot, "condabin", "activate.bat");
        string pythonPath = Path.Combine(condaRoot, "envs", envName, "python.exe");

        string fullCommand = $"call \"{activateCommand}\" {envName} && \"{pythonPath}\" \"{scriptPath}\"";
        Console.WriteLine($"Generated command: {fullCommand}");

        var start = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/C " + fullCommand, // 用 /C 而不是 /K
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
                logWriter.WriteLine($"[{DateTime.Now}] ❌ Flask process exited unexpectedly.");
                Console.WriteLine("❌ Flask process exited unexpectedly.");
            };

            flaskProcess.Start();

            // 异步记录标准错误
            _ = Task.Run(async () =>
            {
                using var errReader = flaskProcess.StandardError;
                string? errLine;
                while ((errLine = await errReader.ReadLineAsync()) != null)
                {
                    logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ERROR] {errLine}");
                }
            });

            // 读取标准输出并写入日志
            using var reader = flaskProcess.StandardOutput;
            string? output;
            while ((output = await reader.ReadLineAsync()) != null)
            {
                logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INFO] {output}");

                // 控制台打印启动成功提示
                if (output.Contains("Running on http://127.0.0.1:5001/"))
                {
                    Console.WriteLine("✅ Flask started successfully.");
                    break;
                }
            }

            // 可选：你可以继续在这里执行 HTTP 请求测试接口
        }
    }


}
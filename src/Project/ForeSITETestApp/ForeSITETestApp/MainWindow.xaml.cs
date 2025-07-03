// -----------------------------------------------------------------------------
//  Author:      Tao He
//  Email:       tao.he@utah.edu
//  Created:     2025-07-01
//  Description: Dashboard user control logic for ForeSITETestApp (WPF).
// -----------------------------------------------------------------------------

using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
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

        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        dynamic config = new
        {
            pythonPath = @"C:\Program Files\ForeSITEAlertingReport\epysurv-env\python.exe",
            scriptPath = @"C:\Program Files\ForeSITEAlertingReport\epyflaServer.py",
            logPath = @"",
            activateCommand = @"C:\Program Files\ForeSITEAlertingReport\epysurv-env\Scripts\activate.bat",
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


        string pythonPath = config.pythonPath;
        string scriptPath = config.scriptPath;

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string logPath = Path.Combine(documentsPath, "flask_log.txt");
        if (!File.Exists(logPath))
        {
            // Ensure the directory exists before creating the log file
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? documentsPath);
            File.Create(logPath).Close(); // 创建空文件
        }

        string activateCommand = config.activateCommand;
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

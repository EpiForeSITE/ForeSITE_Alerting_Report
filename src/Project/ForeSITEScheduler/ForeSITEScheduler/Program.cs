using ForeSITEScheduler;
using Newtonsoft.Json.Linq;
using QuestPDF.Fluent;
using SchedulerRunner;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
// 引用你现有的 DB 助手与实体
// 如果 DBHelper/SchedulerTask 在其他命名空间，请把下面的 using 改为正确命名空间；
// 或者直接在代码里使用全名。
using static System.Console;

internal static class Program
{
    // ================== 配置 ==================
    private const string SERVER_BASE_URL = "http://127.0.0.1:5001";
    private const int SERVER_PORT = 5001;

    // 服务器启动所需（与 WPF 程序保持一致）
    // 这些路径可按你的项目目录结构调整
    private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string ServerDir = Path.Combine(BaseDir, "Server");
    private static readonly string configPath = Path.Combine(ServerDir, "config.json");


    private static string PythonExe = Path.Combine(BaseDir, @"epysurv311\python.exe");
    private static string ServerScript = Path.Combine(ServerDir, "epyflaServer.py");
    private static string R_HOME = Path.Combine(BaseDir, @"epysurv311\Lib\R");
    private static string FullCommand = ""; // 完整启动命令

    private static readonly HttpClient Http = new HttpClient { BaseAddress = new Uri(SERVER_BASE_URL), Timeout = TimeSpan.FromSeconds(60) };
    private static Process? FlaskProcess;

    // ================== 入口 ==================
    private static async Task<int> Main(string[] args)
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "NotebookApp/1.0");
        WriteLine("ForeSITEScheduler Runner starting...");

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
                Console.WriteLine($"⚠️ config.json not found at {configPath}, using default values.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to read config.json: {ex.Message}, using default values.");
        }

        // Resolve all paths
        PythonExe = ResolvePath(BaseDir, (string)config.pythonPath);
        R_HOME = ResolvePath(BaseDir, (string)config.RPath);
        ServerScript = ResolvePath(ServerDir, (string)config.serverPath);
        string activateCommand = ResolvePath(BaseDir, (string)config.activateCommand);

        string envName = config.envName;

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string logPath = Path.Combine(documentsPath, "flask_console_log.txt");
        if (!File.Exists(logPath))
        {
            // Ensure the directory exists before creating the log file
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? documentsPath);
            File.Create(logPath).Close(); // 创建空文件
        }

        // Configure R environment variables
        string rHomePath = R_HOME;
        string rBinPath = Path.Combine(rHomePath, "bin");

        Console.WriteLine($"Setting R_HOME to: {rHomePath}");
        Console.WriteLine($"Adding R bin path to PATH: {rBinPath}");



        FullCommand = $"call \"{activateCommand}\" {envName} && \"{PythonExe}\" \"{ServerScript}\"";
        Console.WriteLine($"Generated command: {FullCommand}");


        try
        {
            // QuestPDF 许可证
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            // 1) 确保 Flask 健康
            await EnsureServerAsync();

            // 2) 读取 scheduler 表（使用相同的 DBHelper）
            //    注意：DBHelper.GetAllSchedulers() 返回 ObservableCollection<SchedulerTask>
            var tasks = DBHelper.GetAllSchedulers().ToList();
            if (tasks.Count == 0)
            {
                WriteLine("No scheduler rows.");
                return 0;
            }

            // 3) 按日期频率筛选“今天应运行”的任务
            var today = DateTime.Today;
            var due = tasks.Where(t => IsDueToday(today, t.StartDate, t.Freq)).ToList();
            if (due.Count == 0)
            {
                WriteLine("No due tasks for today.");
                return 0;
            }

            // 4) 执行任务
            foreach (var task in due)
            {
                WriteLine($"Running scheduler Id={task.Id} ...");

                if (string.IsNullOrWhiteSpace(task.AttachmentPath) || !File.Exists(task.AttachmentPath))
                {
                    WriteLine($"  Skip: AttachmentPath not found: {task.AttachmentPath}");
                    continue;
                }

                // 读取 JSON 模板
                var jsonText = File.ReadAllText(task.AttachmentPath);
                var template = JObject.Parse(jsonText);

                // 给模板打个来源路径，便于确定输出目录
                template["__sourcePath"] = task.AttachmentPath;

                // 处理模板：生成图、合成 PDF
                string pdfOut = await ProcessTemplateAsync(template);
                WriteLine($"  ✅ PDF generated: {pdfOut}");

                // === 新增：发送邮件 ===
                try
                {
                    var recipients = SmtpConfig.ParseRecipients(task.Recipients ?? "");
                    if (recipients.Count > 0)
                    {
                        var smtp = SmtpConfig.LoadSmtpConfig();
                        string subject = $"Automated Report - {DateTime.Now:yyyy-MM-dd}";
                        string body = "Please find the attached report.\n\n(This email was sent automatically.)";

                        await SmtpConfig.SendReportEmailAsync(smtp, recipients, subject, body, pdfOut);
                        WriteLine($"  📧 Email sent to: {string.Join(", ", recipients)}");
                    }
                    else
                    {
                        WriteLine("  (No recipients configured; skip sending email)");
                    }
                }
                catch (Exception mailEx)
                {
                    WriteLine($"  ⚠️ Failed to send email: {mailEx.Message}");
                }

            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"FATAL: {ex}");
            return 1;
        }
    }

    // Helper function to resolve paths (relative to absolute)
    private static string ResolvePath(string baseDirectory, string configPath)
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

    // ================== 服务器相关 ==================

    private static async Task EnsureServerAsync()
    {
        if (await IsHealthyAsync())
        {
            WriteLine("Flask already healthy.");
            return;
        }

        WriteLine("Flask not healthy. Trying graceful shutdown...");
        await TryGracefulShutdownAsync();

        // 如仍占用端口，强杀
        if (await IsPortInUseAsync(SERVER_PORT))
        {
            WriteLine("Port still busy, killing process on port...");
            await KillProcessOnPortAsync(SERVER_PORT);
        }

        // 启动 Flask
        WriteLine("Starting Flask process...");
        await StartFlaskAsync();

        // 等待健康
        var ok = await WaitHealthyAsync(30);
        if (!ok) throw new Exception("Flask failed to become healthy.");
        WriteLine("Flask is healthy.");
    }

    private static async Task<bool> IsHealthyAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var resp = await Http.GetAsync("/health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<bool> WaitHealthyAsync(int timeoutSeconds)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            if (await IsHealthyAsync()) return true;
            await Task.Delay(500);
        }
        return false;
    }

    private static async Task TryGracefulShutdownAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Http.PostAsync("/shutdown", null, cts.Token);
            await WaitPortClosedAsync(SERVER_PORT, 8);
        }
        catch { /* ignore */ }
    }

    private static async Task<bool> IsPortInUseAsync(int port, string host = "127.0.0.1")
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(500));
            return completed == connectTask && client.Connected;
        }
        catch { return false; }
    }

    private static async Task<bool> WaitPortClosedAsync(int port, int timeoutSeconds)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            if (!await IsPortInUseAsync(port)) return true;
            await Task.Delay(200);
        }
        return !await IsPortInUseAsync(port);
    }
    private static async Task KillProcessOnPortAsync(int port)
    {
        try
        {
            // 查 IPv4 & IPv6
            string cmd = $"/c netstat -ano | findstr :{port}";
            var psi = new ProcessStartInfo("cmd.exe", cmd)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string output = await p.StandardOutput.ReadToEndAsync();
            p.WaitForExit(2000);

            var pids = new HashSet<int>();
            foreach (var lineRaw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = lineRaw.Trim();
                if (!line.Contains($":{port}")) continue;
                var cols = System.Text.RegularExpressions.Regex.Split(line, @"\s+");
                if (cols.Length >= 5 && int.TryParse(cols[^1], out int pid))
                    pids.Add(pid);
            }

            foreach (var pid in pids)
            {
                try
                {
                    // 先用 .NET 方式
                    Process.GetProcessById(pid).Kill(entireProcessTree: true);
                }
                catch
                {
                    // 兜底用 taskkill（需要管理员）
                    try
                    {
                        var tk = new ProcessStartInfo("cmd.exe", $"/c taskkill /F /T /PID {pid}")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var tp = Process.Start(tk);
                        tp?.WaitForExit(2000);
                    }
                    catch { /* ignore */ }
                }
            }

            // 等端口释放
            var until = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < until)
            {
                if (!await IsPortInUseAsync(port) && !await IsPortInUseAsync(port, host: "::1")) break;
                await Task.Delay(200);
            }
        }
        catch { /* ignore */ }
    }


    private static async Task StartFlaskAsync()
    {
        if (!File.Exists(PythonExe) || !File.Exists(ServerScript))
            throw new FileNotFoundException($"Python or server script not found. {PythonExe} | {ServerScript}");



        var start = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/C " + FullCommand, // Use /C instead of /K
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ServerDir)
        };

        // 设置 R 环境（如果你的服务器需要）
        start.Environment["R_HOME"] = R_HOME;

        FlaskProcess = new Process { StartInfo = start, EnableRaisingEvents = true };
        FlaskProcess.OutputDataReceived += (_, a) => { if (a.Data != null) WriteLine("[FLASK] " + a.Data); };
        FlaskProcess.ErrorDataReceived += (_, a) => { if (a.Data != null) WriteLine("[FLASK-ERR] " + a.Data); };
        FlaskProcess.Start();
        FlaskProcess.BeginOutputReadLine();
        FlaskProcess.BeginErrorReadLine();
    }

    // ================== 调度判断 ==================

    private static bool IsDueToday(DateTime today, string? startDate, string? freq)
    {
        if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(freq))
            return false;

        if (!DateTime.TryParse(startDate, out var start))
        {
            if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                return false;
        }

        if (today < start.Date) return false;

        var f = freq.Trim().ToLowerInvariant();
        if (f is "by day" or "daily")
            return true;

        if (f is "by week" or "weekly")
            return today.DayOfWeek == start.DayOfWeek;

        if (f is "by month" or "monthly")
        {
            int day = start.Day;
            int lastDay = DateTime.DaysInMonth(today.Year, today.Month);
            int trigger = Math.Min(day, lastDay);
            return today.Day == trigger;
        }

        return false;
    }

    // ================== 模板处理 / 生成 PDF ==================

    private static async Task<string> ProcessTemplateAsync(JObject template)
    {
        // 从模板取 schedule（作为当次运行的条件）
        string scheduleStart = template["schedule"]?["startDate"]?.ToString() ?? DateTime.Today.ToString("yyyy-MM-dd");
        string scheduleFreq = template["schedule"]?["frequency"]?.ToString() ?? "By Day";

        // 收集文本与图像
        var blocks = new List<(string text, bool center)>();
        var images = new List<byte[]>();

        var layout = template["layout"] as JArray ?? new JArray();
        foreach (var tok in layout.OfType<JObject>())
        {
            var type = tok["type"]?.ToString();

            if (type == "Title")
            {
                var content = tok["content"] as JObject;
                string text = content?["text"]?.ToString() ?? "";
                blocks.Add((text, true));
            }
            else if (type == "Comment")
            {
                var content = tok["content"] as JObject;
                string text = content?["text"]?.ToString() ?? "";
                blocks.Add((text, false));
            }
            else if (type == "Plot")
            {
                // 取参数并替换 BeginDate
                var param = tok["params"] as JObject ?? new JObject();
                param["BeginDate"] = scheduleStart;

                string? imgPath = await RequestPlotAsync(param);
                if (!string.IsNullOrWhiteSpace(imgPath) && File.Exists(imgPath))
                    images.Add(await File.ReadAllBytesAsync(imgPath));
            }
        }

        string outputPdf = GetOutputPdfPath(template);
        await GeneratePdfAsync(outputPdf, blocks, images);
        return outputPdf;
    }

    // Program.cs
    static async Task<string?> RequestPlotAsync(JObject graphParams)
    {
        var body = new JObject { ["graph"] = graphParams };
        using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var resp = await Http.PostAsync("http://127.0.0.1:5001/epyapi", content, cts.Token);
        resp.EnsureSuccessStatusCode();

        var txt = await resp.Content.ReadAsStringAsync(cts.Token);
        var jo = JObject.Parse(txt);

        // 兼容两种字段：plot_path 优先，其次是 file
        return jo["plot_path"]?.ToString() ?? jo["file"]?.ToString();
    }


    private static string GetOutputPdfPath(JObject template)
    {
        var sourcePath = template["__sourcePath"]?.ToString();
        if (string.IsNullOrEmpty(sourcePath))
            sourcePath = Path.Combine(AppContext.BaseDirectory, "report_template.json");

        var dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var name = $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        return Path.Combine(dir, name);
    }

    private static Task GeneratePdfAsync(string outputPath, List<(string text, bool center)> blocks, List<byte[]> images)
    {
        return Task.Run(() =>
        {
            QuestPDF.Fluent.Document.Create(c =>
            {
                c.Page(page =>
                {
                    page.Size(QuestPDF.Helpers.PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Content().Column(col =>
                    {
                        // 文本块（Title 居中、Comment 左对齐）
                        foreach (var (text, center) in blocks)
                        {
                            col.Item().PaddingBottom(12).Text(t =>
                            {
                                if (center) t.AlignCenter();
                                t.Span(text ?? "");
                            });
                        }

                        // 图像
                        foreach (var img in images)
                            col.Item().PaddingBottom(18).Image(img).FitWidth();
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(outputPath);
        });
    }
}

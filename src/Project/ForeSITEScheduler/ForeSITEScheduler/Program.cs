using ForeSITEScheduler;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Utilities;
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
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static System.Console;

internal static class Program
{
    // ================== configration ==================
    

    
    private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string ServerDir = Path.Combine(BaseDir, "Server");
    private static readonly string configPath = Path.Combine(ServerDir, "config.json");


    private static string PythonExe = Path.Combine(BaseDir, @"epysurv311\python.exe");
    private static string ServerScript = Path.Combine(ServerDir, "epyflaServer.py");
    private static string R_HOME = Path.Combine(BaseDir, @"epysurv311\Lib\R");
    private static string FullCommand = "";

    private const string SERVER_BASE_URL = "http://127.0.0.1:5001";
    private const int SERVER_PORT = 5001;

    
    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false,   //  disabled proxy
            Proxy = null
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(SERVER_BASE_URL),
     //#if DEBUG
            Timeout = Timeout.InfiniteTimeSpan,    // Debug 
     //#else
     //       Timeout = TimeSpan.FromSeconds(15),    // Release
     //#endif
        };
    }

    private static readonly HttpClient Http = CreateHttpClient();


    private static Process? FlaskProcess;

    enum DocItemType { Text, Image }
    record DocItem(DocItemType Type, string? Text = null, bool Center = false, byte[]? ImageBytes = null);


    // ================== Entry ==================
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
            Console.WriteLine($"configfile= {configPath}");
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
            File.Create(logPath).Close(); // 
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
            // QuestPDF license
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            // 1) ensure Flask server is running
            await EnsureServerAsync();

            // 2) read all scheduled tasks from DB
            
            var tasks = DBHelper.GetAllSchedulers().ToList();
            if (tasks.Count == 0)
            {
                WriteLine("No scheduler rows.");
                return 0;
            }

            // 3) select due tasks
            var today = DateTime.Today;
            var due = tasks.Where(t => IsDueToday(today, t.StartDate, t.Freq)).ToList();
            if (due.Count == 0)
            {
                WriteLine("No due tasks for today.");
                return 0;
            }

            // 4) execute each due task
            foreach (var task in due)
            {
                WriteLine($"Running scheduler Id={task.Id} ...");

                if (string.IsNullOrWhiteSpace(task.AttachmentPath) || !File.Exists(task.AttachmentPath))
                {
                    WriteLine($"  Skip: AttachmentPath not found: {task.AttachmentPath}");
                    continue;
                }

                // read template
                var jsonText = File.ReadAllText(task.AttachmentPath);
                var template = JObject.Parse(jsonText);

                // 
                template["__sourcePath"] = task.AttachmentPath;

                // 
                string? pdfOut = await ProcessTemplateAsync(template);
               

                if (string.IsNullOrEmpty(pdfOut))
                {
                    WriteLine("  ℹ️ No abnormal pattern. Skip PDF and email.");
                    continue; // 
                }
                WriteLine($"  ✅ PDF generated: {pdfOut}");

                // === email ===
                try
                {
                    var recipients = SmtpConfig.ParseRecipients(task.Recipients ?? "");
                    if (recipients.Count > 0)
                    {
                        var smtp = SmtpConfig.LoadSmtpConfig();
                        Console.WriteLine($"  Using SMTP server: {smtp.Host}:{smtp.Port}, SSL={smtp.EnableSsl}, User={smtp.Username}");
                        string subject = $"Automated Report - {DateTime.Now:yyyy-MM-dd}";
                        string body = "Please find the attached report.\n\n(This email was sent automatically.)\n\nJob name:"+ task.Id.ToString();

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

        finally
        {
            //#if DEBUG
            // Debug convenience: always pause
            PauseIfInteractive(force: true);
//#else
//        PauseIfInteractive(force: force );
//#endif
        }
    }

    private static void PauseIfInteractive(bool force = false, string? message = null)
    {
        // when force=true, always pause
        if (!force)
        {
            if (!Environment.UserInteractive) return;        // non-interactive session
            if (Console.IsOutputRedirected) return;          // redirected output/input/pipe
            if (Debugger.IsAttached) return;                 // debugger attached
        }

        Console.WriteLine(message ?? "Press Enter to exit . . .");
        try { Console.ReadLine(); } catch { /* ignore */ }
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

    // ================== Server ==================

    private static async Task EnsureServerAsync()
    {
        if (await IsHealthyAsync())
        {
            WriteLine("Flask already healthy.");
            return;
        }

        WriteLine("Flask not healthy. Trying graceful shutdown...");
        await TryGracefulShutdownAsync();

        // if still not healthy, kill process on port
        if (await IsPortInUseAsync(SERVER_PORT))
        {
            WriteLine("Port still busy, killing process on port...");
            await KillProcessOnPortAsync(SERVER_PORT);
        }

        // start Flask
        WriteLine("Starting Flask process...");
        await StartFlaskAsync();

        // wait healthy
        var ok = await WaitHealthyAsync(30);
        if (!ok) throw new Exception("Flask failed to become healthy.");
        WriteLine("Flask is healthy.");
    }
   
    public static async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "health")
            {
                //   use HTTP/1.0, avoid unexpected HTTP/1.0
                Version = new Version(1, 1),
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));


            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);


            var ok = resp.IsSuccessStatusCode;
            var body = await resp.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"Health {((int)resp.StatusCode)} ; body={body}");
            return ok;
        }
        catch (TaskCanceledException ex)
        {
            // if we want to distinguish timeout vs user cancel, check ex.CancellationToken
            Console.WriteLine($"Canceled/Timeout: {ex.Message}");
            return false;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}");
            return false;
        }
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
            // check IPv4 & IPv6
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
                    // first try .NET kill
                    Process.GetProcessById(pid).Kill(entireProcessTree: true);
                }
                catch
                {
                    // finally use taskkill
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

            // wait port closed
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

        // set R environment variables
        start.Environment["R_HOME"] = R_HOME;

        FlaskProcess = new Process { StartInfo = start, EnableRaisingEvents = true };
        FlaskProcess.OutputDataReceived += (_, a) => { if (a.Data != null) WriteLine("[FLASK] " + a.Data); };
        FlaskProcess.ErrorDataReceived += (_, a) => { if (a.Data != null) WriteLine("[FLASK-ERR] " + a.Data); };
        FlaskProcess.Start();
        FlaskProcess.BeginOutputReadLine();
        FlaskProcess.BeginErrorReadLine();
    }

    // ====================================

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

    // ====================================

    private static async Task<string?> ProcessTemplateAsync(JObject template)
    {
        // read schedule
        string scheduleStart = template["schedule"]?["startDate"]?.ToString() ?? DateTime.Today.ToString("yyyy-MM-dd");
        string scheduleFreq = template["schedule"]?["frequency"]?.ToString() ?? "By Day";

        bool abnormalReportFlag = template["schedule"]?["abnormalReportFlag"]?.ToObject<bool>() ?? false;


        // ordered items
        var items = new List<DocItem>();
        bool anyAbnormal = false;

        var layout = template["layout"] as JArray ?? new JArray();
        foreach (var tok in layout.OfType<JObject>())
        {
            var type = tok["type"]?.ToString();

            if (type == "Title")
            {
                var content = tok["content"] as JObject;
                string text = content?["text"]?.ToString() ?? "";
                items.Add(new DocItem(DocItemType.Text, text, Center: true));
            }
            else if (type == "Comment")
            {
                var content = tok["content"] as JObject;
                string text = content?["text"]?.ToString() ?? "";
                items.Add(new DocItem(DocItemType.Text, text, Center: false));
            }
            else if (type == "Plot")
            {
                // fill in params
                var param = tok["params"] as JObject ?? new JObject();
                param["beginDate"] = scheduleStart;

                param["abnormalReportFlag"] = abnormalReportFlag;

                var resp = await RequestPlotAsync(param);
                if (resp!=null  && !string.IsNullOrWhiteSpace(resp.PlotPath) && File.Exists(resp.PlotPath))
                {
                    anyAbnormal = true;
                    byte[] png = await File.ReadAllBytesAsync(resp.PlotPath!);
                    items.Add(new DocItem(DocItemType.Image, ImageBytes: png));
                }
            }
            // else ignore unknown type such as add Table in the future
        }

        if (!anyAbnormal)
            return null;

        string outputPdf = GetOutputPdfPath(template);
        await GeneratePdfAsync(outputPdf, items);
        return outputPdf;
    }

    private record PlotResponse(bool Abnormal, string? PlotPath);
    static async Task<PlotResponse?> RequestPlotAsync(JObject graphParams)
    {
        var body = new JObject { ["graph"] = graphParams };
        using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var resp = await Http.PostAsync("http://127.0.0.1:5001/epyapi", content, cts.Token);
        resp.EnsureSuccessStatusCode();

        var txt = await resp.Content.ReadAsStringAsync(cts.Token);
        var jo = JObject.Parse(txt);

        string? status = jo["status"]?.ToString();
        string? filePath = jo["plot_path"]?.ToString();

        bool isAbnormal = jo.ContainsKey("abnormal") &&
                  bool.TryParse(jo["abnormal"]?.ToString(), out var flag) && flag;

        if (status?.ToLower() == "processed"  && !string.IsNullOrEmpty(filePath))
        {
            return new PlotResponse(true, filePath);
        }
        else
        {
            return new PlotResponse(false, null);
        }
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

    private static Task GeneratePdfAsync(string outputPath, List<DocItem> items)
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
                        foreach (var it in items)
                        {
                            if (it.Type == DocItemType.Text)
                            {
                                col.Item().PaddingBottom(12).Text(t =>
                                {
                                    if (it.Center) t.AlignCenter();
                                    t.Span(it.Text ?? string.Empty);
                                });
                            }
                            else if (it.Type == DocItemType.Image && it.ImageBytes is not null)
                            {
                                col.Item().PaddingBottom(18).Image(it.ImageBytes).FitWidth();
                            }
                        }
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

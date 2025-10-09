using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace ForeSITEScheduler
{
    // ===== SMTP config =====
    public  sealed class SmtpConfig
    {
        public string Host { get; init; } = "";
        public int Port { get; init; } = 587;
        public bool EnableSsl { get; init; } = true;
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public string From { get; init; } = "";     // 
        public string? FromDisplay { get; init; }   // display name


        // 
        public static SmtpConfig LoadSmtpConfig()
        {
            // 1) 
            string host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "";
            string portStr = Environment.GetEnvironmentVariable("SMTP_PORT") ?? "";
            string user = Environment.GetEnvironmentVariable("SMTP_USER") ?? "";
            string pass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? "";
            string from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? "";
            string enableSslStr = Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL") ?? "";

            int port = 0;
            bool enableSsl = true;
            int.TryParse(portStr, out port);

            if (string.IsNullOrWhiteSpace(enableSslStr) || !bool.TryParse(enableSslStr, out enableSsl))
                enableSsl = true;

            if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(from))
            {
                return new SmtpConfig
                {
                    Host = host,
                    Port = port > 0 ? port : 587,
                    EnableSsl = enableSsl,
                    Username = user,
                    Password = pass,
                    From = from,
                    FromDisplay = Environment.GetEnvironmentVariable("SMTP_FROM_NAME")
                };
            }

            // 2) read config.json
            string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string pythonDirectory = Path.Combine(currentDirectory, "Server");
            var jsonPath = Path.Combine(pythonDirectory, "config.json");
            Console.WriteLine($"Looking for SMTP config in: {jsonPath}");


            if (File.Exists(jsonPath))
            {
                var jo = JObject.Parse(File.ReadAllText(jsonPath));
                return new SmtpConfig
                {
                    Host = jo["host"]?.ToString() ?? "",
                    Port = int.TryParse(jo["port"]?.ToString(), out var p) ? p : 587,
                    EnableSsl = jo["enableSsl"]?.ToObject<bool?>() ?? true,
                    Username = jo["username"]?.ToString() ?? "",
                    Password = jo["password"]?.ToString() ?? "",
                    From = jo["from"]?.ToString() ?? "",
                    FromDisplay = jo["fromDisplay"]?.ToString()
                };
            }

            // 3)
            return new SmtpConfig();
        }

        // resolve recipients string
        public static List<string> ParseRecipients(string recipients)
        {
            return (recipients ?? "")
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // send email
        public static async Task SendReportEmailAsync(
            SmtpConfig cfg, IEnumerable<string> recipients, string subject, string body, string attachmentPath)
        {
            var toList = recipients?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new();
            if (toList.Count == 0) return;
            if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.From))
                throw new InvalidOperationException("SMTP not configured. Missing Host/From.");

            using var msg = new MailMessage
            {
                From = string.IsNullOrWhiteSpace(cfg.FromDisplay)
                       ? new MailAddress(cfg.From)
                       : new MailAddress(cfg.From, cfg.FromDisplay),
                Subject = subject ?? "",
                Body = body ?? "",
                IsBodyHtml = false
            };

            foreach (var r in toList)
                msg.To.Add(new MailAddress(r));

            if (!string.IsNullOrWhiteSpace(attachmentPath) && File.Exists(attachmentPath))
                msg.Attachments.Add(new Attachment(attachmentPath));

            using var client = new SmtpClient(cfg.Host, cfg.Port)
            {
                EnableSsl = cfg.EnableSsl
            };

            if (!string.IsNullOrWhiteSpace(cfg.Username))
            {
                client.Credentials = new NetworkCredential(cfg.Username, cfg.Password);
            }
            else
            {
                client.UseDefaultCredentials = false;
            }

            await client.SendMailAsync(msg);
        }


    }
}

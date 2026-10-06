using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Diagnostics;

namespace ForeSITEScheduler;

internal static class WindowsNotificationService
{
    private static readonly AppNotificationManager Manager = AppNotificationManager.Default;
    private static readonly TaskCompletionSource<bool> ActivationHandled =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool _registered;

    public static bool IsAvailable => _registered;

    public static async Task<bool> InitializeAndHandleActivationAsync()
    {
        if (!AppNotificationManager.IsSupported())
            return false;

        Manager.NotificationInvoked += OnNotificationInvoked;
        Manager.Register();
        _registered = true;

        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activationArgs.Kind != ExtendedActivationKind.AppNotification)
            return false;

        try
        {
            await ActivationHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Notification activation timed out before its arguments were received.");
        }

        return true;
    }

    public static void ShowReportReady(int schedulerId, string reportPath)
    {
        if (!_registered)
            throw new InvalidOperationException(
                "Windows notifications are unavailable. Confirm the task runs as the signed-in, non-elevated user.");

        string fullPath = ValidateReportPath(reportPath);
        string reportName = Path.GetFileName(fullPath);

        var openButton = new AppNotificationButton("Open report")
            .AddArgument("action", "openReport")
            .AddArgument("reportPath", fullPath);

        var notification = new AppNotificationBuilder()
            .AddArgument("action", "openReport")
            .AddArgument("reportPath", fullPath)
            .AddText("ForeSITE report is ready")
            .AddText(reportName)
            .AddText($"Scheduled job {schedulerId} completed at {DateTime.Now:g}.")
            .AddButton(openButton)
            .BuildNotification();

        Manager.Show(notification);
    }

    public static void Shutdown()
    {
        if (!_registered)
            return;

        Manager.NotificationInvoked -= OnNotificationInvoked;
        Manager.Unregister();
        _registered = false;
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        try
        {
            var values = ParseArguments(args.Argument);
            if (!values.TryGetValue("action", out string? action) ||
                !string.Equals(action, "openReport", StringComparison.OrdinalIgnoreCase) ||
                !values.TryGetValue("reportPath", out string? reportPath))
            {
                return;
            }

            string fullPath = ValidateReportPath(reportPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unable to open the report from the notification: {ex.Message}");
        }
        finally
        {
            ActivationHandled.TrySetResult(true);
        }
    }

    private static Dictionary<string, string> ParseArguments(string arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in (arguments ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = item.Split('=', 2);
            string key = Uri.UnescapeDataString(pair[0]);
            string value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            values[key] = value;
        }

        return values;
    }

    private static string ValidateReportPath(string reportPath)
    {
        string fullPath = Path.GetFullPath(reportPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The notification target is not a PDF report.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The generated report no longer exists.", fullPath);
        return fullPath;
    }
}

// -----------------------------------------------------------------------------
//  Author:      Tao He
//  Email:       tao.he@utah.edu
//  Created:     2025-07-01
//  Description: Dashboard user control logic for ForeSITETestApp (WPF).
// -----------------------------------------------------------------------------


using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using Microsoft.Xaml.Behaviors.Layout;
using Newtonsoft.Json.Linq;

using OllamaSharp.Models;

//using PdfSharp.Drawing;
//using PdfSharp.Fonts;
//using PdfSharp.Pdf;
// remove：using PdfSharp.*; and GlobalFontSettings.FontResolver 
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ForeSITETestApp
{
    /// <summary>
    /// Interaction logic for Dashboard.xaml
    /// </summary>
    public partial class Dashboard : UserControl
    {
        private readonly MainWindow window;
        private readonly HttpClient _httpClient;
        private ObservableCollection<DataSource>? _dataSources;
        private List<ReportElement> _reportElements; // Track all elements
        private RichTextEditorWindow? _editorWindow;
        private FlowDocument? _titleDocument; // Store the rich text document

        private TextBlock? _placeholderTextBlock; // Class-level field

        private NotebookWindow? _notebookWindow;

        private ObservableCollection<Model> _models = new();
        private readonly ObservableCollection<ConfigEntry> _systemConfigEntries = new();
        private ObservableCollection<ReportRecord> _allReports = new();
        private ReportRecord? _selectedReport;

        public Dashboard(MainWindow window)
        {
            InitializeComponent();
            this.window = window;
            _httpClient = window.getHttpClient();
            _reportElements = new List<ReportElement>(); // Initialize report elements list
            InitializeDataSources();

            InitializeModels();

            InitTimeSelectors();

            UpdateSchedulerButtons();


            ObservableCollection<SchedulerTask> schedulers = DBHelper.GetAllSchedulers();
            SchedulerTable.ItemsSource = schedulers;
            UpdateSchedulerSelectionState();
            RefreshReportLibrary();


            // Initialize default FlowDocument
            _titleDocument = new FlowDocument(new Paragraph(new Run("Click to edit title")));
            // Set custom font resolver
            //GlobalFontSettings.FontResolver = new CustomFontResolver();
            DrawingCanvas.Height = 668; // Report-page working area inside the live canvas
            CheckAndManagePlaceholder();

            DataContext = this;
        }

        private const string TaskName = "ForeSITE_Alerting_Scheduler";

        private void UpdateSchedulerButtons()
        {
            bool exists = TaskExists(TaskName);

            BtnStart.IsEnabled = true;
            BtnStart.Content = exists ? "UPDATE SERVICE" : "START SERVICE";
            BtnEnd.IsEnabled = exists;
            TimeSpan? configuredTime = exists ? GetConfiguredTaskTime() : null;
            if (configuredTime.HasValue)
            {
                HourSelector.SelectedItem = configuredTime.Value.Hours.ToString("00");
                MinuteSelector.SelectedItem = configuredTime.Value.Minutes.ToString("00");
            }
            SchedulerServiceStatusText.Text = exists
                ? configuredTime.HasValue ? $"Active · daily at {configuredTime.Value:hh\\:mm}" : "Active"
                : "Not configured";
            SchedulerServiceStatusDot.Fill = new SolidColorBrush(exists
                ? System.Windows.Media.Color.FromRgb(46, 125, 50)
                : System.Windows.Media.Color.FromRgb(158, 171, 180));
        }

        private TimeSpan? GetConfiguredTaskTime()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", $"/query /tn {TaskName} /xml")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null)
                    return null;

                string xml = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(xml))
                    return null;

                string? startBoundary = XDocument.Parse(xml)
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "StartBoundary")
                    ?.Value;

                return DateTimeOffset.TryParse(startBoundary, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var scheduledAt)
                    ? scheduledAt.TimeOfDay
                    : null;
            }
            catch
            {
                return null;
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void InitTimeSelectors()
        {
            HourSelector.ItemsSource = Enumerable.Range(0, 24).Select(i => i.ToString("00"));
            MinuteSelector.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00"));

          
            // HourSelector.SelectedItem = next.Hour.ToString("00");
            // MinuteSelector.SelectedItem = next.Minute.ToString("00");
        }

        private void InitializeDataSources()
        {
            // Load data sources from the database
            _dataSources = DBHelper.GetAllDataSources();

            DataSourceTable.ItemsSource = _dataSources;
            DataSourceSelector.ItemsSource = _dataSources; // Bind to DataSourceSelector
        }

        private void InitializeModels()
        {
            // read all models
            _models = DBHelper.GetAllmodels();

            

            // bind（lstModel）
            lstModel.ItemsSource = _models;
            //bind （ModelSelector）
            ModelSelector.ItemsSource = _models;

            if (_models.Any())
            {
                lstModel.SelectedIndex = 0;
                ModelSelector.SelectedIndex = 4;
            }

        }

    
        private void SchedulerButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNavigation(SchedulerButton);
            HeaderTitle.Text = "Schedule Management";
            SchedulerGrid.Visibility = Visibility.Visible;
            ReportsGrid.Visibility = Visibility.Collapsed;
            ReportLibraryGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Collapsed;
            ModelGrid.Visibility = Visibility.Collapsed;
            SetupGrid.Visibility = Visibility.Collapsed;
        }

        private void ReportButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNavigation(ReportButton);
            HeaderTitle.Text = "Report Library";
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            ReportLibraryGrid.Visibility = Visibility.Visible;
            DataSourceGrid.Visibility = Visibility.Collapsed;
            ModelGrid.Visibility = Visibility.Collapsed;
            SetupGrid.Visibility = Visibility.Collapsed;
            RefreshReportLibrary();
        }

        private void LibraryModeButton_Click(object sender, RoutedEventArgs e) => ShowReportLibrary();

        private void BuilderModeButton_Click(object sender, RoutedEventArgs e) => ShowReportBuilder();

        private void NewReportButton_Click(object sender, RoutedEventArgs e) => ShowReportBuilder();

        private void ShowReportLibrary(int? selectReportId = null)
        {
            HeaderTitle.Text = "Report Library";
            ReportsGrid.Visibility = Visibility.Collapsed;
            ReportLibraryGrid.Visibility = Visibility.Visible;
            LibraryModeButton.Foreground = (Brush)new BrushConverter().ConvertFrom("#F2A900")!;
            LibraryModeButton.Background = (Brush)new BrushConverter().ConvertFrom("#27333B")!;
            LibraryModeButton.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#F2A900")!;
            BuilderModeButton.Foreground = (Brush)new BrushConverter().ConvertFrom("#CFD7DD")!;
            BuilderModeButton.Background = Brushes.Transparent;
            BuilderModeButton.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#596873")!;
            RefreshReportLibrary(selectReportId);
        }

        private void ShowReportBuilder()
        {
            HeaderTitle.Text = "Report Builder";
            ReportLibraryGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Visible;
            BuilderModeButton.Foreground = (Brush)new BrushConverter().ConvertFrom("#F2A900")!;
            BuilderModeButton.Background = (Brush)new BrushConverter().ConvertFrom("#27333B")!;
            BuilderModeButton.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#F2A900")!;
            LibraryModeButton.Foreground = (Brush)new BrushConverter().ConvertFrom("#CFD7DD")!;
            LibraryModeButton.Background = Brushes.Transparent;
            LibraryModeButton.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#596873")!;
        }

        private void RefreshReportLibrary(int? selectReportId = null)
        {
            try
            {
                int? desiredId = selectReportId ?? _selectedReport?.Id;
                _allReports = DBHelper.GetAllReports();
                ApplyReportFilter(desiredId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load saved reports.\n\n{ex.Message}", "Report Library",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyReportFilter(int? selectReportId = null)
        {
            string search = ReportSearchBox?.Text?.Trim() ?? "";
            var filtered = _allReports
                .Where(r => string.IsNullOrEmpty(search) || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ReportLibraryList.ItemsSource = filtered;
            ReportLibraryList.SelectedItem = selectReportId.HasValue
                ? filtered.FirstOrDefault(r => r.Id == selectReportId.Value)
                : filtered.FirstOrDefault();
            if (filtered.Count == 0)
                ClearReportView(search.Length == 0 ? "No saved reports" : "No reports match your search");
        }

        private void ReportSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyReportFilter(_selectedReport?.Id);

        private void ReportLibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedReport = ReportLibraryList.SelectedItem as ReportRecord;
            if (_selectedReport == null)
            {
                ClearReportView("Choose a saved report");
                return;
            }

            ReportViewTitle.Text = _selectedReport.Name.ToUpperInvariant();
            ReportDetailsName.Text = _selectedReport.Name;
            ReportDetailsSchedule.Text = _selectedReport.SchedulerId.HasValue
                ? $"{_selectedReport.Frequency} · {(_selectedReport.ScheduleEnabled ? "Active" : "Paused")}" : "Not scheduled";
            ReportDetailsDelivery.Text = _selectedReport.DeliveryMethod;
            ReportDetailsGenerated.Text = _selectedReport.LastGeneratedAt?.ToString("g") ?? "Never";
            LoadDefinitionProvenance(_selectedReport);
            RunSavedReportButton.IsEnabled = true;
            EditSavedReportButton.IsEnabled = true;

            var runs = DBHelper.GetReportRuns(_selectedReport.Id);
            ReportRunHistoryList.ItemsSource = runs;
            ReportRunHistoryEmpty.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var latestCompleted = runs.FirstOrDefault(r => r.CompletedAt.HasValue && !string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase));
            ReportRunHistoryList.SelectedItem = latestCompleted ?? runs.FirstOrDefault();
            if (runs.Count == 0)
            {
                ReportViewStatus.Text = "READY TO RUN";
                ReportDetailsDataAsOf.Text = "Not available";
                OpenLatestPdfButton.IsEnabled = false;
                RenderReportDocument(_selectedReport.DefinitionJson, isSnapshot: false);
            }
        }

        private void ReportRunHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReportRunHistoryList.SelectedItem is not ReportRunRecord run)
                return;

            ReportViewStatus.Text = run.Status.ToUpperInvariant();
            ReportDetailsGenerated.Text = run.CompletedAt?.ToString("g") ?? "In progress";
            ReportDetailsDataAsOf.Text = run.DataAsOf ?? "Not available";
            OpenLatestPdfButton.IsEnabled = !string.IsNullOrWhiteSpace(run.PdfPath) && File.Exists(run.PdfPath);
            if (string.Equals(run.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                ClearReportContent("Report run failed", run.ErrorMessage ?? "No error details were recorded.");
                return;
            }
            RenderReportDocument(run.SnapshotJson, isSnapshot: true);
        }

        private async void RunSavedReportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedReport == null) return;
            ReportRecord report = _selectedReport;
            DateTime startedAt = DateTime.Now;
            RunSavedReportButton.IsEnabled = false;
            RunSavedReportButton.Content = "RUNNING…";
            ReportViewStatus.Text = "RUNNING";

            try
            {
                JObject definition = JObject.Parse(report.DefinitionJson);
                var snapshotLayout = new JArray();
                bool isAbnormal = false;
                foreach (JObject item in definition["layout"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    string type = item.Value<string>("type") ?? "";
                    if (!type.Equals("Plot", StringComparison.OrdinalIgnoreCase))
                    {
                        snapshotLayout.Add(item.DeepClone());
                        continue;
                    }

                    JObject graph = (item["params"] as JObject)?.DeepClone() as JObject ?? new JObject();
                    graph["designFlag"] = true;
                    graph["abnormalReportFlag"] = false;
                    using var content = new StringContent(new JObject { ["graph"] = graph }.ToString(), Encoding.UTF8, "application/json");
                    using HttpResponseMessage response = await _httpClient.PostAsync("/epyapi", content);
                    string body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Plot generation failed ({(int)response.StatusCode}). {TryGetApiError(body)}");

                    JObject result = JObject.Parse(body);
                    string imagePath = result.Value<string>("plot_path") ?? "";
                    if (!File.Exists(imagePath)) throw new FileNotFoundException("The plot service did not create the expected image.", imagePath);
                    bool plotAbnormal = result.Value<bool?>("abnormal") ?? false;
                    isAbnormal |= plotAbnormal;
                    snapshotLayout.Add(new JObject
                    {
                        ["type"] = "Plot", ["params"] = graph,
                        ["imagePath"] = imagePath, ["abnormal"] = plotAbnormal
                    });
                }

                DateTime completedAt = DateTime.Now;
                JObject snapshot = new()
                {
                    ["generatedAt"] = completedAt.ToString("O"),
                    ["dataAsOf"] = DateTime.Today.ToString("yyyy-MM-dd"),
                    ["abnormal"] = isAbnormal,
                    ["layout"] = snapshotLayout
                };
                string outputDirectory = Path.Combine(AppPaths.ReportsDirectory, report.Id.ToString(CultureInfo.InvariantCulture), "Runs");
                Directory.CreateDirectory(outputDirectory);
                string pdfPath = Path.Combine(outputDirectory, $"Report_{completedAt:yyyyMMdd_HHmmss}.pdf");
                GenerateSnapshotPdf(snapshot, pdfPath, report.Name);
                DBHelper.InsertReportRun(new ReportRunRecord
                {
                    ReportId = report.Id, SchedulerId = null, StartedAt = startedAt,
                    CompletedAt = completedAt, DataAsOf = DateTime.Today.ToString("yyyy-MM-dd"),
                    Status = isAbnormal ? "Abnormal" : "Normal", IsAbnormal = isAbnormal,
                    PdfPath = pdfPath, SnapshotJson = snapshot.ToString(Newtonsoft.Json.Formatting.None)
                });
                RefreshReportLibrary(report.Id);
            }
            catch (Exception ex)
            {
                DBHelper.InsertReportRun(new ReportRunRecord
                {
                    ReportId = report.Id, SchedulerId = null, StartedAt = startedAt,
                    CompletedAt = DateTime.Now, Status = "Failed", ErrorMessage = ex.Message
                });
                RefreshReportLibrary(report.Id);
                MessageBox.Show(ex.Message, "Report Run Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RunSavedReportButton.Content = "RUN NOW";
                RunSavedReportButton.IsEnabled = _selectedReport != null;
            }
        }

        private void OpenLatestPdfButton_Click(object sender, RoutedEventArgs e)
        {
            string? path = (ReportRunHistoryList.SelectedItem as ReportRunRecord)?.PdfPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("The PDF for this run is not available.", "Open PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private void EditSavedReportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedReport == null) return;
            try
            {
                JObject definition = JObject.Parse(_selectedReport.DefinitionJson);
                JObject? graph = definition["layout"]?.OfType<JObject>()
                    .FirstOrDefault(i => string.Equals(i.Value<string>("type"), "Plot", StringComparison.OrdinalIgnoreCase))?["params"] as JObject;
                if (graph != null)
                {
                    DataSourceSelector.SelectedItem = _dataSources?.FirstOrDefault(d => string.Equals(d.Name, graph.Value<string>("dataSource"), StringComparison.OrdinalIgnoreCase));
                    ModelSelector.SelectedItem = _models.FirstOrDefault(m => string.Equals(m.Name, graph.Value<string>("model"), StringComparison.OrdinalIgnoreCase));
                    if (DateTime.TryParse(graph.Value<string>("beginDate"), out var beginDate)) BeginDatePicker.SelectedDate = beginDate;
                    SelectComboItem(FreqSelector, graph.Value<string>("freq"));
                    ThresholdInput.Text = graph.Value<string>("threshold") ?? ThresholdInput.Text;
                    TrainSplitCheckBox.IsChecked = graph.Value<bool?>("useTrainSplit") ?? false;
                    TrainSplitRatioInput.Text = graph.Value<string>("trainSplitRatio") ?? TrainSplitRatioInput.Text;
                    if (DateTime.TryParse(graph.Value<string>("trainEndDate"), out var trainEnd)) TrainEndDatePicker.SelectedDate = trainEnd;
                    TxtBaseline.Text = graph.Value<string>("baseline") ?? TxtBaseline.Text;
                    TxtMcMunu.Text = graph.Value<string>("mc_munu") ?? TxtMcMunu.Text;
                    SelectComboItem(YearBackSelector, graph.Value<string>("yearBack"));
                }
                ShowReportBuilder();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load this report definition.\n\n{ex.Message}", "Edit Report", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void SelectComboItem(ComboBox combo, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            combo.SelectedItem = combo.Items.OfType<object>().FirstOrDefault(item =>
                string.Equals((item as ComboBoxItem)?.Content?.ToString() ?? item.ToString(), value, StringComparison.OrdinalIgnoreCase));
        }

        private void LoadDefinitionProvenance(ReportRecord report)
        {
            try
            {
                JObject definition = JObject.Parse(report.DefinitionJson);
                JObject? graph = definition["layout"]?.OfType<JObject>()
                    .FirstOrDefault(i => string.Equals(i.Value<string>("type"), "Plot", StringComparison.OrdinalIgnoreCase))?["params"] as JObject;
                ReportDetailsDataSource.Text = graph?.Value<string>("dataSource") ?? "Not available";
                ReportDetailsAlgorithm.Text = graph?.Value<string>("model") ?? "Not available";
                string begin = graph?.Value<string>("beginDate") ?? "Start not set";
                string frequency = graph?.Value<string>("freq") ?? report.Frequency;
                ReportDetailsPeriod.Text = $"From {begin} · {frequency}";
                ReportDetailsThreshold.Text = graph?.Value<string>("threshold") ?? "Not available";
                ReportDetailsDefinition.Text = $"v1 · {report.UpdatedAt:g}";
            }
            catch
            {
                ReportDetailsDataSource.Text = ReportDetailsAlgorithm.Text = ReportDetailsPeriod.Text =
                    ReportDetailsThreshold.Text = "Not available";
                ReportDetailsDefinition.Text = report.UpdatedAt == DateTime.MinValue ? "Not available" : $"v1 · {report.UpdatedAt:g}";
            }
        }

        private void ClearReportView(string heading)
        {
            _selectedReport = null;
            ReportViewTitle.Text = "REPORT VIEW";
            ReportViewStatus.Text = "SELECT A REPORT";
            ReportDetailsName.Text = "No report selected";
            ReportDetailsDataSource.Text = "Not available";
            ReportDetailsAlgorithm.Text = "Not available";
            ReportDetailsPeriod.Text = "Not available";
            ReportDetailsThreshold.Text = "Not available";
            ReportDetailsDefinition.Text = "Not available";
            ReportDetailsSchedule.Text = "Not scheduled";
            ReportDetailsDelivery.Text = "None";
            ReportDetailsGenerated.Text = "Never";
            ReportDetailsDataAsOf.Text = "Not available";
            ReportRunHistoryList.ItemsSource = null;
            ReportRunHistoryEmpty.Visibility = Visibility.Visible;
            RunSavedReportButton.IsEnabled = OpenLatestPdfButton.IsEnabled = EditSavedReportButton.IsEnabled = false;
            ClearReportContent(heading, "Create a report in Report Builder or choose one from the library.");
        }

        private void ClearReportContent(string heading, string detail)
        {
            ReportViewContent.Children.Clear();
            ReportViewContent.Children.Add(new TextBlock { Text = heading, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(73, 87, 96)), FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 50, 8, 10) });
            ReportViewContent.Children.Add(new TextBlock { Text = detail, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 119, 129)), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 0) });
        }

        private void RenderReportDocument(string json, bool isSnapshot)
        {
            ReportViewContent.Children.Clear();
            JObject document = JObject.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var layout = document["layout"]?.OfType<JObject>().ToList() ?? new List<JObject>();
            if (layout.Count == 0)
            {
                ClearReportContent("This report has no content", "Open it in Report Builder to add a title, narrative, or chart.");
                return;
            }
            foreach (JObject item in layout)
            {
                string type = item.Value<string>("type") ?? "";
                if (type.Equals("Title", StringComparison.OrdinalIgnoreCase))
                    ReportViewContent.Children.Add(new TextBlock { Text = item["content"]?.Value<string>("text") ?? "", Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 42, 49)), FontSize = 26, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 22) });
                else if (type.Equals("Comment", StringComparison.OrdinalIgnoreCase))
                    ReportViewContent.Children.Add(new TextBlock { Text = item["content"]?.Value<string>("text") ?? "", Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 58, 66)), FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, 0, 0, 20) });
                else if (type.Equals("Plot", StringComparison.OrdinalIgnoreCase))
                {
                    string? imagePath = item.Value<string>("imagePath") ?? item.Value<string>("plot_path");
                    if (isSnapshot && !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(imagePath, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                        ReportViewContent.Children.Add(new System.Windows.Controls.Image { Source = bitmap, MaxWidth = 680, Height = 390, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 22) });
                    }
                    else
                    {
                        JObject? parameters = item["params"] as JObject;
                        var panel = new StackPanel();
                        panel.Children.Add(new TextBlock { Text = parameters?.Value<string>("title") ?? "Surveillance visualization", Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 42, 49)), FontSize = 17, FontWeight = FontWeights.SemiBold });
                        panel.Children.Add(new TextBlock { Text = $"{parameters?.Value<string>("dataSource") ?? "Data source"} · {parameters?.Value<string>("model") ?? "Model"}", Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(89, 105, 115)), FontSize = 13, Margin = new Thickness(0, 5, 0, 0) });
                        panel.Children.Add(new TextBlock { Text = "Visualization will be generated when you run this report.", Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 119, 129)), FontSize = 13, Margin = new Thickness(0, 22, 0, 0) });
                        ReportViewContent.Children.Add(new Border { BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 193, 198)), BorderThickness = new Thickness(1), Padding = new Thickness(22), MinHeight = 150, Child = panel, Margin = new Thickness(0, 0, 0, 22) });
                    }
                }
            }
        }

        private static string TryGetApiError(string responseBody)
        {
            try
            {
                JObject json = JObject.Parse(responseBody);
                return json.Value<string>("detail") ?? json.Value<string>("error") ?? json.Value<string>("message") ?? responseBody;
            }
            catch { return responseBody.Length > 500 ? responseBody[..500] : responseBody; }
        }

        private static void GenerateSnapshotPdf(JObject snapshot, string outputPath, string reportName)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var layout = snapshot["layout"]?.OfType<JObject>().ToList() ?? new List<JObject>();
            Document.Create(container => container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(36); page.DefaultTextStyle(x => x.FontSize(11));
                page.Header().Text(reportName).FontSize(9).FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
                page.Content().Column(column =>
                {
                    foreach (JObject item in layout)
                    {
                        string type = item.Value<string>("type") ?? "";
                        if (type.Equals("Title", StringComparison.OrdinalIgnoreCase))
                            column.Item().PaddingBottom(14).AlignCenter().Text(item["content"]?.Value<string>("text") ?? "").FontSize(22).Bold();
                        else if (type.Equals("Comment", StringComparison.OrdinalIgnoreCase))
                            column.Item().PaddingBottom(12).Text(item["content"]?.Value<string>("text") ?? "");
                        else if (type.Equals("Plot", StringComparison.OrdinalIgnoreCase))
                        {
                            string? path = item.Value<string>("imagePath") ?? item.Value<string>("plot_path");
                            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) column.Item().PaddingBottom(18).Image(File.ReadAllBytes(path)).FitWidth();
                        }
                    }
                });
                page.Footer().AlignCenter().Text(text => { text.Span("Page "); text.CurrentPageNumber(); text.Span(" / "); text.TotalPages(); });
            })).GeneratePdf(outputPath);
        }

        private void DataSourceButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNavigation(DataSourceButton);
            HeaderTitle.Text = "Data Source Management";
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            ReportLibraryGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Visible;
            ModelGrid.Visibility = Visibility.Collapsed;
            SetupGrid.Visibility = Visibility.Collapsed;
        }

        private void ModelButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNavigation(ModelButton);
            HeaderTitle.Text = "Algorithms";
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            ReportLibraryGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Collapsed;
            ModelGrid.Visibility = Visibility.Visible;
            SetupGrid.Visibility = Visibility.Collapsed;
        }

        private async void LoadDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prepare JSON payload
                string json = "{\"init\": \"run\"}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Send POST request to 127.0.0.1
                HttpResponseMessage response = await _httpClient.PostAsync("/epyapi", content);

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show("JSON message sent successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to send JSON message. Status: {response.StatusCode}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending JSON message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CheckAndManagePlaceholder()
        {
            if (DrawingCanvas.Children.Count == 0)
            {
                if (_placeholderTextBlock == null)
                {
                    _placeholderTextBlock = new TextBlock
                    {
                        Text = "Build your report\n\nAdd a title, narrative, or chart from the inspector.\nRun Report to generate the current surveillance visualization.",
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 105, 115)),
                        FontSize = 15,
                        LineHeight = 24,
                        TextAlignment = TextAlignment.Left,
                        MaxWidth = 430
                    };
                    Canvas.SetLeft(_placeholderTextBlock, 42);
                    Canvas.SetTop(_placeholderTextBlock, 42);
                    DrawingCanvas.Children.Add(_placeholderTextBlock);
                }
            }
            else if (_placeholderTextBlock != null)
            {
                DrawingCanvas.Children.Remove(_placeholderTextBlock);
                _placeholderTextBlock = null;
            }
        }

        private void AIButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ChatDialog();
            dialog.ShowDialog();

        }

        // Helper method to add new data source to database
        private bool AddDataSourceToDatabase(string name, string dataUrl, string resourceUrl, string appToken, bool isRealtime)
        {
            return DBHelper.InsertDataSource(new DataSource
            {
                Name = name,
                DataURL = dataUrl,
                ResourceURL = resourceUrl,
                AppToken = appToken,
                IsRealtime = isRealtime
            });

        }

        // Helper method to refresh data sources list
        private void RefreshDataSourcesList()
        {
            try
            {
                // Get updated data sources from database
                var localDataSources = DBHelper.GetAllDataSources();

                // Update _dataSources collection (assuming it exists)
                if (_dataSources != null)
                {
                    _dataSources.Clear();
                    foreach (var dataSource in localDataSources)
                    {
                        _dataSources.Add(new DataSource
                        {
                            Name = dataSource.Name,
                            DataURL = dataSource.DataURL,
                            ResourceURL = dataSource.ResourceURL,
                            AppToken = dataSource.AppToken,
                            IsRealtime = dataSource.IsRealtime,
                            IsSelected = false
                        });
                    }
                }
                // Refresh the data source toolbar if it exists
                if (_notebookWindow != null)
                {
                    _notebookWindow.LoadDataSources();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing data sources list: {ex.Message}");
            }
        }


        private void AddDataSourceButton_Click(object sender, RoutedEventArgs e)
        {
            // The form is a permanent tab. Repeated clicks simply return to the same instance.
            DataSourceTabs.SelectedItem = NewDataSourceTab;
            DataSourceNameInput.Focus();
        }

        private void DataSourceType_Checked(object sender, RoutedEventArgs e)
        {
            if (RealtimeFieldsPanel == null || StaticFieldsPanel == null || DataSourceFormStatus == null)
                return;

            bool isRealtime = RealtimeRadio.IsChecked == true;
            RealtimeFieldsPanel.Visibility = isRealtime ? Visibility.Visible : Visibility.Collapsed;
            StaticFieldsPanel.Visibility = isRealtime ? Visibility.Collapsed : Visibility.Visible;
            DataSourceFormStatus.Text = isRealtime
                ? "All fields are required for API sources."
                : "Choose one CSV file that will remain available at this location.";
        }

        private void BrowseDataSourceFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select CSV Data File",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedFilePathBox.Text = openFileDialog.FileName;
                SelectedFilePathBox.ToolTip = openFileDialog.FileName;
                SelectedFilePathBox.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(228, 233, 236));
            }
        }

        private void CancelDataSourceButton_Click(object sender, RoutedEventArgs e)
        {
            ResetDataSourceForm();
            DataSourceTabs.SelectedItem = DataSourceListTab;
        }

        private void ResetDataSourceForm()
        {
            DataSourceNameInput.Clear();
            DataUrlInput.Clear();
            ResourceUrlInput.Clear();
            AppTokenInput.Clear();
            SelectedFilePathBox.Text = "No file selected";
            SelectedFilePathBox.ToolTip = null;
            SelectedFilePathBox.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 171, 180));
            RealtimeRadio.IsChecked = true;
            DataSourceFormStatus.Text = "All fields are required for API sources.";
        }

        private void SaveDataSourceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = DataSourceNameInput.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Enter a name for this data source.", "Name Required",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    DataSourceNameInput.Focus();
                    return;
                }

                if (_dataSources != null && _dataSources.Any(ds => string.Equals(ds.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show($"A data source named '{name}' already exists. Enter a different name or delete the existing source first.",
                                  "Name Already Used", MessageBoxButton.OK, MessageBoxImage.Warning);
                    DataSourceNameInput.Focus();
                    DataSourceNameInput.SelectAll();
                    return;
                }

                bool isRealtime = RealtimeRadio.IsChecked == true;
                string dataUrl;
                string resourceUrl;
                string appToken = "";

                if (isRealtime)
                {
                    if (string.IsNullOrWhiteSpace(DataUrlInput.Text))
                    {
                        MessageBox.Show("Enter the API data URL.", "Data URL Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        DataUrlInput.Focus();
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(ResourceUrlInput.Text))
                    {
                        MessageBox.Show("Enter the resource URL or dataset ID.", "Resource Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ResourceUrlInput.Focus();
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(AppTokenInput.Password))
                    {
                        MessageBox.Show("Enter the app token used by this API.", "App Token Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        AppTokenInput.Focus();
                        return;
                    }
                    dataUrl = DataUrlInput.Text.Trim();
                    resourceUrl = ResourceUrlInput.Text.Trim();
                    appToken = AppTokenInput.Password;
                }
                else
                {
                    string filePath = SelectedFilePathBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(filePath) || filePath == "No file selected")
                    {
                        MessageBox.Show("Choose a CSV file before saving.", "CSV File Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!File.Exists(filePath))
                    {
                        MessageBox.Show("The selected CSV file can no longer be found. Choose the file again.",
                                      "CSV File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    dataUrl = filePath;
                    resourceUrl = "local";
                }

                if (AddDataSourceToDatabase(name, dataUrl, resourceUrl, appToken, isRealtime))
                {
                    RefreshDataSourcesList();
                    ResetDataSourceForm();
                    DataSourceTabs.SelectedItem = DataSourceListTab;
                    MessageBox.Show($"'{name}' is ready to use in reports and notebooks.", "Data Source Saved",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MessageBox.Show("ForeSITE could not save this data source. Check the values and try again.",
                              "Unable to Save", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ForeSITE could not save this data source.\n\n{ex.Message}",
                              "Unable to Save", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DataSourceTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteDataSourceButton.IsEnabled = DataSourceTable.SelectedItem is DataSource;
        }

        private void DeleteDataSourceButton_Click(object sender, RoutedEventArgs e)
        {

            if (DataSourceTable.SelectedItem is DataSource selectedDataSource)
            {
                if (string.IsNullOrWhiteSpace(selectedDataSource.Name))
                {
                    MessageBox.Show("The selected data source does not have a valid name.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var result = MessageBox.Show(
                    $"Are you sure you want to delete the data source '{selectedDataSource.Name}'?",
                    "Confirm Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    // Delete from database
                    bool success = DBHelper.DeleteDataSourceByName(selectedDataSource.Name!);
                    if (success)
                    {
                        // Refresh data sources list
                        RefreshDataSourcesList();
                        MessageBox.Show("Data source deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete the data source. Please check the database connection.",
                                      "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a data source to delete.", "Selection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SchedulingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_reportElements == null || _reportElements.Count == 0)
                {
                    MessageBox.Show("No content on the canvas to export as a template.",
                                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string reportName = Interaction.InputBox(
                    "Name this report. It will appear in Report Library.",
                    "Save & Schedule Report",
                    $"Surveillance Report {DateTime.Today:yyyy-MM-dd}").Trim();
                if (string.IsNullOrWhiteSpace(reportName))
                    return;


                var root = new JObject
                {
                    ["templateVersion"] = "1.0",
                    ["createdAt"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    ["app"] = "ForeSITE",
                    ["name"] = reportName,
                    ["canvas"] = new JObject
                    {
                        ["width"] = Math.Max(DrawingCanvas.ActualWidth, 778),
                        ["height"] = DrawingCanvas.ActualHeight
                    }
                };

                var layout = new JArray();

                foreach (var re in _reportElements)
                {
                    if (re.Element is not Border border) continue;

                    // Title
                    if (re.Type == ReportElementType.Title)
                    {
                        // priority _titleDocument；
                        FlowDocument? titleDoc = _titleDocument ?? TryGetTitleFlowDoc(border);
                        string flowXaml = FlowDocToXaml(titleDoc);
                        string text = FlowDocToPlainText(titleDoc);

                        var titleJson = new JObject
                        {
                            ["type"] = "Title",
                            ["content"] = new JObject
                            {
                                ["flowXaml"] = flowXaml,
                                ["text"] = text
                            }
                        };
                        layout.Add(titleJson);
                    }
                    // Plot
                    else if (re.Type == ReportElementType.Plot)
                    {
                        // read arguments from Tag 
                        var param = ExtractPlotParamsFromTag(border?.Tag);

                        var plotJson = new JObject
                        {
                            ["type"] = "Plot",
                            ["params"] = param
                        };
                        layout.Add(plotJson);
                    }
                    // Comment
                    else if (re.Type == ReportElementType.Comment)
                    {

                        FlowDocument? doc = null;

                        if (border.Tag is CommentMeta meta && meta.Document != null)
                            doc = meta.Document;
                        else
                            doc = (border.Child as Grid)?
                                  .Children.OfType<RichTextBox>().FirstOrDefault()?.Document;

                        string flowXaml = FlowDocToXaml(doc);
                        string text = FlowDocToPlainText(doc);

                        var commentJson = new JObject
                        {
                            ["type"] = "Comment",
                            ["content"] = new JObject
                            {
                                ["flowXaml"] = flowXaml,
                                ["text"] = text
                            }
                        };
                        layout.Add(commentJson);
                    }
                }

                // TODO: it will be nice to validate layout has at least one Plot
                root["layout"] = layout;


                string scheduleStart = "";
                string scheduleFreq = "";


                var firstPlot = _reportElements.FirstOrDefault(e => e.Type == ReportElementType.Plot);
                if (firstPlot?.Element is Border b && b.Tag is JObject tag && tag["graph"] is JObject g)
                {
                    scheduleStart = g["beginDate"]?.ToString() ?? "";
                    scheduleFreq = g["freq"]?.ToString() ?? "";
                }

                bool abnormalReportEnabled = AbnormalReportFlag.IsChecked == true;

                root["schedule"] = new JObject
                {
                    ["startDate"] = scheduleStart,   // e.g. "2025-09-12"
                    ["frequency"] = scheduleFreq,    // e.g. "By Week"
                    ["abnormalReportFlag"] = abnormalReportEnabled,
                    ["cron"] = ""
                };


                string definitionJson = root.ToString(Newtonsoft.Json.Formatting.Indented);
                string definitionPath = Path.Combine(
                    AppPaths.ReportDefinitionsDirectory,
                    $"report_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json");

                // ========== 2) write into scheduler  ==========
                // from x:Name="RecipientEmailsBox"
                string recipients = "";
                if (this.FindName("RecipientEmailsBox") is TextBox recipientBox && !string.IsNullOrWhiteSpace(recipientBox.Text))
                {

                    var lines = recipientBox.Text
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0);
                    recipients = string.Join(",", lines);
                }

                string deliveryMethod = GetSelectedDeliveryMethod();
                if (deliveryMethod == "Email" && string.IsNullOrWhiteSpace(recipients))
                {
                    MessageBox.Show("Enter at least one recipient email address before creating an Email schedule.",
                                    "Recipient Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    RecipientEmailsBox.Focus();
                    return;
                }

                File.WriteAllText(definitionPath, definitionJson);
                int reportId = DBHelper.InsertReport(reportName, definitionJson);

                var task = new SchedulerTask
                {
                    ReportId = reportId,
                    Recipients = recipients,          // allow null or empty
                    AttachmentPath = definitionPath,
                    StartDate = scheduleStart ?? "", // "YYYY-MM-DD"
                    Freq = scheduleFreq ?? "",  // "By Week"/"daily"/"weekly"/...
                    DeliveryMethod = deliveryMethod
                };

                bool ok = DBHelper.InsertScheduler(task);
                if (ok)
                {
                    RefreshSchedulerUI();
                    RefreshReportLibrary(selectReportId: reportId);


                    MessageBox.Show("Report saved to the library and its schedule was created.",
                                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Template saved, but failed to insert scheduler record.",
                                    "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating template: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // refresh Scheduler DataGrid 
        private void RefreshSchedulerUI()
        {

            var latest = DBHelper.GetAllSchedulers(); // ObservableCollection<SchedulerTask>

            if (SchedulerTable.ItemsSource is ObservableCollection<SchedulerTask> current)
            {
                current.Clear();
                foreach (var item in latest)
                    current.Add(item);
            }
            else
            {
                SchedulerTable.ItemsSource = latest;
            }
            UpdateSchedulerSelectionState();
        }


        //  FlowDocument =>xml null-->"
        private static string FlowDocToXaml(FlowDocument? doc)
        {
            if (doc == null) return "";
            try
            {
                string xaml = XamlWriter.Save(doc);
                return xaml ?? "";
            }
            catch { return ""; }
        }


        private static string FlowDocToPlainText(FlowDocument? doc)
        {
            if (doc == null) return "";
            try
            {
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                return (range.Text ?? "").TrimEnd('\r', '\n');
            }
            catch { return ""; }
        }


        private static JObject ExtractPlotParamsFromTag(object? tag)
        {
            // target: only save itle / model / yearBack / trainSplitRatio (if exist)
            // if save into Tag： new JObject { ["graph"] = graphData, ["title"]=..., ... }
            // read first from tag["graph"] ；otherwise read from tag 

            string? title = null;
            string? model = null;
            string? yearBack = null;
            string? trainSplitRatio = null;

            string? dataSource = null;
            string? beginDate = null;
            string? freq = null;
            string? threshold = null;
            string? useTrainSplit = null;
            string? trainEndDate = null;
            string? baseline = null;
            string? mcMunu = null;

            static string? S(JToken? token) => token?.ToString();

            try
            {
                if (tag is JObject jtag)
                {

                    var g = jtag["graph"] as JObject ?? jtag;

                    title = S(jtag["title"]) ?? S(g["Title"]);
                    model = S(g["model"]);
                    yearBack = S(g["yearBack"]) ?? S(g["years_back"]);
                    trainSplitRatio = S(g["trainSplitRatio"]);


                    dataSource = S(g["dataSource"]);
                    beginDate = S(g["beginDate"]);
                    freq = S(g["freq"]);
                    threshold = S(g["threshold"]);
                    useTrainSplit = S(g["useTrainSplit"]);
                    trainEndDate = S(g["trainEndDate"]);
                    baseline = S(g["baseline"]);
                    mcMunu = S(g["mc_munu"]);
                }
            }
            catch { /*  */ }

            var o = new JObject();


            if (!string.IsNullOrEmpty(title)) o["title"] = title;
            if (!string.IsNullOrEmpty(model)) o["model"] = model;
            if (!string.IsNullOrEmpty(yearBack)) o["yearBack"] = yearBack;
            if (!string.IsNullOrEmpty(trainSplitRatio)) o["trainSplitRatio"] = trainSplitRatio;


            if (!string.IsNullOrEmpty(dataSource)) o["dataSource"] = dataSource;
            if (!string.IsNullOrEmpty(beginDate)) o["beginDate"] = beginDate;
            if (!string.IsNullOrEmpty(freq)) o["freq"] = freq;
            if (!string.IsNullOrEmpty(threshold)) o["threshold"] = threshold;
            if (!string.IsNullOrEmpty(useTrainSplit)) o["useTrainSplit"] = useTrainSplit;
            if (!string.IsNullOrEmpty(trainEndDate)) o["trainEndDate"] = trainEndDate;
            if (!string.IsNullOrEmpty(baseline)) o["baseline"] = baseline;
            if (!string.IsNullOrEmpty(mcMunu)) o["mc_munu"] = mcMunu;

            return o;
        }


        private static FlowDocument? TryGetTitleFlowDoc(Border titleBorder)
        {
            if (titleBorder?.Child is Grid g)
            {
                var rtb = g.Children.OfType<RichTextBox>().FirstOrDefault();
                if (rtb?.Document != null) return rtb.Document;

                var tb = g.Children.OfType<TextBlock>().FirstOrDefault();
                if (tb != null)
                {
                    var doc = new FlowDocument(new Paragraph(new Run(tb.Text ?? "")));
                    return doc;
                }
            }
            return null;
        }



        private void AddTitleButton_Click(object sender, RoutedEventArgs e)
        {
            // Close existing editor window if open
            if (_editorWindow != null)
            {
                _editorWindow.Close();
                _editorWindow = null;
            }

            // Create a Border for the title
            Border titleBorder = new Border
            {
                Background = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(5),
                Height = 60
            };

            // Set Width to 90% of Canvas width
            double canvasWidth = Math.Max(DrawingCanvas.ActualWidth, 778);
            titleBorder.Width = canvasWidth * 0.9;

            // Create a Grid to hold TextBlock/RichTextBox and Delete Button
            Grid contentGrid = new Grid
            {
                Margin = new Thickness(0)
            };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });



            // Create a TextBlock to display formatted text
            TextBlock titleTextBlock = new TextBlock
            {
                Text = "Click to edit title",
                Foreground = Brushes.Gray,
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            // Apply initial formatting from _titleDocument
            if (_titleDocument != null && _titleDocument.Blocks.FirstBlock is Paragraph paragraph)
            {
                titleTextBlock.Inlines.Clear();
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is Run run)
                    {
                        Run newRun = new Run(run.Text)
                        {
                            FontWeight = run.FontWeight,
                            FontStyle = run.FontStyle,
                            TextDecorations = run.TextDecorations,
                            FontSize = run.FontSize,
                            FontFamily = run.FontFamily
                        };
                        titleTextBlock.Inlines.Add(newRun);
                    }
                }
                if (titleTextBlock.Text.Trim() != "Click to edit title")
                {
                    titleTextBlock.Foreground = Brushes.Black;
                }
            }
            // Create Delete Button
            Button deleteButton = new Button
            {
                Content = "x",
                Width = 20,
                Height = 20,
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                Cursor = Cursors.Hand
            };

            // Style the button on hover
            Style deleteButtonStyle = new Style(typeof(Button));
            deleteButtonStyle.Setters.Add(new Setter { Property = Button.BackgroundProperty, Value = Brushes.Transparent });
            deleteButtonStyle.Triggers.Add(new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true,
                Setters = { new Setter { Property = Button.BackgroundProperty, Value = Brushes.LightGray } }
            });
            deleteButton.Style = deleteButtonStyle;

            // Attach delete functionality
            deleteButton.Click += (s, args) =>
            {
                _reportElements.RemoveAll(re => re.Element == titleBorder);
                _titleDocument = new FlowDocument(new Paragraph(new Run("Click to edit title")));
                RedrawCanvas();
                CheckAndManagePlaceholder();
            };

            // Add TextBlock and Delete Button to Grid
            Grid.SetColumn(titleTextBlock, 0);
            Grid.SetRow(titleTextBlock, 0);
            Grid.SetColumn(deleteButton, 1);
            Grid.SetRow(deleteButton, 0);
            contentGrid.Children.Add(titleTextBlock);
            contentGrid.Children.Add(deleteButton);


            // Attach click event to TextBlock
            titleTextBlock.MouseLeftButtonDown += (s, args) =>
            {
                // Replace TextBlock with RichTextBox
                RichTextBox richTextBox = new RichTextBox
                {
                    Width = titleBorder.Width - 10,
                    Height = 25,
                    BorderThickness = new Thickness(0),
                    FontSize = 16,
                    Background = Brushes.Transparent,
                    Document = new FlowDocument() // Create a new document
                };

                // Restore the stored FlowDocument
                if (_titleDocument != null)
                {
                    foreach (var block in _titleDocument.Blocks.ToList())
                    {
                        richTextBox.Document.Blocks.Add(block);
                    }
                }

                // Remove TextBlock and add RichTextBox to Grid
                contentGrid.Children.Remove(titleTextBlock);
                Grid.SetColumn(richTextBox, 0);
                Grid.SetRow(richTextBox, 0);
                contentGrid.Children.Add(richTextBox);

                // Show editor window
                _editorWindow = new RichTextEditorWindow(richTextBox);
                _editorWindow.Owner = Window.GetWindow(this);
                _editorWindow.Closed += (ws, we) =>
                {
                    // Store the current FlowDocument
                    _titleDocument = new FlowDocument();
                    foreach (var block in richTextBox.Document.Blocks.ToList())
                    {
                        _titleDocument.Blocks.Add(block);
                    }

                    // Create new TextBlock with formatted text
                    TextBlock newTextBlock = new TextBlock
                    {
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        FontSize = 16
                    };

                    // Extract text and formatting
                    string plainText = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd).Text.Trim();
                    if (string.IsNullOrEmpty(plainText))
                    {
                        plainText = "Click to edit title";
                        newTextBlock.Foreground = Brushes.Gray;
                    }
                    else
                    {
                        newTextBlock.Foreground = Brushes.Black;
                    }

                    // Rebuild formatting
                    if (_titleDocument != null && _titleDocument.Blocks.FirstBlock is Paragraph newParagraph)
                    {
                        newTextBlock.Inlines.Clear();
                        foreach (var inline in newParagraph.Inlines)
                        {
                            if (inline is Run run)
                            {
                                Run newRun = new Run(run.Text)
                                {
                                    FontWeight = run.FontWeight,
                                    FontStyle = run.FontStyle,
                                    TextDecorations = run.TextDecorations,
                                    FontSize = run.FontSize,
                                    FontFamily = run.FontFamily
                                };
                                newTextBlock.Inlines.Add(newRun);
                            }
                        }
                    }
                    else
                    {
                        newTextBlock.Text = plainText;
                    }

                    // Re-attach click event
                    newTextBlock.MouseLeftButtonDown += (newTextBlockSender, newTextBlockArgs) =>
                    {
                        RichTextBox newRichTextBox = new RichTextBox
                        {
                            Width = titleBorder.Width - 10,
                            Height = 30,
                            BorderThickness = new Thickness(0),
                            FontSize = 16,
                            Background = Brushes.Transparent,
                            Document = new FlowDocument()
                        };

                        // Restore the stored FlowDocument
                        if (_titleDocument != null)
                        {
                            foreach (var block in _titleDocument.Blocks.ToList())
                            {
                                newRichTextBox.Document.Blocks.Add(block);
                            }
                        }

                        var newEditorWindow = new RichTextEditorWindow(newRichTextBox);
                        newEditorWindow.Owner = Window.GetWindow(this);
                        newEditorWindow.Closed += (nws, nwe) =>
                        {
                            // Store the current FlowDocument
                            _titleDocument = new FlowDocument();
                            foreach (var block in newRichTextBox.Document.Blocks.ToList())
                            {
                                _titleDocument.Blocks.Add(block);
                            }

                            TextBlock finalTextBlock = new TextBlock
                            {
                                TextAlignment = TextAlignment.Center,
                                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                FontSize = 16
                            };

                            string newPlainText = new TextRange(newRichTextBox.Document.ContentStart, newRichTextBox.Document.ContentEnd).Text.Trim();
                            if (string.IsNullOrEmpty(newPlainText))
                            {
                                newPlainText = "Click to edit title";
                                finalTextBlock.Foreground = Brushes.Gray;
                            }
                            else
                            {
                                finalTextBlock.Foreground = Brushes.Black;
                            }

                            if (_titleDocument != null && _titleDocument.Blocks.FirstBlock is Paragraph finalParagraph)
                            {
                                finalTextBlock.Inlines.Clear();
                                foreach (var inline in finalParagraph.Inlines)
                                {
                                    if (inline is Run run)
                                    {
                                        Run newRun = new Run(run.Text)
                                        {
                                            FontWeight = run.FontWeight,
                                            FontStyle = run.FontStyle,
                                            TextDecorations = run.TextDecorations,
                                            FontSize = run.FontSize,
                                            FontFamily = run.FontFamily
                                        };
                                        finalTextBlock.Inlines.Add(newRun);
                                    }
                                }
                            }
                            else
                            {
                                finalTextBlock.Text = newPlainText;
                            }

                            //(newRichTextBox.Parent as Border).Child = finalTextBlock;
                            contentGrid.Children.Remove(newRichTextBox);
                            Grid.SetColumn(finalTextBlock, 0);
                            Grid.SetRow(finalTextBlock, 0);
                            contentGrid.Children.Add(finalTextBlock);

                            _editorWindow = null;
                        };

                        contentGrid.Children.Remove(newTextBlock);
                        Grid.SetColumn(newRichTextBox, 0);
                        Grid.SetRow(newRichTextBox, 0);
                        contentGrid.Children.Add(newRichTextBox);

                        newRichTextBox.Focus();
                        //(newTextBlock.Parent as Border).Child = newRichTextBox;
                        newEditorWindow.Show();
                        _editorWindow = newEditorWindow;
                    };

                    contentGrid.Children.Remove(richTextBox);
                    Grid.SetColumn(newTextBlock, 0);
                    Grid.SetRow(newTextBlock, 0);
                    contentGrid.Children.Add(newTextBlock);

                    //(richTextBox.Parent as Border).Child = newTextBlock;
                    _editorWindow = null;
                };

                richTextBox.Focus();
                //(titleTextBlock.Parent as Border).Child = richTextBox;
                _editorWindow.Show();
            };

            // Set Grid as Border content
            titleBorder.Child = contentGrid;

            // Update report elements: replace or insert title
            if (_reportElements.Any() && _reportElements.First().Type == ReportElementType.Title)
            {
                _reportElements[0] = new ReportElement { Type = ReportElementType.Title, Element = titleBorder };

            }
            else
            {
                _reportElements.Insert(0, new ReportElement { Type = ReportElementType.Title, Element = titleBorder });
            }

            // Redraw canvas
            RedrawCanvas();
            CheckAndManagePlaceholder();
            UpdateCanvasHeight();
        }

        // Save Comment richtext（put into Comment Border.Tag ）
        private sealed class CommentMeta
        {
            public FlowDocument Document { get; set; } = new FlowDocument();
        }

        // deep copy FlowDocument，avoid UI与存档引用同一个对象
        private static FlowDocument CloneFlowDocument(FlowDocument source)
        {
            if (source == null) return new FlowDocument();
            string xaml = XamlWriter.Save(source);
            using var sr = new System.IO.StringReader(xaml);
            using var xr = System.Xml.XmlReader.Create(sr);
            return (FlowDocument)XamlReader.Load(xr);
        }

        private Border BuildCommentBorder(double canvasWidth)
        {
            var commentBorder = new Border
            {
                Background = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(5),
                Width = canvasWidth * 0.9,
                Height = 100
            };

            var grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });


            var initialParagraph = new Paragraph(new Run(""))
            {
                LineHeight = 16,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 2, 0, 2)
            };

            var initialDoc = new FlowDocument(initialParagraph)
            {
                PageWidth = commentBorder.Width - 60,
            };

            var rich = new RichTextBox
            {
                BorderThickness = new Thickness(0),
                BorderBrush = Brushes.LightGray,
                Padding = new Thickness(8),
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = 100,
                Document = initialDoc
                //Width = canvasWidth * 0.9 - 60 
            };


            rich.TextChanged += (s, e) =>
            {
                rich.Document.PageWidth = commentBorder.Width - 60;

                foreach (var p in rich.Document.Blocks.OfType<Paragraph>())
                {
                    p.LineHeight = 16;
                    p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    p.Margin = new Thickness(0, 2, 0, 2);
                }
                double contentHeight = rich.ExtentHeight + 20;
                commentBorder.Height = Math.Max(160, contentHeight);
                RedrawCanvas();
                UpdateCanvasHeight();


                if (commentBorder.Tag is CommentMeta meta)
                    meta.Document = CloneFlowDocument(rich.Document);
            };

            Grid.SetColumn(rich, 0);
            Grid.SetRow(rich, 0);


            commentBorder.Tag = new CommentMeta
            {
                Document = CloneFlowDocument(initialDoc)
            };

            var editButton = new Button
            {
                Content = "✎",
                Width = 20,
                Height = 20,
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                //Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(0),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                Cursor = Cursors.Hand
            };
            // Style the button on hover
            Style editorButtonStyle = new Style(typeof(Button));
            editorButtonStyle.Setters.Add(new Setter { Property = Button.BackgroundProperty, Value = Brushes.Transparent });
            editorButtonStyle.Triggers.Add(new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true,
                Setters = { new Setter { Property = Button.BackgroundProperty, Value = Brushes.LightGray } }
            });
            editButton.Style = editorButtonStyle;

            editButton.Click += (s, args) =>
            {
                var editor = new RichTextEditorWindow(rich);
                editor.Owner = Window.GetWindow(this);
                editor.ShowDialog();


                rich.Document.PageWidth = commentBorder.Width - 60;
                foreach (var p in rich.Document.Blocks.OfType<Paragraph>())
                {
                    p.LineHeight = 16;
                    p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    p.Margin = new Thickness(0, 2, 0, 2);
                }



                commentBorder.Height = Math.Max(160, rich.ExtentHeight + 20);


                if (commentBorder.Tag is CommentMeta meta)
                    meta.Document = CloneFlowDocument(rich.Document);

                RedrawCanvas();
                UpdateCanvasHeight();
            };

            var deleteButton = new Button
            {
                Content = "x",
                Width = 20,
                Height = 20,
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(0),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                Cursor = Cursors.Hand
            };
            // Style the button on hover
            Style deleteButtonStyle = new Style(typeof(Button));
            deleteButtonStyle.Setters.Add(new Setter { Property = Button.BackgroundProperty, Value = Brushes.Transparent });
            deleteButtonStyle.Triggers.Add(new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true,
                Setters = { new Setter { Property = Button.BackgroundProperty, Value = Brushes.LightGray } }
            });
            deleteButton.Style = deleteButtonStyle;

            deleteButton.Click += (s, args) =>
            {

                _reportElements.RemoveAll(e => ReferenceEquals(e.Element, commentBorder));
                RedrawCanvas();
                CheckAndManagePlaceholder();
                UpdateCanvasHeight();
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = System.Windows.VerticalAlignment.Top
            };
            buttonPanel.Children.Add(editButton);
            buttonPanel.Children.Add(deleteButton);
            Grid.SetColumn(buttonPanel, 1);
            Grid.SetRow(buttonPanel, 0);

            grid.Children.Add(rich);
            grid.Children.Add(buttonPanel);

            commentBorder.Child = grid;
            return commentBorder;
        }

        private void AddCommentButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double canvasWidth = Math.Max(DrawingCanvas.ActualWidth, 778);
                var commentBorder = BuildCommentBorder(canvasWidth);

                _reportElements.Add(new ReportElement
                {
                    Type = ReportElementType.Comment,
                    Element = commentBorder
                });

                RedrawCanvas();
                CheckAndManagePlaceholder();
                UpdateCanvasHeight();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding comment: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_reportElements == null || _reportElements.Count == 0)
                {
                    MessageBox.Show("Please add at least one report element before saving.",
                                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }


                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PDF Files (*.pdf)|*.pdf",
                    DefaultExt = "pdf",
                    FileName = "Report.pdf"
                };
                if (dlg.ShowDialog() != true)
                    return;

                string filePath = dlg.FileName;


                QuestPDF.Settings.License = LicenseType.Community;

                Document
                    .Create(c =>
                    {
                        c.Page(page =>
                        {
                            page.Size(PageSizes.A4);
                            page.Margin(36);
                            page.DefaultTextStyle(x => x.FontSize(11));

                            page.Content().Column(col =>
                            {

                                foreach (var re in _reportElements)
                                {
                                    if (re.Element is not Border border || border.Child is not Grid grid)
                                        continue;

                                    switch (re.Type)
                                    {
                                        case ReportElementType.Title:
                                            {

                                                FlowDocument titleDoc = _titleDocument
                                                                ?? new FlowDocument(new Paragraph(new Run(
                                                                    grid.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "")));

                                                col.Item()
                                           .PaddingBottom(12)
                                           .Text(t =>
                                               {
                                                   t.AlignCenter();
                                                   AppendFlowDocToQuest(t, titleDoc);
                                               });
                                                break;
                                            }

                                        case ReportElementType.Plot:
                                            {
                                                var img = grid.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                                                if (img?.Source is BitmapSource bmp)
                                                {
                                                    var bytes = WpfBitmapToPngBytes(bmp);
                                                    if (bytes != null && bytes.Length > 0)
                                                        col.Item().PaddingBottom(18).Image(bytes).FitWidth();
                                                }
                                                break;
                                            }

                                        case ReportElementType.Comment:
                                            {

                                                FlowDocument? doc = null;
                                                if (border.Tag is CommentMeta meta && meta.Document != null)
                                                    doc = meta.Document;
                                                else
                                                    doc = grid.Children.OfType<RichTextBox>().FirstOrDefault()?.Document;

                                                if (doc != null)
                                                {
                                                    col.Item()
                                               .PaddingBottom(12)
                                               .Text(t =>
                                                   {

                                                       t.DefaultTextStyle(x => x.FontSize(11));
                                                       AppendFlowDocToQuest(t, doc);
                                                   });
                                                }
                                                break;
                                            }
                                    }
                                }
                            });

                            page.Footer().AlignCenter().Text(x =>
                            {
                                x.Span("Page "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages();
                            });
                        });
                    })
                    .GeneratePdf(filePath);

                MessageBox.Show($"PDF saved successfully to {filePath}!", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving PDF: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /* ===== help methods ===== */

        /// <summary>
        /// </summary>
        // using System.Windows.Documents;
        // using QuestPDF.Fluent;

        private static void AppendFlowDocToQuest(QuestPDF.Fluent.TextDescriptor t, FlowDocument doc)
        {
            if (doc == null) return;

            foreach (var block in doc.Blocks)
            {
                if (block is Paragraph para)
                {

                    foreach (var inline in para.Inlines)
                    {
                        if (inline is Run run)
                        {
                            var span = t.Span(run.Text ?? string.Empty);
                            if (run.FontWeight == FontWeights.Bold) span = span.Bold();
                            if (run.FontStyle == FontStyles.Italic) span = span.Italic();
                            if (run.FontSize > 0) span = span.FontSize((float)run.FontSize);
                            if (run.FontFamily != null) span = span.FontFamily(run.FontFamily.Source);
                        }
                        else if (inline is LineBreak)
                        {

                            t.Line("");
                        }
                    }

                    t.Line("");
                }
                else if (block is List list)
                {

                    foreach (ListItem item in list.ListItems)
                    {
                        foreach (var itemBlock in item.Blocks)
                        {
                            if (itemBlock is Paragraph p2)
                            {
                                t.Span("• ").SemiBold();
                                foreach (var inline in p2.Inlines)
                                {
                                    if (inline is Run run)
                                    {
                                        var span = t.Span(run.Text ?? string.Empty);
                                        if (run.FontWeight == FontWeights.Bold) span = span.Bold();
                                        if (run.FontStyle == FontStyles.Italic) span = span.Italic();
                                        if (run.FontSize > 0) span = span.FontSize((float)run.FontSize);
                                        if (run.FontFamily != null) span = span.FontFamily(run.FontFamily.Source);
                                    }
                                    else if (inline is LineBreak)
                                    {
                                        t.Line("");
                                    }
                                }
                                t.Line("");
                            }
                        }
                    }
                }

            }
        }


        /// <summary>

        /// </summary>
        private static byte[]? WpfBitmapToPngBytes(BitmapSource source)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }


        private async void AddPlotButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show plot title dialog
                PlotTitleDialog dialog = new PlotTitleDialog();
                bool? result = dialog.ShowDialog(Window.GetWindow(this));
                if (result != true || dialog.PlotTitle == null)
                {
                    return; // Cancelled
                }



                string plotTitle = dialog.PlotTitle;

                string model = (ModelSelector.SelectedItem as Model)?.Name ?? "Farrington";
                string dataSource = (DataSourceSelector.SelectedItem as DataSource)?.Name ?? "";
                //string yearBack = (YearBackSelector.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "5";
                bool useTrainSplit = TrainSplitCheckBox.IsChecked ?? false;
                string beginDate = BeginDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
                string freq = (FreqSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "By Week";
                string threshold = ThresholdInput.Text?.Trim() ?? "";
                bool abnormalReportEnabled = AbnormalReportFlag.IsChecked == true;

                int? yearsBack = null;
                int? mcMunu = null;
                int? baseline = null;

                var sel = (Model)ModelSelector.SelectedItem;
                var key = sel?.Name?.Trim().ToLowerInvariant() ?? "";

                bool designFlag=true;

                if (key == "farrington" || key == "bayes" || key == "cdc" || key == "cusum")
                {
                    if (YearBackSelector.SelectedItem is ComboBoxItem item &&
                        int.TryParse(item.Content?.ToString(), out var yb))
                        yearsBack = yb;
                }
                else if (key == "boda")
                {
                    if (int.TryParse(TxtMcMunu.Text, out var v)) mcMunu = v;
                }
                else if (key == "earsc1" || key == "earsc2" || key == "earsc3")
                {
                    if (int.TryParse(TxtBaseline.Text, out var b)) baseline = b;
                }

                // Validate inputs
                if (string.IsNullOrEmpty(dataSource))
                {
                    MessageBox.Show("Please select a Data Source.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (string.IsNullOrEmpty(beginDate))
                {
                    MessageBox.Show("Please select a Begin Date.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (string.IsNullOrEmpty(threshold))
                {
                    MessageBox.Show("Please enter a Threshold.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (!Regex.IsMatch(threshold, @"^\d+$") || !int.TryParse(threshold, out int thresholdValue) || thresholdValue < 0 || thresholdValue > 5000)
                {
                    MessageBox.Show("Threshold must be a number between 0 and 5000.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }


                // Create JSON object
                var graphData = new JObject
                {
                    ["model"] = model,
                    ["dataSource"] = dataSource,
                    ["useTrainSplit"] = useTrainSplit,
                    ["beginDate"] = beginDate,
                    ["freq"] = freq,
                    ["threshold"] = threshold,
                    ["title"] = plotTitle,
                    ["abnormalReportFlag"] = abnormalReportEnabled,
                    ["designFlag"] = designFlag,
                };

                // 
                switch (model?.ToLowerInvariant())
                {
                    case "farrington":
                    case "bayes":
                    case "cdc":
                    case "cusum":
                        if (yearsBack.HasValue)
                            graphData["yearBack"] = yearsBack.Value;
                        break;

                    case "boda":
                        if (mcMunu.HasValue)
                            graphData["mc_munu"] = mcMunu;
                        break;

                    case "earsc1":
                    case "earsc2":
                    case "earsc3":
                        if (baseline.HasValue)
                            graphData["baseline"] = baseline;
                        break;

                    default:
                       
                        break;
                }


                if (useTrainSplit)
                {
                    string trainSplitRatio = TrainSplitRatioInput.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(trainSplitRatio))
                    {
                        MessageBox.Show("Please enter a Train Split Ratio.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    if (!Regex.IsMatch(trainSplitRatio, @"^0*\.\d+$") || !double.TryParse(trainSplitRatio, out double ratio) || ratio <= 0 || ratio >= 1)
                    {
                        MessageBox.Show("Train Split Ratio must be a number between 0 and 1 (e.g., 0.8).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    graphData["trainSplitRatio"] = trainSplitRatio;
                }
                else
                {
                    string trainEndDate = TrainEndDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "";

                    if (string.IsNullOrEmpty(trainEndDate))
                    {
                        MessageBox.Show("Please select both Train End Date.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    graphData["trainEndDate"] = trainEndDate;

                }

                var requestData = new JObject
                {
                    ["graph"] = graphData
                };

                var content = new StringContent(requestData.ToString(), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync("/epyapi", content);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        JObject responseJson = JObject.Parse(responseContent);
                        string? status = responseJson["status"]?.ToString();
                        string? filePath = responseJson["plot_path"]?.ToString();

                        bool isAbnormal = false;

                        // read response JSON "abnormal"
                        if (responseJson.ContainsKey("abnormal"))
                        {
                            bool.TryParse(responseJson["abnormal"]?.ToString(), out isAbnormal);
                        }

                        if (status?.ToLower() == "processed")
                        {
                           

                            if ( !string.IsNullOrEmpty(filePath))
                            {
                                // Validate file existence
                                var file = filePath;
                                if (!File.Exists(file))
                                {
                                    MessageBox.Show($"Plot file not found at '{filePath}'.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    return;
                                }

                                // Load plot image
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(file, UriKind.Absolute);
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.EndInit();

                                // Calculate dynamic image size
                                double canvasWidth = DrawingCanvas.ActualWidth > 0 ? DrawingCanvas.ActualWidth : 400;
                                double canvasHeight = DrawingCanvas.ActualHeight > 0 ? DrawingCanvas.ActualHeight : 300;
                                double maxImageWidth = canvasWidth * 0.8; // 80% of canvas width
                                double maxImageHeight = canvasHeight * 0.9; // 90% of canvas height (per image)

                                // Get image aspect ratio
                                double aspectRatio = bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0
                                    ? (double)bitmap.PixelWidth / bitmap.PixelHeight
                                    : 4.0 / 3.0; // Default 4:3 if unknown

                                // Calculate scaled dimensions
                                double imageWidth = maxImageWidth;
                                double imageHeight = imageWidth / aspectRatio;

                                // Cap height to avoid oversized images
                                if (imageHeight > maxImageHeight)
                                {
                                    imageHeight = maxImageHeight;
                                    imageWidth = imageHeight * aspectRatio;
                                }


                                // Add to DrawingCanvas
                                System.Windows.Controls.Image plotImage = new System.Windows.Controls.Image
                                {
                                    Source = bitmap,
                                    Width = /*imageWidth*/700,
                                    Height = /*imageHeight*/400,
                                    Stretch = Stretch.Uniform
                                };

                                // Create a Border for the image
                                Border imageBorder = new Border
                                {
                                    Background = Brushes.White,
                                    BorderThickness = new Thickness(0),
                                    Padding = new Thickness(5),
                                    Width = canvasWidth * 0.9, // 90% of canvas width
                                    Height = /*imageHeight*/400 + 10 // Image height + 5px top/bottom padding
                                };

                                // Create a Grid to hold Image and Delete Button
                                Grid contentGrid = new Grid
                                {
                                    Margin = new Thickness(0)
                                };
                                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                                // Create Delete Button
                                Button deleteButton = new Button
                                {
                                    Content = "x",
                                    Width = 20,
                                    Height = 20,
                                    FontSize = 12,
                                    Background = Brushes.Transparent,
                                    BorderBrush = Brushes.Gray,
                                    BorderThickness = new Thickness(1),
                                    Padding = new Thickness(0),
                                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                                    Cursor = Cursors.Hand
                                };

                                // Style the button on hover
                                Style deleteButtonStyle = new Style(typeof(Button));
                                deleteButtonStyle.Setters.Add(new Setter { Property = Button.BackgroundProperty, Value = Brushes.Transparent });
                                deleteButtonStyle.Triggers.Add(new Trigger
                                {
                                    Property = Button.IsMouseOverProperty,
                                    Value = true,
                                    Setters = { new Setter { Property = Button.BackgroundProperty, Value = Brushes.LightGray } }
                                });
                                deleteButton.Style = deleteButtonStyle;

                                // Attach delete functionality
                                deleteButton.Click += (s, args) =>
                                {
                                    _reportElements.RemoveAll(re => re.Element == imageBorder);
                                    RedrawCanvas();
                                    CheckAndManagePlaceholder();
                                };

                                // Add Image and Delete Button to Grid
                                Grid.SetColumn(plotImage, 0);
                                Grid.SetRow(plotImage, 0);
                                Grid.SetColumn(deleteButton, 1);
                                Grid.SetRow(deleteButton, 0);
                                contentGrid.Children.Add(plotImage);
                                contentGrid.Children.Add(deleteButton);

                                // Set Grid as Border content
                                imageBorder.Child = contentGrid;

                                imageBorder.Tag = new JObject
                                {
                                    // full parameters（ graphData：Model/DataSource/YearBack/UseTrainSplit/BeginDate/Freq/Threshold/Title...）
                                    ["graph"] = graphData,


                                    ["plot_path"] = file,


                                    ["title"] = plotTitle
                                };

                                // Add to report elements
                                _reportElements.Add(new ReportElement
                                {
                                    Type = ReportElementType.Plot,
                                    Element = imageBorder
                                });

                                // Redraw canvas
                                RedrawCanvas();
                                CheckAndManagePlaceholder();
                                UpdateCanvasHeight();
                                MessageBox.Show($"Plot '{plotTitle}' added for model {model} from '{filePath}'!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                            }
                            else
                            {
                                MessageBox.Show($"No plot into the report: {responseJson}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else
                        {
                            MessageBox.Show($"No abnormality detected recently; plot not added to the report.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Newtonsoft.Json.JsonException)
                    {
                        MessageBox.Show($"Invalid JSON response from server: {responseContent}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    string serverMessage = responseContent;
                    try
                    {
                        JObject errorJson = JObject.Parse(responseContent);
                        serverMessage = errorJson["message"]?.ToString()
                            ?? errorJson["error"]?.ToString()
                            ?? responseContent;
                    }
                    catch (Newtonsoft.Json.JsonException)
                    {
                        serverMessage = System.Net.WebUtility.HtmlDecode(
                            Regex.Replace(responseContent, "<[^>]+>", " "));
                        serverMessage = Regex.Replace(serverMessage, @"\s+", " ").Trim();
                    }

                    if (serverMessage.Length > 800)
                        serverMessage = serverMessage[..800] + "…";

                    MessageBox.Show(
                        $"Failed to generate plot ({(int)response.StatusCode} {response.ReasonPhrase}).\n\n{serverMessage}",
                        "Report Generation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating or adding plot: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ThresholdBacktestHelpButton_Click(object sender, RoutedEventArgs e)
        {
            string thresholdText = ThresholdInput.Text?.Trim() ?? string.Empty;
            string selectedSource = (DataSourceSelector.SelectedItem as DataSource)?.Name ?? "COVID-19 Deaths";

            if (selectedSource != "COVID-19 Deaths" &&
                selectedSource != "Pneumonia Deaths" &&
                selectedSource != "Flu Deaths")
            {
                MessageBox.Show(
                    "Backtest currently supports these Data Sources only:\n- COVID-19 Deaths\n- Pneumonia Deaths\n- Flu Deaths",
                    "Unsupported Data Source",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(thresholdText))
            {
                MessageBox.Show("Please enter a numeric value for Maximum threshold first.",
                    "Threshold Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!double.TryParse(thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out double thresholdValue))
            {
                MessageBox.Show("Maximum threshold must be a valid number.",
                    "Invalid Threshold", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            string serverDir = Path.Combine(baseDir, "Server");
            string scriptPath = Path.Combine(serverDir, "cdc_backtest.py");
            string configPath = AppPaths.ConfigPath;

            if (!File.Exists(scriptPath))
            {
                MessageBox.Show($"Cannot find script:\n{scriptPath}",
                    "Backtest Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string pythonPath = "python";
            try
            {
                if (File.Exists(configPath))
                {
                    var cfg = JObject.Parse(File.ReadAllText(configPath));
                    pythonPath = cfg.Value<string>("pythonPath")?.Trim() ?? pythonPath;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to read config.json:\n{ex.Message}",
                    "Config Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            string appToken = DBHelper.GetAllDataSources()
                .FirstOrDefault(ds => string.Equals(ds.Name, selectedSource, StringComparison.OrdinalIgnoreCase))
                ?.AppToken ?? string.Empty;

            if (!File.Exists(pythonPath) && string.Equals(pythonPath, "python", StringComparison.OrdinalIgnoreCase) == false)
            {
                MessageBox.Show($"Python executable not found:\n{pythonPath}",
                    "Backtest Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ThresholdBacktestHelpButton.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    WorkingDirectory = serverDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(scriptPath);
                psi.ArgumentList.Add("--state");
                psi.ArgumentList.Add("United States");
                psi.ArgumentList.Add("--source");
                psi.ArgumentList.Add(selectedSource);
                psi.ArgumentList.Add("--threshold");
                psi.ArgumentList.Add(thresholdValue.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--truth-rule");
                psi.ArgumentList.Add("percent_expected");
                psi.ArgumentList.Add("--percent-threshold");
                psi.ArgumentList.Add("110");
                if (!string.IsNullOrWhiteSpace(appToken))
                {
                    psi.ArgumentList.Add("--app-token");
                    psi.ArgumentList.Add(appToken);
                }

                using var process = new Process { StartInfo = psi };
                process.Start();
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                string stdout = (await stdoutTask).Trim();
                string stderr = (await stderrTask).Trim();
                if (process.ExitCode != 0)
                {
                    string errText = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    ShowBacktestDialog("Backtest failed", errText);
                    return;
                }

                JObject? metrics = TryExtractJsonObject(stdout);
                if (metrics == null)
                {
                    ShowBacktestDialog("Backtest output", stdout);
                    return;
                }

                string summary = metrics.ToString(Newtonsoft.Json.Formatting.Indented);
                string comments = BuildBacktestComments(metrics);
                ShowBacktestDialog(summary, comments);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to run backtest:\n{ex.Message}",
                    "Backtest Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                ThresholdBacktestHelpButton.IsEnabled = true;
            }
        }

        private static JObject? TryExtractJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;

            string json = text.Substring(start, end - start + 1);
            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildBacktestComments(JObject metrics)
        {
            double precision = metrics.Value<double?>("precision") ?? 0.0;
            double recall = metrics.Value<double?>("recall") ?? 0.0;
            double f1 = metrics.Value<double?>("f1") ?? 0.0;
            int fp = metrics.Value<int?>("FP") ?? 0;
            int fn = metrics.Value<int?>("FN") ?? 0;

            string performance = f1 >= 0.70
                ? "Overall signal quality is strong for this threshold."
                : f1 >= 0.50
                    ? "Overall signal quality is moderate; threshold tuning may help."
                    : "Overall signal quality is weak; consider retuning the threshold.";

            string balance = precision >= recall
                ? "Precision is higher than recall, so false positives are relatively controlled."
                : "Recall is higher than precision, so the system is sensitive but may over-alert.";

            string errorHint = fp > fn
                ? "False positives exceed false negatives. You may increase threshold to reduce noise."
                : fn > fp
                    ? "False negatives exceed false positives. You may decrease threshold to catch more events."
                    : "False positives and false negatives are balanced at this setting.";

            return
                "Comments\n" +
                $"- {performance}\n" +
                $"- {balance}\n" +
                $"- {errorHint}\n" +
                "- This summary is based on the percent_expected rule with percent-threshold=110 for United States.";
        }

        private static void ShowBacktestDialog(string summary, string comments)
        {
            Cursor? previousOverrideCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = null;

            var dialog = new Window
            {
                Title = "CDC Backtest Summary",
                Width = 760,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                Cursor = Cursors.Arrow,
                Content = new Grid
                {
                    Margin = new Thickness(12)
                }
            };

            var grid = (Grid)dialog.Content;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Text = $"Summary\n{summary}\n\n{comments}"
            };
            Grid.SetRow(textBox, 0);
            grid.Children.Add(textBox);

            var closeButton = new Button
            {
                Content = "Close",
                Width = 90,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            closeButton.Click += (_, __) => dialog.Close();
            Grid.SetRow(closeButton, 1);
            grid.Children.Add(closeButton);

            try
            {
                dialog.ShowDialog();
            }
            finally
            {
                Mouse.OverrideCursor = previousOverrideCursor;
            }
        }

        private void UpdateCanvasHeight()
        {
            double canvasHeight = 0;
            foreach (var re in _reportElements)
                canvasHeight += GetElementDefaultHeight(re);

            int gaps = Math.Max(_reportElements.Count - 1, 0);
            canvasHeight += gaps * 30;
            canvasHeight += 30;

            DrawingCanvas.Height = canvasHeight;
        }
        private double GetElementDefaultHeight(ReportElement re)
        {
            return re.Type switch
            {
                ReportElementType.Title => 60,
                ReportElementType.Plot => 410,   // 400 plot + 10 padding
                ReportElementType.Comment => Math.Max((re.Element as FrameworkElement)?.Height ?? 160, 160),
                _ => 0
            };
        }

        private void RedrawCanvas()
        {
            DrawingCanvas.Children.Clear();

            double canvasWidth = Math.Max(DrawingCanvas.ActualWidth, 778);
            double y = 0;

            foreach (var re in _reportElements)
            {
                if (re.Element is not Border border) continue;

                border.Width = canvasWidth * 0.9;

                // Comment 
                if (re.Type == ReportElementType.Comment)
                {
                    var rtb = (border.Child as Grid)?
                              .Children.OfType<RichTextBox>().FirstOrDefault();
                    if (rtb != null) rtb.Document.PageWidth = border.Width - 60;
                }

                border.Height = GetElementDefaultHeight(re);

                Canvas.SetTop(border, y);
                Canvas.SetLeft(border, (canvasWidth - border.Width) / 2.0);
                DrawingCanvas.Children.Add(border);

                y += border.Height + 30;
            }
        }



        private void TrainSplitCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (TrainSplitRatioInput != null && DatePickersPanel != null)
            {
                TrainSplitRatioInput.Visibility = Visibility.Visible;
                DatePickersPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void TrainSplitCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (TrainSplitRatioInput != null && DatePickersPanel != null)
            {
                TrainSplitRatioInput.Visibility = Visibility.Collapsed;
                DatePickersPanel.Visibility = Visibility.Visible;
            }
        }

        private void AddNotebookButton_Click(object sender, RoutedEventArgs e)
        {
            if (_notebookWindow == null || !_notebookWindow.IsVisible)
            {
                _notebookWindow = new NotebookWindow(this.window);
                _notebookWindow.Owner = window; // Optional: Sets the main window as owner
                _notebookWindow.Show(); // Non-modal
            }
            else
            {
                _notebookWindow.Activate(); // Bring existing window to front
            }
        }



        private void btnAddModel_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnDeleteModel_Click(object sender, RoutedEventArgs e)
        {

        }

        private void lstModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var m = lstModel.SelectedItem as Model;
            if (m == null)
            {
                // 
                txtModelName.Text = "";
                txtFullModelName.Text = "";
                cmbModelType.Text = "";
                txtModelDescription.Text = "";
                dgProperties.ItemsSource = null;
                return;
            }
            txtModelName.Text = m.Name ?? "";
            txtFullModelName.Text = m.FullName ?? "";
            cmbModelType.Text = m.Type ?? "";
            txtModelDescription.Text = m.Description ?? "";

            // 
            dgProperties.ItemsSource = m.Properties;
        }

        private void btnSaveModel_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnAddProperty_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnDeleteProperty_Click(object sender, RoutedEventArgs e)
        {

        }

        private void dgProperties_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {

        }

        private void SchedulerDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SchedulerTable.ItemsSource is not ObservableCollection<SchedulerTask> collection)
                {
                    MessageBox.Show("Scheduler table not bound to data collection.",
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var selectedRows = collection.Where(r => r.IsSelected).ToList();
                if (!selectedRows.Any())
                {
                    MessageBox.Show("Please check at least one row to delete.",
                                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var row in selectedRows)
                {
                    if (DBHelper.DeleteSchedulerById(row.Id))
                    {
                        collection.Remove(row);
                    }
                }

                MessageBox.Show("Checked scheduler rows deleted.",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                var view = CollectionViewSource.GetDefaultView(SchedulerTable.ItemsSource);
                view?.Refresh();
                UpdateSchedulerSelectionState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting scheduler rows: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void SchedulerSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SchedulerTable.ItemsSource is not ObservableCollection<SchedulerTask> collection)
                {
                    MessageBox.Show("Scheduler table not bound to data collection.",
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // select checked rows
                var selectedRows = collection.Where(r => r.IsSelected).ToList();
                if (!selectedRows.Any())
                {
                    MessageBox.Show("Please check at least one row to save.",
                                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // if bulk recipient emails provided, use them for all selected rows
                string? bulkRecipients = null;
                if (this.FindName("RecipientEmailsBox") is TextBox recipientBox)
                {
                    var lines = recipientBox.Text?
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .ToList();

                    if (lines is { Count: > 0 })
                        bulkRecipients = string.Join(",", lines);
                }

                int ok = 0, fail = 0;
                string deliveryMethod = GetSelectedDeliveryMethod();
                foreach (var row in selectedRows)
                {

                    string recipients = deliveryMethod == "Email"
                        ? bulkRecipients ?? (row.Recipients ?? string.Empty)
                        : row.Recipients ?? string.Empty;
                    if (deliveryMethod == "Email" && string.IsNullOrWhiteSpace(recipients))
                    {
                        MessageBox.Show($"Scheduler row {row.Id} needs at least one recipient email address.",
                                        "Recipient Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        RecipientEmailsBox.Focus();
                        return;
                    }
                    string attachPath = row.AttachmentPath ?? string.Empty;
                    string startDate = row.StartDate ?? string.Empty;   // YYYY-MM-DD
                    string freq = row.Freq ?? string.Empty;

                    if (DBHelper.UpdateScheduler(row.Id, recipients, attachPath, startDate, freq, deliveryMethod))
                    {
                        // update successful, update the memory object too
                        row.Recipients = recipients;
                        row.AttachmentPath = attachPath;
                        row.StartDate = startDate;
                        row.Freq = freq;
                        row.DeliveryMethod = deliveryMethod;
                        ok++;
                    }
                    else
                    {
                        fail++;
                    }
                }

                string msg = $"Saved recipients for {ok} row(s).";
                if (fail > 0) msg += $" {fail} row(s) failed.";
                MessageBox.Show(msg, fail == 0 ? "Success" : "Warning",
                                MessageBoxButton.OK,
                                fail == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving scheduler recipients: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private bool TaskExists(string taskName)
        {
            var psi = new ProcessStartInfo("schtasks", $"/query /tn {taskName}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            string output = p!.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 && output.IndexOf(taskName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private (int exit, string stdout, string stderr) RunSchTasks(string args, bool elevate = true)
        {
            if (elevate)
            {
                var elevatedPsi = new ProcessStartInfo("schtasks", args)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var elevatedProcess = Process.Start(elevatedPsi);
                if (elevatedProcess == null)
                {
                    return (-1, string.Empty, "Failed to start elevated schtasks process.");
                }

                elevatedProcess.WaitForExit();
                string error = elevatedProcess.ExitCode == 0
                    ? string.Empty
                    : $"schtasks failed with exit code {elevatedProcess.ExitCode}.";
                return (elevatedProcess.ExitCode, string.Empty, error);
            }

            var psi = new ProcessStartInfo("schtasks", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            string output = p!.StandardOutput.ReadToEnd();
            string errorText = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output, errorText);
        }

        // In SchedulerStart_Click, fix possible null reference for HourSelector/MinuteSelector.SelectedItem
        private void SchedulerStart_Click(object sender, RoutedEventArgs e)
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, "ForeSITEScheduler.exe");

            try
            {
                // ComboBox read time
                if (HourSelector.SelectedItem == null || MinuteSelector.SelectedItem == null)
                {
                    MessageBox.Show("Please select both hour and minute before starting the scheduler.", "Scheduler");
                    return;
                }

                string hour = HourSelector.SelectedItem?.ToString() ?? "00";
                string minute = MinuteSelector.SelectedItem?.ToString() ?? "00";
                string startTime = $"{hour}:{minute}";

                string args = $"/create /tn {TaskName} /tr \"\\\"{exePath}\\\"\" /sc daily /st {startTime} /it /f";
                var (exit, stdout, stderr) = RunSchTasks(args);

                if (exit == 0)
                {
                    MessageBox.Show($"Task '{TaskName}' was set to run daily at {startTime} while this user is signed in.",
                                    "Scheduler", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to create task.\n{stderr}",
                                    "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 
            UpdateSchedulerButtons();
        }

        private void SchedulerEnd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TaskExists(TaskName))
                {
                    MessageBox.Show($"Task '{TaskName}' does not exist.", "Scheduler");
                    UpdateSchedulerButtons();
                    return;
                }

                string args = $"/delete /tn {TaskName} /f";
                var (exit, stdout, stderr) = RunSchTasks(args);

                if (exit == 0)
                {
                    MessageBox.Show($"Task '{TaskName}' was deleted successfully.",
                                    "Scheduler", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to delete task.\n{stderr}",
                                    "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            UpdateSchedulerButtons();
        }

        private void SchedulerSelectCheckBox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is CheckBox cb && cb.DataContext is SchedulerTask row)
                {
                    if (cb.IsChecked == true)
                    {
                        string recipients = row.Recipients ?? string.Empty;

                        // 
                        var lines = recipients
                            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 0);

                        if (this.FindName("RecipientEmailsBox") is TextBox box)
                            box.Text = string.Join(Environment.NewLine, lines);

                        DeliveryMethodSelector.SelectedIndex =
                            string.Equals(row.DeliveryMethod, "Notification", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    }

                    Dispatcher.BeginInvoke(new Action(UpdateSchedulerSelectionState),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling checkbox click: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSchedulerSelectionState()
        {
            int selectedCount = SchedulerTable.ItemsSource is IEnumerable<SchedulerTask> tasks
                ? tasks.Count(task => task.IsSelected)
                : 0;

            SchedulerSelectionSummary.Text = $"{selectedCount} SELECTED";
            SchedulerSaveButton.IsEnabled = selectedCount > 0;
            SchedulerDeleteButton.IsEnabled = selectedCount > 0;
            SchedulerEmptyState.Visibility = SchedulerTable.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private string GetSelectedDeliveryMethod()
        {
            return DeliveryMethodSelector.SelectedItem is ComboBoxItem item &&
                   string.Equals(item.Content?.ToString(), "Notification", StringComparison.OrdinalIgnoreCase)
                ? "Notification"
                : "Email";
        }

        private void DeliveryMethodSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RecipientEmailsBox == null || RecipientEmailsLabel == null)
                return;

            bool emailSelected = GetSelectedDeliveryMethod() == "Email";
            RecipientEmailsBox.IsEnabled = emailSelected;
            RecipientEmailsBox.Opacity = emailSelected ? 1.0 : 0.55;
            RecipientEmailsLabel.Text = emailSelected
                ? "RECIPIENT EMAILS · ONE PER LINE"
                : "RECIPIENT EMAILS · NOT USED FOR NOTIFICATIONS";
        }

        private void ModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var model = ModelSelector.SelectedItem as Model;
            var name = model?.Name?.Trim() ?? string.Empty;
            var key = name.ToLowerInvariant();

            ShowOnlyPanel(null);

            if (key == "farrington" || key == "bayes" || key == "cdc" || key == "cusum")
            {
                ShowOnlyPanel(YearBackPanel);
                // default
                if (YearBackSelector.SelectedIndex < 0) YearBackSelector.SelectedIndex = 0;
            }
            else if (key == "boda")
            {
                ShowOnlyPanel(BodaPanel);
                if (string.IsNullOrWhiteSpace(TxtMcMunu.Text)) TxtMcMunu.Text = "100";
            }
            else if (key == "earsc1" || key == "earsc2" || key == "earsc3")
            {
                ShowOnlyPanel(EarsC1Panel);
                if (string.IsNullOrWhiteSpace(TxtBaseline.Text)) TxtBaseline.Text = "7";
            }
            else
            {
                // others
                ShowOnlyPanel(null);
            }

        }
        private void ShowOnlyPanel(FrameworkElement? panelToShow)
        {
            YearBackPanel.Visibility = Visibility.Collapsed;
            BodaPanel.Visibility = Visibility.Collapsed;
            EarsC1Panel.Visibility = Visibility.Collapsed;

            if (panelToShow != null)
                panelToShow.Visibility = Visibility.Visible;
        }

        private void SetupButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNavigation(SettingButton);
            HeaderTitle.Text = "Settings";
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            ReportLibraryGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Collapsed;
            ModelGrid.Visibility = Visibility.Collapsed;

            SetupGrid.Visibility = Visibility.Visible;

            LoadSystemConfigIntoUi();
            LoadLlmConfigIntoUi();
        }

        private void SetActiveNavigation(Button activeButton)
        {
            Button[] navigationButtons =
            {
                ReportButton,
                SchedulerButton,
                DataSourceButton,
                ModelButton,
                SettingButton
            };

            foreach (Button button in navigationButtons)
            {
                button.Tag = ReferenceEquals(button, activeButton) ? "Active" : null;
            }
            ReportModeButtons.Visibility = ReferenceEquals(activeButton, ReportButton)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // ---------------------------
        // Setup (LLM config)
        // ---------------------------
        private sealed class ConfigEntry
        {
            public string Key { get; set; } = "";
            public string Value { get; set; } = "";
            public JTokenType TokenType { get; set; }
        }

        private const string LlmConfigFileName = "llm_config.json";

        private sealed class LlmConfig
        {
            public string baseUrl { get; set; } = "";
            public string apiKey { get; set; } = "";
            public string apiKeyProtected { get; set; } = "";
        }

        private string GetLlmConfigPath()
        {
            AppPaths.EnsureInitialized();
            return AppPaths.LlmConfigPath;
        }

        private static string ProtectApiKey(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return string.Empty;

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        private static string UnprotectApiKey(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
                return string.Empty;

            try
            {
                byte[] encrypted = Convert.FromBase64String(cipherText);
                byte[] plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetSystemConfigPath()
        {
            AppPaths.EnsureInitialized();
            return AppPaths.ConfigPath;
        }

        private void LoadSystemConfigIntoUi()
        {
            try
            {
                _systemConfigEntries.Clear();
                string path = GetSystemConfigPath();

                if (!File.Exists(path))
                {
                    _systemConfigEntries.Add(new ConfigEntry
                    {
                        Key = "Error",
                        Value = $"File not found: {path}"
                    });
                }
                else
                {
                    var json = JObject.Parse(File.ReadAllText(path));
                    foreach (var prop in json.Properties())
                    {
                        string valueText = prop.Value.Type == JTokenType.Object || prop.Value.Type == JTokenType.Array
                            ? prop.Value.ToString(Newtonsoft.Json.Formatting.None)
                            : prop.Value.ToString();

                        _systemConfigEntries.Add(new ConfigEntry
                        {
                            Key = prop.Name,
                            Value = valueText,
                            TokenType = prop.Value.Type
                        });
                    }
                }

                if (SystemConfigTable != null)
                    SystemConfigTable.ItemsSource = _systemConfigEntries;
            }
            catch (Exception ex)
            {
                _systemConfigEntries.Clear();
                _systemConfigEntries.Add(new ConfigEntry
                {
                    Key = "Error",
                    Value = ex.Message
                });

                if (SystemConfigTable != null)
                    SystemConfigTable.ItemsSource = _systemConfigEntries;
            }
        }

        private void RefreshSystemConfig_Click(object sender, RoutedEventArgs e)
        {
            LoadSystemConfigIntoUi();
            if (SystemConfigStatusText != null)
            {
                SystemConfigStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(129, 199, 132));
                SystemConfigStatusText.Text = "Refreshed";
            }
        }

        private static JToken ConvertEntryValue(ConfigEntry entry)
        {
            string text = entry.Value ?? string.Empty;

            if (entry.TokenType == JTokenType.Boolean &&
                bool.TryParse(text, out bool boolValue))
            {
                return new JValue(boolValue);
            }

            if ((entry.TokenType == JTokenType.Integer || entry.TokenType == JTokenType.Float) &&
                long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
            {
                return new JValue(longValue);
            }

            if ((entry.TokenType == JTokenType.Integer || entry.TokenType == JTokenType.Float) &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
            {
                return new JValue(doubleValue);
            }

            if (entry.TokenType == JTokenType.Null)
            {
                return string.IsNullOrWhiteSpace(text) ? JValue.CreateNull() : new JValue(text);
            }

            return new JValue(text);
        }

        private void SaveSystemConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = GetSystemConfigPath();
                var obj = new JObject();

                foreach (var entry in _systemConfigEntries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key))
                        continue;
                    if (entry.Key.Equals("password", StringComparison.OrdinalIgnoreCase))
                    {
                        obj[entry.Key] = "";
                        continue;
                    }
                    obj[entry.Key] = ConvertEntryValue(entry);
                }

                obj["passwordEnvironmentVariable"] = "FORESITE_SMTP_PASSWORD";

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
                File.WriteAllText(path, obj.ToString());
                LoadSystemConfigIntoUi();

                if (SystemConfigStatusText != null)
                {
                    SystemConfigStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(129, 199, 132));
                    SystemConfigStatusText.Text = "Saved. SMTP password is read from FORESITE_SMTP_PASSWORD.";
                }
            }
            catch (Exception ex)
            {
                if (SystemConfigStatusText != null)
                {
                    SystemConfigStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 154, 154));
                    SystemConfigStatusText.Text = ex.Message;
                }
                MessageBox.Show($"Failed to save system config: {ex.Message}", "Setup",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadLlmConfigIntoUi()
        {
            try
            {
                string path = GetLlmConfigPath();
                if (!File.Exists(path))
                    return;

                var cfg = JsonSerializer.Deserialize<LlmConfig>(File.ReadAllText(path));
                if (cfg == null) return;

                if (LlmBaseUrlBox != null) LlmBaseUrlBox.Text = cfg.baseUrl ?? "";
                string apiKey = !string.IsNullOrWhiteSpace(cfg.apiKey)
                    ? cfg.apiKey
                    : UnprotectApiKey(cfg.apiKeyProtected ?? string.Empty);

                if (LlmApiKeyBox != null) LlmApiKeyBox.Password = apiKey;

                // Auto-migrate legacy plaintext config to protected form.
                if (!string.IsNullOrWhiteSpace(cfg.apiKey))
                {
                    cfg.apiKeyProtected = ProtectApiKey(cfg.apiKey);
                    cfg.apiKey = string.Empty;
                    string migrated = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, migrated);
                }
            }
            catch (Exception ex)
            {
                // non-fatal
                Debug.WriteLine($"LoadLlmConfigIntoUi error: {ex.Message}");
            }
        }

        private void SaveLlmConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = new LlmConfig
                {
                    baseUrl = LlmBaseUrlBox?.Text?.Trim() ?? "",
                    apiKey = string.Empty,
                    apiKeyProtected = ProtectApiKey(LlmApiKeyBox?.Password?.Trim() ?? string.Empty)
                };

                string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetLlmConfigPath(), json);

                if (LlmConfigStatusText != null)
                {
                    LlmConfigStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(129, 199, 132));
                    LlmConfigStatusText.Text = $"Saved to {LlmConfigFileName}";
                }
            }
            catch (Exception ex)
            {
                if (LlmConfigStatusText != null)
                {
                    LlmConfigStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 154, 154));
                    LlmConfigStatusText.Text = ex.Message;
                }
                MessageBox.Show($"Failed to save LLM config: {ex.Message}", "Setup",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}

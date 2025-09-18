// -----------------------------------------------------------------------------
//  Author:      Tao He
//  Email:       tao.he@utah.edu
//  Created:     2025-07-01
//  Description: Dashboard user control logic for ForeSITETestApp (WPF).
// -----------------------------------------------------------------------------


using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
//using PdfSharp.Drawing;
//using PdfSharp.Fonts;
//using PdfSharp.Pdf;
// remove：using PdfSharp.*; and GlobalFontSettings.FontResolver 
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
        public Dashboard(MainWindow window)
        {
            InitializeComponent();
            this.window = window;
            _httpClient = window.getHttpClient();
            _reportElements = new List<ReportElement>(); // Initialize report elements list
            InitializeDataSources();

            ObservableCollection<SchedulerTask> schedulers = DBHelper.GetAllSchedulers();
            SchedulerTable.ItemsSource = schedulers;


            // Initialize default FlowDocument
            _titleDocument = new FlowDocument(new Paragraph(new Run("Click to edit title")));
            // Set custom font resolver
            //GlobalFontSettings.FontResolver = new CustomFontResolver();
            DrawingCanvas.Height = 300; // Minimum height for placeholder
            CheckAndManagePlaceholder();

            DataContext = this;
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }



        public int DataSourceCount
        {
            get { return _dataSources?.Count ?? 0; }
        }

        private void InitializeDataSources()
        {
            // Load data sources from the database
            _dataSources = DBHelper.GetAllDataSources();

            // Subscribe to collection changed events
            if (_dataSources != null)
            {
                _dataSources.CollectionChanged += (sender, e) =>
                {
                    // Manually update the TextBlock when collection changes
                    UpdateDataSourceCountDisplay();
                };
            }

            DataSourceTable.ItemsSource = _dataSources;
            DataSourceSelector.ItemsSource = _dataSources; // Bind to DataSourceSelector

            // Initialize the display
            UpdateDataSourceCountDisplay();
        }


        private void SchedulerButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Schedule Builder";
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            SchedulerGrid.Visibility = Visibility.Visible;
            ReportsGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Collapsed;
            ModelGrid.Visibility = Visibility.Collapsed;
        }

        // Helper method to refresh just the data source count
        private void RefreshDataSourceCount()
        {
            try
            {
                // Get updated count from database
                var latestDataSources = DBHelper.GetAllDataSources();

                // Update the collection if the count has changed
                if (_dataSources != null && _dataSources.Count != latestDataSources.Count)
                {
                    RefreshDataSourcesList();
                }

                // Manually update the TextBlock display
                UpdateDataSourceCountDisplay();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing data source count: {ex.Message}");
            }
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Home";
            DefaultContentGrid.Visibility = Visibility.Visible;
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Collapsed;
            ModelGrid.Visibility = Visibility.Collapsed;
            RefreshDataSourceCount();
        }

        private void UpdateDataSourceCountDisplay()
        {
            try
            {
                // Update the TextBlock directly
                if (DataSourceCountTextBlock != null)
                {
                    DataSourceCountTextBlock.Text = (_dataSources?.Count ?? 0).ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating data source count display: {ex.Message}");
            }
        }

        private void ReportButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Report Builder";
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Visible;
            DataSourceGrid.Visibility = Visibility.Collapsed;
            ModelGrid.Visibility = Visibility.Collapsed;
        }

        private void DataSourceButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Data Source Builder";
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Visible;
            ModelGrid.Visibility = Visibility.Collapsed;
        }

        private void ModelButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Model Management";
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Collapsed;
            ModelGrid.Visibility = Visibility.Visible;
        }

        private async void LoadDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prepare JSON payload
                string json = "{\"init\": \"run\"}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Send POST request to 127.0.0.1
                HttpResponseMessage response = await _httpClient.PostAsync("http://127.0.0.1:5001/epyapi", content);

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
                        Text = "Drawing Area Placeholder",
                        Foreground = Brushes.Gray,
                        FontSize = 14,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };
                    // Center the placeholder (adjust based on canvas size)
                    Canvas.SetLeft(_placeholderTextBlock, (DrawingCanvas.ActualWidth - _placeholderTextBlock.ActualWidth) / 2);
                    Canvas.SetTop(_placeholderTextBlock, (DrawingCanvas.ActualHeight - _placeholderTextBlock.ActualHeight) / 2);
                    if (DrawingCanvas.ActualWidth == 0 || DrawingCanvas.ActualHeight == 0)
                    {
                        DrawingCanvas.SizeChanged += (s, e) => CheckAndManagePlaceholder();
                    }
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
                // Manually update the TextBlock
                UpdateDataSourceCountDisplay();

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

            var mainStackPanel = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { }
            };

            // Data Source Name
            mainStackPanel.Children.Add(new TextBlock
            {
                Text = "Data Source Name",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var nameInput = new TextBox
            {
                Name = "DataSourceNameInput",
                Width = 400,
                Height = 25,
                Margin = new Thickness(0, 0, 0, 15)
            };
            mainStackPanel.Children.Add(nameInput);

            // Real-time Data Radio Buttons
            mainStackPanel.Children.Add(new TextBlock
            {
                Text = "Data Type",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var realtimePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var realtimeRadio = new RadioButton
            {
                Content = "Real-time Data (API)",
                Name = "RealtimeRadio",
                IsChecked = true,
                Margin = new Thickness(0, 0, 20, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            var staticRadio = new RadioButton
            {
                Content = "Local Data (CSV File)",
                Name = "StaticRadio",
                Margin = new Thickness(0, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            realtimePanel.Children.Add(realtimeRadio);
            realtimePanel.Children.Add(staticRadio);
            mainStackPanel.Children.Add(realtimePanel);

            // Real-time Data Fields (initially visible)
            var realtimeFieldsPanel = new StackPanel
            {
                Name = "RealtimeFieldsPanel",
                Margin = new Thickness(0, 0, 0, 15)
            };

            realtimeFieldsPanel.Children.Add(new TextBlock
            {
                Text = "Data URL",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var dataUrlInput = new TextBox
            {
                Name = "DataUrlInput",
                Width = 400,
                Height = 25,
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = "Enter the API endpoint URL for real-time data"
            };
            realtimeFieldsPanel.Children.Add(dataUrlInput);

            realtimeFieldsPanel.Children.Add(new TextBlock
            {
                Text = "Resource URL",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var resourceUrlInput = new TextBox
            {
                Name = "ResourceUrlInput",
                Width = 400,
                Height = 25,
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = "Enter the resource identifier (e.g., dataset ID)"
            };
            realtimeFieldsPanel.Children.Add(resourceUrlInput);

            realtimeFieldsPanel.Children.Add(new TextBlock
            {
                Text = "App Token",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var appTokenInput = new TextBox
            {
                Name = "AppTokenInput",
                Width = 400,
                Height = 25,
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = "Enter the app token (e.g., app ID)"
            };
            realtimeFieldsPanel.Children.Add(appTokenInput);

            mainStackPanel.Children.Add(realtimeFieldsPanel);

            // Static Data Fields (initially hidden)
            var staticFieldsPanel = new StackPanel
            {
                Name = "StaticFieldsPanel",
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 15)
            };

            staticFieldsPanel.Children.Add(new TextBlock
            {
                Text = "CSV File Path",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var filePathPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var filePathLabel = new Label
            {
                Name = "FilePathLabel",
                Content = "No file selected",
                Width = 300,
                Height = 25,
                Background = Brushes.LightGray,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                Padding = new Thickness(5, 0, 5, 0)
            };

            var browseButton = new Button
            {
                Content = "Browse...",
                Width = 80,
                Height = 25,
                Margin = new Thickness(10, 0, 0, 0)
            };

            // Browse button click event
            browseButton.Click += (s, args) =>
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
                    filePathLabel.Content = openFileDialog.FileName;
                    filePathLabel.ToolTip = openFileDialog.FileName;
                }
            };

            filePathPanel.Children.Add(filePathLabel);
            filePathPanel.Children.Add(browseButton);
            staticFieldsPanel.Children.Add(filePathPanel);

            mainStackPanel.Children.Add(staticFieldsPanel);

            // Radio button event handlers to show/hide fields
            realtimeRadio.Checked += (s, args) =>
            {
                realtimeFieldsPanel.Visibility = Visibility.Visible;
                staticFieldsPanel.Visibility = Visibility.Collapsed;
            };

            staticRadio.Checked += (s, args) =>
            {
                realtimeFieldsPanel.Visibility = Visibility.Collapsed;
                staticFieldsPanel.Visibility = Visibility.Visible;
            };

            // Save and Cancel buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var saveButton = new Button
            {
                Content = "💾 Save Data Source",
                Style = FindResource("HeaderButtonStyle") as Style,
                Width = 150,
                Margin = new Thickness(0, 0, 10, 0)
            };

            buttonPanel.Children.Add(saveButton);
            mainStackPanel.Children.Add(buttonPanel);

            var newTab = new TabItem
            {
                Header = $"➕ New Data Source",
                Content = new ScrollViewer
                {
                    Content = mainStackPanel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };


            // Save button click event
            saveButton.Click += async (s, args) =>
            {
                try
                {
                    // Validate input
                    if (string.IsNullOrWhiteSpace(nameInput.Text))
                    {
                        MessageBox.Show("Data source name is required.", "Validation Error",
                                      MessageBoxButton.OK, MessageBoxImage.Warning);
                        nameInput.Focus();
                        return;
                    }

                    bool isRealtime = realtimeRadio.IsChecked == true;
                    string dataUrl = "";
                    string resourceUrl = "";
                    string appToken = "";

                    if (isRealtime)
                    {
                        // Validate real-time fields
                        if (string.IsNullOrWhiteSpace(dataUrlInput.Text))
                        {
                            MessageBox.Show("Data URL is required for real-time data sources.", "Validation Error",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                            dataUrlInput.Focus();
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(resourceUrlInput.Text))
                        {
                            MessageBox.Show("Resource URL is required for real-time data sources.", "Validation Error",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                            resourceUrlInput.Focus();
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(appTokenInput.Text))
                        {
                            MessageBox.Show("App Token is required for real-time data sources.", "Validation Error",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                            appTokenInput.Focus();
                            return;
                        }

                        dataUrl = dataUrlInput.Text.Trim();
                        resourceUrl = resourceUrlInput.Text.Trim();
                        appToken=appTokenInput.Text.Trim();

                    }
                    else
                    {
                        // Validate static file field
                        string? filePath = filePathLabel.Content?.ToString();
                        if (string.IsNullOrEmpty(filePath) || filePath == "No file selected")
                        {
                            MessageBox.Show("Please select a CSV file for static data sources.", "Validation Error",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        if (!File.Exists(filePath))
                        {
                            MessageBox.Show("The selected file does not exist.", "File Error",
                                          MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        dataUrl = filePath;
                        resourceUrl = "local";
                    }

                    // Check for duplicate names in memory collection
                    if (_dataSources != null && _dataSources.Any(predicate: ds => ds.Name.Equals(nameInput.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        var result = MessageBox.Show(
                            $"A data source with the name '{nameInput.Text.Trim()}' already exists.\nDo you want to overwrite it?",
                            "Duplicate Name",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }

                    // Save to database
                    bool success = AddDataSourceToDatabase(
                        nameInput.Text.Trim(),
                        dataUrl,
                        resourceUrl,
                        appToken,
                        isRealtime);

                    if (success)
                    {
                        // Refresh data sources
                        RefreshDataSourcesList();

                        // Close the tab
                        DataSourceTabs.Items.Remove(newTab);
                        DataSourceTabs.SelectedIndex = 0;

                        // Show success message
                        MessageBox.Show(
                            $"Data source '{nameInput.Text.Trim()}' has been saved successfully!\n\n" +
                            $"Type: {(isRealtime ? "Real-time" : "Static")}\n" +
                            $"Data: {(isRealtime ? dataUrl : Path.GetFileName(dataUrl))}",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to save the data source. Please check the database connection.",
                                      "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while saving the data source:\n\n{ex.Message}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };



            DataSourceTabs.Items.Add(newTab);
            DataSourceTabs.SelectedItem = newTab;
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

                // Choose Save File Dialog
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON Template (*.json)|*.json",
                    DefaultExt = "json",
                    FileName = "report_template.json"
                };
                if (sfd.ShowDialog() != true)
                    return;

                
                var root = new JObject
                {
                    ["templateVersion"] = "1.0",
                    ["createdAt"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    ["app"] = "ForeSITETestApp",
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
                    scheduleStart = g["BeginDate"]?.ToString() ?? "";
                    scheduleFreq = g["Freq"]?.ToString() ?? "";
                }

                
                root["schedule"] = new JObject
                {
                    ["startDate"] = scheduleStart,   // e.g. "2025-09-12"
                    ["frequency"] = scheduleFreq,    // e.g. "By Week"
                    ["cron"] = ""
                };


                File.WriteAllText(sfd.FileName, root.ToString(Newtonsoft.Json.Formatting.Indented));

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

                var task = new SchedulerTask
                {
                    Recipients = recipients,          // allow null or empty
                    AttachmentPath = sfd.FileName,        // full path to the saved template
                    StartDate = scheduleStart ?? "", // "YYYY-MM-DD"
                    Freq = scheduleFreq ?? ""  // "By Week"/"daily"/"weekly"/...
                };

                bool ok = DBHelper.InsertScheduler(task);
                if (ok)
                {
                    RefreshSchedulerUI();


                    MessageBox.Show("Template saved and scheduler record inserted.",
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

            static string? S(JToken? token) => token?.ToString();

            try
            {
                if (tag is JObject jtag)
                {
                    
                    var g = jtag["graph"] as JObject ?? jtag;

                    title = S(jtag["title"]) ?? S(g["Title"]);
                    model = S(g["Model"]);
                    yearBack = S(g["YearBack"]) ?? S(g["years_back"]);
                    trainSplitRatio = S(g["TrainSplitRatio"]);

                    
                    dataSource = S(g["DataSource"]);
                    beginDate = S(g["BeginDate"]);
                    freq = S(g["Freq"]);
                    threshold = S(g["Threshold"]);
                    useTrainSplit = S(g["UseTrainSplit"]);
                    trainEndDate = S(g["TrainEndDate"]);
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

                string model = (ModelSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Farrington";
                string dataSource = (DataSourceSelector.SelectedItem as DataSource)?.Name ?? "";
                string yearBack = (YearBackSelector.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "5";
                bool useTrainSplit = TrainSplitCheckBox.IsChecked ?? false;
                string beginDate = BeginDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
                string freq = (FreqSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "By Week";
                string threshold = ThresholdInput.Text?.Trim() ?? "";

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
                    ["yearBack"] = yearBack,
                    ["useTrainSplit"] = useTrainSplit,
                    ["beginDate"] = beginDate,
                    ["freq"] = freq,
                    ["threshold"] = threshold,
                    ["title"] = plotTitle
                };

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
                HttpResponseMessage response = await _httpClient.PostAsync("http://127.0.0.1:5001/epyapi", content);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        JObject responseJson = JObject.Parse(responseContent);
                        string? status = responseJson["status"]?.ToString();
                        string? filePath = responseJson["plot_path"]?.ToString();

                        if (status?.ToLower() == "processed" && !string.IsNullOrEmpty(filePath))
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
                            MessageBox.Show($"Server response missing 'ready' status or 'file' path. Response: {responseContent}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Newtonsoft.Json.JsonException)
                    {
                        MessageBox.Show($"Invalid JSON response from server: {responseContent}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show($"Failed to generate plot. Status: {response.StatusCode}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating or adding plot: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                foreach (var row in selectedRows)
                {
                    
                    string recipients = bulkRecipients ?? (row.Recipients ?? string.Empty);
                    string attachPath = row.AttachmentPath ?? string.Empty;
                    string startDate = row.StartDate ?? string.Empty;   // YYYY-MM-DD
                    string freq = row.Freq ?? string.Empty;

                    if (DBHelper.UpdateScheduler(row.Id, recipients, attachPath, startDate, freq))
                    {
                        // update successful, update the memory object too
                        row.Recipients = recipients;
                        row.AttachmentPath = attachPath;
                        row.StartDate = startDate;
                        row.Freq = freq;
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
            var psi = new ProcessStartInfo("schtasks", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (elevate) psi.Verb = "runas";   // trigger UAC
            using var p = Process.Start(psi);
            string output = p!.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output, error);
        }

        private void SchedulerStart_Click(object sender, RoutedEventArgs e)
        {
            const string taskName = "foresite_alerting";
            string exePath = Path.Combine(AppContext.BaseDirectory, "ForeSITEScheduler.exe");

            try
            {
                if (TaskExists(taskName))
                {
                    MessageBox.Show($"Task '{taskName}' already exists.", "Scheduler");
                    return;
                }

                // schtasks /create /tn foresite_alerting /tr "C:\...\ForeSITEScheduler.exe" /sc daily /st 09:00 /f
                string args = $"/create /tn {taskName} /tr \"\\\"{exePath}\\\"\" /sc daily /st 09:00 /f";
                var (exit, stdout, stderr) = RunSchTasks(args);

                if (exit == 0)
                    MessageBox.Show($"Task '{taskName}' created successfully to run daily at 09:00.\n{stdout}", "Scheduler");
                else
                    MessageBox.Show($"Failed to create task.\n{stderr}", "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        private void SchedulerEnd_Click(object sender, RoutedEventArgs e)
        {
            const string taskName = "foresite_alerting";
            try
            {
                if (!TaskExists(taskName))
                {
                    MessageBox.Show($"Task '{taskName}' does not exist.", "Scheduler");
                    return;
                }

                var (exit, stdout, stderr) = RunSchTasks($"/delete /tn {taskName} /f");
                if (exit == 0)
                    MessageBox.Show($"Task '{taskName}' deleted.\n{stdout}", "Scheduler");
                else
                    MessageBox.Show($"Failed to delete task.\n{stderr}", "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling checkbox click: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}

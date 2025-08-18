
using ForeSITETestApp;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;


namespace ForeSITETestApp
{
    // Enhanced NotebookWindow class
    public class NotebookWindow : Window
    {
        private StackPanel _cellContainer;
        private string _savePath;
        internal NotebookClient _notebookClient; // Made internal so NamespaceWindow can access it

        private ComboBox _dataSourceComboBox;
        private TextBox _variableNameTextBox;
        private TextBox _thresholdTextBox;
        private Button _addVariableButton;
        private DataSourcesResult _dataSourcesResult = new DataSourcesResult();


        public NotebookWindow(MainWindow window)
        {
            InitializeWindow();
            _notebookClient = new NotebookClient(window.getHttpClient());
            _savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "notebook.json");
            LoadDataSources();
            // Check server connection on startup
            _ = CheckServerConnection();
        }

        /// <summary>
        /// Update ComboBox with a specific list of data sources
        /// </summary>
        /// <param name="dataSources">List of data sources</param>
        /// <param name="defaultThreshold">Default threshold value</param>
        private void UpdateComboBoxWithDataSources()
        {
            try
            {
                _dataSourceComboBox.Items.Clear();

                foreach (var dataSource in _dataSourcesResult.DataSources.OrderBy(ds => ds.Name))
                {
                    var item = new ComboBoxItem
                    {
                        Content = dataSource.Name,
                        Tag = dataSource,
                        ToolTip = $"{dataSource.Name}"
                    };
                    _dataSourceComboBox.Items.Add(item);
                }

                if (_dataSourceComboBox.Items.Count > 0)
                {
                    _dataSourceComboBox.SelectedIndex = 0;
                }

                // Update default threshold
                _thresholdTextBox.Text = _dataSourcesResult.DefaultThreshold.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ComboBox with data sources: {ex.Message}");
            }
        }

        public void LoadDataSources()
        {
            _dataSourcesResult.DataSources = DBHelper.GetAllDataSources().ToList();
            // Update ComboBox with local data sources
            UpdateComboBoxWithDataSources();
        }

        private void InitializeWindow()
        {
            Title = "Notebook";
            Width = 1150;
            Height = 700;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ShowInTaskbar = true;
            ResizeMode = ResizeMode.CanResize;

            _cellContainer = new StackPanel { Margin = new Thickness(10) };

            var toolbar = CreateToolbar();
            var dataSourceToolbar = CreateDataSourceToolbar();
            var scrollViewer = new ScrollViewer
            {
                Content = _cellContainer,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var mainPanel = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(dataSourceToolbar, Dock.Top);

            mainPanel.Children.Add(toolbar);
            mainPanel.Children.Add(dataSourceToolbar);
            mainPanel.Children.Add(scrollViewer);

            Content = mainPanel;
            AddInitialCells();
        }

        private StackPanel CreateDataSourceToolbar()
        {

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 5, 10),
                Background = Brushes.LightGray
            };

            // Title label
            stackPanel.Children.Add(new Label
            {
                Content = "📊 Data Sources:",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });

            // Data source selector
            stackPanel.Children.Add(new Label
            {
                Content = "Source:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });

            _dataSourceComboBox = new ComboBox
            {
                Width = 150,
                Height = 25,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0)
            };
            stackPanel.Children.Add(_dataSourceComboBox);



            foreach (var dataSource in _dataSourcesResult.DataSources)
            {
                var item = new ComboBoxItem
                {
                    Content = dataSource.Name,           // "Covid-19 Deaths"
                    Tag = dataSource,                    // Full DataSourceInfo object
                    ToolTip = $"{dataSource.Name} "
                    // Tooltip: "CDC COVID-19 death data (weekly) (Type: cdc_data)"
                };
                _dataSourceComboBox.Items.Add(item);
            }

            // Variable name input
            stackPanel.Children.Add(new Label
            {
                Content = "Variable:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });

            _variableNameTextBox = new TextBox
            {
                Width = 120,
                Height = 25,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0),
                Text = "data_df"
            };
            stackPanel.Children.Add(_variableNameTextBox);

            // Threshold input
            stackPanel.Children.Add(new Label
            {
                Content = "Threshold:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });

            _thresholdTextBox = new TextBox
            {
                Width = 80,
                Height = 25,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0),
                Text = "1500"
            };
            stackPanel.Children.Add(_thresholdTextBox);

            // Add variable button
            _addVariableButton = new Button
            {
                Content = "➕ Add to Namespace",
                Width = 140,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            _addVariableButton.Click += AddVariable_Click;
            stackPanel.Children.Add(_addVariableButton);



            return stackPanel;

        }

        private void GenerateSampleCode(string variableName, string dataSource)
        {
            var sampleCode = new StringBuilder();
            sampleCode.AppendLine($"# Sample code for {variableName} ({dataSource})");
            sampleCode.AppendLine($"print(f\"Variable '{variableName}' info:\")");
            sampleCode.AppendLine($"print(f\"Type: {{type({variableName})}}\")");
            sampleCode.AppendLine($"print(f\"Shape: {{{variableName}.shape}}\")");
            sampleCode.AppendLine($"print(f\"Columns: {{{variableName}.columns.tolist()}}\")");
            sampleCode.AppendLine($"");
            sampleCode.AppendLine($"# Display first few rows");
            sampleCode.AppendLine($"print(\"\\nFirst 5 rows:\")");
            sampleCode.AppendLine($"{variableName}.head()");

            // Create a new code cell with the sample code
            AddCodeCellWithContent(sampleCode.ToString());
        }

        private bool IsValidPythonIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Must start with letter or underscore
            if (!char.IsLetter(name[0]) && name[0] != '_')
                return false;

            // Rest must be letters, digits, or underscores
            for (int i = 1; i < name.Length; i++)
            {
                if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                    return false;
            }

            // Check against Python keywords
            var pythonKeywords = new HashSet<string>
            {
                "and", "as", "assert", "break", "class", "continue", "def", "del", "elif", "else",
                "except", "exec", "finally", "for", "from", "global", "if", "import", "in", "is",
                "lambda", "not", "or", "pass", "print", "raise", "return", "try", "while", "with",
                "yield", "None", "True", "False"
            };

            return !pythonKeywords.Contains(name.ToLower());
        }

        private async void AddVariable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (_dataSourceComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a data source.", "Validation Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var variableName = _variableNameTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    MessageBox.Show("Please enter a variable name.", "Validation Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Validate variable name is a valid Python identifier
                if (!IsValidPythonIdentifier(variableName))
                {
                    MessageBox.Show("Variable name must be a valid Python identifier.\n" +
                                  "It should start with a letter or underscore, and contain only letters, numbers, and underscores.",
                                  "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(_thresholdTextBox.Text, out int threshold) || threshold < 0)
                {
                    MessageBox.Show("Please enter a valid threshold value (non-negative integer).", "Validation Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Disable button during operation
                _addVariableButton.IsEnabled = false;
                _addVariableButton.Content = "⏳ Adding...";

                var selectedItem = (ComboBoxItem)_dataSourceComboBox.SelectedItem;
                var dataSource = ((DataSource)selectedItem.Tag).Name;

                // Add variable to namespace
                var result = await _notebookClient.AddVariableAsync(dataSource, variableName, threshold);

                if (result.Status == "success" || result.Status == "warning")
                {
                    var message = new StringBuilder();
                    message.AppendLine($"✅ Variable '{variableName}' created successfully!");
                    message.AppendLine($"Data Source: {dataSource}");

                    if (result.DataInfo?.Shape != null && result.DataInfo.Shape.Length >= 2)
                    {
                        message.AppendLine($"Shape: {result.DataInfo.Shape[0]} rows × {result.DataInfo.Shape[1]} columns");
                    }

                    if (result.DataInfo?.Columns != null && result.DataInfo.Columns.Count > 0)
                    {
                        message.AppendLine($"Columns: {string.Join(", ", result.DataInfo.Columns)}");
                    }

                    if (!string.IsNullOrEmpty(result.DataInfo?.MemoryUsage))
                    {
                        message.AppendLine($"Memory Usage: {result.DataInfo.MemoryUsage}");
                    }

                    if (result.DataInfo?.DateRange != null &&
                        !string.IsNullOrEmpty(result.DataInfo.DateRange.Start) &&
                        !string.IsNullOrEmpty(result.DataInfo.DateRange.End))
                    {
                        message.AppendLine($"Date Range: {result.DataInfo.DateRange.Start} to {result.DataInfo.DateRange.End}");
                    }

                    if (result.Overwritten)
                    {
                        message.AppendLine("\n⚠️ Note: This variable already existed and was overwritten.");
                    }

                    message.AppendLine($"\nYou can now use '{variableName}' in your Python code cells.");

                    MessageBox.Show(message.ToString(), "Variable Added",
                                  MessageBoxButton.OK, MessageBoxImage.Information);

                    // Generate sample code
                    GenerateSampleCode(variableName, dataSource);
                }
                else
                {
                    MessageBox.Show($"Failed to add variable:\n\n{result.Message}", "Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding variable: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _addVariableButton.IsEnabled = true;
                _addVariableButton.Content = "➕ Add to Namespace";
            }
        }
        private StackPanel CreateToolbar()
        {
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 5, 10),
                Background = Brushes.LightGray
            };

            var saveButton = CreateButton("💾 Save", SaveNotebook_Click);
            var loadButton = CreateButton("📁 Load", LoadNotebook_Click);
            var addCodeButton = CreateButton("➕ Code", (s, e) => AddCodeCell());
            var addMarkdownButton = CreateButton("📝 Markdown", (s, e) => AddMarkdownCell());
            var runAllButton = CreateButton("▶️ Run All", RunAllCells_Click);
            var clearOutputButton = CreateButton("🗑️ Clear Output", 140, ClearAllOutput_Click);
            var clearNamespaceButton = CreateButton("🧹 Clear Variables", 140, ClearNamespace_Click);
            var viewNamespaceButton = CreateButton("📊 View Variables", 140, ViewNamespace_Click);

            toolbar.Children.Add(saveButton);
            toolbar.Children.Add(loadButton);
            toolbar.Children.Add(new Separator { Width = 10 });
            toolbar.Children.Add(addCodeButton);
            toolbar.Children.Add(addMarkdownButton);
            toolbar.Children.Add(new Separator { Width = 10 });
            toolbar.Children.Add(runAllButton);
            toolbar.Children.Add(clearOutputButton);
            toolbar.Children.Add(new Separator { Width = 10 });
            toolbar.Children.Add(clearNamespaceButton);
            toolbar.Children.Add(viewNamespaceButton);

            return toolbar;
        }

        private Button CreateButton(string content, RoutedEventHandler clickHandler)
        {
            return this.CreateButton(content, 120, clickHandler);
        }

        private Button CreateButton(string content, int width, RoutedEventHandler clickHandler)
        {
            var button = new Button
            {
                Content = content,
                Width = width,
                Height = 30,
                Margin = new Thickness(5, 5, 5, 5),
                Padding = new Thickness(5)
            };
            button.Click += clickHandler;
            return button;
        }

        private void AddInitialCells()
        {
            AddCodeCell();
        }

        private void AddCodeCell()
        {
            var cellPanel = CreateCellPanel();
            var cellContent = new StackPanel();

            // Cell type indicator
            var cellTypeLabel = new Label
            {
                Content = "Code",
                Background = Brushes.LightBlue,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(5, 2, 5, 2)
            };

            // Code editor
            var textEditor = new TextEditor
            {
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python"),
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 120,
                Text = "# Enter Python code here\nprint('Hello, Notebook!')",
                FontSize = 12,
                ShowLineNumbers = true
            };

            // Output area
            var outputArea = new TextBox
            {
                IsReadOnly = true,
                Background = Brushes.Black,
                Foreground = Brushes.LightGreen,
                FontFamily = new FontFamily("Consolas"),
                MinHeight = 60,
                MaxHeight = 200,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, // 添加水平滚动条
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap, // 禁用文本换行，让内容水平滚动
                Text = "Output will appear here...",
                Visibility = Visibility.Collapsed
            };

            // Button panel
            var buttonPanel = CreateCellButtonPanel(textEditor, outputArea, cellPanel, CellType.Code);

            cellContent.Children.Add(cellTypeLabel);
            cellContent.Children.Add(textEditor);
            cellContent.Children.Add(outputArea);
            cellContent.Children.Add(buttonPanel);

            cellPanel.Child = cellContent;
            _cellContainer.Children.Add(cellPanel);
        }

        private void AddMarkdownCell()
        {
            var cellPanel = CreateCellPanel();
            var cellContent = new StackPanel();

            // Cell type indicator
            var cellTypeLabel = new Label
            {
                Content = "Markdown",
                Background = Brushes.LightCoral,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(5, 2, 5, 2)
            };

            // Markdown editor
            var textEditor = new TextEditor
            {
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("MarkDown"),
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 120,
                Text = "# Markdown Cell\n\nWrite your **markdown** content here.\n\n- List item 1\n- List item 2",
                FontSize = 12,
                ShowLineNumbers = false
            };

            // Rendered output area
            var outputArea = new TextBox
            {
                IsReadOnly = true,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                MinHeight = 60,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "Rendered markdown will appear here...",
                Visibility = Visibility.Collapsed
            };

            // Button panel
            var buttonPanel = CreateCellButtonPanel(textEditor, outputArea, cellPanel, CellType.Markdown);

            cellContent.Children.Add(cellTypeLabel);
            cellContent.Children.Add(textEditor);
            cellContent.Children.Add(outputArea);
            cellContent.Children.Add(buttonPanel);

            cellPanel.Child = cellContent;
            _cellContainer.Children.Add(cellPanel);
        }

        private Border CreateCellPanel()
        {
            return new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(5)
            };
        }

        private StackPanel CreateCellButtonPanel(TextEditor editor, TextBox outputArea, Border cellPanel, CellType cellType)
        {
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 0, 0)
            };

            var runButton = new Button
            {
                Content = cellType == CellType.Code ? "▶️ Run" : "📝 Render",
                Width = 100,
                Margin = new Thickness(5, 0, 0, 0)
            };
            runButton.Click += async (s, e) => await RunCell(editor, outputArea, cellType);

            var addCodeButton = new Button
            {
                Content = "➕ Code",
                Width = 100,
                Margin = new Thickness(5, 0, 0, 0)
            };
            addCodeButton.Click += (s, e) => AddCodeCell();

            var addMarkdownButton = new Button
            {
                Content = "➕ MD",
                Width = 100,
                Margin = new Thickness(5, 0, 0, 0)
            };
            addMarkdownButton.Click += (s, e) => AddMarkdownCell();

            var deleteButton = new Button
            {
                Content = "🗑️ Delete",
                Width = 100,
                Margin = new Thickness(5, 0, 0, 0),
                Background = Brushes.LightCoral
            };
            deleteButton.Click += (s, e) => _cellContainer.Children.Remove(cellPanel);

            buttonPanel.Children.Add(runButton);
            buttonPanel.Children.Add(addCodeButton);
            buttonPanel.Children.Add(addMarkdownButton);
            buttonPanel.Children.Add(deleteButton);

            return buttonPanel;
        }

        /// <summary>
        /// Updates the output display (must be called on UI thread)
        /// </summary>
        private void UpdateOutputDisplay(TextBox outputArea, string message, OutputType type)
        {
            outputArea.Visibility = Visibility.Visible;
            outputArea.Text = message;

            // Set colors based on output type
            switch (type)
            {
                case OutputType.Success:
                    outputArea.Background = Brushes.Black;
                    outputArea.Foreground = Brushes.LightGreen;
                    break;
                case OutputType.Error:
                    outputArea.Background = Brushes.DarkRed;
                    outputArea.Foreground = Brushes.White;
                    break;
                case OutputType.Warning:
                    outputArea.Background = Brushes.DarkGoldenrod;
                    outputArea.Foreground = Brushes.White;
                    break;
                case OutputType.Info:
                    outputArea.Background = Brushes.DarkBlue;
                    outputArea.Foreground = Brushes.White;
                    break;
                default:
                    outputArea.Background = Brushes.Black;
                    outputArea.Foreground = Brushes.LightGray;
                    break;
            }

            // Scroll to the end to show latest content
            outputArea.ScrollToEnd();
        }

        /// <summary>
        /// Sets the output display with appropriate styling
        /// </summary>
        /// <param name="outputArea">TextBox to update</param>
        /// <param name="message">Message to display</param>
        /// <param name="type">Type of output for styling</param>
        private void SetOutputDisplay(TextBox outputArea, string message, OutputType type)
        {
            // Ensure we're on the UI thread
            if (outputArea.Dispatcher.CheckAccess())
            {
                UpdateOutputDisplay(outputArea, message, type);
            }
            else
            {
                outputArea.Dispatcher.Invoke(() => UpdateOutputDisplay(outputArea, message, type));
            }
        }

        /// <summary>
        /// Clears the output area and resets it to default state
        /// </summary>
        /// <param name="outputArea">TextBox to clear</param>
        private void ClearOutput(TextBox outputArea)
        {
            if (outputArea != null)
            {
                SetOutputDisplay(outputArea, "Ready to execute code...", OutputType.Default);
                outputArea.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Enhanced version of RunCell that works with the new RunPythonCode implementation
        /// </summary>
        /// <param name="editor">Text editor containing the code</param>
        /// <param name="outputArea">Output display area</param>
        /// <param name="cellType">Type of cell (Code or Markdown)</param>
        private async Task RunCell(TextEditor editor, TextBox outputArea, CellType cellType)
        {
            if (editor == null || outputArea == null)
            {
                MessageBox.Show("Invalid cell configuration.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Show the output area and set initial state
            outputArea.Visibility = Visibility.Visible;

            try
            {
                // Disable the editor during execution to prevent changes
                editor.IsReadOnly = true;

                if (cellType == CellType.Code)
                {
                    // Validate that we have code to execute
                    var code = editor.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        SetOutputDisplay(outputArea, "No code to execute. Please enter some Python code.", OutputType.Warning);
                        return;
                    }

                    // Check server connection before attempting execution
                    SetOutputDisplay(outputArea, "Checking server connection...", OutputType.Info);

                    var isServerRunning = await ValidateServerConnection();
                    if (!isServerRunning)
                    {
                        SetOutputDisplay(outputArea,
                            "❌ Cannot connect to Python server.\n\n" +
                            "Please ensure the Python server is running:\n" +
                            "1. Open terminal/command prompt\n" +
                            "2. Navigate to your Python script directory\n" +
                            "3. Run: python epyflaServer_enhanced.py\n" +
                            "4. Wait for 'Running on http://127.0.0.1:5001' message\n\n" +
                            "Then try executing the cell again.",
                            OutputType.Error);
                        return;
                    }

                    // Execute the Python code
                    await RunPythonCode(code, outputArea);
                }
                else if (cellType == CellType.Markdown)
                {
                    // Handle markdown rendering
                    await RenderMarkdown(editor.Text, outputArea);
                }
                else
                {
                    SetOutputDisplay(outputArea, $"Unknown cell type: {cellType}", OutputType.Error);
                }
            }
            catch (Exception ex)
            {
                SetOutputDisplay(outputArea,
                    $"Cell Execution Error: {ex.Message}\n\n" +
                    "An unexpected error occurred while running this cell.\n" +
                    "Please check your code and try again.",
                    OutputType.Error);

                // Log for debugging
                System.Diagnostics.Debug.WriteLine($"RunCell Exception: {ex}");
            }
            finally
            {
                // Re-enable the editor
                editor.IsReadOnly = false;
            }
        }

        /// <summary>
        /// Renders markdown content (placeholder implementation)
        /// </summary>
        /// <param name="markdownText">Markdown content to render</param>
        /// <param name="outputArea">Output area to display rendered content</param>
        private async Task RenderMarkdown(string markdownText, TextBox outputArea)
        {
            await Task.Delay(100); // Simulate processing time

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                SetOutputDisplay(outputArea, "No markdown content to render.", OutputType.Warning);
                return;
            }

            // Simple markdown rendering (you could integrate a proper markdown library here)
            var rendered = new StringBuilder();
            rendered.AppendLine("📝 Rendered Markdown:");
            rendered.AppendLine(new string('─', 30));

            var lines = markdownText.Split('\n');
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("# "))
                {
                    rendered.AppendLine($"🔵 HEADING 1: {trimmedLine.Substring(2)}");
                }
                else if (trimmedLine.StartsWith("## "))
                {
                    rendered.AppendLine($"🔷 HEADING 2: {trimmedLine.Substring(3)}");
                }
                else if (trimmedLine.StartsWith("### "))
                {
                    rendered.AppendLine($"🔹 HEADING 3: {trimmedLine.Substring(4)}");
                }
                else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
                {
                    rendered.AppendLine($"• {trimmedLine.Substring(2)}");
                }
                else if (trimmedLine.Contains("**") && trimmedLine.Count(c => c == '*') >= 2)
                {
                    var boldText = trimmedLine.Replace("**", "").ToUpper();
                    rendered.AppendLine($"[BOLD] {boldText}");
                }
                else if (!string.IsNullOrWhiteSpace(trimmedLine))
                {
                    rendered.AppendLine(trimmedLine);
                }
                else
                {
                    rendered.AppendLine();
                }
            }

            SetOutputDisplay(outputArea, rendered.ToString(), OutputType.Success);
        }


        /// <summary>
        /// Executes Python code on the server and displays results in the output area
        /// </summary>
        /// <param name="code">Python code to execute</param>
        /// <param name="outputArea">TextBox where results will be displayed</param>
        /// <returns>Task representing the asynchronous operation</returns>
        private async Task RunPythonCode(string code, TextBox outputArea)
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(code))
            {
                SetOutputDisplay(outputArea, "No code to execute.", OutputType.Warning);
                return;
            }

            if (outputArea == null)
            {
                throw new ArgumentNullException(nameof(outputArea));
            }

            // Show initial loading state
            SetOutputDisplay(outputArea, "Executing Python code...", OutputType.Info);

            try
            {
                // Check if client is initialized
                if (_notebookClient == null)
                {
                    SetOutputDisplay(outputArea,
                        "Error: Notebook client not initialized.\nPlease restart the application.",
                        OutputType.Error);
                    return;
                }

                // Execute the code on the server
                var executionResult = await _notebookClient.ExecuteCodeAsync(code, "code");

                // Handle the response
                await ProcessExecutionResult(executionResult, outputArea, code);
            }
            catch (TaskCanceledException)
            {
                SetOutputDisplay(outputArea,
                    "Execution timed out.\nThe Python code took too long to execute (>60 seconds).\n" +
                    "Try breaking your code into smaller chunks or optimizing performance.",
                    OutputType.Error);
            }
            catch (HttpRequestException httpEx)
            {
                var errorMessage = $"Network Error: {httpEx.Message}\n\n";

                if (httpEx.Message.Contains("SSL") || httpEx.Message.Contains("certificate"))
                {
                    errorMessage += "SSL Certificate Issue:\n" +
                                   "• Try changing server URL from https:// to http://\n" +
                                   "• Ensure Python server is running with correct SSL configuration\n" +
                                   "• Check if server is using self-signed certificates";
                }
                else if (httpEx.Message.Contains("refused") || httpEx.Message.Contains("timeout"))
                {
                    errorMessage += "Connection Issue:\n" +
                                   "• Make sure Python server is running on port 5001\n" +
                                   "• Check if port 5001 is blocked by firewall\n" +
                                   "• Verify server address: http://127.0.0.1:5001";
                }
                else
                {
                    errorMessage += "Possible causes:\n" +
                                   "• Python server is not running\n" +
                                   "• Network connectivity issues\n" +
                                   "• Server configuration problems";
                }

                SetOutputDisplay(outputArea, errorMessage, OutputType.Error);
            }
            catch (JsonException jsonEx)
            {
                SetOutputDisplay(outputArea,
                    $"Data Format Error: {jsonEx.Message}\n\n" +
                    "The server response could not be parsed.\n" +
                    "This might indicate a server-side error or incompatible versions.",
                    OutputType.Error);
            }
            catch (Exception ex)
            {
                SetOutputDisplay(outputArea,
                    $"Unexpected Error: {ex.Message}\n\n" +
                    "An unexpected error occurred while executing the code.\n" +
                    $"Error Type: {ex.GetType().Name}\n" +
                    "Please check the application logs for more details.",
                    OutputType.Error);

                // Log the full exception for debugging
                System.Diagnostics.Debug.WriteLine($"RunPythonCode Exception: {ex}");
            }
        }

        /// <summary>
        /// Processes the execution result from the Python server
        /// </summary>
        /// <param name="result">Execution result from server</param>
        /// <param name="outputArea">Output display area</param>
        /// <param name="originalCode">Original code that was executed</param>
        private async Task ProcessExecutionResult(ExecutionResult result, TextBox outputArea, string originalCode)
        {
            if (result == null)
            {
                SetOutputDisplay(outputArea,
                    "Error: No response received from server.\n" +
                    "The server may be experiencing issues.",
                    OutputType.Error);
                return;
            }

            // Build the output message
            var outputBuilder = new StringBuilder();
            var outputType = result.Success ? OutputType.Success : OutputType.Error;

            // Add execution timestamp
            outputBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] Execution completed");
            outputBuilder.AppendLine(new string('─', 50));

            // Add standard output (print statements, etc.)
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                outputBuilder.AppendLine("📤 Output:");
                outputBuilder.AppendLine(result.Output.TrimEnd());
                outputBuilder.AppendLine();
            }

            // Add expression result (return value of last expression)
            if (!string.IsNullOrWhiteSpace(result.Result))
            {
                outputBuilder.AppendLine("📊 Result:");
                outputBuilder.AppendLine(result.Result.TrimEnd());
                outputBuilder.AppendLine();
            }

            // Add error information
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                outputBuilder.AppendLine("❌ Error:");
                outputBuilder.AppendLine(FormatPythonError(result.Error));
                outputBuilder.AppendLine();
            }

            // Handle matplotlib plots
            if (result.HasPlot)
            {
                outputBuilder.AppendLine("📈 Plot Generated:");
                if (!string.IsNullOrWhiteSpace(result.PlotPath))
                {
                    outputBuilder.AppendLine($"Saved to: {result.PlotPath}");

                    // Try to show plot if possible
                    if (File.Exists(result.PlotPath))
                    {
                        outputBuilder.AppendLine("✅ Plot file created successfully");

                        // Optional: Open plot in default image viewer
                        // You could add a button or automatic opening here
                        await Task.Run(() =>
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = result.PlotPath,
                                    UseShellExecute = true
                                });
                            }
                            catch
                            {
                                // Ignore errors when trying to open the plot
                            }
                        });
                    }
                    else
                    {
                        outputBuilder.AppendLine("⚠️ Plot file not found at specified path");
                    }
                }
                outputBuilder.AppendLine();
            }

            // Add execution statistics
            if (result.Success)
            {
                var codeLines = originalCode.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line.Trim())).Count();
                outputBuilder.AppendLine($"✅ Execution successful ({codeLines} lines processed)");
            }
            else
            {
                outputBuilder.AppendLine("❌ Execution failed");
            }

            // Display the formatted output
            SetOutputDisplay(outputArea, outputBuilder.ToString(), outputType);
        }

        /// <summary>
        /// Formats Python error messages for better readability
        /// </summary>
        /// <param name="error">Raw error message from Python</param>
        /// <returns>Formatted error message</returns>
        private string FormatPythonError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return "";

            var lines = error.Split('\n');
            var formattedError = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Highlight common error types
                if (trimmedLine.Contains("SyntaxError"))
                {
                    formattedError.AppendLine($"🔴 {trimmedLine}");
                }
                else if (trimmedLine.Contains("NameError"))
                {
                    formattedError.AppendLine($"🟡 {trimmedLine}");
                }
                else if (trimmedLine.Contains("TypeError") || trimmedLine.Contains("ValueError"))
                {
                    formattedError.AppendLine($"🟠 {trimmedLine}");
                }
                else if (trimmedLine.Contains("ImportError") || trimmedLine.Contains("ModuleNotFoundError"))
                {
                    formattedError.AppendLine($"🟣 {trimmedLine}");
                }
                else if (trimmedLine.StartsWith("  File "))
                {
                    formattedError.AppendLine($"📁 {trimmedLine}");
                }
                else
                {
                    formattedError.AppendLine($"   {trimmedLine}");
                }
            }

            return formattedError.ToString();
        }

        /// <summary>
        /// Helper method to validate server connection before executing code
        /// </summary>
        /// <returns>True if server is accessible, false otherwise</returns>
        private async Task<bool> ValidateServerConnection()
        {
            try
            {
                if (_notebookClient == null)
                    return false;

                return await _notebookClient.IsServerRunningAsync();
            }
            catch
            {
                return false;
            }
        }

        private async Task CheckServerConnection()
        {
            var isRunning = await _notebookClient.IsServerRunningAsync();
            if (!isRunning)
            {
                MessageBox.Show(
                    "Python server is not running or not accessible.\n\n" +
                    "Please make sure the Python server is started:\n" +
                    "python epyflaServer.py\n\n" +
                    "Server should be running on https://127.0.0.1:5001",
                    "Server Connection Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void ClearNamespace_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will clear all user-defined variables from the Python session.\nBuilt-in modules (numpy, pandas, etc.) will remain available.\n\nContinue?",
                "Clear Variables",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var success = await _notebookClient.ClearNamespaceAsync();
                if (success)
                {
                    MessageBox.Show("Variables cleared successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to clear variables. Check server connection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ViewNamespace_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show loading indicator
                var loadingWindow = new Window
                {
                    Title = "Loading Variables...",
                    Width = 300,
                    Height = 100,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Fetching namespace information...",
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    }
                };

                loadingWindow.Show();

                try
                {
                    // Get namespace information from server
                    var namespaceInfo = await _notebookClient.GetNamespaceAsync();

                    // Close loading window
                    loadingWindow.Close();

                    // Check for errors
                    if (!string.IsNullOrEmpty(namespaceInfo.Error))
                    {
                        MessageBox.Show(
                            $"Error retrieving namespace information:\n\n{namespaceInfo.Error}",
                            "Namespace Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    // Check if there are any variables to display
                    if (namespaceInfo.Variables == null || namespaceInfo.Variables.Count == 0)
                    {
                        var result = MessageBox.Show(
                            "No user-defined variables found in the Python session.\n\n" +
                            "Available built-in modules:\n" +
                            string.Join(", ", namespaceInfo.AvailableModules ?? new List<string>()) + "\n\n" +
                            "Would you like to see detailed module information?",
                            "No Variables Found",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            // ShowModuleInformation(namespaceInfo.AvailableModules ?? new List<string>());
                        }
                        return;
                    }

                    // Create and show the namespace window
                    var namespaceWindow = new NamespaceWindow(namespaceInfo)
                    {
                        Owner = this
                    };
                    namespaceWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    // Ensure loading window is closed
                    loadingWindow?.Close();
                    throw; // Re-throw to be caught by outer catch
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to retrieve namespace information:\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    "Please ensure the Python server is running and accessible.",
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Enhanced method to run all cells in the notebook
        /// </summary>
        private async void RunAllCells_Click(object sender, RoutedEventArgs e)
        {
            var totalCells = 0;
            var successfulCells = 0;
            var failedCells = 0;

            try
            {
                // Disable the run all button during execution
                if (sender is Button runAllButton)
                {
                    runAllButton.IsEnabled = false;
                    runAllButton.Content = "⏳ Running...";
                }

                foreach (var child in _cellContainer.Children.Cast<Border>().ToList())
                {
                    if (child.Child is StackPanel panel)
                    {
                        var editor = panel.Children.OfType<TextEditor>().FirstOrDefault();
                        var outputArea = panel.Children.OfType<TextBox>().FirstOrDefault();
                        var label = panel.Children.OfType<Label>().FirstOrDefault();

                        if (editor != null && outputArea != null && label != null)
                        {
                            totalCells++;
                            var cellType = label.Content.ToString() == "Code" ? CellType.Code : CellType.Markdown;

                            // Show which cell is currently executing
                            var originalLabelContent = label.Content;
                            label.Content = $"{originalLabelContent} ⏳";

                            try
                            {
                                await RunCell(editor, outputArea, cellType);

                                // Check if execution was successful by looking at output color
                                if (outputArea.Background == Brushes.Black || outputArea.Background == Brushes.DarkBlue)
                                {
                                    successfulCells++;
                                    label.Content = $"{originalLabelContent} ✅";
                                }
                                else
                                {
                                    failedCells++;
                                    label.Content = $"{originalLabelContent} ❌";
                                }

                                // Small delay between cells to prevent overwhelming the server
                                await Task.Delay(200);
                            }
                            catch
                            {
                                failedCells++;
                                label.Content = $"{originalLabelContent} ❌";
                            }
                            finally
                            {
                                // Reset label after a few seconds
                                _ = Task.Delay(3000).ContinueWith(_ =>
                                {
                                    Dispatcher.Invoke(() => label.Content = originalLabelContent);
                                });
                            }
                        }
                    }
                }

                // Show execution summary
                var summaryMessage = $"Batch Execution Complete\n" +
                                   $"Total cells: {totalCells}\n" +
                                   $"Successful: {successfulCells}\n" +
                                   $"Failed: {failedCells}";

                MessageBox.Show(summaryMessage, "Execution Summary", MessageBoxButton.OK,
                               failedCells == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during batch execution: {ex.Message}", "Batch Execution Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable the run all button
                if (sender is Button runAllButton)
                {
                    runAllButton.IsEnabled = true;
                    runAllButton.Content = "▶️ Run All";
                }
            }
        }

        private void ClearAllOutput_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in _cellContainer.Children.Cast<Border>())
            {
                if (child.Child is StackPanel panel)
                {
                    var outputArea = panel.Children.OfType<TextBox>().FirstOrDefault();
                    if (outputArea != null)
                    {
                        outputArea.Visibility = Visibility.Collapsed;
                        outputArea.Text = "Output will appear here...";
                        outputArea.Background = Brushes.Black;
                        outputArea.Foreground = Brushes.LightGreen;
                    }
                }
            }
        }

        private void SaveNotebook_Click(object sender, RoutedEventArgs e)
        {
            var cells = new List<NotebookCell>();

            foreach (var child in _cellContainer.Children.Cast<Border>())
            {
                if (child.Child is StackPanel panel)
                {
                    var editor = panel.Children.OfType<TextEditor>().FirstOrDefault();
                    var outputArea = panel.Children.OfType<TextBox>().FirstOrDefault();
                    var label = panel.Children.OfType<Label>().FirstOrDefault();

                    if (editor != null && label != null)
                    {
                        cells.Add(new NotebookCell
                        {
                            CellType = label.Content.ToString().ToLower(),
                            Source = editor.Text,
                            Output = outputArea?.Text ?? ""
                        });
                    }
                }
            }

            var notebook = new NotebookDocument
            {
                Cells = cells,
                Metadata = new Dictionary<string, object>
                {
                    ["created"] = DateTime.Now.ToString("O"),
                    ["version"] = "1.0"
                }
            };

            string json = JsonConvert.SerializeObject(notebook, Formatting.Indented);
            File.WriteAllText(_savePath, json);
            MessageBox.Show($"Notebook saved to {_savePath}", "Save Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadNotebook_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_savePath))
            {
                try
                {
                    string json = File.ReadAllText(_savePath);
                    var notebook = JsonConvert.DeserializeObject<NotebookDocument>(json);

                    _cellContainer.Children.Clear();

                    foreach (var cell in notebook.Cells)
                    {
                        if (cell.CellType == "code")
                        {
                            AddCodeCellWithContent(cell.Source, cell.Output);
                        }
                        else if (cell.CellType == "markdown")
                        {
                            AddMarkdownCellWithContent(cell.Source, cell.Output);
                        }
                    }

                    MessageBox.Show($"Notebook loaded from {_savePath}", "Load Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading notebook: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"No saved notebook found at {_savePath}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AddCodeCellWithContent(string content, string output = "")
        {
            var cellPanel = CreateCellPanel();
            var cellContent = new StackPanel();

            var cellTypeLabel = new Label
            {
                Content = "Code",
                Background = Brushes.LightBlue,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(5, 2, 5, 2)
            };

            var textEditor = new TextEditor
            {
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python"),
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 120,
                Text = content,
                FontSize = 12,
                ShowLineNumbers = true
            };

            var outputArea = new TextBox
            {
                IsReadOnly = true,
                Background = Brushes.Black,
                Foreground = Brushes.LightGreen,
                FontFamily = new FontFamily("Consolas"),
                MinHeight = 60,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = output,
                Visibility = string.IsNullOrEmpty(output) ? Visibility.Collapsed : Visibility.Visible
            };

            var buttonPanel = CreateCellButtonPanel(textEditor, outputArea, cellPanel, CellType.Code);

            cellContent.Children.Add(cellTypeLabel);
            cellContent.Children.Add(textEditor);
            cellContent.Children.Add(outputArea);
            cellContent.Children.Add(buttonPanel);

            cellPanel.Child = cellContent;
            _cellContainer.Children.Add(cellPanel);
        }

        private void AddMarkdownCellWithContent(string content, string output = "")
        {
            var cellPanel = CreateCellPanel();
            var cellContent = new StackPanel();

            var cellTypeLabel = new Label
            {
                Content = "Markdown",
                Background = Brushes.LightCoral,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(5, 2, 5, 2)
            };

            var textEditor = new TextEditor
            {
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("MarkDown"),
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 120,
                Text = content,
                FontSize = 12,
                ShowLineNumbers = false
            };

            var outputArea = new TextBox
            {
                IsReadOnly = true,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                MinHeight = 60,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = output,
                Visibility = string.IsNullOrEmpty(output) ? Visibility.Collapsed : Visibility.Visible
            };

            var buttonPanel = CreateCellButtonPanel(textEditor, outputArea, cellPanel, CellType.Markdown);

            cellContent.Children.Add(cellTypeLabel);
            cellContent.Children.Add(textEditor);
            cellContent.Children.Add(outputArea);
            cellContent.Children.Add(buttonPanel);

            cellPanel.Child = cellContent;
            _cellContainer.Children.Add(cellPanel);
        }

        protected override void OnClosed(EventArgs e)
        {
            _notebookClient?.Dispose();
            base.OnClosed(e);
        }
    }



    /// <summary>
    /// Window for displaying namespace variables with enhanced formatting
    /// </summary>
    public class NamespaceWindow : Window
    {
        private ListView _variablesList;
        private TextBox _searchBox;
        private List<VariableDisplayItem> _allVariables;

        public NamespaceWindow(NamespaceInfo namespaceInfo)
        {
            InitializeWindow();
            LoadVariables(namespaceInfo);
        }

        private void InitializeWindow()
        {
            Title = "Python Session Variables";
            Width = 800;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MinWidth = 600;
            MinHeight = 400;

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Variables
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Info panel
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Search panel
            var searchPanel = CreateSearchPanel();
            Grid.SetRow(searchPanel, 0);
            mainGrid.Children.Add(searchPanel);

            // Variables list
            _variablesList = CreateVariablesList();
            Grid.SetRow(_variablesList, 1);
            mainGrid.Children.Add(_variablesList);

            // Info panel
            var infoPanel = CreateInfoPanel();
            Grid.SetRow(infoPanel, 2);
            mainGrid.Children.Add(infoPanel);

            // Button panel
            var buttonPanel = CreateButtonPanel();
            Grid.SetRow(buttonPanel, 3);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private StackPanel CreateSearchPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 10, 10, 5),
                Background = Brushes.LightBlue
            };

            panel.Children.Add(new Label
            {
                Content = "🔍 Search Variables:",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            });

            _searchBox = new TextBox
            {
                Width = 200,
                Height = 25,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };
            _searchBox.TextChanged += SearchBox_TextChanged;

            panel.Children.Add(_searchBox);

            return panel;
        }

        private ListView CreateVariablesList()
        {
            var listView = new ListView
            {
                Margin = new Thickness(10, 5, 10, 5)
            };

            // Create grid view with proper column sizing
            var gridView = new GridView();

            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Variable Name",
                DisplayMemberBinding = new System.Windows.Data.Binding("Name"),
                Width = 180
            });

            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Type",
                DisplayMemberBinding = new System.Windows.Data.Binding("Type"),
                Width = 120
            });

            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Value Preview",
                DisplayMemberBinding = new System.Windows.Data.Binding("Value"),
                Width = 400
            });

            listView.View = gridView;

            // Add context menu
            var contextMenu = new ContextMenu();

            var copyNameItem = new MenuItem { Header = "Copy Variable Name" };
            copyNameItem.Click += (s, e) => CopySelectedVariableName();

            var copyValueItem = new MenuItem { Header = "Copy Variable Value" };
            copyValueItem.Click += (s, e) => CopySelectedVariableValue();

            var generateCodeItem = new MenuItem { Header = "Generate Code to Access" };
            generateCodeItem.Click += (s, e) => GenerateAccessCode();

            contextMenu.Items.Add(copyNameItem);
            contextMenu.Items.Add(copyValueItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(generateCodeItem);

            listView.ContextMenu = contextMenu;

            return listView;
        }

        private Border CreateInfoPanel()
        {
            var border = new Border
            {
                Background = Brushes.LightYellow,
                BorderBrush = Brushes.Orange,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10, 5, 10, 5),
                Padding = new Thickness(10)
            };

            var stackPanel = new StackPanel();

            stackPanel.Children.Add(new TextBlock
            {
                Text = "ℹ️ Variable Information",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "• These variables persist across code cell executions",
                Margin = new Thickness(0, 2, 0, 2)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "• Right-click on variables for more options",
                Margin = new Thickness(0, 2, 0, 2)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "• Use 'Clear Variables' in the main toolbar to reset the namespace",
                Margin = new Thickness(0, 2, 0, 2)
            });

            border.Child = stackPanel;
            return border;
        }

        private StackPanel CreateButtonPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var refreshButton = new Button
            {
                Content = "🔄 Refresh",
                Width = 100,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0)
            };
            refreshButton.Click += RefreshButton_Click;

            var closeButton = new Button
            {
                Content = "Close",
                Width = 100,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0),
                IsDefault = true
            };
            closeButton.Click += (s, e) => Close();

            panel.Children.Add(refreshButton);
            panel.Children.Add(closeButton);

            return panel;
        }

        private void LoadVariables(NamespaceInfo namespaceInfo)
        {
            _allVariables = new List<VariableDisplayItem>();

            if (namespaceInfo.Variables != null)
            {
                foreach (var variable in namespaceInfo.Variables.OrderBy(v => v.Key))
                {
                    _allVariables.Add(new VariableDisplayItem
                    {
                        Name = variable.Key,
                        Type = variable.Value.Type,
                        Value = TruncateValue(variable.Value.Value, 80)
                    });
                }
            }

            UpdateVariablesList();

            // Update title with count
            Title = $"Python Session Variables ({_allVariables.Count} variables)";
        }

        private string TruncateValue(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return "<empty>";

            if (value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength - 3) + "...";
        }

        private void UpdateVariablesList()
        {
            _variablesList.Items.Clear();

            var searchTerm = _searchBox?.Text?.ToLower() ?? "";
            var filteredVariables = string.IsNullOrEmpty(searchTerm)
                ? _allVariables
                : _allVariables.Where(v =>
                    v.Name.ToLower().Contains(searchTerm) ||
                    v.Type.ToLower().Contains(searchTerm) ||
                    v.Value.ToLower().Contains(searchTerm)).ToList();

            foreach (var variable in filteredVariables)
            {
                _variablesList.Items.Add(variable);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateVariablesList();
        }

        private void CopySelectedVariableName()
        {
            if (_variablesList.SelectedItem is VariableDisplayItem variable)
            {
                Clipboard.SetText(variable.Name);
                MessageBox.Show($"Variable name '{variable.Name}' copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopySelectedVariableValue()
        {
            if (_variablesList.SelectedItem is VariableDisplayItem variable)
            {
                Clipboard.SetText(variable.Value);
                MessageBox.Show($"Variable value copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void GenerateAccessCode()
        {
            if (_variablesList.SelectedItem is VariableDisplayItem variable)
            {
                var code = $"# Access variable '{variable.Name}'\nprint(f\"{{'{variable.Name}': {{{variable.Name}}}}}\")\nprint(f\"Type: {{type({variable.Name})}}\")\n{variable.Name}";
                Clipboard.SetText(code);
                MessageBox.Show($"Code to access '{variable.Name}' copied to clipboard.\nPaste it into a code cell to inspect the variable.", "Code Generated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the parent window's notebook client
                if (Owner is NotebookWindow parentWindow)
                {
                    var client = parentWindow._notebookClient; // This would need to be made internal/public
                    var namespaceInfo = await client.GetNamespaceAsync();

                    if (string.IsNullOrEmpty(namespaceInfo.Error))
                    {
                        LoadVariables(namespaceInfo);
                    }
                    else
                    {
                        MessageBox.Show($"Error refreshing variables: {namespaceInfo.Error}", "Refresh Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing variables: {ex.Message}", "Refresh Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

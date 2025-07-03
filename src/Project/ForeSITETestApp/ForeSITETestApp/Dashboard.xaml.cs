// -----------------------------------------------------------------------------
//  Author:      Tao He
//  Email:       tao.he@utah.edu
//  Created:     2025-07-01
//  Description: Dashboard user control logic for ForeSITETestApp (WPF).
// -----------------------------------------------------------------------------


using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

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
        public Dashboard(MainWindow window)
        {
            InitializeComponent();
            this.window = window;
            _httpClient = new HttpClient();
            _reportElements = new List<ReportElement>(); // Initialize report elements list
            InitializeDataSources();
            // Initialize default FlowDocument
            _titleDocument = new FlowDocument(new Paragraph(new Run("Click to edit title")));
            DrawingCanvas.Height = 300; // Minimum height for placeholder         
        }

        private void InitializeDataSources()
        {
            _dataSources = new ObservableCollection<DataSource>
            {
                new DataSource { Name = "Covid-19 Tests", DataUrl = "https://data.cdc.gov", ResourceUrl="vbim-akqf", isRealtime=false, IsSelected = false },
                new DataSource { Name = "Covid-19 Deaths", DataUrl = "https://data.cdc.gov", ResourceUrl="r8kw-7aab", isRealtime=true, IsSelected = false },
                new DataSource { Name = "Pneumonia Deaths", DataUrl = "https://data.cdc.gov", ResourceUrl="r8kw-7aab", isRealtime=true, IsSelected = false },
                new DataSource { Name = "Flu Deaths", DataUrl = "https://data.cdc.gov", ResourceUrl="r8kw-7aab", isRealtime=true, IsSelected = false },

            };
            DataSourceTable.ItemsSource = _dataSources;
            DataSourceSelector.ItemsSource = _dataSources; // Bind to DataSourceSelector
        }

        private void SchedulerButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Schedule Builder";
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            SchedulerGrid.Visibility = Visibility.Visible;
            ReportsGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Collapsed;
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Home";
            DefaultContentGrid.Visibility = Visibility.Visible;
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Collapsed;

        }

        private void ReportButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Report Builder";
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Visible;
            DataSourceGrid.Visibility = Visibility.Collapsed;
        }

        private void DataSourceButton_Click(object sender, RoutedEventArgs e)
        {
            HeaderTitle.Text = "Data Source Builder";
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            SchedulerGrid.Visibility = Visibility.Collapsed;
            ReportsGrid.Visibility = Visibility.Collapsed;
            DataSourceGrid.Visibility = Visibility.Visible;
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
       

        private void AIButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ChatDialog();
            dialog.ShowDialog();

        }

        private void AddDataSourceButton_Click(object sender, RoutedEventArgs e)
        {
            var newTab = new TabItem
            {
                Header = $"New Data Source {DataSourceTabs.Items.Count}",
                Content = new StackPanel
                {
                    Margin = new Thickness(10),
                    Children =
                   {
                       new TextBlock { Text = "Data Source Name", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,5) },
                       new TextBox { Name = "DataSourceNameInput", Width = 300, Margin = new Thickness(0,0,0,10) },
                       new TextBlock { Text = "App Token", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,5) },
                       new TextBox { Name = "AppTokenInput", Width = 300, Margin = new Thickness(0,0,0,10) },
                       new TextBlock { Text = "App URL", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,5) },
                       new TextBox { Name = "AppUrlInput", Width = 300, Margin = new Thickness(0,0,0,10) },
                       new Button
                       {
                           Content = "Save",
                           Style = FindResource("HeaderButtonStyle") as Style,
                           Margin = new Thickness(0,10,0,0),
                           HorizontalAlignment = HorizontalAlignment.Left
                       }
                   }
                }
            };

            var saveButton = (newTab.Content as StackPanel)?.Children.OfType<Button>().FirstOrDefault();
            if (saveButton != null)
            {
                saveButton.Click += (s, args) =>
                {
                    var stackPanel = (newTab.Content as StackPanel);
                    var nameInput = stackPanel.Children.OfType<TextBox>().FirstOrDefault(x => x.Name == "DataSourceNameInput");
                    var tokenInput = stackPanel.Children.OfType<TextBox>().FirstOrDefault(x => x.Name == "AppTokenInput");
                    var urlInput = stackPanel.Children.OfType<TextBox>().FirstOrDefault(x => x.Name == "AppUrlInput");

                    if (string.IsNullOrWhiteSpace(nameInput.Text) || string.IsNullOrWhiteSpace(tokenInput.Text) || string.IsNullOrWhiteSpace(urlInput.Text))
                    {
                        MessageBox.Show("All fields are required.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    _dataSources.Add(new DataSource
                    {
                        Name = nameInput.Text,
                        AppToken = tokenInput.Text,
                        DataUrl = urlInput.Text,
                        IsSelected = false
                    });

                    DataSourceTabs.Items.Remove(newTab);
                    DataSourceTabs.SelectedIndex = 0;
                    MessageBox.Show("Data source added successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                };
            }

            DataSourceTabs.Items.Add(newTab);
            DataSourceTabs.SelectedItem = newTab;
        }

        private void DeleteDataSourceButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void SchedulingButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("TODO: Scheduling function soon ^_^", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
                VerticalAlignment = VerticalAlignment.Center
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
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
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
                        VerticalAlignment = VerticalAlignment.Center,
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
                                VerticalAlignment = VerticalAlignment.Center,
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
        }

        private XFont GetSafeFont(string preferredFont, double size, XFontStyleEx style = XFontStyleEx.Regular)
        {
            string[] fallbackFonts = { preferredFont, "Helvetica", "Times New Roman" };
            foreach (var fontName in fallbackFonts)
            {
                try
                {
                    return new XFont(fontName, size, style);
                }
                catch
                {
                    // Continue to try the next font
                }
            }
            // Ultimate fallback: use a generic font with default style
            return new XFont("Helvetica", size, XFontStyleEx.Regular); // PdfSharpCore may handle this gracefully
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure title is in TextBlock state (not RichTextBox)  
                var titleElement = _reportElements.FirstOrDefault(re => re.Type == ReportElementType.Title);
                if (titleElement != null && titleElement.Element?.Child is Grid contentGrid) // Added null check for Element  
                {
                    var richTextBox = contentGrid.Children.OfType<RichTextBox>().FirstOrDefault();
                    if (richTextBox != null)
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
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 16
                        };

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

                        if (_titleDocument.Blocks.FirstBlock is Paragraph newParagraph)
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

                        contentGrid.Children.Remove(richTextBox);
                        Grid.SetColumn(newTextBlock, 0);
                        Grid.SetRow(newTextBlock, 0);
                        contentGrid.Children.Add(newTextBlock);

                        // Close editor window if open  
                        if (_editorWindow != null)
                        {
                            _editorWindow.Close();
                            _editorWindow = null;
                        }
                    }
                }

                // Prompt user to save PDF  
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "PDF Files (*.pdf)|*.pdf",
                    DefaultExt = "pdf",
                    FileName = "Report.pdf"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    // Create a new PDF document  
                    PdfDocument pdfDocument = new PdfDocument();
                    PdfPage pdfPage = pdfDocument.AddPage();
                    XGraphics gfx = XGraphics.FromPdfPage(pdfPage);

                    // Set page size to A4 and calculate scaling to fit canvas  
                    double a4Width = 595; // A4 width in points (72 DPI)  
                    double a4Height = 842; // A4 height in points  
                    double margin = 20;
                    double canvasWidth = DrawingCanvas.ActualWidth;
                    double canvasHeight = DrawingCanvas.ActualHeight;

                    // Calculate scaling to fit canvas within A4 with margins  
                    double scaleX = (a4Width - 2 * margin) / canvasWidth;
                    double scaleY = (a4Height - 2 * margin) / canvasHeight;
                    double scale = Math.Min(scaleX, scaleY);
                    double offsetX = (a4Width - canvasWidth * scale) / 2;
                    double offsetY = (a4Height - canvasHeight * scale) / 2;

                    // Apply scaling transform  
                    gfx.TranslateTransform(offsetX, offsetY);
                    gfx.ScaleTransform(scale, scale);

                    // Draw canvas background  
                    var bgColor = (DrawingCanvas.Background as SolidColorBrush)?.Color ?? Colors.White;
                    gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(bgColor.A, bgColor.R, bgColor.G, bgColor.B)),
                        0, 0, canvasWidth, canvasHeight);

                    // Draw placeholder TextBlock
                    foreach (var child in DrawingCanvas.Children)
                    {
                        if (child is TextBlock placeholderTextBlock && placeholderTextBlock.Text == "Drawing Area Placeholder")
                        {
                            double left = Canvas.GetLeft(placeholderTextBlock);
                            double top = Canvas.GetTop(placeholderTextBlock);
                            XFont placeholderFont = GetSafeFont("Arial", 14);
                            gfx.DrawString(placeholderTextBlock.Text,
                                placeholderFont,
                                new XSolidBrush(XColor.FromArgb(Colors.Gray.A, Colors.Gray.R, Colors.Gray.G, Colors.Gray.B)),
                                left, top + 14);
                        }
                    }

                    // Draw report elements
                    foreach (var element in _reportElements)
                    {
                        if (element.Element?.Child is Grid plotContentGrid)
                        {
                            double borderLeft = Canvas.GetLeft(element.Element);
                            double borderTop = Canvas.GetTop(element.Element);
                            double borderWidth = element.Element.Width;
                            double borderHeight = element.Element.Height;

                            // Draw border background
                            var borderBgColor = (element.Element.Background as SolidColorBrush)?.Color ?? Colors.White;
                            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(borderBgColor.A, borderBgColor.R, borderBgColor.G, borderBgColor.B)),
                                borderLeft, borderTop, borderWidth, borderHeight);

                            if (element.Type == ReportElementType.Title)
                            {
                                // Draw title text
                                var titleTextBlock = plotContentGrid.Children.OfType<TextBlock>().FirstOrDefault();
                                if (titleTextBlock != null)
                                {
                                    string text = new TextRange(titleTextBlock.ContentStart, titleTextBlock.ContentEnd).Text.Trim();
                                    double textLeft = borderLeft + 5;
                                    double textTop = borderTop + borderHeight / 2;
                                    //XFont font = new XFont("Arial", 16);

                                    if (titleTextBlock.Inlines.Any())
                                    {
                                        double currentX = textLeft;
                                        foreach (var inline in titleTextBlock.Inlines)
                                        {
                                            if (inline is Run run)
                                            {
                                                bool isBold = run.FontWeight == FontWeights.Bold;
                                                bool isItalic = run.FontStyle == FontStyles.Italic;
                                                string fontName = run.FontFamily?.ToString() ?? "Arial";
                                                double fontSize = run.FontSize > 0 ? run.FontSize : 16;

                                                XFont runFont = new XFont(fontName, fontSize,
                                                    isBold ? XFontStyleEx.Bold : isItalic ? XFontStyleEx.Italic : XFontStyleEx.Regular);

                                                gfx.DrawString(run.Text,
                                                    runFont,
                                                    new XSolidBrush(XColor.FromArgb(Colors.Black.A, Colors.Black.R, Colors.Black.G, Colors.Black.B)),
                                                    currentX, textTop);
                                                currentX += gfx.MeasureString(run.Text, runFont).Width;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        XFont defaultFont = GetSafeFont("Arial", 16);
                                        gfx.DrawString(text,
                                            defaultFont,
                                            new XSolidBrush(XColor.FromArgb(Colors.Gray.A, Colors.Gray.R, Colors.Gray.G, Colors.Gray.B)),
                                            textLeft, textTop);
                                    }
                                }

                               
                            }
                            else // Plot
                            {
                                // Draw image
                                var image = plotContentGrid.Children.OfType<Image>().FirstOrDefault();
                                if (image != null && image.Source is BitmapImage bitmapImage)
                                {
                                    double left = borderLeft + (borderWidth - 700) / 2;
                                    double top = borderTop + (borderHeight - 400) / 2;
                                    double width = 700;
                                    double height = 400;

                                    try
                                    {
                                        // Save BitmapImage to a temporary file
                                        string tempFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"plot_{Guid.NewGuid()}.png");
                                        using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                                        {
                                            BitmapEncoder encoder = new PngBitmapEncoder();
                                            encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
                                            encoder.Save(fileStream);
                                        }

                                        // Load image into XImage
                                        XImage xImage = XImage.FromFile(tempFilePath);
                                        gfx.DrawImage(xImage, left, top, width, height);

                                        // Clean up temporary file
                                        File.Delete(tempFilePath);
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show($"Error rendering image to PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    }
                                }

                                
                            }
                        }
                    }

                    // Save the PDF  
                    string pdfFile = saveFileDialog.FileName;
                    pdfDocument.Save(pdfFile);

                    MessageBox.Show($"PDF saved successfully to {pdfFile}!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    ["Model"] = model,
                    ["DataSource"] = dataSource,
                    ["YearBack"] = yearBack,
                    ["UseTrainSplit"] = useTrainSplit,
                    ["BeginDate"] = beginDate,
                    ["Freq"] = freq,
                    ["Threshold"] = threshold,
                    ["Title"] = plotTitle
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
                    graphData["TrainSplitRatio"] = trainSplitRatio;
                }
                else
                {
                    string trainEndDate = TrainEndDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
                    
                    if (string.IsNullOrEmpty(trainEndDate) )
                    {
                        MessageBox.Show("Please select both Train End Date.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    graphData["TrainEndDate"] = trainEndDate;
                   
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
                            var file =  filePath;
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
                            Image plotImage = new Image
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
                                VerticalContentAlignment = VerticalAlignment.Center,
                                HorizontalContentAlignment = HorizontalAlignment.Center,
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

                            // Add to report elements
                            _reportElements.Add(new ReportElement
                            {
                                Type = ReportElementType.Plot,
                                Element = imageBorder
                            });

                            // Redraw canvas
                            RedrawCanvas();

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
            double canvasHeight = _reportElements.Any(e => e.Type == ReportElementType.Title) ? 60 : 0; // Title height
            int plotCount = _reportElements.Count(e => e.Type == ReportElementType.Plot);
            canvasHeight += plotCount * 410; // Each plot: 400px image + 10px padding
            canvasHeight += plotCount > 0 ? (plotCount - 1) * 30 : 0; // Gaps between plots
            canvasHeight += 30; // Bottom margin
            DrawingCanvas.Height = Math.Max(canvasHeight, 300); // Minimum height for placeholder
        }

        private void RedrawCanvas()
        {
            // Preserve placeholder
            var placeholder = DrawingCanvas.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == "Drawing Area Placeholder");
            DrawingCanvas.Children.Clear();
            if (placeholder != null)
            {
                DrawingCanvas.Children.Add(placeholder);
            }

            double topPosition = 0;
            double canvasWidth = Math.Max(DrawingCanvas.ActualWidth, 778); // Ensure canvas is wide enough

            foreach (var element in _reportElements)
            {
                if (element.Element != null) // Ensure the element is not null
                {
                    Border border = element.Element;
                    if (element.Type == ReportElementType.Title)
                    {
                        border.Width = canvasWidth * 0.9;
                        Canvas.SetTop(border, 0);
                        topPosition = 30; // Title height
                    }
                    else // Plot
                    {
                        border.Width = canvasWidth * 0.9;
                        Canvas.SetTop(border, topPosition);
                        topPosition += border.Height + 30; // 410px height + 30px gap
                    }
                    Canvas.SetLeft(border, (canvasWidth - border.Width) / 2);
                    DrawingCanvas.Children.Add(border);
                }
            }

            UpdateCanvasHeight();
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

        private void AddCommentButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("TODO: Comment function soon^_^", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

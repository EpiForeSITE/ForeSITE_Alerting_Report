using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ForeSITETestApp
{
    /// <summary>
    /// Interaction logic for RichTextEditorWindow.xaml
    /// </summary>
    public partial class RichTextEditorWindow : Window
    {
        private readonly RichTextBox _targetRichTextBox;

        public RichTextEditorWindow(RichTextBox targetRichTextBox)
        {
            InitializeComponent();
            _targetRichTextBox = targetRichTextBox;

            // Sync toolbar state with RichTextBox selection
            UpdateToolbarState();
            _targetRichTextBox.SelectionChanged += (s, e) => UpdateToolbarState();
        }

        private void UpdateToolbarState()
        {
            var selection = _targetRichTextBox?.Selection;

            if (selection == null)
                return;

            // Update toggle buttons
            BoldButton.IsChecked = selection.GetPropertyValue(TextElement.FontWeightProperty)?.Equals(FontWeights.Bold) == true;
            ItalicButton.IsChecked = selection.GetPropertyValue(TextElement.FontStyleProperty)?.Equals(FontStyles.Italic) == true;
            UnderlineButton.IsChecked = selection.GetPropertyValue(Inline.TextDecorationsProperty) == TextDecorations.Underline;

            // Update alignment buttons
            var alignment = selection.GetPropertyValue(Paragraph.TextAlignmentProperty);
            AlignLeftButton.IsChecked = alignment?.Equals(TextAlignment.Left) == true;
            AlignCenterButton.IsChecked = alignment?.Equals(TextAlignment.Center) == true;
            AlignRightButton.IsChecked = alignment?.Equals(TextAlignment.Right) == true;

            // Update font size
            var fontSize = selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (fontSize != DependencyProperty.UnsetValue)
            {
                FontSizeComboBox.SelectedItem = FontSizeComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(item => item.Content.ToString() == fontSize.ToString());
            }

            // Update font family
            var fontFamily = selection.GetPropertyValue(TextElement.FontFamilyProperty);
            if (fontFamily != null)
            {
                FontFamilyComboBox.SelectedItem = FontFamilyComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(item => item.Content.ToString() == fontFamily.ToString());
            }
        }

        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = _targetRichTextBox?.Selection;
            if (selection != null)
            {
                var currentWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
                selection.ApplyPropertyValue(TextElement.FontWeightProperty,
                    currentWeight.Equals(FontWeights.Bold) ? FontWeights.Normal : FontWeights.Bold);
            }
        }

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = _targetRichTextBox?.Selection;
            if (selection != null)
            {
                var currentStyle = selection.GetPropertyValue(TextElement.FontStyleProperty);
                selection.ApplyPropertyValue(TextElement.FontStyleProperty,
                    currentStyle != null && currentStyle.Equals(FontStyles.Italic) ? FontStyles.Normal : FontStyles.Italic);
            }
        }

        private void UnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = _targetRichTextBox?.Selection;
            if (selection != null)
            {
                var currentDecoration = selection.GetPropertyValue(Inline.TextDecorationsProperty);
                selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
                    currentDecoration == TextDecorations.Underline ? null : TextDecorations.Underline);
            }
        }

        private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontSizeComboBox.SelectedItem is ComboBoxItem selectedItem &&
                double.TryParse(selectedItem.Content.ToString(), out double fontSize))
            {
                
                _targetRichTextBox?.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, fontSize);
            }
        }

        private void AlignLeftButton_Click(object sender, RoutedEventArgs e)
        {
            _targetRichTextBox?.Selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, TextAlignment.Left);
            AlignCenterButton.IsChecked = false;
            AlignRightButton.IsChecked = false;
        }

        private void AlignCenterButton_Click(object sender, RoutedEventArgs e)
        {
            _targetRichTextBox?.Selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, TextAlignment.Center);
            AlignLeftButton.IsChecked = false;
            AlignRightButton.IsChecked = false;
        }

        private void AlignRightButton_Click(object sender, RoutedEventArgs e)
        {
            _targetRichTextBox?.Selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, TextAlignment.Right);
            AlignLeftButton.IsChecked = false;
            AlignCenterButton.IsChecked = false;
        }

        private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontFamilyComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                _targetRichTextBox?.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, selectedItem.Content.ToString());
            }
        }
    }
}

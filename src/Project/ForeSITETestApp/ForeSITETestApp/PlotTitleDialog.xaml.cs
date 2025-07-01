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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ForeSITETestApp
{
    /// <summary>
    /// Interaction logic for PlotTitleDialog.xaml
    /// </summary>
    public partial class PlotTitleDialog : UserControl
    {
        private Window _window;
        public string PlotTitle { get; private set; }

        public PlotTitleDialog()
        {
            InitializeComponent();
            // Wrap UserControl in a Window
            _window = new Window
            {
                Title = "Add Plot Title",
                Content = this,
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
        }

        public bool? ShowDialog(Window owner)
        {
            _window.Owner = owner;
            return _window.ShowDialog();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            PlotTitle = PlotTitleInput.Text?.Trim() ?? "";
            _window.DialogResult = true;
            _window.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            PlotTitle = null;
            _window.DialogResult = false;
            _window.Close();
        }
    }
}

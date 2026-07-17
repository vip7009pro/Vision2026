using System.Windows;

namespace VisionInspectionApp.UI.Views
{
    public partial class GlobalPreprocessWindow : Window
    {
        public GlobalPreprocessWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

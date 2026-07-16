using System.Windows;

namespace VisionInspectionApp.UI.Views
{
    public partial class PortValueDialog : Window
    {
        public PortValueDialog(string nodeName, string portName, string value)
        {
            InitializeComponent();
            TitleText.Text = $"Node: {nodeName}  |  Port: {portName}";
            ValueTextBox.Text = value;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

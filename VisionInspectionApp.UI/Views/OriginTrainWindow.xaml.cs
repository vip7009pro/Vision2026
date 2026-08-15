using System.Windows;
using VisionInspectionApp.UI.ViewModels;

namespace VisionInspectionApp.UI.Views
{
    public partial class OriginTrainWindow : Window
    {
        public OriginTrainViewModel? ViewModel => DataContext as OriginTrainViewModel;

        public OriginTrainWindow(OriginTrainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Closed += (_, _) => viewModel.Dispose();

            if (viewModel != null)
            {
                viewModel.RequestCloseDialog += () =>
                {
                    DialogResult = true;
                    Close();
                };
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

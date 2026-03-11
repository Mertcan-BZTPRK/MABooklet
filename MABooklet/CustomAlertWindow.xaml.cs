using System.Windows;
// using System.Windows.Input; // Sürükleme kalktığı için buna gerek kalmadı

namespace MABooklet
{
    public partial class CustomAlertWindow : Window
    {
        public CustomAlertWindow(string title, string message, bool isError = false, bool isConfirmation = false)
        {
            InitializeComponent();
            lblTitle.Text = title;
            lblMessage.Text = message;

            if (isError)
                lblTitle.Foreground = System.Windows.Media.Brushes.Crimson;

            if (isConfirmation)
            {
                btnOk.Content = "EVET";
                btnCancel.Visibility = Visibility.Visible;
                btnCancel.Content = "HAYIR";
            }

        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
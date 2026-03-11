using System.Windows;
using System.Windows.Controls;

namespace MABooklet
{
    public partial class DownloadRangeWindow : Window
    {
        public int StartPage { get; private set; }
        public int EndPage { get; private set; }
        public bool IsConfirmed { get; private set; } = false;

        public DownloadRangeWindow(int totalPages)
        {
            InitializeComponent();

            // Sayfa numaralarını doldur
            for (int i = 1; i <= totalPages; i++)
            {
                cmbStart.Items.Add(i);
                cmbEnd.Items.Add(i);
            }

            // Varsayılan değerler
            cmbStart.SelectedIndex = 0;
            cmbEnd.SelectedIndex = totalPages - 1;

            cmbStart.SelectionChanged += CalculateDiff;
            cmbEnd.SelectionChanged += CalculateDiff;
            CalculateDiff(null, null);
        }

        private void CalculateDiff(object sender, SelectionChangedEventArgs e)
        {
            if (cmbStart.SelectedItem != null && cmbEnd.SelectedItem != null)
            {
                int start = (int)cmbStart.SelectedItem;
                int end = (int)cmbEnd.SelectedItem;

                if (end < start) lblInfo.Text = "Hatalı Aralık!";
                else lblInfo.Text = $"Toplam: {end - start + 1} Sayfa";
            }
        }

        private void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            StartPage = (int)cmbStart.SelectedItem;
            EndPage = (int)cmbEnd.SelectedItem;

            if (EndPage < StartPage)
            {
                new CustomAlertWindow("Hata", "Bitiş sayfası başlangıçtan küçük olamaz!").ShowDialog();
                return;
            }

            IsConfirmed = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
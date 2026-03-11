using MABooklet;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace MABooklet
{
    public partial class MainWindow : Window
    {
        private string _sourceFilePath;
        private string _destinationFilePath;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
        }

        private void btnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void btnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                DropZone.Background = new SolidColorBrush(Color.FromRgb(235, 245, 255));
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e) => DropZone.Background = Brushes.White;

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.Background = Brushes.White;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && System.IO.Path.GetExtension(files[0]).ToLower() == ".pdf")
                {
                    _sourceFilePath = files[0];
                    lblFileInfo.Text = System.IO.Path.GetFileName(_sourceFilePath);
                    lblFileInfo.Foreground = Brushes.Black;

                    _destinationFilePath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(_sourceFilePath),
                        System.IO.Path.GetFileNameWithoutExtension(_sourceFilePath) + "_Booklet.pdf");
                    txtOutputPath.Text = _destinationFilePath;
                }
                else
                {
                    new CustomAlertWindow("Hata", "Sadece PDF dosyası kabul edilir.", true).ShowDialog();
                }
            }
        }

        private void btnSelectOutput_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog { Filter = "PDF Files|*.pdf" };
            if (!string.IsNullOrEmpty(_sourceFilePath))
                sfd.FileName = System.IO.Path.GetFileNameWithoutExtension(_sourceFilePath) + "_Booklet.pdf";

            if (sfd.ShowDialog() == true)
            {
                _destinationFilePath = sfd.FileName;
                txtOutputPath.Text = _destinationFilePath;
            }
        }

        private async void btnConvert_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_sourceFilePath))
            {
                new CustomAlertWindow("Uyarı", "Dosya seçmedin.", true).ShowDialog();
                return;
            }

            if (File.Exists(_destinationFilePath))
            {
                CustomAlertWindow confirmDlg = new CustomAlertWindow(
                    "Mevcut Dosya",
                    "Bu isimde bir dosya zaten var. Üzerine yazılmasını onaylıyor musun?",
                    isError: false,
                    isConfirmation: true);

                if (confirmDlg.ShowDialog() != true)
                {
                    return;
                }
            }

            pBar.Visibility = Visibility.Visible;
            await Task.Run(() =>
            {
                try
                {
                    BookletProcessor.CreateImposedBooklet(_sourceFilePath, _destinationFilePath);
                    Dispatcher.Invoke(() => new CustomAlertWindow("Başarılı", "Sıralama tamamlandı!").ShowDialog());
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => new CustomAlertWindow("Hata", ex.Message, true).ShowDialog());
                }
            });
            pBar.Visibility = Visibility.Collapsed;
        }
        private void btnMerger_Click(object sender, RoutedEventArgs e)
        {
            MergerWindow merger = new MergerWindow(this);
            this.Hide();
            merger.Show();
        }
        private void btnReaderMode_Click(object sender, RoutedEventArgs e)
        {
            // 1. Dosya Kontrolü
            if (string.IsNullOrEmpty(_sourceFilePath))
            {
                // Eğer dosya seçili değilse uyar
                new CustomAlertWindow("Uyarı", "Önce bir PDF dosyası seçmelisin.", true).ShowDialog();
                return;
            }

            ReaderWindow reader = new ReaderWindow(this, _sourceFilePath);

            this.Hide();

            reader.Show();
        }
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Link açılamadı: " + ex.Message);
            }
        }
        private void btnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                maximizeBut.Content = "⬛";
            }
            else if (this.WindowState == WindowState.Normal)
            {
                this.WindowState = WindowState.Maximized;
                maximizeBut.Content = "⬚";
            }
        }
        private void btnSplitter_Click(object sender, RoutedEventArgs e)
        {
            SplitterWindow splitter = new SplitterWindow(this);
            this.Hide();
            splitter.Show();
        }
    }
}
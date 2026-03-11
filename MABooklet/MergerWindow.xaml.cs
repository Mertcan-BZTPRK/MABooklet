using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace MABooklet
{
    // Dosya Modeli
    public class PdfFile
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
    }

    public partial class MergerWindow : Window
    {
        private Window _parent;
        private ObservableCollection<PdfFile> _files = new ObservableCollection<PdfFile>();

        // Sürükle-Bırak Takibi İçin
        private Point _startPoint;
        private bool _isDragging = false;

        public MergerWindow(Window parent)
        {
            InitializeComponent();
            _parent = parent;
            lstFiles.ItemsSource = _files;
        }

        // --- PENCERE KONTROLLERİ ---
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void btnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void btnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void btnBack_Click(object sender, RoutedEventArgs e) { _parent.Show(); this.Close(); }
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true; }
            catch (Exception ex) { new CustomAlertWindow("Hata", "Link açılamadı: " + ex.Message, true).ShowDialog(); }
        }


        // --- DOSYA EKLEME (DIŞARIDAN SÜRÜKLEME) ---
        private void lstFiles_DragEnter(object sender, DragEventArgs e)
        {
            // Hem dosya kabul et hem de kendi içinde taşımaya izin ver
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("myFormat"))
            {
                e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
            }
        }

        // --- SIRALAMA (İÇERİDEN SÜRÜKLEME BAŞLANGICI) ---
        private void lstFiles_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        private void lstFiles_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _startPoint - mousePos;

                // Fare belli bir miktar hareket ettiyse sürüklemeyi başlat
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    ListBox listBox = sender as ListBox;
                    ListBoxItem listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);

                    if (listBoxItem != null)
                    {
                        _isDragging = true;
                        PdfFile data = (PdfFile)listBox.ItemContainerGenerator.ItemFromContainer(listBoxItem);

                        // "myFormat" etiketiyle veriyi paketle
                        DataObject dragData = new DataObject("myFormat", data);
                        DragDrop.DoDragDrop(listBoxItem, dragData, DragDropEffects.Move);
                        _isDragging = false;
                    }
                }
            }
        }

        // --- BIRAKMA İŞLEMİ (DROP) ---
        private void lstFiles_Drop(object sender, DragEventArgs e)
        {
            // 1. Durum: Dışarıdan Dosya Geldi
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (System.IO.Path.GetExtension(file).ToLower() == ".pdf")
                    {
                        _files.Add(new PdfFile { Name = System.IO.Path.GetFileName(file), FullPath = file });
                    }
                }
            }
            // 2. Durum: İçeride Sıralama Yapılıyor
            else if (e.Data.GetDataPresent("myFormat"))
            {
                PdfFile droppedData = e.Data.GetData("myFormat") as PdfFile;
                PdfFile target = ((FrameworkElement)e.OriginalSource).DataContext as PdfFile;

                if (droppedData != null && target != null && droppedData != target)
                {
                    int oldIndex = _files.IndexOf(droppedData);
                    int newIndex = _files.IndexOf(target);

                    if (oldIndex != -1 && newIndex != -1)
                    {
                        _files.Move(oldIndex, newIndex);
                    }
                }
            }

            lblHint.Visibility = _files.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // Yardımcı: Tıklanan objenin üstündeki ListBoxItem'ı bulur
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T) return (T)current;
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        // --- BUTONLARLA ÇOKLU YUKARI/AŞAĞI TAŞIMA ---
        private void btnUp_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = lstFiles.SelectedItems.Cast<PdfFile>().OrderBy(x => _files.IndexOf(x)).ToList();
            if (selectedItems.Count == 0) return;
            if (_files.IndexOf(selectedItems.First()) == 0) return; // En tepedeyse dur

            foreach (var item in selectedItems)
            {
                int index = _files.IndexOf(item);
                if (index > 0) _files.Move(index, index - 1);
            }
            ScrollToSelection();
        }

        private void btnDown_Click(object sender, RoutedEventArgs e)
        {
            // Aşağı taşırken tersten (sondan) başlamalıyız ki indeksler karışmasın
            var selectedItems = lstFiles.SelectedItems.Cast<PdfFile>().OrderByDescending(x => _files.IndexOf(x)).ToList();
            if (selectedItems.Count == 0) return;
            if (_files.IndexOf(selectedItems.First()) == _files.Count - 1) return; // En alttaysa dur

            foreach (var item in selectedItems)
            {
                int index = _files.IndexOf(item);
                if (index < _files.Count - 1) _files.Move(index, index + 1);
            }
            ScrollToSelection();
        }

        private void btnRemove_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = lstFiles.SelectedItems.Cast<PdfFile>().ToList();
            foreach (var item in selectedItems) _files.Remove(item);
            lblHint.Visibility = _files.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ScrollToSelection()
        {
            if (lstFiles.SelectedItems.Count > 0)
                lstFiles.ScrollIntoView(lstFiles.SelectedItems[0]);
        }

        // --- BİRLEŞTİRME ---
        private void btnMerge_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count < 2)
            {
                new CustomAlertWindow("Uyarı", "En az 2 dosya eklemelisin.", true).ShowDialog();
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog { Filter = "PDF Dosyası|*.pdf", FileName = "Birlesmis_Dosya.pdf" };
            if (sfd.ShowDialog() == true)
            {
                string outputPath = sfd.FileName;
                string args = $"\"{outputPath}\" " + string.Join(" ", _files.Select(f => $"\"{f.FullPath}\""));
                RunMergeScript(args);
            }
        }

        private void RunMergeScript(string args)
        {
            try
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python", "merge.exe");
                if (!File.Exists(exePath))
                    exePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\python\merge.exe"));

                if (!File.Exists(exePath))
                {
                    new CustomAlertWindow("Hata", "merge.exe bulunamadı!", true).ShowDialog();
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    new CustomAlertWindow("Başarılı", "PDF dosyaları başarıyla birleştirildi!").ShowDialog();
                }
            }
            catch (Exception ex)
            {
                new CustomAlertWindow("Hata", ex.Message, true).ShowDialog();
            }
        }
    }
}
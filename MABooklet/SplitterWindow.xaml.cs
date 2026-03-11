using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Fium = PdfiumViewer;

namespace MABooklet
{
    public class PdfPageItem
    {
        public int PageIndex { get; set; }
        public int PageNumber => PageIndex + 1;
        public string PageLabel => $"Sayfa {PageNumber}";
        public ImageSource Image { get; set; }
    }

    public partial class SplitterWindow : Window
    {
        private Window _parent;
        private string _sourcePdfPath;
        private ObservableCollection<PdfPageItem> _pages = new ObservableCollection<PdfPageItem>();

        // Çoklu Seçim Mantığı İçin Değişkenler
        private bool _isPainting = false;
        private bool? _targetSelectionState = null; // true: Seç, false: Kaldır
        private object _lastHoveredItem = null;

        public SplitterWindow(Window parent)
        {
            InitializeComponent();
            _parent = parent;
            lstPages.ItemsSource = _pages;

            lstPages.SelectionChanged += (s, e) =>
            {
                lblSelectedCount.Text = $"{lstPages.SelectedItems.Count} Sayfa Seçildi";
            };
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

        // --- DOSYA SEÇME ---
        private void dropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { btnSelectFile_Click(sender, e); }

        private void btnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "PDF Dosyası|*.pdf" };
            if (ofd.ShowDialog() == true) LoadPdf(ofd.FileName);
        }

        private void dropZone_Drop(object sender, DragEventArgs e)
        {
            dropZone.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Path.GetExtension(files[0]).ToLower() == ".pdf") LoadPdf(files[0]);
                else new CustomAlertWindow("Hata", "Lütfen sadece PDF dosyası sürükleyin.", true).ShowDialog();
            }
        }

        private void dropZone_DragEnter(object sender, DragEventArgs e) => dropZone.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        private void dropZone_DragLeave(object sender, DragEventArgs e) => dropZone.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));

        // ==========================================
        //  ÖZEL SEÇİM MANTIĞI (PAINT SELECTION)
        // ==========================================

        private void lstPages_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Tıklanan elemanı bul
            var element = e.OriginalSource as DependencyObject;
            var listBoxItem = FindAncestor<ListBoxItem>(element);

            // Eğer boşluğa veya scrollbar'a tıklandıysa native davranışa karışma
            if (listBoxItem == null) return;

            // --- MANUEL TOGGLE MANTIĞI ---

            // Hedef Durumu Belirle: Şu an seçiliyse -> Kaldır, Değilse -> Seç
            _targetSelectionState = !listBoxItem.IsSelected;

            // Durumu uygula
            listBoxItem.IsSelected = _targetSelectionState.Value;
            _lastHoveredItem = listBoxItem;

            // Boyama modunu başlat
            _isPainting = true;

            // EN ÖNEMLİ KISIM: WPF'in varsayılan "Tıklayınca diğerlerini sil" olayını engelliyoruz.
            e.Handled = true;
        }

        private void lstPages_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPainting && e.LeftButton == MouseButtonState.Pressed && _targetSelectionState.HasValue)
            {
                // Fare altındaki öğeyi bul
                var listBoxItem = GetItemAt(e.GetPosition(lstPages));

                if (listBoxItem != null && listBoxItem != _lastHoveredItem)
                {
                    // Hedef durumu (Seç/Kaldır) uygula
                    listBoxItem.IsSelected = _targetSelectionState.Value;
                    _lastHoveredItem = listBoxItem;
                }

                // Kenara geldiyse kaydır
                AutoScrollList(e.GetPosition(lstPages));
            }
        }

        private void lstPages_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPainting = false;
            _targetSelectionState = null;
            _lastHoveredItem = null;
        }

        // Yardımcı: Koordinattaki ListBoxItem'ı bulur
        private ListBoxItem GetItemAt(Point position)
        {
            var element = VisualTreeHelper.HitTest(lstPages, position)?.VisualHit;
            return FindAncestor<ListBoxItem>(element);
        }

        // Yardımcı: Visual Tree'de yukarı doğru ebeveyn arar
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T) return (T)current;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // Yardımcı: Otomatik Scroll
        private void AutoScrollList(Point pos)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(lstPages);
            if (scrollViewer == null) return;

            double tolerance = 30;
            double offset = 5;

            if (pos.Y > lstPages.ActualHeight - tolerance)
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + offset);
            else if (pos.Y < tolerance)
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - offset);
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T) return (T)child;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string query = txtSearch.Text.Trim();
                if (string.IsNullOrWhiteSpace(query)) return;

                try
                {
                   
                    int totalPages = _pages.Count;
                    if (totalPages == 0) return;

                    var parts = query.Split(',');
                    PdfPageItem firstFound = null;

                    foreach (var part in parts)
                    {
                        string p = part.Trim();
                        if (string.IsNullOrWhiteSpace(p)) continue;

                        if (p.Contains("-")) 
                        {
                            var range = p.Split('-');

                           
                            int start = 1;
                            int end = totalPages;
                            bool isValid = true;

                            if (!string.IsNullOrWhiteSpace(range[0]))
                            {
                                if (!int.TryParse(range[0], out start)) isValid = false;
                            }

                            if (range.Length > 1 && !string.IsNullOrWhiteSpace(range[1]))
                            {
                                if (!int.TryParse(range[1], out end)) isValid = false;
                            }

                            if (isValid)
                            {
                                start = Math.Max(1, start);
                                end = Math.Min(totalPages, end);

                                if (start <= end)
                                {
                                    for (int i = start; i <= end; i++)
                                    {
                                        var item = _pages.FirstOrDefault(x => x.PageNumber == i);
                                        if (item != null)
                                        {
                                            
                                            if (!lstPages.SelectedItems.Contains(item))
                                                lstPages.SelectedItems.Add(item);

                                            if (firstFound == null) firstFound = item;
                                        }
                                    }
                                }
                            }
                        }
                        else 
                        {
                            if (int.TryParse(p, out int pageNum))
                            {
                                var item = _pages.FirstOrDefault(x => x.PageNumber == pageNum);
                                if (item != null)
                                {
                                    if (!lstPages.SelectedItems.Contains(item))
                                        lstPages.SelectedItems.Add(item);

                                    if (firstFound == null) firstFound = item;
                                }
                            }
                        }
                    }

                    // İlk bulunan sayfaya odaklan (Kullanıcı deneyimi)
                    if (firstFound != null) lstPages.ScrollIntoView(firstFound);
                }
                catch
                {
                    // Sessiz fail - kullanıcı hatalı bir metin girerse çökmesin
                }
            }
        }

        // --- PDF YÜKLEME ---
        private async void LoadPdf(string path)
        {
            _sourcePdfPath = path;
            txtFileName.Text = Path.GetFileName(path);
            _pages.Clear();
            loadingGrid.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    using (var doc = Fium.PdfDocument.Load(path))
                    {
                        for (int i = 0; i < doc.PageCount; i++)
                        {
                            using (var image = doc.Render(i, 300, 300, 96, 96, false))
                            {
                                var bitmapSrc = ConvertBitmapToImageSource(image);
                                Dispatcher.Invoke(() =>
                                {
                                    _pages.Add(new PdfPageItem { PageIndex = i, Image = bitmapSrc });
                                });
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                new CustomAlertWindow("Hata", "PDF yüklenirken sorun oluştu: " + ex.Message, true).ShowDialog();
            }
            finally
            {
                loadingGrid.Visibility = Visibility.Collapsed;
            }
        }

        private BitmapImage ConvertBitmapToImageSource(System.Drawing.Image src)
        {
            using (var ms = new MemoryStream())
            {
                src.Save(ms, ImageFormat.Png);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                ms.Seek(0, SeekOrigin.Begin);
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        private void btnSelectAll_Click(object sender, RoutedEventArgs e) => lstPages.SelectAll();
        private void btnClearSelection_Click(object sender, RoutedEventArgs e) => lstPages.UnselectAll();

        private void btnSplit_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_sourcePdfPath))
            {
                new CustomAlertWindow("Uyarı", "Lütfen önce bir PDF dosyası açın.", true).ShowDialog();
                return;
            }

            if (lstPages.SelectedItems.Count == 0)
            {
                new CustomAlertWindow("Uyarı", "Hiçbir sayfa seçmediniz.", true).ShowDialog();
                return;
            }

            var selectedIndices = lstPages.SelectedItems.Cast<PdfPageItem>()
                                          .OrderBy(p => p.PageIndex)
                                          .Select(p => p.PageNumber.ToString())
                                          .ToList();

            string rangeString = string.Join(",", selectedIndices);

            SaveFileDialog sfd = new SaveFileDialog { Filter = "PDF Dosyası|*.pdf", FileName = "Secilen_Sayfalar.pdf" };
            if (sfd.ShowDialog() == true)
            {
                RunSplitScript(_sourcePdfPath, rangeString, sfd.FileName);
            }
        }

        private void RunSplitScript(string inputFile, string range, string outputFile)
        {
            try
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python", "split.exe");

                if (!File.Exists(exePath))
                    exePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\python\split.exe"));

                if (!File.Exists(exePath))
                {
                    new CustomAlertWindow("Hata", "split.exe bulunamadı!", true).ShowDialog();
                    return;
                }

                string args = $"\"{inputFile}\" \"{range}\" \"{outputFile}\"";

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
                    string result = p.StandardOutput.ReadToEnd();

                    if (result.Contains("error"))
                        new CustomAlertWindow("Hata", "İşlem başarısız. " + result, true).ShowDialog();
                    else
                        new CustomAlertWindow("Başarılı", "Seçilen sayfalar ayrıldı ve kaydedildi!").ShowDialog();
                }
            }
            catch (Exception ex)
            {
                new CustomAlertWindow("Hata", ex.Message, true).ShowDialog();
            }
        }
    }
}
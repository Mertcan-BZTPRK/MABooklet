using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using static MABooklet.EdgeTtsClient;
using Fium = PdfiumViewer;
using Pig = UglyToad.PdfPig;
using PigContent = UglyToad.PdfPig.Content;
using SD = System.Drawing;
namespace MABooklet
{
    // Bu sınıfı ReaderWindow class'ının dışına veya içine ekle
    public class AppSettings
    {
        public string LastVoice { get; set; }
    }
    public class WordTimeData
    {
        public long offset { get; set; }
        public long length { get; set; }
        public double time_ms { get; set; }
    }

    public class TextLine
    {
        public string Text { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
    }

    public partial class ReaderWindow : Window
    {
        private Window _parentWindow;
        private bool _isGoingBack = false;

        private Pig.PdfDocument _pdfPigDoc;
        private Fium.PdfDocument _renderDoc;

        private string _cleanFullText;
        private List<TextLine> _currentLines;

        private bool _isPaused = false;
        private bool _isOnlineMode = false;
        private bool _isDisposed = false;
        private int _lastGeneratedSpeed = 0;

        private List<WordTimeData> _wordTimings;
        private int _lastFoundTimingIndex = 0;
        private bool _isPageChanging = false; // Crash önleyici kilit
        private MediaPlayer _mediaPlayer;
        private DispatcherTimer _highlightTimer;
        private EdgeTtsClient _edgeClient;
        private SpeechSynthesizer _synthesizer;

        private string _tempAudioPath;
        private string _currentPdfPath;

        // --- BUFFER SİSTEMİ DEĞİŞKENLERİ ---
        private string _bufferedAudioPath; // Hazırda bekleyen ses dosyası
        private int _bufferedPageIndex = -1; // Hangi sayfa için hazırlandı?
        private bool _isAutoPageTurn = false; // Otomatik geçiş mi yapılıyor?

        private PigContent.Page _currentPageData;
        private double _renderedWidth;
        private double _renderedHeight;
        private AIClient _aiClient = new AIClient();
        private ObservableCollection<ChatMessage> _chatHistory = new ObservableCollection<ChatMessage>();
        private bool _isChatOpen = false;
        public ReaderWindow(Window parent, string initialPath = "")
        {
            InitializeComponent();
            _parentWindow = parent;

            InitializeEngines();
            if (!string.IsNullOrEmpty(initialPath)) LoadPdf(initialPath);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); }
        private void btnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true; }
            catch (Exception ex) { new CustomAlertWindow("Hata", "Link açılamadı: " + ex.Message, true).ShowDialog(); }
        }

        private void InitializeEngines()
        {
            _mediaPlayer = new MediaPlayer();
            _edgeClient = new EdgeTtsClient();
            _mediaPlayer.MediaEnded += (s, e) => Dispatcher.Invoke(OnPlaybackFinished);

            _highlightTimer = new DispatcherTimer(DispatcherPriority.Render);
            _highlightTimer.Interval = TimeSpan.FromMilliseconds(20);
            _highlightTimer.Tick += UpdateHighlights;

            try { _synthesizer = new SpeechSynthesizer(); _synthesizer.SpeakCompleted += (s, e) => Dispatcher.Invoke(OnPlaybackFinished); } catch { }

            CheckConnectionAndSetupVoices();
        }
        private string _settingsPath = "user_settings.json";
        private List<EdgeVoice> _allVoices; // Tüm sesleri burada tutacağız

        // 1. BU METODU GÜNCELLE (CheckConnectionAndSetupVoices yerine bunu kullan)
        private async void CheckConnectionAndSetupVoices()
        {
            _isOnlineMode = true;
            lblStatus.Text = "Sesler yükleniyor...";
            cmbVoices.Items.Clear();

            try
            {
                // EXE'den sesleri çek
                _allVoices = await _edgeClient.GetVoicesAsync();

                if (_allVoices == null || _allVoices.Count == 0)
                {
                    lblStatus.Text = "Ses listesi alınamadı.";
                    return;
                }

                // ÖNCEKİ SEÇİMİ YÜKLE
                string lastVoiceShortName = LoadSettings();

                int selectedIndex = 0;
                int index = 0;

                // Listeyi ComboBox'a doldur
                foreach (var voice in _allVoices)
                {
                    // Görünüm: [tr-TR] EmelNeural (Female)
                    string displayName = $"[{voice.Locale}] {voice.ShortName.Split('-').Last()} ({voice.Gender})";
                    cmbVoices.Items.Add(displayName);

                    // Eğer bu ses, son kaydedilen ses ise indeksi tut
                    if (voice.ShortName == lastVoiceShortName)
                    {
                        selectedIndex = index;
                    }

                    // Varsayılan olarak Türkçe Emel'i bulsun (hiç ayar yoksa)
                    if (string.IsNullOrEmpty(lastVoiceShortName) && voice.ShortName == "tr-TR-EmelNeural")
                    {
                        selectedIndex = index;
                    }

                    index++;
                }

                // Seçimi yap
                if (cmbVoices.Items.Count > 0)
                {
                    cmbVoices.SelectedIndex = selectedIndex;
                }

                lblStatus.Text = "Hazır.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Ses hatası.";
                MessageBox.Show("Ses listesi alınırken hata: " + ex.Message);
            }
        }

        // 2. KAYDETME VE YÜKLEME METOTLARI
        private void SaveSettings(string shortName)
        {
            try
            {
                var settings = new AppSettings { LastVoice = shortName };
                string json = JsonConvert.SerializeObject(settings);
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }

        private string LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    return settings.LastVoice;
                }
            }
            catch { }
            return null;
        }

        // 3. COMBOBOX SEÇİM OLAYI (SelectionChanged eventine bağlamayı unutma!)
        // XAML tarafında: SelectionChanged="cmbVoices_SelectionChanged" zaten var ama içini güncelle:
        private void cmbVoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Eğer liste henüz dolmadıysa veya boşsa çık
            if (cmbVoices.SelectedIndex == -1 || _allVoices == null || cmbVoices.SelectedIndex >= _allVoices.Count) return;

            // Seçilen sesi bul
            var selectedVoice = _allVoices[cmbVoices.SelectedIndex];

            // Ayarlara kaydet
            SaveSettings(selectedVoice.ShortName);

            // ... Diğer kodların (oynatmayı durdur vs) ...
        }


        private void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "PDF Files|*.pdf" };
            if (ofd.ShowDialog() == true) LoadPdf(ofd.FileName);
        }

        private void LoadPdf(string path)
        {
            try
            {
                _currentPdfPath = path;
                _pdfPigDoc?.Dispose();
                _pdfPigDoc = Pig.PdfDocument.Open(path);
                _renderDoc?.Dispose();
                _renderDoc = Fium.PdfDocument.Load(path);

                cmbPages.Items.Clear();
                for (int i = 0; i < _pdfPigDoc.NumberOfPages; i++) cmbPages.Items.Add($"Sayfa {i + 1}");

                cmbPages.IsEnabled = true;
                cmbPages.SelectedIndex = 0;
                btnPlay.IsEnabled = true;
                lblStatus.Text = System.IO.Path.GetFileName(path) + " hazır.";

                UpdateNavButtons();
            }
            catch (Exception ex) { new CustomAlertWindow("Hata", ex.Message, true).ShowDialog(); }
        }

        private void UpdateNavButtons()
        {
            if (cmbPages.Items.Count == 0)
            {
                btnPrevPageOverlay.Visibility = Visibility.Collapsed;
                btnNextPageOverlay.Visibility = Visibility.Collapsed;
                return;
            }
            int current = cmbPages.SelectedIndex;
            int total = cmbPages.Items.Count;
            btnPrevPageOverlay.Visibility = (current > 0) ? Visibility.Visible : Visibility.Collapsed;
            btnNextPageOverlay.Visibility = (current < total - 1) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void btnPrevPage_Click(object sender, RoutedEventArgs e) { if (cmbPages.SelectedIndex > 0) cmbPages.SelectedIndex--; }
        private void btnNextPage_Click(object sender, RoutedEventArgs e) { if (cmbPages.SelectedIndex < cmbPages.Items.Count - 1) cmbPages.SelectedIndex++; }

        private void txtGoToPage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (int.TryParse(txtGoToPage.Text.Trim(), out int pageNum))
                {
                    int index = pageNum - 1;
                    if (index >= 0 && index < cmbPages.Items.Count)
                    {
                        cmbPages.SelectedIndex = index;
                        txtGoToPage.Text = "";
                    }
                }
            }
        }

        private void cmbPages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPages.SelectedIndex == -1 || _pdfPigDoc == null) return;

            // --- CRASH FIX: Sayfa değişirken diğer işlemleri kilitle ---
            _isPageChanging = true;
            _highlightTimer.Stop(); // Highlight çizimini durdur
            highlightCanvas.Children.Clear();

            try
            {
                if (!_isAutoPageTurn)
                {
                    StopAll();
                    _bufferedAudioPath = null;
                    _bufferedPageIndex = -1;
                }

                UpdateNavButtons();

                int pageIndex = cmbPages.SelectedIndex;
                int pageNumber = pageIndex + 1;

                // PDF Pig ile metin verilerini al (Hata korumalı)
                if (pageNumber <= _pdfPigDoc.NumberOfPages)
                {
                    _currentPageData = _pdfPigDoc.GetPage(pageNumber);
                    PrepareTextAndLines();
                }

                // Render işlemi
                using (SD.Image image = _renderDoc.Render(pageIndex, 600, 600, true))
                {
                    _renderedWidth = image.Width;
                    _renderedHeight = image.Height;
                    pdfImage.Source = ConvertBitmapToImageSource(image);
                }

                // Boyutları güncelle
                if (_currentPageData != null)
                {
                    pdfContentGrid.Width = _currentPageData.Width;
                    pdfContentGrid.Height = _currentPageData.Height;
                    highlightCanvas.Width = _currentPageData.Width;
                    highlightCanvas.Height = _currentPageData.Height;
                }

                pdfScroll.ScrollToTop();

                if (_isAutoPageTurn)
                {
                    _isAutoPageTurn = false;
                    btnPlay_Click(null, null);
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile kilidi aç
                new CustomAlertWindow("Sayfa Hatası", "Bu sayfa yüklenirken sorun oluştu: " + ex.Message, true).ShowDialog();
            }
            finally
            {
                _isPageChanging = false; // Kilidi kaldır
            }
        }

        private void PrepareTextAndLines()
        {
            var rawWords = _currentPageData.GetWords().ToList();
            var unsortedLines = GroupWordsIntoLines(rawWords);
            _currentLines = unsortedLines.OrderByDescending(l => l.Y).ToList();

            StringBuilder sb = new StringBuilder();
            int currentIndex = 0;

            foreach (var line in _currentLines)
            {
                string lineText = line.Text.Trim();
                if (string.IsNullOrWhiteSpace(lineText)) continue;

                sb.Append(lineText);
                sb.Append(" ");

                line.StartIndex = currentIndex;
                line.EndIndex = currentIndex + lineText.Length;
                currentIndex += lineText.Length + 1;
            }

            _cleanFullText = sb.ToString();
        }
        private void btnToggleChat_Click(object sender, RoutedEventArgs e)
        {
            _isChatOpen = !_isChatOpen;
            chatPanel.Width = _isChatOpen ? 350 : 0; // Genişlik ayarı

            if (listChat.ItemsSource == null)
                listChat.ItemsSource = _chatHistory;
        }

        private async void btnSendChat_Click(object sender, RoutedEventArgs e)
        {
            string question = txtChatInput.Text.Trim();
            if (string.IsNullOrEmpty(question)) return;

            // 1. Kullanıcı Mesajını Ekle
            // (AddMessage metodunda zaten ScrollToBottom var, o yüzden burada ekstra scrola gerek yok)
            AddMessage(question, true);
            txtChatInput.Text = "";

            // Yükleniyor Mesajı
            var loadingMsg = new ChatMessage
            {
                Text = "Ders notları inceleniyor...",
                IsUser = false,
                Time = DateTime.Now.ToShortTimeString(),
                // Burayı ekledik ki baştan null olmasın:
                FormattedText = CreateMarkdown("Ders notları inceleniyor...")
            };

            _chatHistory.Add(loadingMsg);

            // HATA DÜZELTME: ItemsControl'de ScrollIntoView yoktur. 
            // Onun yerine ScrollViewer'ı (chatScroller) aşağı kaydırıyoruz.
            chatScroller.ScrollToBottom();

            try
            {
                // 2. Python'a Sor
                bool isDetailed = rbLong.IsChecked == true;

                // _currentPdfPath'in dolu olduğundan emin ol
                if (string.IsNullOrEmpty(_currentPdfPath))
                {
                    _chatHistory.Remove(loadingMsg);
                    AddMessage("Hata: Önce bir PDF dosyası açmalısın.", false);
                    return;
                }

                string answer = await _aiClient.AskGeminiAsync(_currentPdfPath, question, isDetailed);

                // 3. Yükleniyor mesajını sil, Cevabı ekle
                _chatHistory.Remove(loadingMsg);
                AddMessage(answer, false);
            }
            catch (Exception ex)
            {
                _chatHistory.Remove(loadingMsg);
                AddMessage("Hata oluştu: " + ex.Message, false);
            }
        }
        private FlowDocument CreateMarkdown(string text)
        {
            FlowDocument doc = new FlowDocument();
            doc.PagePadding = new Thickness(0); // Kenar boşluklarını sıfırla

            // Satır satır işle
            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                Paragraph p = new Paragraph();
                string cleanLine = line.Trim();

                // --- BAŞLIK KONTROLÜ (##) ---
                if (cleanLine.StartsWith("##"))
                {
                    p.FontSize = 16;
                    p.FontWeight = FontWeights.Bold;
                    p.Foreground = new SolidColorBrush(Colors.LightSkyBlue); // Başlık rengi
                    cleanLine = cleanLine.Replace("#", "").Trim();
                }
                // --- MADDE İŞARETİ KONTROLÜ (*) ---
                else if (cleanLine.StartsWith("* ") || cleanLine.StartsWith("- "))
                {
                    p.Margin = new Thickness(10, 0, 0, 0); // İçerden başlat
                }

                // --- KALIN YAZI KONTROLÜ (**) ---
                // Örnek: "Bu **önemli** bir konu."
                string[] parts = cleanLine.Split(new[] { "**" }, StringSplitOptions.None);

                for (int i = 0; i < parts.Length; i++)
                {
                    Run run = new Run(parts[i]);

                    // Tek sayılar kalın kısımlardır (0: normal, 1: kalın, 2: normal...)
                    if (i % 2 == 1)
                    {
                        run.FontWeight = FontWeights.Bold;
                        run.Foreground = new SolidColorBrush(Colors.Yellow); // Kalın yerleri Sarı yap
                    }

                    p.Inlines.Add(run);
                }

                doc.Blocks.Add(p);
            }
            return doc;
        }
        private void AddMessage(string text, bool isUser)
        {
            var msg = new ChatMessage
            {
                Text = text,
                IsUser = isUser,
                Time = DateTime.Now.ToShortTimeString(),
                // İşte sihirli dokunuş: Metni formata çeviriyoruz
                FormattedText = CreateMarkdown(text)
            };

            _chatHistory.Add(msg);

            Dispatcher.InvokeAsync(() =>
            {
                chatScroller.ScrollToBottom();
            });
        }
        private void RichTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var rtb = sender as RichTextBox;
            // DataContext'in ChatMessage olup olmadığını ve null olmadığını kontrol et
            if (rtb != null && rtb.DataContext is ChatMessage msg)
            {
                // --- EMNİYET KEMERİ ---
                // Eğer FormattedText null ise (örneğin Loading mesajında), o an oluştur.
                if (msg.FormattedText == null)
                {
                    // Eğer Text de null ise boş string kullan
                    msg.FormattedText = CreateMarkdown(msg.Text ?? "");
                }

                rtb.Document = msg.FormattedText;
            }
        }
        private void txtChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                btnSendChat_Click(sender, e);
            }
        }
        // --- ARKA PLAN METİN ÇIKARMA (BUFFER İÇİN) ---
        private string ExtractTextForBuffer(int pageIndex)
        {
            try
            {
                var page = _pdfPigDoc.GetPage(pageIndex + 1); // PdfPig 1-based index kullanır
                var words = page.GetWords().ToList();
                var lines = GroupWordsIntoLines(words).OrderByDescending(l => l.Y).ToList();

                StringBuilder sb = new StringBuilder();
                foreach (var line in lines)
                {
                    string txt = line.Text.Trim();
                    if (!string.IsNullOrWhiteSpace(txt)) sb.Append(txt + " ");
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // --- BUFFER OLUŞTURMA GÖREVİ ---
        private async Task CreateBufferForNextPageAsync(int nextPageIndex, string voice, int speed)
        {
            try
            {
                string nextText = ExtractTextForBuffer(nextPageIndex);
                if (string.IsNullOrWhiteSpace(nextText)) return;

                string bufferFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mabooklet_buffer_{nextPageIndex}.mp3");

                // Python'a oluştur emri ver
                await _edgeClient.SynthesizeWithPythonAsync(nextText, voice, speed, bufferFile);

                // Dosya başarıyla oluştuysa kaydet
                if (File.Exists(bufferFile) && File.Exists(bufferFile + ".json"))
                {
                    _bufferedAudioPath = bufferFile;
                    _bufferedPageIndex = nextPageIndex;
                }
            }
            catch { /* Sessizce başarısız olsun */ }
        }

        private List<TextLine> GroupWordsIntoLines(List<PigContent.Word> words)
        {
            var lines = new List<TextLine>();
            if (words == null || words.Count == 0) return lines;

            // Koordinata göre sırala
            var sortedWords = words.OrderByDescending(w => w.BoundingBox.Bottom)
                                   .ThenBy(w => w.BoundingBox.Left).ToList();

            TextLine currentLine = null;

            foreach (var word in sortedWords)
            {
                string txt = word.Text.Trim();

                // --- TEMİZLİK ROBOTU ---
                // 1. Boşsa geç
                if (string.IsNullOrWhiteSpace(txt)) continue;

                // 2. Sadece noktalama işareti veya tireden ibaretse geç (Örn: "----", "...", "•")
                // Bu Regex "İçinde en az bir harf veya rakam var mı?" diye sorar. Yoksa atar.
                if (!Regex.IsMatch(txt, @"[\w\d]")) continue;

                bool isNewLine = false;

                if (currentLine == null)
                {
                    isNewLine = true;
                }
                else
                {
                    // Dikey Mesafe (Satır kayması)
                    double yDiff = Math.Abs(word.BoundingBox.Bottom - currentLine.Y);
                    if (yDiff > (word.BoundingBox.Height * 0.3)) isNewLine = true;

                    // Font Boyutu (Başlık ayrımı)
                    double heightDiff = Math.Abs(word.BoundingBox.Height - currentLine.Height);
                    if (heightDiff > 2.0 && !isNewLine) isNewLine = true;

                    // Yatay Boşluk (Sütun ayrımı)
                    double currentRight = currentLine.X + currentLine.Width;
                    double gap = word.BoundingBox.Left - currentRight;

                    if (word.BoundingBox.Left < currentLine.X) isNewLine = true; // Başa döndüyse
                    else if (gap > 100) isNewLine = true; // Çok uzaksa
                }

                if (isNewLine)
                {
                    if (currentLine != null) lines.Add(currentLine);
                    currentLine = new TextLine
                    {
                        Text = txt, // Temizlenmiş text
                        X = word.BoundingBox.Left,
                        Y = word.BoundingBox.Bottom,
                        Width = word.BoundingBox.Width,
                        Height = word.BoundingBox.Height
                    };
                }
                else
                {
                    currentLine.Text += " " + txt;

                    // Genişlik Güncelle
                    double currentRight = currentLine.X + currentLine.Width;
                    double wordRight = word.BoundingBox.Right;
                    if (wordRight > currentRight) currentLine.Width = wordRight - currentLine.X;

                    // Yükseklik Güncelle
                    if (word.BoundingBox.Height > currentLine.Height) currentLine.Height = word.BoundingBox.Height;
                }
            }
            if (currentLine != null) lines.Add(currentLine);
            return lines;
        }

        private BitmapImage ConvertBitmapToImageSource(SD.Image src)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                src.Save(ms, SD.Imaging.ImageFormat.Bmp);
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                ms.Seek(0, SeekOrigin.Begin);
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        private void UpdateHighlights(object sender, EventArgs e)
        {
            // 1. GÜVENLİK KİLİDİ: Sayfa değişiyorsa veya oynatıcı yoksa hemen çık.
            if (_isPageChanging || _mediaPlayer == null || _currentLines == null || _currentPageData == null) return;

            try
            {
                // 2. DURUMU SABİTLE (Kritik Nokta)
                // Özelliği bir değişkene kopyalıyoruz. Artık bu 'duration' değişkeni değişemez.
                Duration duration = _mediaPlayer.NaturalDuration;

                // 3. KONTROLÜ SABİT DEĞİŞKEN ÜZERİNDEN YAP
                if (!duration.HasTimeSpan) return;

                double currentMs = _mediaPlayer.Position.TotalMilliseconds;
                double adjustedMs = currentMs + 500;

                TextLine activeLine = null;

                if (_wordTimings != null && _wordTimings.Count > 0)
                {
                    WordTimeData activeWord = null;
                    for (int i = _lastFoundTimingIndex; i < _wordTimings.Count; i++)
                    {
                        if (_wordTimings[i].time_ms > adjustedMs)
                        {
                            if (i > 0)
                            {
                                activeWord = _wordTimings[i - 1];
                                _lastFoundTimingIndex = i - 1;
                            }
                            break;
                        }
                    }
                    if (activeWord == null && _wordTimings.Last().time_ms <= adjustedMs) activeWord = _wordTimings.Last();

                    if (activeWord != null)
                    {
                        long currentOffset = activeWord.offset;
                        foreach (var line in _currentLines)
                        {
                            if (currentOffset >= line.StartIndex && currentOffset <= (line.EndIndex + 15))
                            {
                                activeLine = line;
                                break;
                            }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(_cleanFullText))
                {
                    // 4. GÜVENLİ KULLANIM
                    // Burada artık _mediaPlayer.NaturalDuration değil, kopyaladığımız 'duration'ı kullanıyoruz.
                    double totalMs = duration.TimeSpan.TotalMilliseconds;

                    if (totalMs > 0)
                    {
                        double percent = adjustedMs / totalMs;
                        if (percent > 1) percent = 1;
                        int predictedCharIndex = (int)(_cleanFullText.Length * percent);

                        foreach (var line in _currentLines)
                        {
                            if (predictedCharIndex >= line.StartIndex && predictedCharIndex <= line.EndIndex)
                            {
                                activeLine = line;
                                break;
                            }
                        }
                    }
                }

                if (activeLine != null)
                {
                    DrawLineHighlight(activeLine);
                }
            }
            catch
            {
                // Timer hatalarını yut, uygulama akmaya devam etsin.
            }
        }

        private void DrawLineHighlight(TextLine line)
        {
            highlightCanvas.Children.Clear();

            // 1. Koordinatları Yüksek Çözünürlüklü Resme Göre Hesapla
            double scaleX = _renderedWidth / _currentPageData.Width;
            double scaleY = _renderedHeight / _currentPageData.Height;

            double x = line.X * scaleX;
            double y = (_currentPageData.Height - (line.Y + line.Height)) * scaleY;
            double w = line.Width * scaleX;
            double h = line.Height * scaleY;

            // 2. Highlight Kutusunu Çiz
            Rectangle rect = new Rectangle
            {
                Width = w + 15,
                Height = h + 6,
                Fill = new SolidColorBrush(Color.FromArgb(60, 0, 120, 215)),
                Stroke = Brushes.Transparent,
                RadiusX = 4,
                RadiusY = 4
            };

            Canvas.SetLeft(rect, x - 5);
            Canvas.SetTop(rect, y - 3);
            highlightCanvas.Children.Add(rect);

            // --- DÜZELTİLEN SCROLL MANTIĞI ---

            // Eğer grid henüz oluşmadıysa işlem yapma
            if (pdfContentGrid.ActualHeight <= 0) return;

            // Viewbox Ölçeğini Bul: (Scrollun Toplam Boyu / Resmin Gerçek Boyu)
            // Bu bize resmin ekranda ne kadar küçültüldüğünü (veya büyütüldüğünü) verir.
            double screenScaleFactor = pdfScroll.ExtentHeight / pdfContentGrid.ActualHeight;

            // Y koordinatını ekran (scroll) koordinatına dönüştür
            double screenY = y * screenScaleFactor;
            double screenHighlightHeight = h * screenScaleFactor;

            // Hedef: Highlight'ın ortası, ekranın (Viewport) tam ortasına gelsin
            double viewportCenter = pdfScroll.ViewportHeight / 2;

            // Hedef Offset = (Dönüştürülmüş Y) - (Ekran Yarısı) + (Highlight Yarısı)
            double targetOffset = screenY - viewportCenter + (screenHighlightHeight / 2);

            // Negatif değerlere düşmesin (Sayfa başı)
            if (targetOffset < 0) targetOffset = 0;

            // Max scroll değerini aşmasın (Sayfa sonu)
            if (targetOffset > pdfScroll.ScrollableHeight) targetOffset = pdfScroll.ScrollableHeight;

            // Kaydır
            pdfScroll.ScrollToVerticalOffset(targetOffset);
        }

        private async void btnDowloand_Click(object sender, RoutedEventArgs e)
        {
            // 1. Kontroller
            if (_allVoices == null || _allVoices.Count == 0)
            {
                new CustomAlertWindow("Hata", "Ses motoru hazır değil.", true).ShowDialog();
                return;
            }

            int totalPages = cmbPages.Items.Count;
            if (totalPages == 0) return;

            // 2. Aralık Seçim Penceresi
            DownloadRangeWindow rangeWin = new DownloadRangeWindow(totalPages);
            rangeWin.Owner = this;
            rangeWin.ShowDialog();

            if (!rangeWin.IsConfirmed) return;

            int start = rangeWin.StartPage;
            int end = rangeWin.EndPage;
            int countToProcess = end - start + 1;

            // 3. Kayıt Yeri
            var sfd = new Microsoft.Win32.SaveFileDialog();
            sfd.Filter = "MP3 Ses Dosyası|*.mp3";
            sfd.FileName = $"Booklet_Sayfa_{start}-{end}.mp3";

            if (sfd.ShowDialog() != true) return;

            // 4. Arayüzü İndirme Moduna Al
            pBar.Visibility = Visibility.Visible;
            pBar.IsIndeterminate = false; // Artık biz yöneteceğiz
            pBar.Value = 0;
            lblPercentage.Visibility = Visibility.Visible;
            lblPercentage.Text = "%0";
            lblStatus.Text = "İndirme Başlıyor...";
            btnDowloand.IsEnabled = false;

            string selectedVoice = _allVoices[cmbVoices.SelectedIndex].ShortName;
            int currentSpeed = (int)sliderSpeed.Value * 10;
            string savePath = sfd.FileName;

            await Task.Run(async () =>
            {
                string tempChunkPath = "";
                FileStream finalFileStream = null;

                try
                {
                    // Hedef dosyayı oluştur (Append modunda değil, Create modunda)
                    finalFileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write);

                    for (int i = start; i <= end; i++)
                    {
                        // A. Metni Al
                        string pageText = ExtractTextForBuffer(i - 1);

                        // Metin varsa "Sayfa X" diye ekle, yoksa boş geçme
                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            pageText = $"Sayfa {i}. \n" + pageText;
                        }
                        else
                        {
                            // Boş sayfa olsa bile yüzdeyi ilerletmek için devam et
                            Dispatcher.Invoke(() => UpdateProgress(i - start + 1, countToProcess));
                            continue;
                        }

                        // B. O Sayfayı Seslendir (Geçici Dosyaya)
                        tempChunkPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"chunk_{Guid.NewGuid()}.mp3");
                        await _edgeClient.SynthesizeWithPythonAsync(pageText, selectedVoice, currentSpeed, tempChunkPath);

                        // C. Geçici Dosyayı Ana Dosyanın Ucuna Ekle (Merge İşlemi)
                        if (File.Exists(tempChunkPath))
                        {
                            using (var chunkStream = File.OpenRead(tempChunkPath))
                            {
                                await chunkStream.CopyToAsync(finalFileStream);
                            }
                            // Parçayı sil
                            File.Delete(tempChunkPath);
                        }

                        // D. Yüzdeyi Güncelle
                        Dispatcher.Invoke(() => UpdateProgress(i - start + 1, countToProcess));
                    }

                    // İşlem Bitti Mesajı
                    Dispatcher.Invoke(() =>
                    {
                        new CustomAlertWindow("Başarılı", "İndirme tamamlandı!").ShowDialog();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        new CustomAlertWindow("Hata", "İndirme başarısız: " + ex.Message, true).ShowDialog();
                    });
                }
                finally
                {
                    // Dosyayı kapat ve temizlik yap
                    if (finalFileStream != null) finalFileStream.Close();

                    Dispatcher.Invoke(() =>
                    {
                        pBar.Visibility = Visibility.Collapsed;
                        lblPercentage.Visibility = Visibility.Collapsed;
                        lblStatus.Text = "Hazır.";
                        btnDowloand.IsEnabled = true;
                    });
                }
            });
        }

        // Yüzde Hesaplama Yardımcısı
        private void UpdateProgress(int current, int total)
        {
            double percent = (double)current / total * 100;
            pBar.Value = percent;
            lblPercentage.Text = $"%{Math.Round(percent)} Tamamlandı";
            lblStatus.Text = $"Sayfa {current}/{total} işleniyor...";
        }
        private async void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            int currentSpeed = (int)sliderSpeed.Value * 10;
            if (_isPaused && currentSpeed == _lastGeneratedSpeed) { ResumeAll(); return; }
            if (string.IsNullOrEmpty(_cleanFullText)) return;

            _mediaPlayer.Stop();
            _highlightTimer.Stop();
            highlightCanvas.Children.Clear();
            _wordTimings = null;
            _lastFoundTimingIndex = 0;
            btnDowloand.IsEnabled = false;
            _isPaused = false;
            btnPlay.IsEnabled = false;

            if (_isOnlineMode)
            {
                _isDisposed = false;

                try
                {
                    // Listeden seçili olan nesnenin gerçek ID'sini (ShortName) alıyoruz
                    string selectedVoice = "tr-TR-EmelNeural"; // Güvenlik için default
                    if (cmbVoices.SelectedIndex != -1 && _allVoices != null)
                    {
                        selectedVoice = _allVoices[cmbVoices.SelectedIndex].ShortName;
                    }

                    bool isBuffered = (_bufferedPageIndex == cmbPages.SelectedIndex && !string.IsNullOrEmpty(_bufferedAudioPath) && File.Exists(_bufferedAudioPath));

                    if (isBuffered)
                    {
                        _tempAudioPath = _bufferedAudioPath;
                        lblStatus.Text = "▶ Okunuyor (Kesintisiz)...";
                        _bufferedAudioPath = null;
                        _bufferedPageIndex = -1;
                    }
                    else
                    {
                        lblStatus.Text = "Ses hazırlanıyor...";
                        pBar.Visibility = Visibility.Visible;
                        _tempAudioPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mabooklet_sound_{DateTime.Now.Ticks}.mp3");
                        await _edgeClient.SynthesizeWithPythonAsync(_cleanFullText, selectedVoice, currentSpeed, _tempAudioPath);
                    }

                    _lastGeneratedSpeed = currentSpeed;

                    string jsonPath = _tempAudioPath + ".json";
                    if (File.Exists(jsonPath))
                    {
                        try
                        {
                            string jsonContent = File.ReadAllText(jsonPath);
                            _wordTimings = JsonConvert.DeserializeObject<List<WordTimeData>>(jsonContent);
                        }
                        catch { }
                    }

                    pBar.Visibility = Visibility.Collapsed;
                    if (_isDisposed) { lblStatus.Text = "İptal edildi."; return; }

                    if (!isBuffered) lblStatus.Text = "▶ Okunuyor...";

                    _mediaPlayer.Open(new Uri(_tempAudioPath));
                    _mediaPlayer.Play();
                    _highlightTimer.Start();
                    btnPause.IsEnabled = true;
                    btnDowloand.IsEnabled = true;

                    int nextPage = cmbPages.SelectedIndex + 1;
                    if (nextPage < cmbPages.Items.Count)
                    {
                        _ = Task.Run(() => CreateBufferForNextPageAsync(nextPage, selectedVoice, currentSpeed));
                    }
                }
                catch (Exception ex)
                {
                    pBar.Visibility = Visibility.Collapsed;
                    if (!_isDisposed)
                    {
                        new CustomAlertWindow("Hata", ex.Message, true).ShowDialog();
                        btnPlay.IsEnabled = true;
                        lblStatus.Text = "Hata.";
                    }
                }
            }
        }

        private void btnPause_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer.Pause();
            _highlightTimer.Stop();
            _isPaused = true;
            btnPlay.IsEnabled = true;
            btnPause.IsEnabled = false;
            lblStatus.Text = "Duraklatıldı.";
        }

        private void StopAll()
        {
            _mediaPlayer.Close();
            _highlightTimer.Stop();
            highlightCanvas.Children.Clear();
            _isDisposed = true;
            _isPaused = false;
            btnPlay.IsEnabled = true;
            btnPause.IsEnabled = false;
            btnDowloand.IsEnabled = false;
            pBar.Visibility = Visibility.Collapsed;
            lblStatus.Text = "Hazır.";
        }

        private void ResumeAll()
        {
            _mediaPlayer.Play();
            _highlightTimer.Start();
            _isPaused = false;
            btnPlay.IsEnabled = false;
            btnPause.IsEnabled = true;
            lblStatus.Text = "Okunuyor...";
        }

        private void OnPlaybackFinished()
        {
            if (!_isDisposed)
            {
                lblStatus.Text = "Sayfa bitti.";
                _highlightTimer.Stop();
                highlightCanvas.Children.Clear();

                if (cmbPages.SelectedIndex < cmbPages.Items.Count - 1)
                {
                    _isAutoPageTurn = true;

                    cmbPages.SelectedIndex++;
                }
                else
                {
                    btnPlay.IsEnabled = true;
                    btnPause.IsEnabled = false;
                    pBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void sliderSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (lblSpeed != null) lblSpeed.Text = $"⚡ HIZ: {e.NewValue}";
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            _isGoingBack = true;
            if (_parentWindow != null) _parentWindow.Show();
            this.Close();
        }
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
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
        protected override void OnClosed(EventArgs e)
        {
            StopAll();
            _renderDoc?.Dispose();
            _pdfPigDoc?.Dispose();
            try { if (File.Exists(_tempAudioPath)) File.Delete(_tempAudioPath); } catch { }
            try { if (File.Exists(_tempAudioPath + ".json")) File.Delete(_tempAudioPath + ".json"); } catch { }

            try
            {
                if (!string.IsNullOrEmpty(_bufferedAudioPath) && File.Exists(_bufferedAudioPath))
                    File.Delete(_bufferedAudioPath);
            }
            catch { }

            base.OnClosed(e);

            if (!_isGoingBack)
            {
                Application.Current.Shutdown();
            }
        }
    }
}
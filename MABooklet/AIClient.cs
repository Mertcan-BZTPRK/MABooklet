using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace MABooklet
{
    // Sohbet Balonu Modeli
    // ReaderWindow.xaml.cs'in en altına veya uygun bir yere:
    public class ChatMessage
    {
        public string Text { get; set; } // Ham metin
        public FlowDocument FormattedText { get; set; } // Süslü metin (Yeni ekledik)

        public bool IsUser { get; set; }
        public string Time { get; set; }
        public System.Windows.HorizontalAlignment Alignment => IsUser ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        public string BgColor => IsUser ? "#005F73" : "#383838";
        public string TextColor => "#EAEAEA";
        public System.Windows.CornerRadius CornerRadius => IsUser ? new System.Windows.CornerRadius(15, 15, 0, 15) : new System.Windows.CornerRadius(15, 15, 15, 0);
    }

    public class AIClient
    {
        public async Task<string> AskGeminiAsync(string pdfPath, string question, bool isDetailed)
        {
            string detailArg = isDetailed ? "long" : "short";

            // EXE Yolu Bulma (Klasik yöntemimiz)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Path.Combine(baseDir, "python", "ai.exe");

            if (!File.Exists(exePath))
            {
                string devPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\python\ai.exe"));
                if (File.Exists(devPath)) exePath = devPath;
            }

            if (!File.Exists(exePath)) return "Hata: ai.exe bulunamadı.";

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            // Argümanlar: [PDF] [Soru] [Detay]
            psi.Arguments = $"\"{pdfPath}\" \"{question}\" \"{detailArg}\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8; // Türkçe karakter sorunu olmasın

            return await Task.Run(() =>
            {
                try
                {
                    using (Process process = Process.Start(psi))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        // JSON çözümle
                        var result = JsonConvert.DeserializeObject<Dictionary<string, string>>(output);
                        if (result.ContainsKey("error")) return "Hata: " + result["error"];
                        return result["answer"];
                    }
                }
                catch (Exception ex)
                {
                    return "Bağlantı Hatası: " + ex.Message;
                }
            });
        }
    }
}
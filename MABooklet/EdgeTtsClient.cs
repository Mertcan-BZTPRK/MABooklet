using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MABooklet
{
    public class EdgeTtsClient
    {
        // Ses bilgilerini tutacak model
        public class EdgeVoice
        {
            public string Name { get; set; }      // Örn: Microsoft Server Speech Text to Speech Voice (tr-TR, EmelNeural)
            public string ShortName { get; set; } // Örn: tr-TR-EmelNeural
            public string Gender { get; set; }    // Female / Male
            public string Locale { get; set; }    // tr-TR
        }

        // EdgeTtsClient sınıfının içine bu metodu ekle:
        public async Task<List<EdgeVoice>> GetVoicesAsync()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Path.Combine(baseDir, "python", "tts.exe");

            // Debug/Release yolu kontrolü (Önceki fix'in aynısı)
            if (!File.Exists(exePath))
            {
                string devPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\python\tts.exe"));
                if (File.Exists(devPath)) exePath = devPath;
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            psi.Arguments = "--list"; // Python'a "Listeyi ver" diyoruz
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;

            var result = await Task.Run(() =>
            {
                using (Process process = Process.Start(psi))
                {
                    // Çıktıyı sonuna kadar oku
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output;
                }
            });

            // JSON'ı listeye çevir
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<List<EdgeVoice>>(result);
            }
            catch
            {
                return new List<EdgeVoice>(); // Hata olursa boş liste dön
            }
        }
        public async Task SynthesizeWithPythonAsync(string text, string voice, int rate, string outputPath)
        {
            // Geçici metin dosyası
            string tempTextFile = Path.Combine(Path.GetTempPath(), $"tts_input_{Guid.NewGuid()}.txt");
            File.WriteAllText(tempTextFile, text);

            string rateStr = rate >= 0 ? $"+{rate}%" : $"{rate}%";

            // --- EXE BULMA MANTIĞI (GÜNCELLENDİ) ---
            string baseDir = AppDomain.CurrentDomain.BaseDirectory; // Programın çalıştığı yer (bin\Debug...)

            // 1. Seçenek: Programın yanındaki 'python' klasörüne bak (Dağıtım/Release modu için)
            string exePath = Path.Combine(baseDir, "python", "tts.exe");

            // 2. Seçenek: Eğer orada yoksa, geliştirme ortamındaki (Source) klasöre bak
            if (!File.Exists(exePath))
            {
                // bin\Debug\net6.0-windows içinden 3 adım geri çıkıp proje köküne iniyoruz
                string devPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\python\tts.exe"));
                if (File.Exists(devPath)) exePath = devPath;
            }

            // Hala yoksa hata fırlat ve NEREYE BAKTIĞINI söyle
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException($"Patron, tts.exe kayıp! \n1. Bakılan yer: {Path.Combine(baseDir, "python", "tts.exe")}\n2. Bakılan yer: {Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\python\tts.exe"))}\nLütfen 'tts.exe' dosyasının 'Copy to Output Directory' ayarını 'Copy Always' yaptığından emin ol.");
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath; // ARTIK BULDUĞUMUZ YOL
            psi.Arguments = $"\"{tempTextFile}\" \"{voice}\" \"{rateStr}\" \"{outputPath}\"";

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            await Task.Run(() =>
            {
                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    try { File.Delete(tempTextFile); } catch { }

                    if (process.ExitCode != 0 || output.Contains("ERROR"))
                    {
                        throw new Exception($"Python Hatası: {error} \nKonsol: {output}");
                    }
                }
            });
        }
    }
}
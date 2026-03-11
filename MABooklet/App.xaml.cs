using System;
using System.Diagnostics;
using System.Windows;

namespace MABooklet
{
    public partial class App : Application
    {
        // Uygulama kapanırken tetiklenen olay
        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            KillGhostProcesses(); // Temizlik ekibini çağır
        }

        private void KillGhostProcesses()
        {
            // Bizim Python ajanlarının isimleri
            string[] ghosts = { "tts", "ai", "merge", "split" };

            foreach (var ghost in ghosts)
            {
                try
                {
                    // İsmi bu olan tüm çalışan programları bul
                    Process[] processes = Process.GetProcessesByName(ghost);

                    foreach (var proc in processes)
                    {
                        // Ve acıma, kapat.
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            proc.WaitForExit(100); // 0.1 saniye emin olmak için bekle
                        }
                    }
                }
                catch
                {
                    // Zaten kapanmışsa veya yetki yoksa hata verme, devam et.
                }
            }
        }
    }
}
using System;
using System.Threading;
using System.Windows.Forms;
using DnsToolbox95.Settings;
using DnsToolbox95.Ui;

namespace DnsToolbox95
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppSettings.Load();

            bool createdNew;
            using (Mutex single = new Mutex(true, @"Local\DnsToolbox95.SingleInstance", out createdNew))
            {
                if (!createdNew && !TryBecomePrimaryInstance(single))
                {
                    Win95MessageBox.ShowInfo(null, "DNS Toolbox 95",
                        "DNS Toolbox 95 is already running.");
                    return 0;
                }

                try
                {
                    Application.Run(new MainForm());
                }
                catch (Exception ex)
                {
                    ShowFatal(ex);
                    return 1;
                }
                finally
                {
                    try { single.ReleaseMutex(); } catch { }
                }
            }

            return 0;
        }

        /// <summary>
        /// Waits for the existing instance to release the single-instance mutex and
        /// takes ownership, so an elevated relaunch can hand over cleanly.
        /// </summary>
        private static bool TryBecomePrimaryInstance(Mutex single)
        {
            const int Attempts = 30;
            const int WaitMs = 200;

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                try
                {
                    if (single.WaitOne(WaitMs, false))
                    {
                        single.ReleaseMutex();
                        single.WaitOne(WaitMs, false);
                        return true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    try { single.ReleaseMutex(); } catch { }
                    single.WaitOne(WaitMs, false);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                Win95MessageBox.ShowError(null, "DNS Toolbox 95",
                    "The utility could not continue because of an unexpected error.",
                    ex.GetType().Name + ": " + ex.Message);
            }
            catch
            {
                // Nothing more can be shown if even the dialog fails.
            }
        }
    }
}
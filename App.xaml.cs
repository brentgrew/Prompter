using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Prompter.Services;

namespace Prompter
{
    public partial class App : Application
    {
        private const string MutexName = "Prompter_SingleInstance_Mutex_99e4e700";
        private const string ActivateMessageName = "PROMPTER_ACTIVATE_INSTANCE_MSG";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        public static uint ActivateWindowMessage { get; private set; }

        private Mutex? _mutex;
        private bool _isSingleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            ActivateWindowMessage = RegisterWindowMessage(ActivateMessageName);

            _mutex = new Mutex(true, MutexName, out _isSingleInstance);
            if (!_isSingleInstance)
            {
                // Another instance is already running -> signal it to bring itself to the front
                if (ActivateWindowMessage != 0)
                {
                    PostMessage(HWND_BROADCAST, ActivateWindowMessage, IntPtr.Zero, IntPtr.Zero);
                }

                // Immediately shut down this second instance
                Shutdown();
                return;
            }

            base.OnStartup(e);
            ThemeService.Instance.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_isSingleInstance && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                }
                catch { }
            }

            base.OnExit(e);
        }
    }
}

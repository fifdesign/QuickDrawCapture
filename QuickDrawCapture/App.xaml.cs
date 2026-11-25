using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace QuickDrawCapture
{
    public partial class App : Application
    {
        const int HOTKEY_ID = 0xD12F;
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
        const uint VK_F12 = 0x7B;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

        Mutex _mutex;
        Forms.NotifyIcon _tray;
        HwndSource _source;
        OverlayWindow _overlay;
        IntPtr _previous;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _mutex = new Mutex(true, "QuickDrawCapture_SingleInstance", out bool created);
            if (!created) { Shutdown(); return; }

            // Hidden message window for the global hotkey: AltGr+F12 == Ctrl+Alt+F12
            var p = new HwndSourceParameters("QDCHotkey") { Width = 0, Height = 0, WindowStyle = 0 };
            p.ParentWindow = new IntPtr(-3); // HWND_MESSAGE
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
            _settings = HotkeySettings.Load();
            bool ok = RegisterHotKey(_source.Handle, HOTKEY_ID, _settings.Modifiers | MOD_NOREPEAT, _settings.Vk);

            _tray = new Forms.NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? SystemIcons.Application,
                Visible = true
            };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Capture now", null, (s, a) => Capture());
            menu.Items.Add("Settings", null, (s, a) => OpenSettings());
            menu.Items.Add("Help", null, (s, a) => OpenHelp());
            menu.Items.Add("Exit", null, (s, a) => Shutdown());
            _tray.ContextMenuStrip = menu;
            UpdateTrayText(ok);
            if (!ok)
                _tray.ShowBalloonTip(4000, "QuickDrawCapture", _settings + " is already used by another program. Change it in Settings.", Forms.ToolTipIcon.Warning);
        }

        HotkeySettings _settings { get => Settings; set => Settings = value; }
        public static HotkeySettings Settings = new HotkeySettings();
        SettingsWindow _settingsWin;
        HelpWindow _helpWin;

        void OpenHelp()
        {
            if (_helpWin != null) { _helpWin.Activate(); return; }
            _helpWin = new HelpWindow(_settings);
            _helpWin.Closed += (s, a) => _helpWin = null;
            _helpWin.Show();
        }

        void UpdateTrayText(bool ok)
        {
            string t = ok ? "QuickDrawCapture - " + _settings : "QuickDrawCapture - hotkey in use!";
            _tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void OpenSettings()
        {
            if (_settingsWin != null) { _settingsWin.Activate(); return; }
            _settingsWin = new SettingsWindow(_settings);
            bool? saved = _settingsWin.ShowDialog();
            var result = _settingsWin.Result;
            _settingsWin = null;
            if (saved != true || result == null) return;

            UnregisterHotKey(_source.Handle, HOTKEY_ID);
            if (RegisterHotKey(_source.Handle, HOTKEY_ID, result.Modifiers | MOD_NOREPEAT, result.Vk))
            {
                _settings = result;
                _settings.Save();
                UpdateTrayText(true);
            }
            else
            {
                // shortcut taken by another program: restore the previous one
                RegisterHotKey(_source.Handle, HOTKEY_ID, _settings.Modifiers | MOD_NOREPEAT, _settings.Vk);
                MessageBox.Show("The shortcut " + result + " is already used by another program.\nThe old shortcut stays active.",
                    "QuickDrawCapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0312 && w.ToInt32() == HOTKEY_ID) { Capture(); handled = true; }
            return IntPtr.Zero;
        }

        void Capture()
        {
            if (_overlay != null) return;
            _previous = GetForegroundWindow();
            var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            var b = screen.Bounds; // physical pixels (per-monitor DPI aware)
            var bmp = new Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
            _overlay = new OverlayWindow(bmp, b);
            _overlay.Closed += (s, a) =>
            {
                _overlay = null;
                bmp.Dispose();
                if (_previous != IntPtr.Zero) SetForegroundWindow(_previous);
            };
            _overlay.Show();
            _overlay.Activate();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_source != null) { UnregisterHotKey(_source.Handle, HOTKEY_ID); _source.Dispose(); }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnExit(e);
        }
    }
}

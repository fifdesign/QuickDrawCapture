using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickDrawCapture
{
    public class HotkeySettings
    {
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8;

        // capture shortcut, default AltGr+F12 == Ctrl+Alt+F12
        public uint Modifiers { get; set; } = MOD_CONTROL | MOD_ALT;
        public uint Vk { get; set; } = 0x7B;

        // confirm (copy to clipboard and close), default Esc and Enter
        public uint Confirm1Mod { get; set; } = 0;
        public uint Confirm1Vk { get; set; } = 0x1B;
        public uint Confirm2Mod { get; set; } = 0;
        public uint Confirm2Vk { get; set; } = 0x0D;

        // crop the copied image around the drawing (smallest circle + margin)
        public bool CropEnabled { get; set; } = true;
        public double CropMarginPercent { get; set; } = 30;

        static string PathFile => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickDrawCapture", "settings.json");

        public static HotkeySettings Load()
        {
            try { return JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(PathFile)) ?? new HotkeySettings(); }
            catch { return new HotkeySettings(); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile));
                File.WriteAllText(PathFile, JsonSerializer.Serialize(this));
            }
            catch { }
        }

        public HotkeySettings Clone() => (HotkeySettings)MemberwiseClone();

        public static string Describe(uint mod, uint vk)
        {
            var parts = new List<string>();
            if ((mod & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mod & MOD_ALT) != 0) parts.Add("Alt");
            if ((mod & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((mod & MOD_WIN) != 0) parts.Add("Win");
            string key = KeyInterop.KeyFromVirtualKey((int)vk).ToString();
            if (key == "Return") key = "Enter";
            if (key == "Escape") key = "Esc";
            parts.Add(key);
            string s = string.Join("+", parts);
            if ((mod & (MOD_CONTROL | MOD_ALT)) == (MOD_CONTROL | MOD_ALT)) s += "  (= AltGr+" + key + ")";
            return s;
        }

        public override string ToString() => Describe(Modifiers, Vk);

        public static uint ModsFromKeyboard()
        {
            uint mod = 0;
            var m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Control) != 0) mod |= MOD_CONTROL;
            if ((m & ModifierKeys.Alt) != 0) mod |= MOD_ALT;
            if ((m & ModifierKeys.Shift) != 0) mod |= MOD_SHIFT;
            if ((m & ModifierKeys.Windows) != 0) mod |= MOD_WIN;
            return mod;
        }

        public static bool IsModifierKey(Key key) =>
            key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin ||
            key == Key.None || key == Key.DeadCharProcessed || key == Key.ImeProcessed;

        public bool IsConfirm(Key key)
        {
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            uint mod = ModsFromKeyboard();
            return (vk == Confirm1Vk && mod == Confirm1Mod) || (vk == Confirm2Vk && mod == Confirm2Mod);
        }
    }

    public class SettingsWindow : Window
    {
        readonly HotkeySettings _pending;
        readonly TextBlock _hint;
        public HotkeySettings Result { get; private set; }

        public SettingsWindow(HotkeySettings current)
        {
            _pending = current.Clone();
            Title = "QuickDrawCapture - Settings";
            Width = 480; MinWidth = 400; MinHeight = 320;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x24));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            var panel = new StackPanel { Margin = new Thickness(18) };
            _hint = new TextBlock
            {
                Text = "Click a field and press the new key combination.", Foreground = Brushes.Gray,
                FontSize = 12, TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_hint);
            AddRow(panel, "Capture shortcut (opens the screenshot)", true,
                () => HotkeySettings.Describe(_pending.Modifiers, _pending.Vk),
                (m, v) => { _pending.Modifiers = m; _pending.Vk = v; });
            AddRow(panel, "Confirm shortcut (copy to clipboard and close)", false,
                () => HotkeySettings.Describe(_pending.Confirm1Mod, _pending.Confirm1Vk),
                (m, v) => { _pending.Confirm1Mod = m; _pending.Confirm1Vk = v; });
            AddRow(panel, "Second confirm shortcut", false,
                () => HotkeySettings.Describe(_pending.Confirm2Mod, _pending.Confirm2Vk),
                (m, v) => { _pending.Confirm2Mod = m; _pending.Confirm2Vk = v; });

            panel.Children.Add(new TextBlock { Text = "Crop to drawing", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) });
            var cropRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var cropCheck = new CheckBox
            {
                Content = "Only copy the area around my drawing", IsChecked = _pending.CropEnabled,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
            };
            cropCheck.Click += (s, e) => { _pending.CropEnabled = cropCheck.IsChecked == true; };
            cropRow.Children.Add(cropCheck);
            panel.Children.Add(cropRow);
            var marginRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            marginRow.Children.Add(new TextBlock { Text = "Margin around the drawing circle:", VerticalAlignment = VerticalAlignment.Center });
            var marginBox = new TextBox
            {
                Text = _pending.CropMarginPercent.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 60,
                Margin = new Thickness(8, 0, 4, 0), Padding = new Thickness(4, 3, 4, 3), HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x30, 0x31, 0x36)), Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x8B, 0xFF))
            };
            marginBox.TextChanged += (s, e) =>
            {
                if (double.TryParse(marginBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    _pending.CropMarginPercent = Math.Max(0, Math.Min(300, v));
            };
            marginRow.Children.Add(marginBox);
            marginRow.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(marginRow);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var save = RoundButton("Save", "#168BFF", "#3AA0FF", "#0F6FD0");
            save.Margin = new Thickness(0, 0, 8, 0);
            save.Click += (s, e) => { Result = _pending; DialogResult = true; };
            var cancel = RoundButton("Cancel", "#3C4043", "#4E5256", "#2E3134");
            cancel.IsCancel = true;
            row.Children.Add(save); row.Children.Add(cancel);
            panel.Children.Add(row);
            Content = panel;
        }

        static Button RoundButton(string text, string normal, string hover, string pressed)
        {
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                "<Border x:Name='Bd' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='" + normal + "' CornerRadius='15'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='" + hover + "'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='Bd' Property='Background' Value='" + pressed + "'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>";
            return new Button
            {
                Content = text, Width = 92, Height = 32, Foreground = Brushes.White, FontSize = 13,
                FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
                Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml)
            };
        }

        void AddRow(StackPanel panel, string label, bool needModifier, Func<string> text, Action<uint, uint> set)
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            var box = new TextBox
            {
                Text = text(), IsReadOnly = true, Margin = new Thickness(0, 5, 0, 0),
                FontSize = 15, Padding = new Thickness(8, 5, 8, 5), IsReadOnlyCaretVisible = false,
                Background = new SolidColorBrush(Color.FromRgb(0x30, 0x31, 0x36)), Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x8B, 0xFF))
            };
            box.PreviewKeyDown += (s, e) =>
            {
                e.Handled = true;
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (HotkeySettings.IsModifierKey(key)) return;
                uint mod = HotkeySettings.ModsFromKeyboard();
                bool fKey = key >= Key.F1 && key <= Key.F24;
                if (needModifier && mod == 0 && !fKey)
                {
                    _hint.Text = "The capture shortcut needs at least one modifier (Ctrl, Alt, Shift, Win) or an F-key.";
                    _hint.Foreground = Brushes.OrangeRed;
                    return;
                }
                set(mod, (uint)KeyInterop.VirtualKeyFromKey(key));
                box.Text = text();
                _hint.Text = "Click Save to apply.";
                _hint.Foreground = Brushes.Gray;
            };
            panel.Children.Add(box);
        }
    }

    public class HelpWindow : Window
    {
        public HelpWindow(HotkeySettings s)
        {
            Title = "QuickDrawCapture - Help";
            Width = 520; Height = 480; MinWidth = 380; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x24));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            string confirm = HotkeySettings.Describe(s.Confirm1Mod, s.Confirm1Vk) + " or " + HotkeySettings.Describe(s.Confirm2Mod, s.Confirm2Vk);
            string text =
                "WHAT IS THIS?\n" +
                "QuickDrawCapture is a tiny, fast screenshot tool. It captures the screen your mouse is on, lets you scribble on it, and puts the result on the clipboard so you can paste it anywhere (chat, e-mail, AI agents).\n\n" +
                "HOW TO USE\n" +
                "1.  Press " + s + " (the capture shortcut). The screen freezes.\n" +
                "2.  Draw with the mouse. Pick a color and a thickness in the toolbar. The toolbar can be dragged.\n" +
                "3.  Press " + confirm + ". The picture with your drawing is copied to the clipboard and the overlay closes. With \"Crop to drawing\" on (default), only the area around your drawing is copied; with no drawing you get the whole screen.\n" +
                "4.  Paste it with Ctrl+V.\n\n" +
                "TOOLBAR\n" +
                "Colors and thicknesses: choose the pen.\n" +
                "Eraser: removes whole strokes you click.\n" +
                "Undo (Ctrl+Z): removes the last stroke.\n" +
                "Green check: copy and close, same as the confirm shortcut.\n" +
                "Red X: close without copying.\n\n" +
                "TRAY ICON (right click)\n" +
                "Capture now: same as the capture shortcut.\n" +
                "Settings: change the capture and confirm shortcuts, switch the crop on or off, and set its margin in percent.\n" +
                "Help: this page.\n" +
                "Exit: quit the program. It keeps running in the background otherwise.\n\n" +
                "CROP TO DRAWING\n" +
                "The program finds the smallest circle that contains everything you drew, adds a margin (30% of the radius by default) and copies the square around that circle. A bigger margin shows more of the surroundings.\n\n" +
                "PRIVACY\n" +
                "Screenshots are never saved to disk and nothing is sent anywhere. Only your settings are stored, in %AppData%\\QuickDrawCapture.\n\n" +
                "ABOUT\n" +
                "QuickDrawCapture, version 0.1\n" +
                "Author: fifdesign, 2026\n" +
                "License: free for everyone. Anyone may use, copy, modify and share it (MIT License).";
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(20), LineHeight = 21 }
            };
        }
    }
}

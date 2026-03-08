#nullable disable
// SoftwareFnLock.cs
// Software FN-Lock for ASUS LAPTOPS

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Win32;

class SoftwareFnLock
{
    // ── Win32 Imports ──
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG lpMsg);
    
    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    const uint MOD_NOREPEAT = 0x4000;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_ALT = 0x0001;
    const uint WM_HOTKEY = 0x0312;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    static uint[] FKeys = { 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A };

    const byte VK_VOLUME_MUTE = 0xAD;
    const byte VK_SNAPSHOT = 0x2C;
    const byte VK_LWIN = 0x5B;
    const byte VK_P = 0x50;
    const byte VK_SLEEP = 0x5F;

    const string FILE_NAME = @"\\.\ATKACPI";
    const uint CONTROL_CODE = 0x0022240C;
    const uint DEVS = 0x53564544;

    const int UniversalControl = 0x00100021;
    const int Brightness_Up = 0x20;
    const int Brightness_Down = 0x10;
    const int KB_Light_Up = 0xC4;
    const int KB_Light_Down = 0xC5;

    static OsdForm currentOsd = null;
    static int currentTickCount = 0;

    static string lockedIconPath = @"C:\Users\Jatin\Desktop\code\FnLockToggle\OSD\Locked.png";
    static string unlockedIconPath = @"C:\Users\Jatin\Desktop\code\FnLockToggle\OSD\Normal.png";

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize,
        ref uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    static IntPtr acpiHandle;

    static void AcpiDeviceSet(int DeviceID, int Status)
    {
        byte[] args = new byte[8];
        BitConverter.GetBytes((uint)DeviceID).CopyTo(args, 0);
        BitConverter.GetBytes((uint)Status).CopyTo(args, 4);

        byte[] acpiBuf = new byte[8 + args.Length];
        byte[] outBuffer = new byte[16];
        BitConverter.GetBytes(DEVS).CopyTo(acpiBuf, 0);
        BitConverter.GetBytes((uint)args.Length).CopyTo(acpiBuf, 4);
        Array.Copy(args, 0, acpiBuf, 8, args.Length);

        uint bytesReturned = 0;
        DeviceIoControl(acpiHandle, CONTROL_CODE, acpiBuf, (uint)acpiBuf.Length,
            outBuffer, (uint)outBuffer.Length, ref bytesReturned, IntPtr.Zero);
    }

    static void KeyPress(byte vk)
    {
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    static void KeyCombo(byte vk1, byte vk2)
    {
        keybd_event(vk1, 0, 0, UIntPtr.Zero);
        keybd_event(vk2, 0, 0, UIntPtr.Zero);
        keybd_event(vk2, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(vk1, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    static bool fnLockActive = false;
    const int TOGGLE_HOTKEY_ID = 100;

    const string AppName = "AsusFunctionLock";
    static NotifyIcon trayIcon;

    static void PerformToggle()
    {
        fnLockActive = !fnLockActive;
        if (fnLockActive)
        {
            for (int i = 0; i < FKeys.Length; i++)
                RegisterHotKey(IntPtr.Zero, i + 1, MOD_NOREPEAT, FKeys[i]);
        }
        else
        {
            for (int i = 0; i < FKeys.Length; i++)
                UnregisterHotKey(IntPtr.Zero, i + 1);
        }
        
        SaveState();
        ShowOSD(fnLockActive);
    }

    static void Main(string[] args)
    {
        acpiHandle = CreateFile(FILE_NAME, 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        bool hasAcpi = acpiHandle != IntPtr.Zero && acpiHandle != new IntPtr(-1);

        RegisterHotKey(IntPtr.Zero, TOGGLE_HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x70);

        LoadState();
        if (fnLockActive)
        {
            for (int i = 0; i < FKeys.Length; i++)
                RegisterHotKey(IntPtr.Zero, i + 1, MOD_NOREPEAT, FKeys[i]);
        }

        SetupTrayIcon();

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);

            if (msg.message == WM_HOTKEY)
            {
                int id = msg.wParam.ToInt32();

                if (id == TOGGLE_HOTKEY_ID)
                {
                    PerformToggle();
                    continue;
                }

                if (!fnLockActive) continue;

                switch (id)
                {
                    case 1: KeyPress(VK_VOLUME_MUTE); break;
                    case 2: if (hasAcpi) AcpiDeviceSet(UniversalControl, KB_Light_Down); break;
                    case 3: if (hasAcpi) AcpiDeviceSet(UniversalControl, KB_Light_Up); break;
                    case 4: if (hasAcpi) AcpiDeviceSet(UniversalControl, 0xB3); break;
                    case 5: if (hasAcpi) AcpiDeviceSet(UniversalControl, 0xAE); break;
                    case 6: KeyPress(VK_SNAPSHOT); break;
                    case 7: if (hasAcpi) AcpiDeviceSet(UniversalControl, Brightness_Down); else AdjustBrightnessFallback(false); break;
                    case 8: if (hasAcpi) AcpiDeviceSet(UniversalControl, Brightness_Up); else AdjustBrightnessFallback(true); break;
                    case 9: KeyCombo(VK_LWIN, VK_P); break;
                    case 10: 
                        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                        keybd_event(0xA2, 0, 0, UIntPtr.Zero); 
                        keybd_event(0x87, 0, 0, UIntPtr.Zero); 
                        Thread.Sleep(50);
                        keybd_event(0x87, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(0xA2, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;
                    case 11: SetSuspendState(false, false, false); break;
                }
            }
        }

        for (int i = 0; i < FKeys.Length; i++) UnregisterHotKey(IntPtr.Zero, i + 1);
        UnregisterHotKey(IntPtr.Zero, TOGGLE_HOTKEY_ID);
        if (hasAcpi) CloseHandle(acpiHandle);
    }

    /* static void AdjustBrightnessFallback(bool up)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell",
                $"-Command \"(Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods).WmiSetBrightness(1, [Math]::Max(0, [Math]::Min(100, (Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightness).CurrentBrightness {(up ? "+ 10" : "- 10")})))\"")
            { CreateNoWindow = true, UseShellExecute = false };
            Process.Start(psi);
        }
        catch { }
    } */

    static void AdjustBrightnessFallback(bool up)
    {
        try
        {
            int currentBrightness = 50;
            
            // 1. Ask Windows for the current brightness level instantly
            using (var searcher = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness"))
            {
                using (var instances = searcher.Get())
                {
                    foreach (System.Management.ManagementObject mo in instances)
                    {
                        currentBrightness = (byte)mo["CurrentBrightness"];
                        break;
                    }
                }
            }

            // 2. Calculate the new brightness (+10 or -10) and clamp it between 0 and 100
            int newBrightness = up ? currentBrightness + 10 : currentBrightness - 10;
            newBrightness = Math.Max(0, Math.Min(100, newBrightness));

            // 3. Send the new brightness level directly back to the Windows display driver
            using (var searcher = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
            {
                using (var instances = searcher.Get())
                {
                    foreach (System.Management.ManagementObject mo in instances)
                    {
                        mo.InvokeMethod("WmiSetBrightness", new object[] { 1, newBrightness });
                        break;
                    }
                }
            }
        }
        catch 
        { 
            // Fails silently if the display driver doesn't support WMI brightness scaling
        }
    }

    // ─── STATE SAVING LOGIC ───
    static void LoadState()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\{AppName}"))
        {
            fnLockActive = ((int)key.GetValue("FnLockState", 0) == 1);
        }
    }

    static void SaveState()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\{AppName}"))
        {
            key.SetValue("FnLockState", fnLockActive ? 1 : 0);
        }
        if (trayIcon != null && trayIcon.ContextMenuStrip != null)
        {
            trayIcon.ContextMenuStrip.Items[0].Text = $"Status: {(fnLockActive ? "ON" : "OFF")}";
        }
    }

    // ─── TASK SCHEDULER (STARTUP) LOGIC ───
    static bool IsStartupEnabled()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks", $"/query /tn \"{AppName}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode == 0; 
            }
        }
        catch { return false; }
    }

    static void ToggleStartup(object sender, EventArgs e)
    {
        bool isEnabled = IsStartupEnabled();
        string args = isEnabled 
            ? $"/delete /tn \"{AppName}\" /f" 
            : $"/create /tn \"{AppName}\" /tr \"\\\"{Application.ExecutablePath}\\\"\" /sc onlogon /rl highest /f";

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                if (p.ExitCode == 0)
                {
                    ((ToolStripMenuItem)sender).Checked = !isEnabled;
                }
            }
        }
        catch { }
    }

    static void UninstallAndExit(object sender, EventArgs e)
    {
        var confirm = MessageBox.Show(
            "This will remove the app from Windows Task Scheduler (Startup), delete all saved settings from the Registry, and close the program. Continue?",
            "Uninstall ASUS Fn-Lock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm == DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo("schtasks", $"/delete /tn \"{AppName}\" /f") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\{AppName}", false);
                MessageBox.Show("Uninstalled successfully. You can safely delete the .exe file after the app closes.", "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not clean completely: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            trayIcon.Dispose();
            Environment.Exit(0);
        }
    }

    static void SetupTrayIcon()
    {
        string actualExePath = Process.GetCurrentProcess().MainModule.FileName;
        trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(actualExePath),
            Text = "ASUS Fn-Lock Controller",
            Visible = true
        };

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"Status: {(fnLockActive ? "ON" : "OFF")}", null, (s, e) => PerformToggle()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Run on Startup", null, ToggleStartup) { Checked = IsStartupEnabled() });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Uninstall / Remove completely", null, UninstallAndExit));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => { trayIcon.Dispose(); Environment.Exit(0); }));

        trayIcon.ContextMenuStrip = menu; 

        trayIcon.DoubleClick += (s, e) =>
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x70, 0, 0, UIntPtr.Zero);
            keybd_event(0x70, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        };
    }

    static void ShowOSD(bool active)
    {
        if (currentOsd != null && currentOsd.IsHandleCreated && !currentOsd.IsDisposed)
        {
            try
            {
                currentOsd.Invoke(new Action(() =>
                {
                    Image oldImg = currentOsd.BackgroundImage;
                    currentOsd.BackgroundImage = Image.FromFile(active ? lockedIconPath : unlockedIconPath);
                    if (oldImg != null) oldImg.Dispose();
                    
                    currentTickCount = 15; 
                    
                    // THE FIX: "Jiggle" the opacity briefly to force Windows to recalculate the transparency!
                    currentOsd.Opacity = 0.89;
                    currentOsd.Opacity = 0.90;     
                }));
                return; 
            }
            catch { }
        }

        Thread t = new Thread(() =>
        {
            currentOsd = new OsdForm
            {
                Size = new Size(240, 240), 
                BackColor = Color.Black, 
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                ShowInTaskbar = false,
                Opacity = 0.0,
                BackgroundImage = Image.FromFile(active ? lockedIconPath : unlockedIconPath),
                BackgroundImageLayout = ImageLayout.Zoom 
            };

            currentOsd.HandleCreated += (s, e) =>
            {
                int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                int DWMWCP_ROUND = 2; 
                DwmSetWindowAttribute(currentOsd.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, sizeof(int));
            };

            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            currentOsd.Location = new Point((screen.Width - currentOsd.Width) / 2, screen.Bottom - currentOsd.Height - 80);

            System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer { Interval = 15 };
            currentTickCount = 0;

            animTimer.Tick += (sender, args) =>
            {
                if (currentTickCount < 15) { currentOsd.Opacity += 0.06; } 
                else if (currentTickCount > 100 && currentTickCount < 115) { currentOsd.Opacity -= 0.06; } 
                else if (currentTickCount >= 115) 
                { 
                    animTimer.Stop(); 
                    currentOsd.Close(); 
                    Application.ExitThread(); 
                }
                currentTickCount++;
            };

            currentOsd.FormClosed += (s, e) => { currentOsd = null; };

            currentOsd.Load += (sender, args) => animTimer.Start();
            Application.Run(currentOsd);
        });

        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }
}

// Custom Form to enable DoubleBuffered (which is normally protected)
class OsdForm : Form
{
    public OsdForm()
    {
        this.DoubleBuffered = true;
    }

}

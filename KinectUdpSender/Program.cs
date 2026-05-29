using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace KinectUdpSender
{
    internal static class Program
    {
        private const string DefaultIp = "127.0.0.1";
        private const int DefaultPort = 5052;

        private static KinectSender sender;

        // Win32 console-mode plumbing used to disable QuickEdit. When QuickEdit is
        // on (the default), clicking inside the console window selects text and
        // BLOCKS every Console.Write until the user presses Esc/Enter — which
        // would also block the Kinect frame handler and stall UDP transmission.
        private const int STD_INPUT_HANDLE = -10;
        private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
        private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
        private const uint ENABLE_MOUSE_INPUT = 0x0010;
        private const uint ENABLE_INSERT_MODE = 0x0020;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        private static void DisableConsoleQuickEdit()
        {
            try
            {
                IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;

                uint mode;
                if (!GetConsoleMode(handle, out mode)) return;

                // Must set ENABLE_EXTENDED_FLAGS in the same call as clearing
                // QuickEdit, otherwise the change is ignored.
                mode &= ~(ENABLE_QUICK_EDIT_MODE | ENABLE_MOUSE_INPUT | ENABLE_INSERT_MODE);
                mode |= ENABLE_EXTENDED_FLAGS;
                SetConsoleMode(handle, mode);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Warning: could not disable console QuickEdit: " + ex.Message);
            }
        }

        // Usage:
        //   KinectUdpSender.exe                       (default 127.0.0.1:5052, verbose)
        //   KinectUdpSender.exe --port=5060           (override port)
        //   KinectUdpSender.exe --ip=192.168.1.50     (override ip)
        //   KinectUdpSender.exe --quiet               (no per-frame console output)
        private static void Main(string[] args)
        {
            string ip = DefaultIp;
            int port = DefaultPort;
            bool verbose = true;

            foreach (string arg in args)
            {
                if (arg.StartsWith("--ip=", StringComparison.OrdinalIgnoreCase))
                {
                    ip = arg.Substring("--ip=".Length);
                }
                else if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (int.TryParse(arg.Substring("--port=".Length), NumberStyles.Integer,
                                     CultureInfo.InvariantCulture, out parsed))
                    {
                        port = parsed;
                    }
                }
                else if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase))
                {
                    verbose = false;
                }
            }

            DisableConsoleQuickEdit();

            Console.WriteLine("Kinect v2 UDP sender starting...");
            Console.WriteLine("Sending body joint data to " + ip + ":" + port +
                              " (up to " + KinectSender.MaxPlayers + " players)");
            Console.WriteLine("Press Enter or Ctrl+C to stop.");

            using (sender = new KinectSender(ip, port, verbose))
            {
                Console.CancelKeyPress += OnCancelKeyPress;

                sender.Start();
                Console.ReadLine();
            }
        }

        private static void OnCancelKeyPress(object senderObject, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            if (sender != null)
            {
                sender.Dispose();
                sender = null;
            }

            Environment.Exit(0);
        }
    }
}

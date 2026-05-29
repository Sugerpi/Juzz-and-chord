using System;
using System.Globalization;

namespace KinectUdpSender
{
    internal static class Program
    {
        private const string DefaultIp = "127.0.0.1";
        private const int DefaultPort = 5052;

        private static KinectSender sender;

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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using Microsoft.Kinect;

namespace KinectUdpSender
{
    /// <summary>
    /// Reads Kinect v2 body data and ships up to 2 players over UDP as JSON.
    ///
    /// Design: this sender is a DUMB PIPE. It does NOT interpret gestures, smooth
    /// joints, or decide which player is "drummer" vs "chord player". All of that
    /// lives in Unity, where you have an Inspector, Time.deltaTime, and hot reload.
    /// The sender only:
    ///   1. Reads raw joint positions + Kinect-native HandState
    ///   2. Assigns each tracked body to a stable slot (0 or 1) by TrackingId
    ///   3. Serializes both slots into ONE JSON packet and UDP-sends it
    ///
    /// Slot stability: if Player 0 leaves the frame and comes back, the TrackingId
    /// changes, so they may land in a different slot than before — but they will
    /// not steal the slot of someone still being tracked. A slot is freed only
    /// when its TrackingId stops appearing in the frame entirely.
    ///
    /// Wire format (one packet per Kinect frame, ~30 Hz):
    ///   { "players": [
    ///       { "slot":0, "tracked":true,
    ///         "handLeft":{"x":..,"y":..,"z":..}, "handRight":..., "footLeft":...,
    ///         "footRight":..., "shoulderLeft":..., "shoulderRight":...,
    ///         "spineMid":..., "spineBase":...,
    ///         "handLeftState":"Open|Closed|Lasso|Unknown|NotTracked",
    ///         "handRightState":"..." },
    ///       { "slot":1, "tracked":false, ... zeros ... }
    ///   ]}
    /// </summary>
    public sealed class KinectSender : IDisposable
    {
        public const int MaxPlayers = 2;

        private readonly UdpClient udpClient;
        private readonly IPEndPoint targetEndPoint;
        private readonly bool printJsonToConsole;
        private readonly Stopwatch consoleLogTimer = new Stopwatch();

        private KinectSensor sensor;
        private BodyFrameReader bodyFrameReader;
        private Body[] bodies;
        private bool disposed;
        private int frameCount;

        // Stable slot assignment. A TrackingId stays mapped to the same slot for
        // as long as that body keeps being tracked. When it disappears, the slot
        // is released and the next new body can take it.
        private readonly Dictionary<ulong, int> trackingIdToSlot = new Dictionary<ulong, int>();
        private readonly Body[] slotBodies = new Body[MaxPlayers];

        // Reused per frame to avoid per-frame allocations on the Kinect event thread.
        private readonly HashSet<ulong> currentIds = new HashSet<ulong>();
        private readonly List<Body> unassigned = new List<Body>(MaxPlayers);
        private readonly List<ulong> toRemove = new List<ulong>(MaxPlayers);
        private readonly StringBuilder jsonBuilder = new StringBuilder(1024);

        // Windows-only ioctl that disables the WSAECONNRESET behavior on UDP sockets.
        // Without this, when the receiver isn't listening, the OS surfaces the ICMP
        // "port unreachable" as a SocketException on the NEXT Send(), which would
        // otherwise propagate out of the FrameArrived handler and silently kill the
        // reader thread.
        private const int SIO_UDP_CONNRESET = -1744830452; // unchecked((int)0x9800000C)

        public KinectSender(string targetIp, int targetPort, bool printJsonToConsole)
        {
            udpClient = new UdpClient();
            try
            {
                udpClient.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception ex)
            {
                // Non-Windows or unsupported — log and continue; the try/catch in
                // OnBodyFrameArrived is the backup defense.
                Console.WriteLine("Warning: could not disable SIO_UDP_CONNRESET: " + ex.Message);
            }

            targetEndPoint = new IPEndPoint(IPAddress.Parse(targetIp), targetPort);
            this.printJsonToConsole = printJsonToConsole;
            consoleLogTimer.Start();
        }

        public void Start()
        {
            ThrowIfDisposed();

            sensor = KinectSensor.GetDefault();
            if (sensor == null)
            {
                throw new InvalidOperationException("No Kinect v2 sensor was found.");
            }

            sensor.IsAvailableChanged += OnSensorIsAvailableChanged;

            bodyFrameReader = sensor.BodyFrameSource.OpenReader();
            bodyFrameReader.FrameArrived += OnBodyFrameArrived;

            bodies = new Body[sensor.BodyFrameSource.BodyCount];

            if (!sensor.IsOpen)
            {
                sensor.Open();
            }

            Console.WriteLine("Kinect sensor opened. Tracking up to " + MaxPlayers + " players.");
            Console.WriteLine("Sensor available: " + sensor.IsAvailable);
        }

        private void OnSensorIsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            Console.WriteLine("Sensor available: " + e.IsAvailable);
        }

        private void OnBodyFrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            if (disposed)
            {
                return;
            }

            // CRITICAL: this entire method runs on a Kinect SDK background thread.
            // Any exception that escapes here will tear down that thread and the
            // FrameArrived event will never fire again — UDP traffic silently stops
            // while the process keeps "running". Swallow everything and keep going.
            try
            {
                using (BodyFrame frame = e.FrameReference.AcquireFrame())
                {
                    if (frame == null)
                    {
                        return;
                    }

                    frame.GetAndRefreshBodyData(bodies);
                    frameCount++;

                    AssignSlots();

                    string json = BuildJson();
                    byte[] data = Encoding.UTF8.GetBytes(json);

                    try
                    {
                        udpClient.Send(data, data.Length, targetEndPoint);
                    }
                    catch (SocketException sx)
                    {
                        // Transient: receiver gone, network change, etc. Drop this
                        // frame and try again on the next one.
                        if (consoleLogTimer.ElapsedMilliseconds >= 500)
                        {
                            Console.WriteLine("UDP send failed (frame=" + frameCount +
                                              "): " + sx.SocketErrorCode + " " + sx.Message);
                        }
                    }

                    if (printJsonToConsole && consoleLogTimer.ElapsedMilliseconds >= 500)
                    {
                        int trackedCount = 0;
                        for (int i = 0; i < MaxPlayers; i++) if (slotBodies[i] != null) trackedCount++;
                        Console.WriteLine(
                            "frame=" + frameCount +
                            " tracked=" + trackedCount + "/" + MaxPlayers +
                            " bytes=" + data.Length +
                            " json=" + json);
                        consoleLogTimer.Restart();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Frame handler error (frame=" + frameCount +
                                  "): " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Refresh slotBodies[] based on this frame's tracked bodies, keeping the
        // TrackingId -> slot mapping stable across frames.
        private void AssignSlots()
        {
            for (int i = 0; i < MaxPlayers; i++) slotBodies[i] = null;
            currentIds.Clear();
            unassigned.Clear();
            toRemove.Clear();

            // 1) Place tracked bodies whose TrackingId we've already seen back in
            //    their slot. Collect first-time arrivals for step 3.
            foreach (Body body in bodies)
            {
                if (body == null || !body.IsTracked) continue;
                currentIds.Add(body.TrackingId);

                int slot;
                if (trackingIdToSlot.TryGetValue(body.TrackingId, out slot) &&
                    slot >= 0 && slot < MaxPlayers)
                {
                    slotBodies[slot] = body;
                }
                else
                {
                    unassigned.Add(body);
                }
            }

            // 2) Release slots whose TrackingIds no longer appear this frame.
            foreach (KeyValuePair<ulong, int> kv in trackingIdToSlot)
            {
                if (!currentIds.Contains(kv.Key)) toRemove.Add(kv.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                ulong id = toRemove[i];
                int releasedSlot = trackingIdToSlot[id];
                trackingIdToSlot.Remove(id);
                Console.WriteLine("TRACKING LOST | slot=" + releasedSlot + " trackingId=" + id);
            }

            // 3) Assign first-time bodies to free slots. 3rd+ tracked body is
            //    silently ignored until a slot frees up.
            for (int i = 0; i < unassigned.Count; i++)
            {
                Body body = unassigned[i];
                for (int slot = 0; slot < MaxPlayers; slot++)
                {
                    if (slotBodies[slot] == null)
                    {
                        slotBodies[slot] = body;
                        trackingIdToSlot[body.TrackingId] = slot;
                        Console.WriteLine(
                            "TRACKING ACQUIRED | slot=" + slot +
                            " trackingId=" + body.TrackingId);
                        break;
                    }
                }
            }
        }

        private string BuildJson()
        {
            jsonBuilder.Length = 0;
            jsonBuilder.Append("{\"players\":[");
            for (int slot = 0; slot < MaxPlayers; slot++)
            {
                if (slot > 0) jsonBuilder.Append(',');
                AppendPlayer(jsonBuilder, slot, slotBodies[slot]);
            }
            jsonBuilder.Append("]}");
            return jsonBuilder.ToString();
        }

        private static void AppendPlayer(StringBuilder sb, int slot, Body body)
        {
            sb.Append("{\"slot\":").Append(slot);
            if (body == null)
            {
                sb.Append(",\"tracked\":false");
                AppendZeroJoint(sb, "handLeft");
                AppendZeroJoint(sb, "handRight");
                AppendZeroJoint(sb, "footLeft");
                AppendZeroJoint(sb, "footRight");
                AppendZeroJoint(sb, "shoulderLeft");
                AppendZeroJoint(sb, "shoulderRight");
                AppendZeroJoint(sb, "spineMid");
                AppendZeroJoint(sb, "spineBase");
                sb.Append(",\"handLeftState\":\"NotTracked\"");
                sb.Append(",\"handRightState\":\"NotTracked\"");
            }
            else
            {
                sb.Append(",\"tracked\":true");
                AppendJoint(sb, "handLeft",      body.Joints[JointType.HandLeft].Position);
                AppendJoint(sb, "handRight",     body.Joints[JointType.HandRight].Position);
                AppendJoint(sb, "footLeft",      body.Joints[JointType.FootLeft].Position);
                AppendJoint(sb, "footRight",     body.Joints[JointType.FootRight].Position);
                AppendJoint(sb, "shoulderLeft",  body.Joints[JointType.ShoulderLeft].Position);
                AppendJoint(sb, "shoulderRight", body.Joints[JointType.ShoulderRight].Position);
                AppendJoint(sb, "spineMid",      body.Joints[JointType.SpineMid].Position);
                AppendJoint(sb, "spineBase",     body.Joints[JointType.SpineBase].Position);
                sb.Append(",\"handLeftState\":\"").Append(body.HandLeftState).Append('"');
                sb.Append(",\"handRightState\":\"").Append(body.HandRightState).Append('"');
            }
            sb.Append('}');
        }

        private static void AppendJoint(StringBuilder sb, string name, CameraSpacePoint p)
        {
            sb.Append(",\"").Append(name).Append("\":{\"x\":");
            sb.Append(p.X.ToString("G7", CultureInfo.InvariantCulture));
            sb.Append(",\"y\":");
            sb.Append(p.Y.ToString("G7", CultureInfo.InvariantCulture));
            sb.Append(",\"z\":");
            sb.Append(p.Z.ToString("G7", CultureInfo.InvariantCulture));
            sb.Append('}');
        }

        private static void AppendZeroJoint(StringBuilder sb, string name)
        {
            sb.Append(",\"").Append(name).Append("\":{\"x\":0,\"y\":0,\"z\":0}");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("KinectSender");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (bodyFrameReader != null)
            {
                bodyFrameReader.FrameArrived -= OnBodyFrameArrived;
                bodyFrameReader.Dispose();
                bodyFrameReader = null;
            }

            if (sensor != null)
            {
                sensor.IsAvailableChanged -= OnSensorIsAvailableChanged;

                if (sensor.IsOpen)
                {
                    sensor.Close();
                }

                sensor = null;
            }

            udpClient.Close();
            udpClient.Dispose();
        }
    }
}

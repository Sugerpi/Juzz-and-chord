using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Kinect;

namespace KinectUdpSender
{
    /// <summary>
    /// Reads Kinect v2 body data and ships up to 2 players over UDP as a
    /// fixed-layout BINARY packet (little-endian). The previous JSON wire
    /// format caused per-frame GC on the Unity side and had no way to detect
    /// dropped / reordered packets — both showed up as jitter and double-hits
    /// in the drum app. This format fixes that.
    ///
    /// Design: this sender is a DUMB PIPE. It does NOT interpret gestures,
    /// smooth joints, or decide which player is "drummer" vs "chord player".
    /// All of that lives in Unity. The sender only:
    ///   1. Reads raw joint positions + Kinect-native HandState
    ///   2. Assigns each tracked body to a stable slot (0 or 1). When BOTH
    ///      slots have to be filled in the same frame, the leftmost body
    ///      (smallest spineBase.X) takes slot 0. This stops the drummer and
    ///      chord player from swapping roles after they both leave + return.
    ///   3. Packs everything into ONE 219-byte UDP datagram and sends it.
    ///
    /// =========================================================================
    /// WIRE FORMAT v1  (all little-endian, total 219 bytes for MaxPlayers=2)
    /// =========================================================================
    ///   offset  size  field
    ///   ------  ----  ------------------------------------------------------
    ///   0       4     magic       = 'K','I','N','E' (bytes 0x4B 0x49 0x4E 0x45)
    ///   4       2     version     = 1 (uint16)
    ///   6       1     playerCount (uint8) — number of player blocks that follow
    ///   7       4     seq         (uint32) — monotonic, wraps; receiver uses
    ///                              signed diff to handle wrap correctly
    ///   11      8     tMs         (uint64) — Stopwatch elapsed ms since sender
    ///                              start. Use as "time the frame was captured"
    ///                              on the receiver for timing-sensitive logic.
    ///   19      ...   player block × playerCount
    ///
    /// Per-player block (100 bytes):
    ///   +0      1     slot              (uint8)
    ///   +1      1     tracked           (uint8: 0=false, 1=true)
    ///   +2      1     handLeftState     (uint8: Microsoft.Kinect.HandState
    ///                                    numeric value — 0=Unknown,
    ///                                    1=NotTracked, 2=Open, 3=Closed,
    ///                                    4=Lasso. Receiver translates.)
    ///   +3      1     handRightState    (uint8, same encoding)
    ///   +4      96    8 joints × (float32 x, y, z), order:
    ///                    HandLeft, HandRight, FootLeft, FootRight,
    ///                    ShoulderLeft, ShoulderRight, SpineMid, SpineBase
    ///                 — same order on receiver. NOTE: when tracked=0 these
    ///                 are zero-filled; receiver should hold previous value
    ///                 to avoid limbs snapping to origin.
    /// =========================================================================
    /// </summary>
    public sealed class KinectSender : IDisposable
    {
        public const int MaxPlayers = 2;

        // Wire format constants — keep in sync with the receiver. If you bump
        // anything, increment WireVersion and update the spec comment above.
        private const ushort WireVersion = 1;
        private const int HeaderBytes = 4 + 2 + 1 + 4 + 8;          // 19
        private const int JointsPerPlayer = 8;
        private const int PerPlayerBytes = 4 + JointsPerPlayer * 3 * 4; // 100
        public const int PacketBytes = HeaderBytes + MaxPlayers * PerPlayerBytes; // 219

        private readonly UdpClient udpClient;
        private readonly IPEndPoint targetEndPoint;
        private readonly bool printJsonToConsole;
        private readonly Stopwatch consoleLogTimer = new Stopwatch();
        // Stopwatch readings on the wire let the Unity side know "when did the
        // Kinect actually see this frame", independent of OS clock skew or
        // receive-time jitter.
        private readonly Stopwatch sinceStart = Stopwatch.StartNew();

        private KinectSensor sensor;
        private BodyFrameReader bodyFrameReader;
        private Body[] bodies;
        private bool disposed;
        private int frameCount;
        private uint sequenceCounter;
        private int sendFailuresSinceLastLog;

        // Stable slot assignment. A TrackingId stays mapped to the same slot for
        // as long as that body keeps being tracked. When it disappears, the slot
        // is released and the next new body can take it — picked by station
        // (leftmost spineBase.X wins the lowest free slot).
        private readonly Dictionary<ulong, int> trackingIdToSlot = new Dictionary<ulong, int>();
        private readonly Body[] slotBodies = new Body[MaxPlayers];

        // Reused per frame to avoid per-frame allocations on the Kinect event thread.
        private readonly HashSet<ulong> currentIds = new HashSet<ulong>();
        private readonly List<Body> unassigned = new List<Body>(MaxPlayers);
        private readonly List<ulong> toRemove = new List<ulong>(MaxPlayers);
        private readonly byte[] sendBuffer = new byte[PacketBytes];
        // Cached so List.Sort doesn't allocate a new comparer wrapper per frame.
        private static readonly Comparison<Body> CompareByStationX = (a, b) =>
        {
            float ax = a.Joints[JointType.SpineBase].Position.X;
            float bx = b.Joints[JointType.SpineBase].Position.X;
            return ax.CompareTo(bx);
        };

        // Used to write floats as raw IEEE-754 bytes without allocating a
        // BitConverter byte[] each call (and without /unsafe).
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float F;
            [FieldOffset(0)] public uint U;
        }

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
            Console.WriteLine("Wire format: binary v" + WireVersion + ", " + PacketBytes + " bytes/packet.");
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
                    int len = BuildBinary();

                    try
                    {
                        udpClient.Send(sendBuffer, len, targetEndPoint);
                    }
                    catch (SocketException sx)
                    {
                        // Transient: receiver gone, network change, etc. Drop this
                        // frame and try again on the next one. Rate-limit the log
                        // so a long absence doesn't spam the console.
                        sendFailuresSinceLastLog++;
                        if (consoleLogTimer.ElapsedMilliseconds >= 500)
                        {
                            Console.WriteLine("UDP send failed (frame=" + frameCount +
                                              ", failures since last log=" + sendFailuresSinceLastLog +
                                              "): " + sx.SocketErrorCode + " " + sx.Message);
                            sendFailuresSinceLastLog = 0;
                        }
                    }

                    if (printJsonToConsole && consoleLogTimer.ElapsedMilliseconds >= 500)
                    {
                        int trackedCount = 0;
                        for (int i = 0; i < MaxPlayers; i++) if (slotBodies[i] != null) trackedCount++;
                        Console.WriteLine(
                            "frame=" + frameCount +
                            " seq=" + sequenceCounter +
                            " tMs=" + sinceStart.ElapsedMilliseconds +
                            " tracked=" + trackedCount + "/" + MaxPlayers +
                            " bytes=" + len);
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
        // TrackingId -> slot mapping stable across frames. First-time bodies are
        // assigned to free slots in left-to-right order by spineBase.X, so the
        // person standing on the left side of the Kinect's view always lands in
        // the lowest free slot — drummer / chord-player roles stay put across
        // simultaneous tracking losses.
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

            // 3) Sort unassigned bodies by station (spineBase.X) so the leftmost
            //    person consistently takes the lowest free slot. Then assign.
            //    3rd+ tracked body is silently ignored until a slot frees up.
            if (unassigned.Count > 1)
            {
                unassigned.Sort(CompareByStationX);
            }
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
                            " trackingId=" + body.TrackingId +
                            " spineBaseX=" + body.Joints[JointType.SpineBase].Position.X.ToString("F2"));
                        break;
                    }
                }
            }
        }

        // ---------- Binary packet writer (zero-alloc on the hot path) ----------

        private int BuildBinary()
        {
            int o = 0;

            // Magic: 'K','I','N','E' literally as bytes (so little-endian read
            // yields 0x454E494B). Easier to recognize in Wireshark too.
            sendBuffer[o++] = (byte)'K';
            sendBuffer[o++] = (byte)'I';
            sendBuffer[o++] = (byte)'N';
            sendBuffer[o++] = (byte)'E';

            WriteU16(o, WireVersion); o += 2;
            sendBuffer[o++] = (byte)MaxPlayers;

            unchecked { sequenceCounter++; }
            WriteU32(o, sequenceCounter); o += 4;
            WriteU64(o, (ulong)sinceStart.ElapsedMilliseconds); o += 8;

            for (int slot = 0; slot < MaxPlayers; slot++)
            {
                Body body = slotBodies[slot];

                sendBuffer[o++] = (byte)slot;
                sendBuffer[o++] = body != null ? (byte)1 : (byte)0;
                // Microsoft.Kinect.HandState's underlying integer maps directly
                // (Unknown=0, NotTracked=1, Open=2, Closed=3, Lasso=4).
                // When the slot has no body, write NotTracked (1) explicitly so
                // the receiver's hand-tracked flag stays consistent with the
                // previous JSON behavior.
                sendBuffer[o++] = body != null ? (byte)body.HandLeftState  : (byte)HandState.NotTracked;
                sendBuffer[o++] = body != null ? (byte)body.HandRightState : (byte)HandState.NotTracked;

                if (body != null)
                {
                    o = WriteJoint(o, body.Joints[JointType.HandLeft].Position);
                    o = WriteJoint(o, body.Joints[JointType.HandRight].Position);
                    o = WriteJoint(o, body.Joints[JointType.FootLeft].Position);
                    o = WriteJoint(o, body.Joints[JointType.FootRight].Position);
                    o = WriteJoint(o, body.Joints[JointType.ShoulderLeft].Position);
                    o = WriteJoint(o, body.Joints[JointType.ShoulderRight].Position);
                    o = WriteJoint(o, body.Joints[JointType.SpineMid].Position);
                    o = WriteJoint(o, body.Joints[JointType.SpineBase].Position);
                }
                else
                {
                    Array.Clear(sendBuffer, o, JointsPerPlayer * 3 * 4);
                    o += JointsPerPlayer * 3 * 4;
                }
            }

            return o;
        }

        private void WriteU16(int off, ushort v)
        {
            sendBuffer[off + 0] = (byte)v;
            sendBuffer[off + 1] = (byte)(v >> 8);
        }

        private void WriteU32(int off, uint v)
        {
            sendBuffer[off + 0] = (byte)v;
            sendBuffer[off + 1] = (byte)(v >> 8);
            sendBuffer[off + 2] = (byte)(v >> 16);
            sendBuffer[off + 3] = (byte)(v >> 24);
        }

        private void WriteU64(int off, ulong v)
        {
            sendBuffer[off + 0] = (byte)v;
            sendBuffer[off + 1] = (byte)(v >> 8);
            sendBuffer[off + 2] = (byte)(v >> 16);
            sendBuffer[off + 3] = (byte)(v >> 24);
            sendBuffer[off + 4] = (byte)(v >> 32);
            sendBuffer[off + 5] = (byte)(v >> 40);
            sendBuffer[off + 6] = (byte)(v >> 48);
            sendBuffer[off + 7] = (byte)(v >> 56);
        }

        private int WriteJoint(int off, CameraSpacePoint p)
        {
            FloatBits fb;
            fb.U = 0; fb.F = p.X; WriteU32(off + 0, fb.U);
            fb.U = 0; fb.F = p.Y; WriteU32(off + 4, fb.U);
            fb.U = 0; fb.F = p.Z; WriteU32(off + 8, fb.U);
            return off + 12;
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

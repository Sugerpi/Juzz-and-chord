using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

/// <summary>
/// 接 KinectUdpSender 送來的 UDP 封包，每幀內含最多兩位玩家的骨架資料。
///
/// 對外提供兩種介面：
/// 1. 把每位玩家的肢體位置寫到 Inspector 上指派的 Transform — 給鼓的 trigger collider 用
/// 2. 公開 <see cref="LatestData"/> 屬性 — 給其他系統 (例如和弦輸入橋接) 直接拉資料用
///
/// 設計重點：這個 Receiver 不知道也不關心「誰是鼓手、誰是和弦手」，它只負責把資料攤平
/// 給場景上的物件。角色分配在別處 (鼓的 Transform 通常綁 Player 0、和弦的 InputProvider 通常讀 Player 1)。
///
/// === Wire format ===
/// 從 v1 起 Sender 改送固定長度 binary 封包 (219 bytes)，取代原本的 JSON。
/// 完整規格寫在 <c>KinectUdpSender/KinectSender.cs</c> 上方。重點：
///   - 4B magic "KINE" + 2B version + 1B playerCount + 4B seq + 8B tMs
///   - 每位玩家 100 bytes：4B (slot/tracked/handLeftState/handRightState) +
///     8 個關節 × (float x,y,z)
/// 改 binary 的動機是 (a) 解掉每幀 JsonUtility.FromJson 造成的 GC 抖動，
/// (b) 加上 seq/tMs 讓 timing 與掉包都可被觀察到。
/// </summary>
public class KinectUdpReceiver : MonoBehaviour
{
    public const int MaxPlayers = 2;

    // 必須跟 KinectSender 對齊。如果改 sender 的 wire format，記得這裡同步。
    private const ushort WireVersion = 1;
    private const int HeaderBytes = 4 + 2 + 1 + 4 + 8;          // 19
    private const int JointsPerPlayer = 8;
    private const int PerPlayerBytes = 4 + JointsPerPlayer * 3 * 4; // 100
    private const int MaxPacketBytes = HeaderBytes + MaxPlayers * PerPlayerBytes; // 219
    // UDP MTU 給點 headroom，避免之後 sender 增加欄位時 receiver 沒跟上就 truncate。
    private const int RecvBufferBytes = 1500;

    [Header("UDP 設定")]
    [Tooltip("要跟 KinectUdpSender 的 --port 一致，預設 5052")]
    public int port = 5052;

    [Header("玩家肢體綁定 (給 trigger collider 用)")]
    [Tooltip("陣列長度固定為 2。Player 0 通常是鼓手，Player 1 通常是和弦手。" +
             "和弦手如果不用碰撞偵測，可以全部留空。")]
    public PlayerTransforms[] players = new PlayerTransforms[MaxPlayers]
    {
        new PlayerTransforms(),
        new PlayerTransforms(),
    };

    [Header("座標轉換：Kinect 公尺 → Unity 世界")]
    [Tooltip("整體縮放。1 = 一公尺對一單位，動作要更明顯可以放大")]
    public float scale = 1f;
    [Tooltip("世界座標偏移，用來把整個身體擺到鼓組前的正確位置")]
    public Vector3 worldOffset = Vector3.zero;
    [Tooltip("翻轉 X 軸 (鏡像左右手)。如果動作跟畫面相反就打開")]
    public bool flipX = false;
    [Tooltip("翻轉 Z 軸。Kinect 的 z 是「離鏡頭距離」，多數情況要翻成 Unity 的「向前」")]
    public bool flipZ = true;

    [Header("平滑 (Smoothing)")]
    [Tooltip("0 = 不平滑、直接套用；越大越平滑但延遲越高。建議 10~25")]
    public float smoothing = 15f;

    [Header("除錯")]
    public bool logIncoming = false;
    [Tooltip("每秒印一次封包統計 (收到 / 消化 / 掉包 / 格式錯)")]
    public bool logStats = false;

    [Header("Gizmo (Scene 視窗預覽)")]
    public bool drawGizmos = true;
    public float kinectNearMeters = 0.5f;
    public float kinectFarMeters = 4.5f;
    public float kinectHFovDeg = 70f;
    public float kinectVFovDeg = 60f;

    /// <summary>
    /// 解析過後、座標已轉到 Unity 世界空間的每個玩家最新資料。
    /// 陣列長度永遠是 <see cref="MaxPlayers"/>，沒在追的玩家 <see cref="PlayerData.tracked"/> = false。
    /// 主執行緒 Update 中安全讀取；其他執行緒請勿存取。
    /// </summary>
    public PlayerData[] LatestData { get; private set; }

    /// <summary>最近一個被主執行緒消化的封包的 sender 端 seq (uint32, 會 wrap)。</summary>
    public uint LatestPacketSeq { get; private set; }
    /// <summary>最近一個封包在 sender 端的 Stopwatch 時間 (ms)。用來做 timing-sensitive 對齊。</summary>
    public long LatestPacketKinectTimeMs { get; private set; }

    private Socket socket;
    private Thread receiveThread;
    private volatile bool running;
    private EndPoint anyEndPoint = new IPEndPoint(IPAddress.Any, 0);

    // 三個固定 buffer：bg 收 → 主執行緒讀。零 allocation 在熱路徑上。
    private readonly byte[] bgRecvBuffer = new byte[RecvBufferBytes];
    private readonly byte[] consumeBuffer = new byte[RecvBufferBytes];
    private readonly byte[] parseBuffer  = new byte[RecvBufferBytes];

    // 0 = 沒有待消化的封包；非 0 = bg 寫過、主執行緒還沒拿走。
    private int consumeLength;
    private readonly object consumeLock = new object();

    // bg 端紀錄。為了 cross-thread 讀寫安全用 Interlocked。
    private long bgPacketsReceived;     // 通過格式檢查的封包數
    private long bgInvalidPackets;      // 長度太短 / magic 錯 / version 錯
    private long bgPacketsDropped;      // bg 看到的 seq gap (網路掉包)
    private long bgOutOfOrderDiscarded; // seq <= 已收最大 (UDP 偶爾會亂序)
    private uint bgLastReceivedSeq;     // bg 看過最大 seq (用來算 gap)
    private bool bgHasLastSeq;

    private long mainPacketsConsumed;
    private float nextStatsLogTime;

    // 解 binary 時用來把 4 bytes 重新組回 float 而不走 BitConverter 配置 byte[]。
    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)] public float F;
        [FieldOffset(0)] public uint U;
    }

    // ---------- Inspector / public-data 型別 ----------

    [Serializable]
    public class PlayerTransforms
    {
        public Transform handLeft;
        public Transform handRight;
        public Transform footLeft;
        public Transform footRight;
        [Tooltip("非必填。和弦手需要這些做骨架顯示時才指派。")]
        public Transform shoulderLeft;
        public Transform shoulderRight;
        public Transform spineMid;
        public Transform spineBase;
    }

    /// <summary>SDK-agnostic hand gesture，對應 Microsoft.Kinect.HandState。</summary>
    public enum HandState
    {
        Unknown = 0,
        Open = 1,
        Closed = 2,
        Lasso = 3,
        NotTracked = 4,
    }

    public class PlayerData
    {
        public int slot;
        public bool tracked;
        public Vector3 handLeft, handRight, footLeft, footRight;
        public Vector3 shoulderLeft, shoulderRight, spineMid, spineBase;
        public HandState handLeftState = HandState.Unknown;
        public HandState handRightState = HandState.Unknown;
    }

    // ---------- 生命週期 ----------

    void Awake()
    {
        LatestData = new PlayerData[MaxPlayers];
        for (int i = 0; i < MaxPlayers; i++) LatestData[i] = new PlayerData { slot = i };

        // 確保 Inspector 陣列長度永遠對得上 MaxPlayers
        if (players == null || players.Length != MaxPlayers)
        {
            var fixedArr = new PlayerTransforms[MaxPlayers];
            for (int i = 0; i < MaxPlayers; i++)
            {
                fixedArr[i] = (players != null && i < players.Length && players[i] != null)
                    ? players[i] : new PlayerTransforms();
            }
            players = fixedArr;
        }
    }

    void Start()
    {
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            // 預防 receiver 停掉時 Sender ICMP 反彈讓我們這邊 ReceiveFrom 拋例外。
            // Windows-only ioctl，非 Windows 會 throw，吃掉就好。
            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception) { /* 不是 Windows 沒差 */ }
        }
        catch (Exception e)
        {
            Debug.LogError("[KinectUdpReceiver] 開 UDP port " + port + " 失敗: " + e.Message);
            return;
        }

        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "KinectUdpReceiver" };
        receiveThread.Start();

        Debug.Log("[KinectUdpReceiver] Listening on UDP " + port + " (binary wire v" + WireVersion + ")");
    }

    void Update()
    {
        // 把 bg 累積的最新封包搬過來解析。一次只解一包 (最新)。
        int len;
        lock (consumeLock)
        {
            if (consumeLength == 0)
            {
                MaybeLogStats();
                return;
            }
            len = consumeLength;
            Buffer.BlockCopy(consumeBuffer, 0, parseBuffer, 0, len);
            consumeLength = 0;
        }

        ParsePacket(parseBuffer, len);
        mainPacketsConsumed++;

        MaybeLogStats();
    }

    void OnDisable()
    {
        running = false;
        if (socket != null)
        {
            // Close() 會讓 ReceiveFrom 拋 SocketException / ObjectDisposedException，
            // bg loop 看到就會結束。
            try { socket.Close(); } catch { }
            socket = null;
        }
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(200);
            receiveThread = null;
        }
    }

    void OnApplicationQuit()
    {
        OnDisable();
    }

    // ---------- 主執行緒：binary 封包 → PlayerData ----------

    private void ParsePacket(byte[] buf, int len)
    {
        // 格式檢查理論上 bg 已經做過一次了，但便宜，這裡再做一次更安全。
        if (len < HeaderBytes) return;
        if (buf[0] != (byte)'K' || buf[1] != (byte)'I' || buf[2] != (byte)'N' || buf[3] != (byte)'E') return;
        ushort version = (ushort)(buf[4] | (buf[5] << 8));
        if (version != WireVersion) return;

        int playerCount = buf[6];
        uint seq = ReadU32(buf, 7);
        ulong tMs = ReadU64(buf, 11);

        LatestPacketSeq = seq;
        LatestPacketKinectTimeMs = (long)tMs;

        int off = HeaderBytes;
        for (int i = 0; i < playerCount; i++)
        {
            if (off + PerPlayerBytes > len) return;

            int slot = buf[off + 0];
            bool tracked = buf[off + 1] != 0;
            HandState hlState = MapWireHandState(buf[off + 2]);
            HandState hrState = MapWireHandState(buf[off + 3]);

            if (slot < 0 || slot >= MaxPlayers)
            {
                off += PerPlayerBytes;
                continue;
            }

            int jointOff = off + 4;
            ApplyPlayer(slot, tracked, hlState, hrState, buf, jointOff);
            off += PerPlayerBytes;
        }

        if (logIncoming)
        {
            Debug.Log($"[KinectUdpReceiver] seq={seq} tMs={tMs} players={playerCount}");
        }
    }

    private void ApplyPlayer(int slot, bool tracked, HandState hlState, HandState hrState,
                              byte[] buf, int jointOff)
    {
        PlayerData data = LatestData[slot];
        data.tracked = tracked;
        data.handLeftState = hlState;
        data.handRightState = hrState;

        // Joint 順序固定 (跟 sender 對齊)：
        //   HL, HR, FL, FR, ShL, ShR, SpM, SpB
        data.handLeft      = UpdateJoint(GetTf(slot, t => t.handLeft),      buf, jointOff +  0, data.handLeft);
        data.handRight     = UpdateJoint(GetTf(slot, t => t.handRight),     buf, jointOff + 12, data.handRight);
        data.footLeft      = UpdateJoint(GetTf(slot, t => t.footLeft),      buf, jointOff + 24, data.footLeft);
        data.footRight    = UpdateJoint(GetTf(slot, t => t.footRight),     buf, jointOff + 36, data.footRight);
        data.shoulderLeft  = UpdateJoint(GetTf(slot, t => t.shoulderLeft),  buf, jointOff + 48, data.shoulderLeft);
        data.shoulderRight = UpdateJoint(GetTf(slot, t => t.shoulderRight), buf, jointOff + 60, data.shoulderRight);
        data.spineMid      = UpdateJoint(GetTf(slot, t => t.spineMid),      buf, jointOff + 72, data.spineMid);
        data.spineBase     = UpdateJoint(GetTf(slot, t => t.spineBase),     buf, jointOff + 84, data.spineBase);
    }

    private Transform GetTf(int slot, Func<PlayerTransforms, Transform> picker)
    {
        if (players == null || slot < 0 || slot >= players.Length) return null;
        return players[slot] == null ? null : picker(players[slot]);
    }

    /// <summary>
    /// 把一個關節資料 (a) 寫到 Transform (如果有指派)、(b) 回傳轉換後的世界座標 (給 LatestData 存)。
    /// </summary>
    private Vector3 UpdateJoint(Transform target, byte[] buf, int off, Vector3 previousValue)
    {
        float jx = ReadFloat(buf, off + 0);
        float jy = ReadFloat(buf, off + 4);
        float jz = ReadFloat(buf, off + 8);

        // Sender 對未追到的關節送 (0,0,0)，這裡保留上一個值避免肢體跳到原點。
        if (jx == 0f && jy == 0f && jz == 0f) return previousValue;

        Vector3 desired = KinectToUnity(jx, jy, jz);
        Vector3 next;
        if (target == null)
        {
            // 沒有 Transform 就用簡單 lerp 平滑 (避免 chord 端拿到鋸齒值)
            next = (smoothing <= 0f)
                ? desired
                : Vector3.Lerp(previousValue, desired, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        }
        else
        {
            if (smoothing <= 0f)
            {
                target.position = desired;
            }
            else
            {
                float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
                target.position = Vector3.Lerp(target.position, desired, t);
            }
            next = target.position;
        }
        return next;
    }

    private Vector3 KinectToUnity(float kx, float ky, float kz)
    {
        float x = flipX ? -kx : kx;
        float z = flipZ ? -kz : kz;
        return new Vector3(x, ky, z) * scale + worldOffset;
    }

    private static HandState MapWireHandState(byte b)
    {
        // Microsoft.Kinect.HandState 數值: Unknown=0, NotTracked=1, Open=2, Closed=3, Lasso=4
        // 本地 enum 順序不同，所以這裡手動 map。
        switch (b)
        {
            case 0: return HandState.Unknown;
            case 1: return HandState.NotTracked;
            case 2: return HandState.Open;
            case 3: return HandState.Closed;
            case 4: return HandState.Lasso;
            default: return HandState.Unknown;
        }
    }

    // ---------- 背景 UDP 收信 ----------

    private void ReceiveLoop()
    {
        // 本地捕獲 socket reference，避免 OnDisable 把 field 設成 null 之後
        // 這裡 NRE。Close 由 main thread 觸發，下面 ReceiveFrom 就會丟例外，
        // 我們 catch 後 running 也已經是 false，正常退出。
        Socket localSocket = socket;
        EndPoint ep = anyEndPoint;
        while (running)
        {
            int n;
            try
            {
                n = localSocket.ReceiveFrom(bgRecvBuffer, 0, bgRecvBuffer.Length, SocketFlags.None, ref ep);
            }
            catch (SocketException)
            {
                // 關閉時會丟，忽略；running 變 false 後迴圈會退。
                if (!running) return;
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KinectUdpReceiver] receive error: " + e.Message);
                continue;
            }

            // 在 bg thread 上先做基本驗證 + seq 排序，主執行緒只看到「對的、最新的」。
            if (n < HeaderBytes)
            {
                Interlocked.Increment(ref bgInvalidPackets);
                continue;
            }
            if (bgRecvBuffer[0] != (byte)'K' || bgRecvBuffer[1] != (byte)'I' ||
                bgRecvBuffer[2] != (byte)'N' || bgRecvBuffer[3] != (byte)'E')
            {
                Interlocked.Increment(ref bgInvalidPackets);
                continue;
            }
            ushort version = (ushort)(bgRecvBuffer[4] | (bgRecvBuffer[5] << 8));
            if (version != WireVersion)
            {
                Interlocked.Increment(ref bgInvalidPackets);
                continue;
            }

            uint seq = ReadU32(bgRecvBuffer, 7);

            if (bgHasLastSeq)
            {
                // 用 unchecked signed diff 處理 uint32 wrap：
                //   diff > 0  → 新 (可能有 gap)
                //   diff <= 0 → 舊或重複，丟掉
                int diff = unchecked((int)(seq - bgLastReceivedSeq));
                if (diff <= 0)
                {
                    Interlocked.Increment(ref bgOutOfOrderDiscarded);
                    continue;
                }
                if (diff > 1)
                {
                    Interlocked.Add(ref bgPacketsDropped, diff - 1);
                }
            }
            else
            {
                bgHasLastSeq = true;
            }
            bgLastReceivedSeq = seq;
            Interlocked.Increment(ref bgPacketsReceived);

            // 寫到主執行緒會 pick 的 buffer。前一個還沒被 consume 的就直接覆蓋
            // (我們的策略就是「主執行緒只在乎最新」)。
            lock (consumeLock)
            {
                Buffer.BlockCopy(bgRecvBuffer, 0, consumeBuffer, 0, n);
                consumeLength = n;
            }
        }
    }

    private void MaybeLogStats()
    {
        if (!logStats) return;
        if (Time.unscaledTime < nextStatsLogTime) return;
        nextStatsLogTime = Time.unscaledTime + 1f;

        Debug.Log(
            $"[KinectUdpReceiver] recv={Interlocked.Read(ref bgPacketsReceived)} " +
            $"consumed={mainPacketsConsumed} " +
            $"netDropped={Interlocked.Read(ref bgPacketsDropped)} " +
            $"outOfOrder={Interlocked.Read(ref bgOutOfOrderDiscarded)} " +
            $"invalid={Interlocked.Read(ref bgInvalidPackets)} " +
            $"lastSeq={LatestPacketSeq} lastTMs={LatestPacketKinectTimeMs}");
    }

    // ---------- Binary 解析小工具 ----------

    private static uint ReadU32(byte[] b, int off)
    {
        return (uint)(b[off + 0] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
    }

    private static ulong ReadU64(byte[] b, int off)
    {
        ulong lo = ReadU32(b, off);
        ulong hi = ReadU32(b, off + 4);
        return lo | (hi << 32);
    }

    private static float ReadFloat(byte[] b, int off)
    {
        FloatBits fb;
        fb.F = 0f;
        fb.U = (uint)(b[off + 0] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
        return fb.F;
    }

    // ---------- Scene Gizmo (調 offset 用) ----------

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        DrawKinectFrustum();
        DrawGroundCross();

        if (players == null) return;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) continue;
            // 用顏色區分玩家：P0 暖色、P1 冷色
            Color baseHue = (i == 0) ? new Color(1f, 0.5f, 0.3f) : new Color(0.3f, 0.7f, 1f);
            string prefix = "P" + i + " ";
            DrawLimb(players[i].handLeft,  Lerp(baseHue, Color.red,    0.3f), prefix + "L Hand");
            DrawLimb(players[i].handRight, Lerp(baseHue, Color.blue,   0.3f), prefix + "R Hand");
            DrawLimb(players[i].footLeft,  Lerp(baseHue, Color.yellow, 0.3f), prefix + "L Foot");
            DrawLimb(players[i].footRight, Lerp(baseHue, Color.green,  0.3f), prefix + "R Foot");
        }
    }

    private static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, t);

    private void DrawLimb(Transform t, Color c, string label)
    {
        if (t == null) return;
        Gizmos.color = c;
        Gizmos.DrawSphere(t.position, 0.06f);
        Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
        Gizmos.DrawWireSphere(t.position, 0.12f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = c;
        UnityEditor.Handles.Label(t.position + Vector3.up * 0.15f, label);
#endif
    }

    private void DrawGroundCross()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
        Vector3 c = worldOffset;
        Gizmos.DrawLine(c + new Vector3(-0.3f, 0, 0), c + new Vector3(0.3f, 0, 0));
        Gizmos.DrawLine(c + new Vector3(0, 0, -0.3f), c + new Vector3(0, 0, 0.3f));
        Gizmos.DrawWireSphere(c, 0.04f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(c + Vector3.up * 0.05f, "Kinect 原點 (worldOffset)");
#endif
    }

    private void DrawKinectFrustum()
    {
        float halfH = Mathf.Tan(kinectHFovDeg * 0.5f * Mathf.Deg2Rad);
        float halfV = Mathf.Tan(kinectVFovDeg * 0.5f * Mathf.Deg2Rad);

        Vector3 nearTL = FrustumPoint(-halfH, +halfV, kinectNearMeters);
        Vector3 nearTR = FrustumPoint(+halfH, +halfV, kinectNearMeters);
        Vector3 nearBL = FrustumPoint(-halfH, -halfV, kinectNearMeters);
        Vector3 nearBR = FrustumPoint(+halfH, -halfV, kinectNearMeters);

        Vector3 farTL = FrustumPoint(-halfH, +halfV, kinectFarMeters);
        Vector3 farTR = FrustumPoint(+halfH, +halfV, kinectFarMeters);
        Vector3 farBL = FrustumPoint(-halfH, -halfV, kinectFarMeters);
        Vector3 farBR = FrustumPoint(+halfH, -halfV, kinectFarMeters);

        Vector3 apex = KinectMetersToUnity(new Vector3(0, 0, 0));

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.4f);
        Gizmos.DrawLine(apex, farTL);
        Gizmos.DrawLine(apex, farTR);
        Gizmos.DrawLine(apex, farBL);
        Gizmos.DrawLine(apex, farBR);

        Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.7f);
        Gizmos.DrawLine(nearTL, nearTR);
        Gizmos.DrawLine(nearTR, nearBR);
        Gizmos.DrawLine(nearBR, nearBL);
        Gizmos.DrawLine(nearBL, nearTL);

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.7f);
        Gizmos.DrawLine(farTL, farTR);
        Gizmos.DrawLine(farTR, farBR);
        Gizmos.DrawLine(farBR, farBL);
        Gizmos.DrawLine(farBL, farTL);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(apex, Vector3.one * 0.08f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.Label(apex + Vector3.up * 0.1f, "Kinect 鏡頭");
        UnityEditor.Handles.color = new Color(1f, 0.4f, 0.4f);
        UnityEditor.Handles.Label((nearTL + nearTR) * 0.5f, "近 " + kinectNearMeters + "m");
        UnityEditor.Handles.color = new Color(0.2f, 0.9f, 1f);
        UnityEditor.Handles.Label((farTL + farTR) * 0.5f, "遠 " + kinectFarMeters + "m");
#endif
    }

    private Vector3 FrustumPoint(float xRatio, float yRatio, float depthMeters)
    {
        return KinectMetersToUnity(new Vector3(xRatio * depthMeters, yRatio * depthMeters, depthMeters));
    }

    private Vector3 KinectMetersToUnity(Vector3 k)
    {
        float x = flipX ? -k.x : k.x;
        float z = flipZ ? -k.z : k.z;
        return new Vector3(x, k.y, z) * scale + worldOffset;
    }
}

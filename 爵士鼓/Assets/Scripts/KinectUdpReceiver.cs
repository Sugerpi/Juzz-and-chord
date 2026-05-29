using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
/// </summary>
public class KinectUdpReceiver : MonoBehaviour
{
    public const int MaxPlayers = 2;

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

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool running;
    private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();

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

    // ---------- JSON 解析用的暫時型別 (對應 Sender 的輸出) ----------

    [Serializable] private class JointVec { public float x; public float y; public float z; }

    [Serializable]
    private class PlayerJson
    {
        public int slot;
        public bool tracked;
        public JointVec handLeft, handRight, footLeft, footRight;
        public JointVec shoulderLeft, shoulderRight, spineMid, spineBase;
        public string handLeftState;
        public string handRightState;
    }

    [Serializable]
    private class Payload
    {
        public PlayerJson[] players;
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
            udpClient = new UdpClient(port);
        }
        catch (Exception e)
        {
            Debug.LogError("[KinectUdpReceiver] 開 UDP port " + port + " 失敗: " + e.Message);
            return;
        }

        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();

        Debug.Log("[KinectUdpReceiver] Listening on UDP " + port);
    }

    void Update()
    {
        // 把背景執行緒累積的封包全部消化掉，只用最新的一筆。
        string latest = null;
        string tmp;
        while (incoming.TryDequeue(out tmp))
        {
            latest = tmp;
        }
        if (latest == null) return;

        Payload p;
        try
        {
            p = JsonUtility.FromJson<Payload>(latest);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[KinectUdpReceiver] JSON 解析失敗: " + e.Message);
            return;
        }
        if (p == null || p.players == null) return;

        foreach (PlayerJson pj in p.players)
        {
            if (pj == null) continue;
            if (pj.slot < 0 || pj.slot >= MaxPlayers) continue;
            ApplyPlayer(pj);
        }
    }

    void OnDisable()
    {
        running = false;
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
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

    // ---------- 解析後處理 ----------

    private void ApplyPlayer(PlayerJson pj)
    {
        PlayerData data = LatestData[pj.slot];
        data.tracked = pj.tracked;
        data.handLeftState = ParseHandState(pj.handLeftState);
        data.handRightState = ParseHandState(pj.handRightState);

        // 更新資料 + 推到 Transform (兩個都做，這樣 chord 端 polling 也能拿到值)
        data.handLeft      = UpdateJoint(GetTf(pj.slot, t => t.handLeft),      pj.handLeft,      data.handLeft);
        data.handRight     = UpdateJoint(GetTf(pj.slot, t => t.handRight),     pj.handRight,     data.handRight);
        data.footLeft      = UpdateJoint(GetTf(pj.slot, t => t.footLeft),      pj.footLeft,      data.footLeft);
        data.footRight     = UpdateJoint(GetTf(pj.slot, t => t.footRight),     pj.footRight,     data.footRight);
        data.shoulderLeft  = UpdateJoint(GetTf(pj.slot, t => t.shoulderLeft),  pj.shoulderLeft,  data.shoulderLeft);
        data.shoulderRight = UpdateJoint(GetTf(pj.slot, t => t.shoulderRight), pj.shoulderRight, data.shoulderRight);
        data.spineMid      = UpdateJoint(GetTf(pj.slot, t => t.spineMid),      pj.spineMid,      data.spineMid);
        data.spineBase     = UpdateJoint(GetTf(pj.slot, t => t.spineBase),     pj.spineBase,     data.spineBase);
    }

    private Transform GetTf(int slot, Func<PlayerTransforms, Transform> picker)
    {
        if (players == null || slot < 0 || slot >= players.Length) return null;
        return players[slot] == null ? null : picker(players[slot]);
    }

    /// <summary>
    /// 把一個關節資料 (a) 寫到 Transform (如果有指派)、(b) 回傳轉換後的世界座標 (給 LatestData 存)。
    /// </summary>
    private Vector3 UpdateJoint(Transform target, JointVec j, Vector3 previousValue)
    {
        if (j == null) return previousValue;
        // Sender 對未追到的關節送 (0,0,0)，這裡保留上一個值避免肢體跳到原點。
        if (j.x == 0f && j.y == 0f && j.z == 0f) return previousValue;

        Vector3 desired = KinectToUnity(j);
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

    private Vector3 KinectToUnity(JointVec j)
    {
        float x = flipX ? -j.x : j.x;
        float z = flipZ ? -j.z : j.z;
        return new Vector3(x, j.y, z) * scale + worldOffset;
    }

    private static HandState ParseHandState(string s)
    {
        if (string.IsNullOrEmpty(s)) return HandState.Unknown;
        // Microsoft.Kinect.HandState 字面值: Unknown / NotTracked / Open / Closed / Lasso
        HandState result;
        return Enum.TryParse(s, true, out result) ? result : HandState.Unknown;
    }

    // ---------- 背景 UDP 收信 ----------

    private void ReceiveLoop()
    {
        IPEndPoint anyEndpoint = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = udpClient.Receive(ref anyEndpoint);
                string json = Encoding.UTF8.GetString(data);
                if (logIncoming)
                {
                    Debug.Log("[KinectUdpReceiver] recv: " + json);
                }
                incoming.Enqueue(json);
            }
            catch (SocketException)
            {
                // 關閉 UdpClient 時會丟，忽略
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KinectUdpReceiver] receive error: " + e.Message);
            }
        }
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

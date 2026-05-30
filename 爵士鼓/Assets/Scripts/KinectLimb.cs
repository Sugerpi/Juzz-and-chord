using UnityEngine;

/// <summary>
/// 黏在每個被 KinectUdpReceiver 推位置的 limb GameObject 上 (HandLeft/HandRight/
/// FootLeft/FootRight)。它的存在就是給打擊判定一個「我是哪個玩家的哪隻肢體、
/// 我現在手握不握拳」的可查詢介面，讓 VirtualDrum 不用靠名字猜或靠 Transform
/// reference 比對。
///
/// 用法：在 Inspector 把 receiver 拖進來 (或留空自動 Find)、選 playerIndex 跟 kind。
/// VirtualDrum 在 OnTrigger 裡 GetComponent&lt;KinectLimb&gt;() 拿到本物件就走新邏輯；
/// 拿不到就 fallback 到舊的速度判定，所以可以一個一個 scene 慢慢遷移。
///
/// 設計選擇：IsClosed 是即時 sample receiver.LatestData 的 property，不快取。
/// 原因是 Unity 在同一幀的順序是 physics(OnTrigger) → Update，如果用 cached 值
/// 會晚一幀 — 對打鼓 timing 不要的延遲。
/// </summary>
[DisallowMultipleComponent]
public class KinectLimb : MonoBehaviour
{
    public enum Kind { HandLeft, HandRight, FootLeft, FootRight }

    [Header("綁定")]
    [Tooltip("UDP 來源。留空會在 Start 自動 FindFirstObjectByType。")]
    public KinectUdpReceiver receiver;

    [Tooltip("這個肢體屬於哪個玩家。鼓手通常是 0、和弦手是 1。")]
    [Range(0, 1)] public int playerIndex = 0;

    [Tooltip("這個 GameObject 對應的肢體種類。")]
    public Kind kind = Kind.HandLeft;

    /// <summary>true 表示這是手 (HandLeft 或 HandRight)。</summary>
    public bool IsHand => kind == Kind.HandLeft || kind == Kind.HandRight;

    /// <summary>true 表示這是腳 (FootLeft 或 FootRight)。</summary>
    public bool IsFoot => !IsHand;

    /// <summary>
    /// 即時從 receiver 拿最新的 hand state，不快取。腳一律回 NotTracked。
    /// </summary>
    public KinectUdpReceiver.HandState CurrentHandState
    {
        get
        {
            if (receiver == null || receiver.LatestData == null) return KinectUdpReceiver.HandState.Unknown;
            if (playerIndex < 0 || playerIndex >= receiver.LatestData.Length) return KinectUdpReceiver.HandState.Unknown;
            var d = receiver.LatestData[playerIndex];
            if (d == null) return KinectUdpReceiver.HandState.Unknown;
            switch (kind)
            {
                case Kind.HandLeft:  return d.handLeftState;
                case Kind.HandRight: return d.handRightState;
                default:             return KinectUdpReceiver.HandState.NotTracked;
            }
        }
    }

    /// <summary>true = 手是握拳 (Microsoft.Kinect.HandState.Closed)。腳永遠 false。</summary>
    public bool IsClosed => CurrentHandState == KinectUdpReceiver.HandState.Closed;

    /// <summary>true = 整個玩家本幀有被追到 (任一肢體都共享這個旗標)。</summary>
    public bool IsTracked
    {
        get
        {
            if (receiver == null || receiver.LatestData == null) return false;
            if (playerIndex < 0 || playerIndex >= receiver.LatestData.Length) return false;
            var d = receiver.LatestData[playerIndex];
            return d != null && d.tracked;
        }
    }

    void Start()
    {
        if (receiver == null)
        {
            receiver = FindFirstObjectByType<KinectUdpReceiver>();
            if (receiver == null)
            {
                Debug.LogWarning("[KinectLimb] 找不到 KinectUdpReceiver — " +
                                 "場景上沒有的話，VirtualDrum 會走 fallback 速度判定。", this);
            }
        }
    }
}

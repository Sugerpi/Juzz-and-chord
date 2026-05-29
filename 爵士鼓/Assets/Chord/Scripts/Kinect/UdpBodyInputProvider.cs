using UnityEngine;
using ChordPlayer.Core;

namespace ChordPlayer.Kinect
{
    /// <summary>
    /// 把鼓專案的 <see cref="global::KinectUdpReceiver"/> (UDP 來源) 轉接到
    /// chord 的 <see cref="IBodyInputProvider"/> 介面。每幀從 receiver 拉指定玩家
    /// slot 的最新資料，重新打包成 chord 認識的 <see cref="BodyData"/>。
    ///
    /// 標準配置：Player 0 = 鼓手 (用 Transform 觸發 trigger collider)、
    /// Player 1 = 和弦手 (走這個 Provider，給 ChordEngine 用)。
    ///
    /// 把這個元件放到場景，Inspector 拖一個 KinectUdpReceiver (或留空走自動搜尋)，
    /// 並選 playerIndex = 1。
    ///
    /// 註冊行為：本元件「只有在找到 receiver 時」才註冊到 ServiceLocator。沒 receiver
    /// 時 (例如 chord 場景單跑作 mouse 測試)，它會主動讓位給 SimulatedInputProvider，
    /// 避免兩個 provider 在 ServiceLocator 互搶造成沒人能控制 ChordEngine。
    /// </summary>
    public class UdpBodyInputProvider : MonoBehaviour, IBodyInputProvider
    {
        [Header("UDP source")]
        [Tooltip("拖場景上掛 KinectUdpReceiver 的 GameObject。" +
                 "留空會在 Start 時自動 FindFirstObjectByType 抓場景上唯一的 receiver — " +
                 "在 additive scene 載入時很方便 (chord 場景不必知道 receiver 在哪個 scene)。")]
        [SerializeField] KinectUdpReceiver _receiver;

        [Tooltip("讀哪個玩家。0 = 第一個被追到的人 (通常是鼓手)、1 = 第二個 (通常是和弦手)。")]
        [SerializeField, Range(0, 1)] int _playerIndex = 1;

        [Header("Debug")]
        [SerializeField] bool _logToConsole = false;
        [SerializeField, Min(0.1f)] float _logInterval = 1.0f;

        BodyData _body = BodyData.Empty;
        float _nextLogTime;
        bool _registered;

        public BodyData Body => _body;
        public int PlayerIndex => _playerIndex;

        // We intentionally do NOT register in OnEnable. Registration is deferred
        // until we actually have a working KinectUdpReceiver in the scene — see
        // Update(). This avoids a race with SimulatedInputProvider when both are
        // present: in chord-only scenes (mouse-test), no receiver exists, so we
        // stay out of ServiceLocator and Sim wins; in chord+drum additive scenes,
        // we find the receiver in the drum scene, register, and take over.
        void OnDisable() => UnregisterIfMine();

        void Update()
        {
            // Auto-find a receiver if none was assigned in the Inspector. Makes
            // additive scene loading work without manual cross-scene wiring.
            if (_receiver == null)
                _receiver = FindFirstObjectByType<KinectUdpReceiver>();

            if (_receiver == null || _receiver.LatestData == null)
            {
                // No UDP data source available — step aside so another provider
                // (e.g. SimulatedInputProvider in chord-only test) can drive
                // ChordEngine via ServiceLocator.
                UnregisterIfMine();
                _body.IsTracked = false;
                return;
            }

            // We have a receiver. Claim the IBodyInputProvider slot if we haven't
            // already, then keep filling _body each frame.
            if (!_registered)
            {
                ServiceLocator.Register<IBodyInputProvider>(this);
                _registered = true;
            }

            int idx = Mathf.Clamp(_playerIndex, 0, _receiver.LatestData.Length - 1);
            KinectUdpReceiver.PlayerData data = _receiver.LatestData[idx];
            if (data == null)
            {
                _body.IsTracked = false;
                return;
            }

            _body.IsTracked       = data.tracked;
            _body.LeftHand        = data.handLeft;
            _body.RightHand       = data.handRight;
            _body.SpineMid        = data.spineMid;
            _body.SpineBase       = data.spineBase;
            _body.ShoulderLeft    = data.shoulderLeft;
            _body.ShoulderRight   = data.shoulderRight;

            _body.LeftHandState   = MapHand(data.handLeftState);
            _body.RightHandState  = MapHand(data.handRightState);

            // chord 的 BodyData 期待 per-hand tracked 旗標。UDP 來源沒有獨立追蹤
            // 每隻手的位置 (只有整個身體 + hand state)，所以這裡用「身體有追到 且
            // 手勢不是 NotTracked」當作 hand-tracked 的近似。
            _body.LeftHandTracked  = data.tracked &&
                                     data.handLeftState != KinectUdpReceiver.HandState.NotTracked;
            _body.RightHandTracked = data.tracked &&
                                     data.handRightState != KinectUdpReceiver.HandState.NotTracked;

            if (_logToConsole && Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + _logInterval;
                Debug.Log(
                    $"[UdpBodyInputProvider P{idx}] tracked={_body.IsTracked}  " +
                    $"L {_body.LeftHand:F2} ({_body.LeftHandState})  " +
                    $"R {_body.RightHand:F2} ({_body.RightHandState})", this);
            }
        }

        // Unregister only if WE are the currently-registered IBodyInputProvider.
        // ServiceLocator's Unregister<T> doesn't check identity — it just removes
        // whatever's stored under the type — so a naive Unregister could rip out
        // SimulatedInputProvider's registration by accident.
        void UnregisterIfMine()
        {
            if (!_registered) return;
            if (ServiceLocator.TryGet<IBodyInputProvider>(out var current) &&
                ReferenceEquals(current, this))
            {
                ServiceLocator.Unregister<IBodyInputProvider>();
            }
            _registered = false;
        }

        // KinectUdpReceiver.HandState 跟 ChordPlayer.Kinect.HandGesture 在語意上一一對應
        // (兩個 enum 都直接對應 Microsoft.Kinect.HandState)，名字一樣，但分別在不同
        // namespace，所以這邊做明確的 switch 映射。
        static HandGesture MapHand(KinectUdpReceiver.HandState s) => s switch
        {
            KinectUdpReceiver.HandState.Open       => HandGesture.Open,
            KinectUdpReceiver.HandState.Closed     => HandGesture.Closed,
            KinectUdpReceiver.HandState.Lasso      => HandGesture.Lasso,
            KinectUdpReceiver.HandState.NotTracked => HandGesture.NotTracked,
            _                                      => HandGesture.Unknown,
        };
    }
}

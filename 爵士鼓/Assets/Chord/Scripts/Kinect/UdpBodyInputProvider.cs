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

        [Header("Anchor to Wheels (推薦：跟鼓一樣的相對定位)")]
        [Tooltip("打開後，會強制把肩膀「貼」在輪盤中心，手位置 = 輪盤中心 + (Kinect 手 - Kinect 肩)。" +
                 "玩家就不必站在特定 Unity 世界座標，伸手揮動就掃 segment。")]
        [SerializeField] bool _anchorToWheels = true;

        [Tooltip("左輪盤的 Transform (Root wheel)。會被當作左肩 anchor，左手的相對位移會以此為原點。")]
        [SerializeField] Transform _leftWheelAnchor;

        [Tooltip("右輪盤的 Transform (Quality wheel)。同理。")]
        [SerializeField] Transform _rightWheelAnchor;

        [Tooltip("Kinect 公尺 → 輪盤平面尺寸的放大倍率。 1 = 一公尺對一單位 (Kinect 原尺度)。" +
                 "輪盤外徑 0.6 的話建議 1.5~2.0：揮手伸到約 0.3-0.4m 就能掃到外圈邊緣。")]
        [SerializeField, Min(0.1f)] float _armScale = 1.7f;

        [Header("Yielding to SimulatedInputProvider")]
        [Tooltip("收不到封包超過這秒數就讓位給 SimulatedInputProvider — " +
                 "讓你在 Editor 裡用滑鼠測試時 receiver 還在場景上也不會卡住輪盤。")]
        [SerializeField, Min(0.1f)] float _staleTimeout = 1.0f;

        [Header("Debug")]
        [SerializeField] bool _logToConsole = false;
        [SerializeField, Min(0.1f)] float _logInterval = 1.0f;

        BodyData _body = BodyData.Empty;
        float _nextLogTime;
        bool _registered;
        uint _lastSeenSeq;
        float _lastSeqChangeTime;
        bool _hasSeenAnySeq;

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

            // Receiver 物件在場景上 ≠ 真的在收封包。當 chord 場景被 additive 載到
            // 鼓主場景時，receiver 永遠存在；但如果 sender 沒開，LatestPacketSeq 永遠
            // 是 0、或停在某個值不動。我們追蹤 seq 變化時間：超過 _staleTimeout 沒動，
            // 就讓位給 SimulatedInputProvider，這樣 Editor mouse-test 才能用。
            uint seqNow = _receiver.LatestPacketSeq;
            if (!_hasSeenAnySeq || seqNow != _lastSeenSeq)
            {
                _hasSeenAnySeq = true;
                _lastSeenSeq = seqNow;
                _lastSeqChangeTime = Time.unscaledTime;
            }
            bool dataFlowing = _hasSeenAnySeq && seqNow != 0 &&
                               (Time.unscaledTime - _lastSeqChangeTime) <= _staleTimeout;
            if (!dataFlowing)
            {
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
            _body.LeftHandState   = MapHand(data.handLeftState);
            _body.RightHandState  = MapHand(data.handRightState);

            // ============== Anchor-to-Wheels 模式 ==============
            // 玩家在 Unity 世界的絕對位置 (站哪、距 Kinect 多遠) 都丟掉。只看「手相對肩
            // 膀的位移」，然後把肩膀強制貼在輪盤中心，所以手就會以輪盤中心為原點掃過去。
            // 等於 ChordEngine 看到的「肩膀」永遠 = 輪盤中心，揮手就直接是 hand offset
            // = 選擇用的 angle/radius。這跟鼓組「手到 trigger collider 的世界距離」邏
            // 輯類似 — 都是用相對位移而不是絕對座標。
            if (_anchorToWheels && data.tracked &&
                _leftWheelAnchor != null && _rightWheelAnchor != null)
            {
                Vector3 leftAnchor  = _leftWheelAnchor.position;
                Vector3 rightAnchor = _rightWheelAnchor.position;

                Vector3 leftArm  = (data.handLeft  - data.shoulderLeft)  * _armScale;
                Vector3 rightArm = (data.handRight - data.shoulderRight) * _armScale;

                _body.ShoulderLeft  = leftAnchor;
                _body.ShoulderRight = rightAnchor;
                _body.LeftHand      = leftAnchor  + leftArm;
                _body.RightHand     = rightAnchor + rightArm;

                // SpineMid/SpineBase 拿不到肩膀基準時也補一下，避免 HandPointer 那些
                // 共用 shoulderWidth 的工具拿到 0。
                _body.SpineMid  = (leftAnchor + rightAnchor) * 0.5f;
                _body.SpineBase = _body.SpineMid + Vector3.down * 0.3f;
            }
            else
            {
                // 直送模式：保留原本「絕對世界座標」邏輯。如果想要校準位置才能玩，
                // 或想用 SkeletonView 看完整骨架，用這個。
                _body.LeftHand        = data.handLeft;
                _body.RightHand       = data.handRight;
                _body.SpineMid        = data.spineMid;
                _body.SpineBase       = data.spineBase;
                _body.ShoulderLeft    = data.shoulderLeft;
                _body.ShoulderRight   = data.shoulderRight;
            }

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

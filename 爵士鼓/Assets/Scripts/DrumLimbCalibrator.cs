using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 把鼓手的 4 個肢體 Transform 鎖在「你在編輯器擺好的鼓組位置」，玩家進畫面後做
/// 一次「靜止 N 秒」的校準，從那一刻起，玩家肢體相對 Kinect 起始姿勢的位移就會
/// 被加到預設位置上。等於：「站好之後，你做什麼動作，肢體 GameObject 就跟著動，
/// 但起點是你 design 的位置，不是 Kinect 看你的絕對座標」。
///
/// 為什麼跟 chord 的 CalibrationController 不一樣：chord 是把「目標」(輪盤) 移到
/// 玩家手上，鼓相反 — 鼓組位置是固定的，玩家肢體要「對齊到」鼓組上。
///
/// 使用方式 (重要)：
///   1. 把肢體 Transform 從 KinectUdpReceiver 的 Players[0] 移除 (清空)
///   2. 改把同樣 4 個 Transform 拖到本元件對應欄位
///   3. KinectUdpReceiver 的 Smoothing 可以保留 (它影響 LatestData 的內部平滑)；
///      本元件也有自己的 Smoothing，疊加一點點延遲但效果可接受。若要極致 snappy
///      可以把 KinectUdpReceiver.smoothing 設成 0 讓本元件獨自負責平滑。
///   4. 在編輯器把 4 個肢體擺到你想要的「鼓位置」(例如手在鼓面上、腳在大鼓踏板上)
///   5. 跑遊戲 → 站進 Kinect 視野 → 停 2 秒 → 校準完成 → 開始打鼓
/// </summary>
public class DrumLimbCalibrator : MonoBehaviour
{
    [Header("UDP 來源")]
    [Tooltip("拖鼓場景上的 KinectUdpReceiver")]
    public KinectUdpReceiver receiver;
    [Tooltip("要追蹤哪一位玩家。鼓手通常是 0。")]
    [Range(0, 1)] public int playerIndex = 0;

    [Header("肢體 Transform (預先擺在鼓組位置)")]
    [Tooltip("在編輯器把這 4 個 GameObject 放到你想要的初始位置 — 通常是各鼓 / 踏板上方。")]
    public Transform handLeft;
    public Transform handRight;
    public Transform footLeft;
    public Transform footRight;

    [Header("校準時機")]
    [Tooltip("玩家被追蹤後，要靜止這麼多秒才自動校準。")]
    [Min(0.1f)] public float autoHoldSeconds = 2f;
    [Tooltip("玩家失去追蹤後是否要求重新校準 (建議勾選，免得玩家走出走進畫面後座標歪掉)。")]
    public bool recalibrateOnRetrack = true;
    [Tooltip("手動觸發重新校準的按鍵。")]
    public Key recalibrateKey = Key.Space;

    [Header("平滑")]
    [Tooltip("0 = 無延遲、跟得緊；越大越平滑但有延遲。建議 15~25。")]
    public float smoothing = 18f;

    [Header("除錯")]
    public bool logToConsole = true;

    // 預設位置（編輯器擺的）
    Vector3 _targetHL, _targetHR, _targetFL, _targetFR;

    // 校準時記下的 offset = target - rawKinect
    Vector3 _offsetHL, _offsetHR, _offsetFL, _offsetFR;

    bool _calibrated;
    float _holdTimer;
    bool _wasTracked;

    void Awake()
    {
        // 記下你編輯器擺好的位置 — 這就是校準後肢體在「Kinect 起始姿勢」時要落在的位置。
        if (handLeft  != null) _targetHL = handLeft.position;
        if (handRight != null) _targetHR = handRight.position;
        if (footLeft  != null) _targetFL = footLeft.position;
        if (footRight != null) _targetFR = footRight.position;
    }

    void LateUpdate()
    {
        // 在 LateUpdate 跑，確保如果 Receiver 有意外寫到同一個 Transform，
        // 我們的值最後蓋過去。
        if (receiver == null || receiver.LatestData == null) return;
        int idx = Mathf.Clamp(playerIndex, 0, receiver.LatestData.Length - 1);
        KinectUdpReceiver.PlayerData data = receiver.LatestData[idx];
        if (data == null) return;

        // 手動重校準
        if (Keyboard.current != null && Keyboard.current[recalibrateKey].wasPressedThisFrame)
        {
            ResetCalibration("手動觸發重校準");
        }

        // 失追自動 reset
        if (recalibrateOnRetrack && _wasTracked && !data.tracked)
        {
            ResetCalibration("失去追蹤");
        }
        _wasTracked = data.tracked;

        if (!data.tracked)
        {
            // 沒人 → 肢體鎖在預設位置 (不亂飄)
            HoldAtTargets();
            _holdTimer = 0f;
            return;
        }

        if (!_calibrated)
        {
            _holdTimer += Time.deltaTime;
            HoldAtTargets(); // 倒數期間還是先鎖在預設位置
            if (_holdTimer >= autoHoldSeconds)
            {
                Calibrate(data);
            }
            return;
        }

        // 已校準 → 套用 (raw + offset)，加上平滑
        ApplyLimb(handLeft,  data.handLeft  + _offsetHL);
        ApplyLimb(handRight, data.handRight + _offsetHR);
        ApplyLimb(footLeft,  data.footLeft  + _offsetFL);
        ApplyLimb(footRight, data.footRight + _offsetFR);
    }

    void Calibrate(KinectUdpReceiver.PlayerData data)
    {
        // offset = 想要的位置 - 目前 Kinect 量到的位置
        _offsetHL = _targetHL - data.handLeft;
        _offsetHR = _targetHR - data.handRight;
        _offsetFL = _targetFL - data.footLeft;
        _offsetFR = _targetFR - data.footRight;
        _calibrated = true;

        if (logToConsole)
        {
            Debug.Log(
                $"[DrumLimbCalibrator] 校準完成 (Player {playerIndex})。" +
                $"HL offset={_offsetHL:F2}  HR offset={_offsetHR:F2}  " +
                $"FL offset={_offsetFL:F2}  FR offset={_offsetFR:F2}", this);
        }
    }

    void ResetCalibration(string reason)
    {
        _calibrated = false;
        _holdTimer = 0f;
        if (logToConsole) Debug.Log("[DrumLimbCalibrator] 校準重置: " + reason, this);
    }

    void HoldAtTargets()
    {
        if (handLeft  != null) handLeft.position  = _targetHL;
        if (handRight != null) handRight.position = _targetHR;
        if (footLeft  != null) footLeft.position  = _targetFL;
        if (footRight != null) footRight.position = _targetFR;
    }

    void ApplyLimb(Transform t, Vector3 desired)
    {
        if (t == null) return;
        if (smoothing <= 0f)
        {
            t.position = desired;
        }
        else
        {
            float a = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            t.position = Vector3.Lerp(t.position, desired, a);
        }
    }
}

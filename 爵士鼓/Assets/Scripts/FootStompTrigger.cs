using UnityEngine;

/// <summary>
/// 追蹤一隻腳的 Y 來偵測『踩下』手勢 — 不依賴 collider、不依賴絕對位置。
///
/// 演算法：兩階段 (UP / DOWN) 狀態機，純看相對位移：
///   UP 階段：紀錄『目前看過的最高 Y』。當腳從最高點下沉 ≥ triggerDropAmount 公尺
///            → 擊發、切到 DOWN，把『最低 Y』重置為目前 Y。
///   DOWN 階段：紀錄『目前看過的最低 Y』。當腳從最低點回升 ≥ reArmRiseAmount 公尺
///            → 切回 UP，重置最高 Y。
///
/// 為什麼比 collider 版本好：
///   - 不需要把虛擬腳對準某個 trigger 體積，只看相對位移
///   - Kinect 抖動 < triggerDropAmount 一定不會誤觸
///   - 玩家蹲下/站起/換姿勢都會自動跟上 (每階段自己抓 extremum)
///   - 內建雙閾值 Schmitt trigger，比單一閾值穩定得多
///   - cooldown 仍留著作為硬性兜底
///
/// 用法：掛在任何 GameObject 上 (建議空物件、命名 BassDrumStomp)，把右腳 Transform
/// 跟大鼓 VirtualDrum 拖進來。可選把踏板物件拖進來做按下視覺。
/// </summary>
[DisallowMultipleComponent]
public class FootStompTrigger : MonoBehaviour
{
    [Header("追蹤目標")]
    [Tooltip("要追蹤的腳 Transform — 通常把 DrumLimbCalibrator.footRight 拖進來")]
    public Transform foot;

    [Tooltip("觸發時要打的鼓 (通常是 BassDrum)")]
    public VirtualDrum drum;

    [Header("觸發 (UP → DOWN)")]
    [Tooltip("腳從『本次抬到的最高點』下沉這麼多公尺就算踩下。" +
             "Kinect 抖動通常 < 3 cm，建議 0.08~0.15。" +
             "太小會誤觸、太大要跺很用力。")]
    [Min(0.02f)] public float triggerDropAmount = 0.10f;

    [Header("重新裝彈 (DOWN → UP)")]
    [Tooltip("腳從『踩下後的最低點』回升這麼多公尺才會重置成可擊發狀態。" +
             "建議 = triggerDropAmount 的 50~70%。" +
             "太小：抖動容易連發；太大：要刻意把腳抬很高才能連踩。")]
    [Min(0.01f)] public float reArmRiseAmount = 0.06f;

    [Header("防連點：硬性 cooldown")]
    [Tooltip("兩次擊發最少間隔 (秒)。Schmitt 雙閾值本身已經能擋大部分抖動，這層是兜底。" +
             "想做快速雙踩可以調小到 0.08；想完全保險可以拉到 0.15。")]
    [Min(0f)] public float minIntervalSeconds = 0.10f;

    [Header("啟動延遲")]
    [Tooltip("Script 啟動後這麼多秒內不會觸發、只持續刷新基準 — 讓 DrumLimbCalibrator " +
             "的校準先穩定。如果你的 autoHoldSeconds = 2，這裡 2.5~3 比較安全。")]
    [Min(0f)] public float initialDelaySeconds = 2.5f;

    [Header("視覺回饋 (可選)")]
    [Tooltip("可選的踏板物件 — 進入 DOWN 階段時會視覺下沉。留空就不做視覺。")]
    public Transform pedalVisual;
    [Tooltip("踏板下沉的距離 (公尺，沿 local Y)")]
    public float pedalPressDepth = 0.02f;
    [Tooltip("踏板下沉 / 回彈的速度")]
    public float pedalPressSpeed = 20f;

    [Header("除錯")]
    [Tooltip("每次擊發 / 被擋下 / re-arm 都印到 Console，調參數時必開")]
    public bool logDecisions = false;
    [Tooltip("選到本物件時，Scene 視窗畫出當前 extremum / 觸發閾值線")]
    public bool drawGizmo = true;

    // --- 狀態 ---
    private enum Phase { Up, Down }
    private Phase phase = Phase.Up;
    private float extremumY;          // Up: 最高 Y / Down: 最低 Y
    private float lastFireTime = -999f;
    private float startTime;
    private Vector3 pedalVisualOriginal;

    void Start()
    {
        if (foot == null)
        {
            Debug.LogError("[FootStompTrigger] foot 未指定 — 元件停用。", this);
            enabled = false;
            return;
        }
        extremumY = foot.position.y;
        startTime = Time.time;
        if (pedalVisual != null) pedalVisualOriginal = pedalVisual.localPosition;
    }

    void Update()
    {
        if (foot == null) return;
        float footY = foot.position.y;

        // 啟動延遲期間：持續把 extremum 同步成現在 Y、不偵測
        // (讓 DrumLimbCalibrator 校準完成那一刻的位置 snap 不會被誤判成踩踏)
        if (Time.time < startTime + initialDelaySeconds)
        {
            extremumY = footY;
            UpdatePedalVisual(false);
            return;
        }

        if (phase == Phase.Up)
        {
            // 抬高就更新最高點
            if (footY > extremumY) extremumY = footY;

            float drop = extremumY - footY;
            if (drop >= triggerDropAmount)
            {
                // 硬性 cooldown 守一下
                if (Time.time >= lastFireTime + minIntervalSeconds)
                {
                    Fire(drop);
                    lastFireTime = Time.time;
                }
                else if (logDecisions)
                {
                    Debug.Log($"[FootStompTrigger] 擋下：cooldown 未滿 (drop={drop:F3} m)", this);
                }
                // 不論有沒有 fire，達到 drop 門檻就切到 DOWN
                // 避免 cooldown 內反覆觸發 → 一次 stomp 只算一次
                phase = Phase.Down;
                extremumY = footY;
            }
        }
        else // Down
        {
            // 下沉就更新最低點
            if (footY < extremumY) extremumY = footY;

            float rise = footY - extremumY;
            if (rise >= reArmRiseAmount)
            {
                phase = Phase.Up;
                extremumY = footY;
                if (logDecisions) Debug.Log($"[FootStompTrigger] re-armed (rise={rise:F3} m)", this);
            }
        }

        UpdatePedalVisual(phase == Phase.Down);
    }

    private void UpdatePedalVisual(bool pressed)
    {
        if (pedalVisual == null || pedalPressDepth <= 0f) return;
        Vector3 desired = pressed ? pedalVisualOriginal + Vector3.down * pedalPressDepth : pedalVisualOriginal;
        float a = 1f - Mathf.Exp(-pedalPressSpeed * Time.deltaTime);
        pedalVisual.localPosition = Vector3.Lerp(pedalVisual.localPosition, desired, a);
    }

    private void Fire(float drop)
    {
        if (drum != null) drum.TriggerHitExternally();
        if (logDecisions) Debug.Log($"[FootStompTrigger] STOMP! drop={drop:F3} m, foot Y={foot.position.y:F3}", this);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmo || foot == null) return;
        Vector3 footP = foot.position;

        // extremum 線：UP 是綠色 (高點)、DOWN 是青色 (低點)
        Color extColor = phase == Phase.Up ? new Color(0.4f, 1f, 0.4f) : new Color(0.4f, 0.7f, 1f);
        DrawHLine(new Vector3(footP.x, extremumY, footP.z), 0.30f, extColor);

        // 閾值線
        if (phase == Phase.Up)
        {
            DrawHLine(new Vector3(footP.x, extremumY - triggerDropAmount, footP.z), 0.22f, Color.red);
        }
        else
        {
            DrawHLine(new Vector3(footP.x, extremumY + reArmRiseAmount, footP.z), 0.22f, Color.yellow);
        }

        // 腳的當前位置
        Gizmos.color = Color.white;
        Gizmos.DrawSphere(footP, 0.02f);
    }

    private static void DrawHLine(Vector3 center, float halfW, Color c)
    {
        Gizmos.color = c;
        Gizmos.DrawLine(center - Vector3.right * halfW, center + Vector3.right * halfW);
        Gizmos.DrawLine(center - Vector3.forward * halfW * 0.5f, center + Vector3.forward * halfW * 0.5f);
    }
}

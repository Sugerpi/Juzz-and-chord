using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 大鼓踏板：玩家的腳踩進這個 trigger 體積就觸發大鼓打擊。
///
/// 設計概念：
///   - 真正可碰的「踏板」放在地面、大鼓踩鎚正前方。本元件掛在踏板上。
///   - 大鼓本體 (帶 VirtualDrum 元件) 要把 Inspector 上的 "Trigger On Touch" 取消勾選，
///     避免腳直接踢到鼓也會發聲。
///   - 踩下：trigger 偵測腳進入 → 呼叫 VirtualDrum.TriggerHitExternally()。
///
/// === 防連點 (Re-trigger Guard) ===
/// 之所以需要這個是因為 Kinect 平滑 + 抖動會讓虛擬腳在 trigger 邊界進出好幾次，
/// 等於一次實體踩踏被誤判成多次。三層保護全部要過才會擊發：
///   1. minIntervalSeconds — 全域 cooldown。打完後 X 秒內絕不再響。
///   2. reArmHeight        — 腳要實際抬離踏板表面 N 公尺才會「裝彈」。模擬真踏板物理。
///   3. requireDownwardMotion — 只認往下進入。擋掉抖動把腳從下方推進 trigger 的誤觸。
/// 另外還有 (4) minDownwardVelocity 速度門檻 — 預設關，需要「用力踏」才響時開啟。
/// </summary>
[RequireComponent(typeof(Collider))]
public class BassDrumPedal : MonoBehaviour
{
    public enum FootSide { Either, LeftOnly, RightOnly }

    [Header("連結 (Link)")]
    [Tooltip("要踩響的大鼓。把 BassDrum 物件 (帶 VirtualDrum 的那個) 拖進來。")]
    public VirtualDrum bassDrum;

    [Header("觸發條件 (Trigger Filter)")]
    [Tooltip("限制只接受哪一隻腳。建議右腳玩家選 RightOnly，避免左腳走位時誤觸。")]
    public FootSide acceptedFoot = FootSide.Either;

    [Header("防連點：第 1 層 — 全域 cooldown")]
    [Tooltip("兩次擊發最少間隔 (秒)。即使其他條件都成立，這時間內第二次擊發也會被吞掉。" +
             "想連踩 16 分音符的話最多到 0.10；防抖建議 0.15~0.25。")]
    [Min(0f)] public float minIntervalSeconds = 0.18f;

    [Header("防連點：第 2 層 — 抬腳重新裝彈")]
    [Tooltip("打完後腳要抬離踏板表面這麼高 (公尺) 才會重新『裝彈』。" +
             "Kinect 抖動造成的虛擬連發大多在 1~3 cm 範圍內，設 0.05 就能擋掉大部分。" +
             "設 0 = 關閉這層保護 (只靠第 1 層 cooldown)。")]
    [Min(0f)] public float reArmHeight = 0.05f;

    [Header("防連點：第 3 層 — 方向過濾")]
    [Tooltip("只在腳正在『往下』移動時才擊發。擋掉抖動把腳往上推進 trigger 的誤觸。" +
             "建議保持勾選。")]
    public bool requireDownwardMotion = true;

    [Header("防連點：第 4 層 — 速度門檻 (選配)")]
    [Tooltip("最小向下速度 (m/s)。0 = 任何往下都算 (推薦預設)。" +
             "設 > 0 改成『要用力踏才響』 — 緩慢漂下的腳不會觸發。")]
    [Min(0f)] public float minDownwardVelocity = 0f;

    [Header("視覺 (Visual)")]
    [Tooltip("被踩下時踏板下沉的距離 (沿 local Y 軸往下)。0 = 不動")]
    public float pressDepth = 0.02f;
    [Tooltip("下沉 / 回彈的速度，越大越乾脆")]
    public float pressSpeed = 20f;

    [Header("測試用 (Testing)")]
    [Tooltip("勾選後可以用滑鼠按住踏板模擬踩下、放開模擬抬腳。Kinect 接好後可關掉。")]
    public bool enableMouseTest = true;

    [Header("除錯 (Debug)")]
    [Tooltip("勾選後每次擊發 / 被擋下都會印一行到 Console，方便調參數。")]
    public bool logFireDecisions = false;

    // --- 每隻腳的內部狀態 ---
    private class FootState
    {
        public Transform tf;
        public float lastY;        // 上一幀 (Update 結束時) 的 Y，用來算往下移動 / 速度
        public bool armed = true;  // false = 剛擊發過、還沒抬高到 reArmHeight
        public bool insideTrigger; // 是否在 trigger 體積內 (給視覺用)
        public bool hasPriorY;     // 第一次出現的腳沒有「上一幀 Y」，避免 requireDownwardMotion 把首擊吞掉
    }

    private Vector3 originalLocalPos;
    private float pedalSurfaceY;            // 原始位置的踏板頂部 Y (世界座標)
    private float lastFireTime = -999f;
    private bool mousePressed;
    private readonly Dictionary<Collider, FootState> footStates = new Dictionary<Collider, FootState>();

    void Start()
    {
        originalLocalPos = transform.localPosition;
        Collider col = GetComponent<Collider>();
        // 在動畫還沒下沉前抓 — 之後即使踏板動畫晃動也用同一個基準。
        pedalSurfaceY = col.bounds.max.y;
        if (bassDrum == null)
        {
            Debug.LogWarning("[BassDrumPedal] 沒指定 bassDrum — 踩了不會發聲。", this);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerLimb")) return;
        if (!IsAcceptedFoot(other)) return;

        if (!footStates.TryGetValue(other, out FootState s))
        {
            s = new FootState
            {
                tf = other.transform,
                lastY = other.transform.position.y,
                armed = true,
                hasPriorY = false, // 首次進場沒有歷史 Y 可比，第 3 層放行
            };
            footStates[other] = s;
        }
        s.insideTrigger = true;

        TryFire(s);
    }

    void OnTriggerExit(Collider other)
    {
        if (footStates.TryGetValue(other, out FootState s))
        {
            s.insideTrigger = false;
            // 注意：不從 dictionary 移除。Update 還要繼續追 Y 才能判斷 re-arm。
        }
    }

    void Update()
    {
        // 1) 每幀更新每隻腳的 Y 軌跡 + 處理 re-arm
        foreach (FootState s in footStates.Values)
        {
            if (s.tf == null) continue;
            float curY = s.tf.position.y;

            // re-arm：腳抬離踏板夠高 → 允許下次擊發
            if (!s.armed && reArmHeight > 0f && curY >= pedalSurfaceY + reArmHeight)
            {
                s.armed = true;
                if (logFireDecisions) Debug.Log($"[BassDrumPedal] re-armed (foot Y={curY:F3}, threshold={pedalSurfaceY + reArmHeight:F3})", this);
            }

            s.lastY = curY;
            s.hasPriorY = true;
        }

        // 2) 視覺：踏板下沉 / 回彈
        if (pressDepth > 0f)
        {
            bool anyPressed = mousePressed;
            if (!anyPressed)
            {
                foreach (var s in footStates.Values)
                {
                    if (s.insideTrigger) { anyPressed = true; break; }
                }
            }
            Vector3 desired = anyPressed ? originalLocalPos + Vector3.down * pressDepth : originalLocalPos;
            float a = 1f - Mathf.Exp(-pressSpeed * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, desired, a);
        }
    }

    // 嘗試擊發 — 跑完所有四層防連點檢查
    private void TryFire(FootState s)
    {
        // 第 1 層：全域 cooldown
        if (Time.time < lastFireTime + minIntervalSeconds)
        {
            if (logFireDecisions) Debug.Log("[BassDrumPedal] 擋下：cooldown 未滿", this);
            return;
        }

        // 第 2 層：必須處於 armed
        if (!s.armed)
        {
            if (logFireDecisions) Debug.Log("[BassDrumPedal] 擋下：腳還沒抬到 reArmHeight 之上", this);
            return;
        }

        // 第 3 層：必須往下
        if (requireDownwardMotion && s.hasPriorY)
        {
            float curY = s.tf.position.y;
            float deltaY = s.lastY - curY; // 正值 = 往下
            if (deltaY <= 0f)
            {
                if (logFireDecisions) Debug.Log($"[BassDrumPedal] 擋下：不是往下 (Δy={deltaY:F4})", this);
                return;
            }

            // 第 4 層：速度門檻 (選配)
            if (minDownwardVelocity > 0f)
            {
                float velocityDown = deltaY / Mathf.Max(Time.fixedDeltaTime, 0.001f);
                if (velocityDown < minDownwardVelocity)
                {
                    if (logFireDecisions) Debug.Log($"[BassDrumPedal] 擋下：速度不夠 ({velocityDown:F2} m/s)", this);
                    return;
                }
            }
        }

        // 全過 → 開火
        FirePedal();
        s.armed = false;
        lastFireTime = Time.time;
        if (logFireDecisions) Debug.Log($"[BassDrumPedal] 擊發 (foot Y={s.tf.position.y:F3})", this);
    }

    // 滑鼠測試也要走 cooldown，否則狂點滑鼠也會穿過防連點。
    void OnMouseDown()
    {
        if (!enableMouseTest) return;
        if (Time.time < lastFireTime + minIntervalSeconds) return;
        mousePressed = true;
        FirePedal();
        lastFireTime = Time.time;
    }

    void OnMouseUp()
    {
        if (enableMouseTest) mousePressed = false;
    }

    private bool IsAcceptedFoot(Collider other)
    {
        KinectLimb limb = other.GetComponent<KinectLimb>();
        // 沒掛 KinectLimb (fallback / 測試用 cube) 一律允許。
        if (limb == null) return true;
        // 手不能踩踏板 — 否則手揮過去也會響。
        if (!limb.IsFoot) return false;
        if (acceptedFoot == FootSide.LeftOnly)  return limb.kind == KinectLimb.Kind.FootLeft;
        if (acceptedFoot == FootSide.RightOnly) return limb.kind == KinectLimb.Kind.FootRight;
        return true;
    }

    private void FirePedal()
    {
        if (bassDrum != null) bassDrum.TriggerHitExternally();
    }
}

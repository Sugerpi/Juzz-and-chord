using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 大鼓踏板：玩家的腳踩進這個 trigger 體積就觸發大鼓打擊。
///
/// 設計概念：
///   - 真正可碰的「踏板」放在地面、大鼓踩鎚正前方。本元件掛在踏板上。
///   - 大鼓本體 (帶 VirtualDrum 元件) 要把 Inspector 上的 "Trigger On Touch" 取消勾選，
///     避免腳直接踢到鼓也會發聲 (= 你現在不想要的「踢」)。
///   - 踩下：本 trigger 偵測腳進入 → 呼叫 VirtualDrum.TriggerHitExternally() —
///     共用鼓自己的 cooldown、音效、閃光。
///   - 抬起：腳離開 → 內部 counter 歸零、視覺踏板彈回。腳要先離開才能再踩。
///
/// 為什麼是 OnTriggerEnter 而不是速度判定：DrumLimbCalibrator 已經把腳的位置鎖在
/// 預設高度，玩家踩下去的瞬間，虛擬腳會穿過踏板上方的薄 trigger — 進入這個 trigger
/// 本身就是「踩下」事件，不必再算速度。如果要求「用力踏」才有聲音，把
/// minDownwardVelocity 設 > 0 即可走速度路線。
/// </summary>
[RequireComponent(typeof(Collider))]
public class BassDrumPedal : MonoBehaviour
{
    public enum FootSide { Either, LeftOnly, RightOnly }

    [Header("連結 (Link)")]
    [Tooltip("要踩響的大鼓。把 BassDrum 物件 (帶 VirtualDrum 的那個) 拖進來。")]
    public VirtualDrum bassDrum;

    [Header("觸發條件 (Trigger Filter)")]
    [Tooltip("限制只接受哪一隻腳。Either = 兩隻腳都可以。" +
             "建議右腳玩家選 RightOnly，避免左腳走位時誤觸。")]
    public FootSide acceptedFoot = FootSide.Either;

    [Tooltip("最小向下速度 (m/s)。0 = 進入 trigger 就算踩 (推薦從這開始)。" +
             "設 > 0 後改成『要往下衝才響』 — 避免腳放在踏板上不動也會響。")]
    public float minDownwardVelocity = 0f;

    [Header("視覺 (Visual)")]
    [Tooltip("被踩下時踏板下沉的距離 (沿 local Y 軸往下)。0 = 不動")]
    public float pressDepth = 0.02f;
    [Tooltip("下沉 / 回彈的速度，越大越乾脆")]
    public float pressSpeed = 20f;

    [Header("測試用 (Testing)")]
    [Tooltip("勾選後可以用滑鼠按住踏板模擬踩下、放開模擬抬腳。Kinect 接好後可關掉。")]
    public bool enableMouseTest = true;

    private Vector3 originalLocalPos;
    private int limbsOnPedal;     // 真實肢體踩在踏板上的數量
    private bool mousePressed;    // 滑鼠是否按著 (測試用)
    private readonly Dictionary<Collider, float> previousY = new Dictionary<Collider, float>();

    void Start()
    {
        originalLocalPos = transform.localPosition;
        if (bassDrum == null)
        {
            Debug.LogWarning("[BassDrumPedal] 沒指定 bassDrum — 踩了不會發聲。", this);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerLimb")) return;
        if (!IsAcceptedFoot(other)) return;

        previousY[other] = other.transform.position.y;
        limbsOnPedal++;

        // 速度門檻 = 0 → 進入即觸發。> 0 改由 OnTriggerStay 判定。
        if (minDownwardVelocity <= 0f) FirePedal();
    }

    void OnTriggerStay(Collider other)
    {
        if (minDownwardVelocity <= 0f) return;
        if (!other.CompareTag("PlayerLimb")) return;
        if (!IsAcceptedFoot(other)) return;
        if (!previousY.TryGetValue(other, out float prevY)) return;

        float currentY = other.transform.position.y;
        // OnTriggerStay 跑在 physics step，用 fixedDeltaTime 才正確。
        float velocityDown = (prevY - currentY) / Time.fixedDeltaTime;
        previousY[other] = currentY;

        if (velocityDown >= minDownwardVelocity) FirePedal();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("PlayerLimb")) return;
        if (previousY.Remove(other))
        {
            limbsOnPedal = Mathf.Max(0, limbsOnPedal - 1);
        }
    }

    void Update()
    {
        if (pressDepth <= 0f) return;
        bool pressed = (limbsOnPedal > 0) || mousePressed;
        Vector3 desired = pressed ? originalLocalPos + Vector3.down * pressDepth : originalLocalPos;
        float a = 1f - Mathf.Exp(-pressSpeed * Time.deltaTime);
        transform.localPosition = Vector3.Lerp(transform.localPosition, desired, a);
    }

    // 滑鼠測試：按一下就觸發一次 (按著不會連發 — 跟真踏板的單踩語意一致)
    void OnMouseDown()
    {
        if (!enableMouseTest) return;
        mousePressed = true;
        FirePedal();
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

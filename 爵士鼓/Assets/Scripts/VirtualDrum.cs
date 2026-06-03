using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 一顆鼓 / 鈸的打擊判定 + 視覺/聲音回饋。判定走兩條路：
///
/// 1) 新路 (有 KinectLimb 元件的肢體 collider)：
///    - 手 (KinectLimb.IsHand)：要握拳才算打 — 進場時已 Closed 就觸發；
///      若進場時非 Closed，待停留期間做出「非Closed → Closed」這個 transition
///      才觸發。這就是「揮下去碰到鼓的瞬間握拳 = 打」。
///    - 腳 (KinectLimb.IsFoot)：進場就打 (踩到 = hit)，停留不再觸發；
///      要再打必須離開再進入。這是踏板的自然行為。
///
/// 2) 舊路 (collider 沒掛 KinectLimb)：fallback 走原本的速度向下判定。
///    保留是為了讓還沒做遷移的 scene 仍能跑。注意：舊版本在 OnTriggerStay
///    內錯用了 Time.deltaTime，這次順手修成 Time.fixedDeltaTime — 連點問題
///    在 fallback 路徑上也會減輕。
///
/// 兩路共用 cooldownTime 防連點。
/// </summary>
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(MeshRenderer))]
public class VirtualDrum : MonoBehaviour
{
    [Header("打擊設定 (Hit Settings)")]
    [Tooltip("防連點的冷卻時間 (秒)")]
    public float cooldownTime = 0.08f;

    [Tooltip("肢體碰到鼓本體的 trigger collider 就觸發。大鼓改用獨立踏板時請取消勾選 — " +
             "鼓還是會播音 + 閃光，只是只能由 BassDrumPedal 透過 TriggerHitExternally() 觸發。")]
    public bool triggerOnTouch = true;

    [Header("Fallback：沒掛 KinectLimb 時用的速度判定")]
    [Tooltip("collider 沒有 KinectLimb 元件時的回退門檻。手部/腳部向下速度 (m/s)。" +
             "正常情況不會走到這條路。")]
    // FormerlySerializedAs 讓 Unity 把你 prefab/scene 上舊欄位 "hitVelocityThreshold"
    // 的調過值搬到這個新欄位，避免你各鼓的 threshold 全部 reset 回 1.2。
    [FormerlySerializedAs("hitVelocityThreshold")]
    public float legacyHitVelocityThreshold = 1.2f;

    [Header("視覺回饋 (Visuals)")]
    [Tooltip("被打中時的閃光顏色")]
    public Color hitColor = Color.white;
    [Tooltip("閃光持續時間 (秒)")]
    public float flashDuration = 0.1f;

    [Header("替代音效 (踩鈸合起來等用)")]
    [Tooltip("狀態啟動時改播這個音效，例如踩鈸踏板踩下時的合音")]
    public AudioClip alternateClip;
    private bool useAlternateClip = false;

    // 給外部腳本呼叫，例如踏板告訴鈸「現在我踩下去了」
    public void SetUseAlternate(bool value)
    {
        useAlternateClip = value;
    }

    [Header("測試用 (Testing)")]
    [Tooltip("勾選後可以用滑鼠左鍵點鼓來觸發，Kinect 接好後可關掉")]
    public bool enableMouseClickTest = true;

    private AudioSource audioSource;
    private MeshRenderer meshRenderer;
    private Color originalColor;
    private float lastHitTime = 0f;

    // 走新路的手：上一幀是否處於握拳狀態。用來偵測「非 Closed → Closed」的 transition。
    private readonly Dictionary<Collider, bool> handWasClosed = new Dictionary<Collider, bool>();
    // 走 fallback 路的肢體：上一幀的 Y，用來算速度。
    private readonly Dictionary<Collider, float> limbPreviousY = new Dictionary<Collider, float>();

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        meshRenderer = GetComponent<MeshRenderer>();
        originalColor = meshRenderer.material.color;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!triggerOnTouch) return;
        if (!other.CompareTag("PlayerLimb")) return;

        KinectLimb limb = other.GetComponent<KinectLimb>();
        if (limb != null)
        {
            if (limb.IsHand)
            {
                // 進場那一刻已經是拳頭 → 算打。記錄狀態給 OnTriggerStay 用。
                bool closedNow = limb.IsClosed;
                handWasClosed[other] = closedNow;
                if (closedNow && CanHit()) HitDrum();
            }
            else
            {
                // 腳：進場就打。
                if (CanHit()) HitDrum();
            }
        }
        else
        {
            // Fallback 路：記下進場高度，stay 時算速度。
            limbPreviousY[other] = other.transform.position.y;
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (!triggerOnTouch) return;
        if (!other.CompareTag("PlayerLimb")) return;

        KinectLimb limb = other.GetComponent<KinectLimb>();
        if (limb != null)
        {
            if (limb.IsHand)
            {
                // 偵測「非 Closed → Closed」的 transition。代表玩家在鼓區內握拳。
                bool closedNow = limb.IsClosed;
                bool closedBefore = handWasClosed.TryGetValue(other, out bool w) && w;
                if (closedNow && !closedBefore && CanHit()) HitDrum();
                handWasClosed[other] = closedNow;
            }
            // 腳走 KinectLimb 時 stay 不再觸發 — enter 已經處理過了。
        }
        else
        {
            // Fallback 速度判定。OnTriggerStay 跑在 physics step，所以用 fixedDeltaTime。
            if (!limbPreviousY.TryGetValue(other, out float previousY)) return;
            float currentY = other.transform.position.y;
            float velocityDown = (previousY - currentY) / Time.fixedDeltaTime;
            if (velocityDown > legacyHitVelocityThreshold && CanHit()) HitDrum();
            limbPreviousY[other] = currentY;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("PlayerLimb")) return;
        handWasClosed.Remove(other);
        limbPreviousY.Remove(other);
    }

    // 測試用：滑鼠點擊鼓也能觸發
    void OnMouseDown()
    {
        if (enableMouseClickTest && CanHit()) HitDrum();
    }

    private bool CanHit()
    {
        return Time.time >= (lastHitTime + cooldownTime);
    }

    /// <summary>
    /// 給外部腳本 (例如 BassDrumPedal) 呼叫的觸發入口。沿用同一條 cooldown，
    /// 所以連續呼叫不會穿過防連點保護。
    /// </summary>
    public void TriggerHitExternally()
    {
        if (CanHit()) HitDrum();
    }

    // --- 觸發打擊的執行動作 ---
    private void HitDrum()
    {
        lastHitTime = Time.time;

        if (useAlternateClip && alternateClip != null)
        {
            audioSource.PlayOneShot(alternateClip);
        }
        else
        {
            audioSource.Play();
        }

        StartCoroutine(ShowHitEffect());
    }

    // 切換顏色的協程
    IEnumerator ShowHitEffect()
    {
        meshRenderer.material.color = hitColor;
        yield return new WaitForSeconds(flashDuration);
        meshRenderer.material.color = originalColor;
    }
}

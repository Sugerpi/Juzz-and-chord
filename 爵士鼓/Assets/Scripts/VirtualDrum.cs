using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(MeshRenderer))]
public class VirtualDrum : MonoBehaviour
{
    [Header("打擊設定 (Hit Settings)")]
    [Tooltip("手部/腳部向下的速度門檻，數值越大需要打越大力才會有聲音")]
    public float hitVelocityThreshold = 1.2f;
    [Tooltip("防連點的冷卻時間(秒)")]
    public float cooldownTime = 0.08f;

    [Header("視覺回饋 (Visuals)")]
    [Tooltip("被打中時的閃光顏色")]
    public Color hitColor = Color.white;
    [Tooltip("閃光持續時間(秒)")]
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

    // 字典：用來記錄目前「正在判定區內」的四肢，它們在「上一幀」的 Y 軸高度
    private Dictionary<Collider, float> limbPreviousY = new Dictionary<Collider, float>();

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        meshRenderer = GetComponent<MeshRenderer>();

        // 記錄原本顏色，閃光結束後要還原
        originalColor = meshRenderer.material.color;
    }

    // 1. 當肢體剛進入打擊區塊，馬上把它的高度記下來
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("PlayerLimb"))
        {
            limbPreviousY[other] = other.transform.position.y;
        }
    }

    // 2. 當肢體「停留」在打擊區塊內時，持續計算它的向下速度
    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("PlayerLimb") && limbPreviousY.ContainsKey(other))
        {
            float currentY = other.transform.position.y;
            float previousY = limbPreviousY[other];

            // 計算向下的速度 (距離差 / 幀時間)
            float velocityDown = (previousY - currentY) / Time.deltaTime;

            if (velocityDown > hitVelocityThreshold && Time.time >= (lastHitTime + cooldownTime))
            {
                HitDrum();
            }

            limbPreviousY[other] = currentY;
        }
    }

    // 3. 當肢體離開打擊區，把它從字典裡清掉
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("PlayerLimb"))
        {
            limbPreviousY.Remove(other);
        }
    }

    // 測試用：滑鼠點擊鼓也能觸發 (Kinect 整合後可在 Inspector 關掉)
    void OnMouseDown()
    {
        if (enableMouseClickTest && Time.time >= (lastHitTime + cooldownTime))
        {
            HitDrum();
        }
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

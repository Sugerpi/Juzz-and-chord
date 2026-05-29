using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HiHatPedal : MonoBehaviour
{
    [Header("連結 (Link)")]
    [Tooltip("這個踏板要控制的踩鈸 (把 HiHat 物件拖進來)")]
    public VirtualDrum hiHatDrum;

    [Header("視覺 (Visual)")]
    [Tooltip("被踩下去時 Y 軸下沉的距離")]
    public float pressDepth = 0.03f;

    [Header("測試用 (Testing)")]
    [Tooltip("勾選後可以用滑鼠按住踏板模擬踩下，放開模擬鬆腳")]
    public bool enableMouseTest = true;

    private Vector3 originalPos;
    private int limbsOnPedal = 0;   // 真實肢體踩在踏板上的數量
    private bool mousePressed = false; // 滑鼠是否按著（測試用）

    void Start()
    {
        originalPos = transform.position;
    }

    // Kinect 的腳進入踏板範圍
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("PlayerLimb"))
        {
            limbsOnPedal++;
            UpdateState();
        }
    }

    // Kinect 的腳離開踏板範圍
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("PlayerLimb"))
        {
            limbsOnPedal = Mathf.Max(0, limbsOnPedal - 1);
            UpdateState();
        }
    }

    // 滑鼠測試：按下
    void OnMouseDown()
    {
        if (enableMouseTest)
        {
            mousePressed = true;
            UpdateState();
        }
    }

    // 滑鼠測試：放開
    void OnMouseUp()
    {
        if (enableMouseTest)
        {
            mousePressed = false;
            UpdateState();
        }
    }

    void UpdateState()
    {
        bool pressed = (limbsOnPedal > 0) || mousePressed;

        // 告訴鈸：現在要播合起來的聲音 / 還是打開的聲音
        if (hiHatDrum != null)
        {
            hiHatDrum.SetUseAlternate(pressed);
        }

        // 視覺：踏板下沉
        transform.position = pressed
            ? originalPos + Vector3.down * pressDepth
            : originalPos;
    }
}

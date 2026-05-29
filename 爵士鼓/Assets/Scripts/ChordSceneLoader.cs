using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 在 Start 時把指定的 chord 場景「附加」載入到目前的鼓場景，讓兩邊的 GameObject
/// 同時存在於同一個遊戲世界。
///
/// 用法：
///   1. 把這個元件掛到鼓場景的任一個 GameObject (例如 GameManager 或新建一個空物件)
///   2. Inspector 把 SceneName 設為要載入的場景名稱 (預設 "ChordReference")
///   3. 重要：把該場景加入 Build Settings (File ▸ Build Profiles ▸ 拖場景進去)，
///      否則 LoadSceneAsync 會失敗找不到場景
///
/// 為什麼需要：兩個場景同時跑時，Unity 規定每幀只能有「一個」AudioListener，而
/// chord 原本的場景自帶 Main Camera + AudioListener；如果不停掉，會跟鼓場景的
/// camera 衝突 (Audio listener already exists in scene 警告 + 渲染順序怪)。這個
/// 載入器在載入完成後，會自動關掉 chord 場景帶來的所有 Camera 跟 AudioListener，
/// 讓鼓場景的 Camera 當主鏡頭。
/// </summary>
public class ChordSceneLoader : MonoBehaviour
{
    [Header("要附加載入的場景")]
    [Tooltip("場景檔案名稱 (不含 .unity 副檔名)。必須在 Build Settings 裡。")]
    public string sceneName = "ChordReference";

    [Header("載入後的清理")]
    [Tooltip("關掉從附加場景帶進來的 Camera (避免跟主場景 camera 打架)")]
    public bool disableExtraCameras = true;

    [Tooltip("關掉從附加場景帶進來的 AudioListener (Unity 只允許一個)")]
    public bool disableExtraAudioListeners = true;

    [Header("除錯")]
    public bool logToConsole = true;

    void Start()
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[ChordSceneLoader] sceneName is empty, nothing to load.", this);
            return;
        }

        // 已經載入了就不要重複載 (例如熱重載、或場景在 Build Settings 是 auto-load)
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).name == sceneName)
            {
                if (logToConsole)
                    Debug.Log("[ChordSceneLoader] '" + sceneName + "' is already loaded — skipping.");
                return;
            }
        }

        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError("[ChordSceneLoader] 無法載入場景 '" + sceneName +
                           "'。請確認場景檔在 Build Settings 裡 (File ▸ Build Profiles)。", this);
            return;
        }
        op.completed += OnLoaded;
    }

    void OnLoaded(AsyncOperation op)
    {
        Scene loaded = SceneManager.GetSceneByName(sceneName);
        if (!loaded.IsValid())
        {
            Debug.LogWarning("[ChordSceneLoader] 場景 '" + sceneName + "' 載入完成但 IsValid=false?", this);
            return;
        }

        int disabledCams = 0, disabledAudio = 0;
        foreach (GameObject root in loaded.GetRootGameObjects())
        {
            if (disableExtraCameras)
            {
                foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cam.enabled) { cam.enabled = false; disabledCams++; }
                }
            }
            if (disableExtraAudioListeners)
            {
                foreach (AudioListener al in root.GetComponentsInChildren<AudioListener>(true))
                {
                    if (al.enabled) { al.enabled = false; disabledAudio++; }
                }
            }
        }

        if (logToConsole)
        {
            Debug.Log("[ChordSceneLoader] 載入完成: '" + sceneName + "'。" +
                      "停用 " + disabledCams + " 個 Camera、" +
                      disabledAudio + " 個 AudioListener。", this);
        }
    }
}

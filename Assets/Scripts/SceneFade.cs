using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 씬 전환을 검은 화면으로 감싼다. "GO를 누르면 게임이 뚝 끊겼다가 튀어나온다"는 게 고치려는 것이다.
//
// 부르는 곳: MapSelectUI.Confirm(맵선택 → 게임) · GameManager의 ReturnToTitle/Retry.
// SceneManager.LoadScene을 직접 부르는 자리가 새로 생기면 여기로 바꿔야 연출이 붙는다.
public static class SceneFade
{
    public const float FadeOutDuration = 0.5f;   // 어두워지는 시간. BGM도 이 시간에 맞춰 잦아든다 —
                                                 // 0.35s는 음악이 잦아드는 게 안 들려서 늘렸다
    public const float FadeInDuration = 0.45f;   // 밝아지는 시간(새 씬을 보여주는 쪽이라 조금 길게)

    public static void LoadScene(string sceneName)
    {
        SceneFadeRunner.Get().Begin(sceneName);
    }
}

// 씬을 넘어 살아남아야 해서(로드 도중에도 검은 화면이 떠 있어야 한다) DontDestroyOnLoad로 둔다.
// 런타임 AddComponent 전용이라 씬·프리팹에 직렬화되지 않는다 — 그래서 SceneFade.cs 안에 같이 둔다.
public class SceneFadeRunner : MonoBehaviour
{
    private static SceneFadeRunner instance;
    private Image cover;
    private bool busy;

    public static SceneFadeRunner Get()
    {
        if (instance != null) return instance;

        var go = new GameObject("SceneFade");
        Object.DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue; // 무엇보다도 위에
        go.AddComponent<GraphicRaycaster>();  // 이게 있어야 페이드 중 클릭이 막힌다(GO 연타 방지)

        var imgGo = new GameObject("Cover", typeof(RectTransform), typeof(Image));
        RectTransform rt = (RectTransform)imgGo.transform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        instance = go.AddComponent<SceneFadeRunner>();
        instance.cover = imgGo.GetComponent<Image>();
        instance.SetAlpha(0f);
        return instance;
    }

    public void Begin(string sceneName)
    {
        if (busy) return; // 페이드 도중 또 누르면 무시 — 두 번 로드하면 씬이 겹친다
        busy = true;
        StartCoroutine(Run(sceneName));
    }

    private IEnumerator Run(string sceneName)
    {
        // 게임오버 화면은 timeScale=0이다. 스케일 시간으로 재면 여기서 영원히 멈춘다 → 전부 unscaled.
        yield return Fade(0f, 1f, SceneFade.FadeOutDuration);

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;

        yield return Fade(1f, 0f, SceneFade.FadeInDuration);
        busy = false;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        cover.raycastTarget = true;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            SetAlpha(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetAlpha(to);
        cover.raycastTarget = to > 0f; // 다 밝아졌으면 클릭을 다시 통과시킨다
    }

    // 화면이 어두워지는 만큼 BGM도 물러난다. 이걸 안 하면 음악은 만땅으로 울리다가
    // 씬이 언로드되는 순간 뚝 끊긴다 — 검은 화면만으로는 그 끊김이 안 가려진다.
    private void SetAlpha(float a)
    {
        cover.color = new Color(0f, 0f, 0f, a);
        BgmMuffle.SceneFadeGain = 1f - a;
    }
}

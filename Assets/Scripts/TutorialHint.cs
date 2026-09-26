using System.Collections;
using TMPro;
using UnityEngine;

// 판 시작 한 줄 안내 — 엔딩 대사(EndingSequence의 Intro)와 같은 글자·자리로 화면 중앙에 떴다가 사라진다.
//  ① 첫 판: 조작(QWER)   ② 1차 진화(New_Evolution)를 산 뒤 첫 판: 진화 규칙
// 각각 세이브당 한 번. 둘 다 밀려 있으면 한 판에 하나씩(조작 먼저).
// 문구는 inline_*.tsv의 tutorial.*, 글자 모양은 Resources/TutorialHint 프리팹에서 고친다.
public class TutorialHint : MonoBehaviour
{
    private const string ResourcePath = "TutorialHint";
    private const string SkillsKey = "tutorial.skills";
    private const string EvolutionKey = "tutorial.evolution";

    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text label;
    [SerializeField] private float hold = 5f;
    [SerializeField] private float fade = 0.9f;

    // RunBootstrap.Awake가 판마다 부른다.
    public static void TryShow()
    {
        string key = null;
        if (SaveStore.GetInt(SkillsKey) == 0) key = SkillsKey;
        else if (SaveStore.GetInt(EvolutionKey) == 0 && SkillTreeSave.IsUnlocked(SkillEffects.EvolutionNodeId)) key = EvolutionKey;
        if (key == null) return;

        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null) { Debug.LogError("[Tutorial] Resources/" + ResourcePath + " 프리팹이 없다"); return; }

        // 띄운 순간 본 것으로 친다 — 5초 안에 죽거나 나가도 다음 판에 또 뜨지 않게.
        SaveStore.SetInt(key, 1);
        SaveStore.Save();
        var hint = Instantiate(prefab).GetComponent<TutorialHint>();
        hint.StartCoroutine(hint.Run(key));
    }

    private IEnumerator Run(string key)
    {
        // 🔴 진화 안내는 **두 장으로 나눠** 띄운다(2026-09-27 사용자 — 영어 문구가 3줄이라 한 장에 너무 많다).
        //    세이브 플래그 키(EvolutionKey)는 그대로 두고 문구 키만 쪼갠다 — 키를 바꾸면 이미 본 사람이 다시 본다.
        string[] pages = key == EvolutionKey
            ? new[] { EvolutionKey + ".1", EvolutionKey + ".2" }
            : new[] { key };

        foreach (string page in pages)
        {
            label.text = Loc.T(page);
            group.alpha = 0f;
            yield return Fade(1f);
            yield return new WaitForSeconds(hold);
            yield return Fade(0f);
        }
        Destroy(gameObject);
    }

    // 스케일 시간 — 일시정지(ESC)·레벨업 창이 뜨면 안내도 같이 멈춘다(EndingSequence와 같은 규칙).
    private IEnumerator Fade(float to)
    {
        float from = group.alpha;
        for (float t = 0f; t < fade; t += Time.deltaTime)
        {
            float k = t / fade;
            group.alpha = Mathf.Lerp(from, to, k * k * (3f - 2f * k));
            yield return null;
        }
        group.alpha = to;
    }
}

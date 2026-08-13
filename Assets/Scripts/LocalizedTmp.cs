using TMPro;
using UnityEngine;

// 씬·프리팹에 박힌 TMP 하나를 표(Game)의 키에 묶는다.
//
// 왜 Unity Localization의 LocalizeStringEvent가 아닌가 — 조회가 Loc 한 곳을 지나야
// 폴백(en이 비면 ko)과 표 캐싱이 그대로 걸린다. LocalizeStringEvent는 Loc을 우회한다.
//
// ⚠️ **런타임에 코드가 값을 덮어쓰는 TMP에는 붙이지 말 것.** 언어가 바뀌면 이 컴포넌트가
//    계산된 값(예: "1250 정수")을 정적 문구로 되돌려 버린다. 그런 자리는 코드 쪽에서 Loc.F로 짓는다.
//
// ⚠️ [RequireComponent(typeof(TMP_Text))]를 일부러 안 걸었다 — TMP_Text가 추상 클래스라
//    에디트모드 AddComponent가 자동 추가를 못 하고 조용히 null을 반환한다.
public class LocalizedTmp : MonoBehaviour
{
    [Tooltip("표(Game)의 키. 비면 아무것도 하지 않는다.")]
    public string key;

    private TMP_Text target;

    private void OnEnable()
    {
        Apply();
        Loc.LocaleChanged += Apply;
    }

    private void OnDisable() => Loc.LocaleChanged -= Apply;

    public void Apply()
    {
        if (target == null) target = GetComponent<TMP_Text>();
        if (target == null || string.IsNullOrEmpty(key)) return;
        target.text = Loc.T(key);
    }
}

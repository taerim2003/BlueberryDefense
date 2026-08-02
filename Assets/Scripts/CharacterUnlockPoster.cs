using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 스킬트리 창 우측 하단에 붙는 "다음에 열릴 캐릭터" 포스터(원티드 스타일).
// 스킬트리 화면에 두는 이유: 해금 조건이 **누적 정수**라, 정수를 쓰는 화면에서 진행도를 같이 본다.
// (스테이지 선택창은 캐릭터를 *고르는* 자리 — CharacterSelectUI 담당이라 역할이 겹치지 않는다.)
// 아직 안 열린 캐릭터 중 첫 번째를 실루엣으로 띄우고 해금 조건 진행도를 적는다.
// 전부 해금됐으면 통째로 숨는다.
//
// 배선 규칙: 이 컴포넌트는 **항상 켜져 있는 오브젝트**에 붙이고, 실제로 껐다 켜는 것은 `root`(자식 패널)다.
// 자기 자신을 끄면 다음에 패널이 열려도 OnEnable이 안 돌아 갱신 기회를 잃는다.
public class CharacterUnlockPoster : MonoBehaviour
{
    [SerializeField] private CharacterDefinition[] characters; // 감시 대상(CharacterSelectUI와 같은 로스터)
    [SerializeField] private GameObject root;                  // 포스터 본체 — 전부 해금이면 끈다
    [SerializeField] private Image silhouette;                 // 초상화(실루엣으로 덧칠)
    [SerializeField] private TMP_Text conditionText;           // 조건 진행도 두 줄

    private static readonly Color SilhouetteTint = new Color(0.06f, 0.06f, 0.08f, 0.95f);

    private void OnEnable() => Refresh();

    public void Refresh()
    {
        CharacterDefinition next = null;
        if (characters != null)
        {
            foreach (var c in characters)
                if (c != null && !c.IsUnlocked) { next = c; break; }
        }

        if (root != null) root.SetActive(next != null);
        if (next == null) return;

        if (silhouette != null)
        {
            silhouette.sprite = next.portrait;
            silhouette.enabled = next.portrait != null;
            silhouette.color = SilhouetteTint;
        }

        if (conditionText != null)
            conditionText.text = string.Join("\n", next.UnlockProgressLines());
    }
}

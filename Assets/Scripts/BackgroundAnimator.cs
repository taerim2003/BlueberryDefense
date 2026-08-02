using UnityEngine;

// 맵 배경을 여러 컷으로 돌린다(농장 4컷). RunBootstrap이 판 시작 시 런타임으로 붙인다 — 씬 배선 없음.
// 컷이 1장 이하면 아무것도 안 하므로, 프레임을 안 채운 맵은 기존처럼 정지 배경으로 남는다.
// timeScale을 따르는 Time.deltaTime을 쓴다 — 일시정지·게임오버 모달에서 배경만 계속 움직이면 어색하다.
public class BackgroundAnimator : MonoBehaviour
{
    private Sprite[] frames;
    private float frameSeconds;
    private SpriteRenderer sr;
    private float timer;
    private int index;

    public void Init(Sprite[] frames, float frameSeconds)
    {
        this.frames = frames;
        this.frameSeconds = Mathf.Max(0.01f, frameSeconds); // 0이면 프레임마다 넘어가 깜빡인다
        sr = GetComponent<SpriteRenderer>();
        index = 0;
        timer = 0f;
        if (sr != null && frames != null && frames.Length > 0) sr.sprite = frames[0];
    }

    private void Update()
    {
        if (sr == null || frames == null || frames.Length < 2) return;

        timer += Time.deltaTime;
        if (timer < frameSeconds) return;

        timer -= frameSeconds;
        index = (index + 1) % frames.Length;
        sr.sprite = frames[index];
    }
}

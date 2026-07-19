using UnityEngine;

// 짧은 스프라이트 시퀀스(예: 4프레임)를 재생하는 경량 VFX 컴포넌트. ObjectPool로 스폰/디스폰되며,
// 활성화될 때마다 첫 프레임부터 재생한다. 비루프면 마지막 프레임에서 정지(디스폰은 호출측 타이머가 담당).
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFlipbook : MonoBehaviour
{
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float fps = 16f;
    [SerializeField] private bool loop = false;

    private SpriteRenderer sr;
    private float timer;

    private void Awake() => sr = GetComponent<SpriteRenderer>();

    private void OnEnable()
    {
        timer = 0f;
        if (frames != null && frames.Length > 0) sr.sprite = frames[0];
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0) return;
        timer += Time.deltaTime;
        int frame = Mathf.FloorToInt(timer * fps);
        frame = loop ? frame % frames.Length : Mathf.Min(frame, frames.Length - 1);
        sr.sprite = frames[frame];
    }
}

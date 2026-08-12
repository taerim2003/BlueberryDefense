using UnityEngine;
using UnityEngine.UI;

// 끝없이 흐르는 타일 벽지. RawImage의 uv를 매 프레임 밀어 텍스처가 대각선으로 지나가게 한다.
// 타일 텍스처는 wrapMode=Repeat여야 이음매가 안 보인다(UI_BlueberryTile).
[RequireComponent(typeof(RawImage))]
public class ScrollingWallpaper : MonoBehaviour
{
    [SerializeField] private Vector2 speed = new Vector2(0.025f, 0.025f); // uv/초. 양수 = 화면에서 왼쪽 아래로 흐른다
    [SerializeField] private float tileSize = 320f;                       // 타일 한 변(px) — 반복 횟수 계산용

    private RawImage image;
    private RectTransform rt;

    private void Awake()
    {
        image = GetComponent<RawImage>();
        rt = (RectTransform)transform;
    }

    private void Update()
    {
        if (image == null || tileSize <= 0f) return;

        // 화면 크기 ÷ 타일 크기 = 반복 횟수. 매 프레임 다시 잡는다 —
        // OnEnable 시점엔 레이아웃 전이라 rect가 0일 수 있다.
        Vector2 size = rt.rect.size;
        Rect uv = image.uvRect;
        uv.width = size.x / tileSize;
        uv.height = size.y / tileSize;

        // unscaledDeltaTime — 이 화면은 timeScale이 0인 상태에서도 열려 있을 수 있다.
        uv.x = Mathf.Repeat(uv.x + speed.x * Time.unscaledDeltaTime, 1f);
        uv.y = Mathf.Repeat(uv.y + speed.y * Time.unscaledDeltaTime, 1f);

        image.uvRect = uv;
    }
}

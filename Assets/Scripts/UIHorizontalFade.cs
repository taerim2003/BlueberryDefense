using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 가로 방향 알파 그라데이션 — 그림을 굽지 않고 **정점 색만** 바꾼다.
// 타이틀 화면에서 오른쪽(버튼 쪽)으로 갈수록 어두워지게 해 버튼을 도드라지게 하는 용도.
//
// 왜 스프라이트가 아닌가: 그라데이션을 PNG로 구우면 화면 크기에 맞춰 **늘려 써야** 하고,
// 그건 이 프로젝트의 UI 규칙(CLAUDE.md §5-1 — 원본 크기 그대로)과 정면으로 부딪친다.
// 정점 색은 해상도와 무관해서 늘어남 자체가 없다.
//
// ⚠️ uGUI의 Image는 기본이 사각형 **4정점**이라 좌↔우 선형 보간만 된다.
//    "화면 중간부터 어두워지기 시작"을 하려면 정점이 더 필요해서 가로로 잘라 다시 채운다.
[RequireComponent(typeof(Graphic))]
public class UIHorizontalFade : BaseMeshEffect
{
    [Tooltip("왼쪽 끝 알파")]
    [SerializeField, Range(0f, 1f)] private float leftAlpha = 0f;
    [Tooltip("오른쪽 끝 알파")]
    [SerializeField, Range(0f, 1f)] private float rightAlpha = 0.55f;
    [Tooltip("이 지점(0=왼쪽 끝, 1=오른쪽 끝)부터 어두워지기 시작한다")]
    [SerializeField, Range(0f, 1f)] private float startAt = 0.45f;
    [Tooltip("1=직선. 값이 커질수록 오른쪽 끝에 몰려 더 부드럽게 깔린다")]
    [SerializeField, Range(1f, 4f)] private float curve = 1.6f;
    [Tooltip("가로 분할 수. 클수록 곡선이 매끄럽다")]
    [SerializeField, Range(2, 64)] private int steps = 24;

    private static readonly List<UIVertex> Buffer = new List<UIVertex>();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;

        Buffer.Clear();
        vh.GetUIVertexStream(Buffer);
        if (Buffer.Count < 6) return;

        // 원본 quad의 범위와 색을 첫 삼각형에서 뽑는다(Image의 Simple/Sliced 모두 사각 영역이다).
        Rect r = graphic.rectTransform.rect;
        UIVertex sample = Buffer[0];
        Vector2 uvMin = sample.uv0, uvMax = sample.uv0;
        foreach (UIVertex v in Buffer)
        {
            uvMin = Vector2.Min(uvMin, v.uv0);
            uvMax = Vector2.Max(uvMax, v.uv0);
        }

        vh.Clear();
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float x = Mathf.Lerp(r.xMin, r.xMax, t);
            float u = Mathf.Lerp(uvMin.x, uvMax.x, t);

            UIVertex top = sample, bottom = sample;
            Color32 c = ColorAt(t, sample.color);
            top.color = c; bottom.color = c;
            top.position = new Vector3(x, r.yMax, 0f);
            bottom.position = new Vector3(x, r.yMin, 0f);
            top.uv0 = new Vector2(u, uvMax.y);
            bottom.uv0 = new Vector2(u, uvMin.y);
            vh.AddVert(bottom);
            vh.AddVert(top);
        }
        for (int i = 0; i < steps; i++)
        {
            int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            vh.AddTriangle(b0, t0, t1);
            vh.AddTriangle(b0, t1, b1);
        }
    }

    private Color32 ColorAt(float t, Color32 baseColor)
    {
        float k = startAt >= 1f ? 0f : Mathf.Clamp01((t - startAt) / (1f - startAt));
        k = Mathf.Pow(k, curve);
        Color32 c = baseColor;
        c.a = (byte)Mathf.RoundToInt(Mathf.Lerp(leftAlpha, rightAlpha, k) * baseColor.a);
        return c;
    }
}

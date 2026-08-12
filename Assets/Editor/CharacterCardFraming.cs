using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Animations;

// 캐릭터 선택 카드에서 도는 공격 모션의 프레임·크기·위치를 **아트에서 다시 굽는다.**
//
// 왜 도구인가: 공격 프레임 PNG를 다시 그리면 캔버스 크기와 몸통 위치가 바뀌는데,
// 카드는 preserveAspect로 캔버스를 맞추므로 몸통이 제멋대로 커지거나 밀린다.
// 실제로 파인애플이 150x60 -> 150x96으로 바뀌면서 구워둔 값이 통째로 어긋났다.
// **그림을 고쳤으면 이걸 다시 돌리면 된다.**
//
// ⚠️ Title 씬이 열려 있어야 한다 — 보정값이 카드 썸네일의 표시 크기에 의존한다.
public static class CharacterCardFraming
{
    // 인게임 클립 속도 그대로면 카드에서는 너무 빨라 안 보인다(사용자 요청으로 2배 느리게).
    private const float CardSlowdown = 2f;

    [MenuItem("Window/Blueberry Defense/캐릭터 카드 공격모션 재계산")]
    private static void Menu() => Debug.Log(Rebake());

    public static string Rebake()
    {
        var sb = new StringBuilder();

        RectTransform thumb = FindCardThumb();
        if (thumb == null)
            return "실패: 캐릭터 카드의 Thumb을 못 찾았다. Title 씬을 열고 다시 실행할 것.";

        float W = thumb.rect.width, H = thumb.rect.height;
        sb.Append("카드 썸네일 표시 크기 ").Append(W.ToString("0.##")).Append("x").Append(H.ToString("0.##")).Append("\n");

        foreach (var guid in AssetDatabase.FindAssets("t:CharacterDefinition"))
        {
            var cd = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (cd == null) continue;

            if (!PullFramesFromAnimator(cd, sb)) continue;
            if (cd.portrait == null || cd.attackFrames.Length == 0) continue;

            Sprite p = cd.portrait, f = cd.attackFrames[0];
            Rect pBody = OpaqueBounds(p), fBody = OpaqueBounds(f);

            // preserveAspect가 각 그림에 적용하는 배율
            float pScale = Mathf.Min(W / p.rect.width, H / p.rect.height);
            float fScale = Mathf.Min(W / f.rect.width, H / f.rect.height);

            // 몸통(불투명 영역)이 초상화 때와 같은 크기로 보이게
            cd.attackFrameScale = (pBody.height * pScale) / (fBody.height * fScale);

            // 몸통 중심이 초상화 때 있던 자리로 오게. 두 그림이 캔버스 중심에서 얼마나 치우쳤는지의 차.
            Vector2 pOff = (pBody.center - new Vector2(p.rect.width, p.rect.height) * 0.5f) * pScale;
            Vector2 fOff = (fBody.center - new Vector2(f.rect.width, f.rect.height) * 0.5f) * (fScale * cd.attackFrameScale);
            Vector2 move = pOff - fOff;
            cd.attackFrameOffset = new Vector2(move.x / W, move.y / H);

            EditorUtility.SetDirty(cd);
            sb.Append("[").Append(cd.name).Append("] 초상화 ").Append(p.rect.width).Append("x").Append(p.rect.height)
              .Append("(몸통 ").Append(pBody.width).Append("x").Append(pBody.height).Append(")")
              .Append(" / 공격 ").Append(f.rect.width).Append("x").Append(f.rect.height)
              .Append("(몸통 ").Append(fBody.width).Append("x").Append(fBody.height).Append(")")
              .Append("  → scale=").Append(cd.attackFrameScale.ToString("0.####"))
              .Append(" offset=").Append(cd.attackFrameOffset.ToString("F4"))
              .Append(" (화면px ").Append(move.ToString("F1")).Append(")\n");
        }

        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // 카드 템플릿은 CharacterSelectRoot 아래에 있다. 런타임 복제본이 아니라 원본 템플릿을 잡는다.
    private static RectTransform FindCardThumb()
    {
        foreach (var rt in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rt.name != "Thumb" || rt.hideFlags != HideFlags.None) continue;
            for (Transform a = rt; a != null; a = a.parent)
                if (a.name == "CharacterSelectRoot") return rt;
        }
        return null;
    }

    // 인게임 Attack 클립의 스프라이트를 그대로 가져온다. 마지막 프레임을 유지하려고 넣은
    // 중복 키는 카드에서는 의미가 없어 뺀다.
    private static bool PullFramesFromAnimator(CharacterDefinition cd, StringBuilder sb)
    {
        var ac = cd.animatorController as AnimatorController;
        if (ac == null) ac = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Player.controller");
        if (ac == null) { sb.Append("[").Append(cd.name).Append("] 애니메이터를 못 찾았다\n"); return false; }

        foreach (var clip in ac.animationClips)
        {
            if (!clip.name.Contains("Attack")) continue;

            var frames = new List<Sprite>();
            float last = -1f, step = 0.1f;
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                foreach (var k in AnimationUtility.GetObjectReferenceCurve(clip, b))
                {
                    var sp = k.value as Sprite;
                    if (sp == null) continue;
                    if (frames.Count > 0 && frames[frames.Count - 1] == sp) continue;
                    if (last >= 0f && k.time > last) step = k.time - last;
                    last = k.time;
                    frames.Add(sp);
                }
            if (frames.Count == 0) continue;

            cd.attackFrames = frames.ToArray();
            cd.attackFrameSeconds = step * CardSlowdown;
            return true;
        }
        sb.Append("[").Append(cd.name).Append("] Attack 클립이 없다\n");
        return false;
    }

    // 불투명 픽셀의 경계상자(스프라이트 로컬 픽셀). 읽기 권한은 잠깐 켰다 되돌린다.
    private static Rect OpaqueBounds(Sprite sp)
    {
        string path = AssetDatabase.GetAssetPath(sp.texture);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        bool was = ti.isReadable;
        if (!was) { ti.isReadable = true; ti.SaveAndReimport(); }

        int w = (int)sp.rect.width, h = (int)sp.rect.height;
        var px = sp.texture.GetPixels((int)sp.rect.x, (int)sp.rect.y, w, h);
        int minX = w, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 0.05f)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

        if (!was) { ti.isReadable = false; ti.SaveAndReimport(); }
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}

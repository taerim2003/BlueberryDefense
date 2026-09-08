using UnityEngine;

// UI 트리에서 부품을 찾는 공용 헬퍼. 네 화면(도감·맵 선택·캐릭터 선택·일시정지)이
// 같은 함수를 각자 들고 있던 것을 한 곳으로 모았다.
public static class UITreeUtil
{
    // 🔴 자식을 **깊이** 찾는다 — 직속 자식만 보면 안 된다.
    //    `Transform.Find`는 직속 자식만 보기 때문에, 칸 안에 액자를 한 겹 더 두는 배치
    //    (`Node_R0T1/IconBox/Icon`)나 씬에서 Thumb을 Bg 아래로 옮기는(마스크를 걸려고) 순간
    //    **조용히 null**이 되어 아이콘이 통째로 안 그려지거나 카드가 템플릿 그림을 그대로 쓴다.
    public static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}

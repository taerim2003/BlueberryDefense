using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 빌드셋 슬롯 1개(저장/불러오기 버튼). SkillTreeUI가 런타임 생성·바인딩.
public class BuildSlotUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button loadButton;

    public void Bind(int slot, System.Action<int> onSave, System.Action<int> onLoad)
    {
        if (label != null) label.text = "빌드 " + slot;
        if (saveButton != null) saveButton.onClick.AddListener(() => onSave(slot));
        if (loadButton != null) loadButton.onClick.AddListener(() => onLoad(slot));
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;

// "태양의 가호" 상점 그리드의 한 칸. 업그레이드 이름 + 현재/최대 레벨 표시, 클릭 시 선택 콜백.
public class UpgradeCell : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private Button button;

    private MetaUpgradeId id;
    private System.Action<MetaUpgradeId> onClick;

    public void Bind(MetaUpgradeId upgradeId, System.Action<MetaUpgradeId> clickCallback)
    {
        id = upgradeId;
        onClick = clickCallback;
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke(id));
        }
        Refresh();
    }

    public void Refresh()
    {
        MetaUpgradeDef def = MetaUpgrades.Get(id);
        int level = MetaSave.GetLevel(id);
        if (nameText != null) nameText.text = def.Name;
        if (levelText != null) levelText.text = level + " / " + def.MaxLevel;
    }
}

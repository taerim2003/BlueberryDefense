using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// "태양의 가호" 업그레이드 상점 패널. 8종 그리드 + 하단 상세(설명/가격/구매).
// 셀은 cellPrefab을 런타임에 8개 생성해 채운다.
public class UpgradeShopUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform cellContainer;
    [SerializeField] private UpgradeCell cellPrefab;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private TMP_Text detailNameText;
    [SerializeField] private TMP_Text detailDescText;
    [SerializeField] private TMP_Text detailCostText;
    [SerializeField] private Button buyButton;
    [SerializeField] private Button closeButton;

    private readonly List<UpgradeCell> cells = new();
    private MetaUpgradeId selected;
    private bool built;

    private void Awake()
    {
        if (buyButton != null) buyButton.onClick.AddListener(OnBuy);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open()
    {
        if (!built) Build();
        if (panelRoot != null) panelRoot.SetActive(true);
        selected = MetaUpgradeId.Attack;
        RefreshAll();
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Build()
    {
        if (cellPrefab == null || cellContainer == null) return;
        foreach (MetaUpgradeDef def in MetaUpgrades.All)
        {
            UpgradeCell cell = Instantiate(cellPrefab, cellContainer);
            cell.gameObject.SetActive(true);
            cell.Bind(def.Id, Select);
            cells.Add(cell);
        }
        built = true;
    }

    private void Select(MetaUpgradeId id)
    {
        selected = id;
        RefreshDetail();
    }

    private void OnBuy()
    {
        if (MetaSave.TryPurchase(selected)) RefreshAll();
    }

    private void RefreshAll()
    {
        if (currencyText != null) currencyText.text = MetaSave.Currency + " 정수";
        foreach (UpgradeCell c in cells) c.Refresh();
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        MetaUpgradeDef def = MetaUpgrades.Get(selected);
        int level = MetaSave.GetLevel(selected);
        bool maxed = level >= def.MaxLevel;

        if (detailNameText != null) detailNameText.text = def.Name;

        if (detailDescText != null)
        {
            string current = string.Format(def.DescFormat, FormatNum(def.TotalAt(level)));
            if (maxed)
                detailDescText.text = current + "  (MAX)";
            else
                detailDescText.text = current + "  →  " + string.Format(def.DescFormat, FormatNum(def.TotalAt(level + 1)));
        }

        if (maxed)
        {
            if (detailCostText != null) detailCostText.text = "MAX";
            if (buyButton != null) buyButton.interactable = false;
        }
        else
        {
            int cost = def.CostForLevel(level);
            if (detailCostText != null) detailCostText.text = cost + " 정수";
            if (buyButton != null) buyButton.interactable = MetaSave.Currency >= cost;
        }
    }

    private static string FormatNum(float v)
        => Mathf.Approximately(v, Mathf.Round(v)) ? ((int)v).ToString() : v.ToString("0.#");
}

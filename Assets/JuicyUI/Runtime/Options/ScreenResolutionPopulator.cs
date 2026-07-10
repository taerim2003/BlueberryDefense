using System.Linq;
using UnityEngine;
using TMPro;

// DropdownOption과 같은 오브젝트에 추가
// onValueChanged → Apply 연결
[DefaultExecutionOrder(1)] // DropdownOption.Start() 이후 실행되어 목록을 덮어씀
public class ScreenResolutionPopulator : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private Vector2Int[] supportedResolutions;

    private Resolution[] _resolutions;

    private void Start()
    {
        var all = Screen.resolutions
            .GroupBy(r => (r.width, r.height))
            .Select(g => g.Last());

        _resolutions = (supportedResolutions != null && supportedResolutions.Length > 0)
            ? all.Where(r => supportedResolutions.Any(s => s.x == r.width && s.y == r.height)).ToArray()
            : all.ToArray();

        dropdown.ClearOptions();
        dropdown.AddOptions(_resolutions.Select(r => $"{r.width} x {r.height}").ToList());

        dropdown.value = FindCurrentIndex();
        dropdown.RefreshShownValue();
    }

    public void Apply(int index)
    {
        var r = _resolutions[index];
        Screen.SetResolution(r.width, r.height, Screen.fullScreen);
    }

    private int FindCurrentIndex()
    {
        for (int i = 0; i < _resolutions.Length; i++)
            if (_resolutions[i].width  == Screen.currentResolution.width &&
                _resolutions[i].height == Screen.currentResolution.height)
                return i;
        return _resolutions.Length - 1;
    }
}

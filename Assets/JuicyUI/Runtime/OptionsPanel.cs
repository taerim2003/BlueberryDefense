using UnityEngine;

public class OptionsPanel : MonoBehaviour
{
    public void ResetToDefaults()
    {
        foreach (var resettable in GetComponentsInChildren<IResettable>())
            resettable.ResetToDefault();
    }
}

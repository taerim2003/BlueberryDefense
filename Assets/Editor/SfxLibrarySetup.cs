using System.IO;
using UnityEditor;
using UnityEngine;

// SfxLibrary 에셋을 정해진 자리에 만든다.
//
// 왜 메뉴로 뺐나: SfxPlayer가 Resources.Load<SfxLibrary>("SfxLibrary")로 찾으므로
// **경로와 파일명이 곧 배선**이다. 손으로 만들면 폴더나 이름이 어긋나 조용히 무음이 된다.
// 게다가 SfxSlot이 구조체라 그냥 만들면 volume이 전부 0이다 — 여기서 기본값까지 깔아 준다.
public static class SfxLibrarySetup
{
    private const string FolderPath = "Assets/Resources";
    private const string AssetPath = FolderPath + "/SfxLibrary.asset";

    [MenuItem("Blueberry Defense/사운드 라이브러리 만들기")]
    private static void CreateOrSelect()
    {
        SfxLibrary existing = AssetDatabase.LoadAssetAtPath<SfxLibrary>(AssetPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("사운드 라이브러리가 이미 있다 — 인스펙터에서 음원을 꽂으면 된다: " + AssetPath);
            return;
        }

        if (!Directory.Exists(FolderPath))
        {
            Directory.CreateDirectory(FolderPath);
            AssetDatabase.Refresh();
        }

        SfxLibrary library = ScriptableObject.CreateInstance<SfxLibrary>();
        library.ApplyDefaultVolumes(); // CreateInstance로는 Reset()이 안 불린다 — 안 부르면 전 슬롯이 무음
        AssetDatabase.CreateAsset(library, AssetPath);
        AssetDatabase.SaveAssets();

        // 만들어 놓고 되읽어 확인한다(에셋을 코드로 만들면 조용히 실패한 적이 있다).
        SfxLibrary reloaded = AssetDatabase.LoadAssetAtPath<SfxLibrary>(AssetPath);
        if (reloaded == null)
        {
            Debug.LogError("사운드 라이브러리 생성에 실패했다: " + AssetPath);
            return;
        }

        Selection.activeObject = reloaded;
        EditorGUIUtility.PingObject(reloaded);
        Debug.Log("사운드 라이브러리를 만들었다: " + AssetPath +
                  " — 인스펙터의 슬롯에 음원을 드래그하면 바로 소리가 난다(빈 슬롯은 조용히 무시된다).");
    }
}

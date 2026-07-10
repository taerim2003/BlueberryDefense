#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class SpriteCreator
{
    [MenuItem("JuicyUI/Create White Circle Sprite")]
    public static void CreateWhiteCircle()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size / 2f;
        float radius = center - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(radius - dist + 0.5f); // anti-alias
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();

        const string path = "Assets/Resources/Sprites/WhiteCircle.png";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.Refresh();

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 100;
        importer.filterMode = FilterMode.Bilinear;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        Debug.Log("WhiteCircle sprite created at " + path);
    }
}
#endif

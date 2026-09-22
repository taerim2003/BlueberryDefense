using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 빌드가 끝나면 프로젝트 루트 `Licenses/`의 파일을 exe 옆 `Licenses/`로 복사한다.
// 폰트(OFL)는 게임에 묶여 나가므로 저작권 고지·라이선스를 같이 싣는다(OFL FAQ 1.20) — 인게임 크레딧 대신 이 방식.
public class CopyLicensesPostBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        string src = Path.Combine(Directory.GetCurrentDirectory(), "Licenses");
        if (!Directory.Exists(src)) { Debug.LogWarning("[Licenses] 프로젝트 루트에 Licenses 폴더가 없다 — 복사 생략"); return; }

        string dst = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "Licenses");
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        Debug.Log("[Licenses] " + Directory.GetFiles(src).Length + "개 복사 → " + dst);
    }
}

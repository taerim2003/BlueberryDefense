// 한 판의 선택값(맵·캐릭터)을 담는 static 홀더. 선택 화면이 씬 로드 전에 채우고,
// 게임 씬의 RunBootstrap이 읽어 적용한다. static이라 SceneManager.LoadScene를 넘어 값이 유지됨
// — 여기서 직접 초기화하지 말 것(선택값이 지워짐). MetaBonuses/MetaRun은 판마다 리셋되지만 이건 아님.
// null이면 RunBootstrap이 직렬화된 기본값으로 폴백 → Battle 씬 단독 실행도 현재와 동일하게 동작.
public static class RunConfig
{
    public static MapDefinition Map;
    public static CharacterDefinition Character; // 선택 화면이 채움. null이면 프리팹 기본값(현행)으로 동작
    public static int AscensionLevel = 1;        // 이번 판 승천(난이도) 등급. 선택 화면이 채움(기본 1 = 현재 난이도)

    // 이번 실행에서 판을 한 번이라도 시작했는가. TitleBgm이 첫 접속곡/복귀곡을 가르는 데 쓴다.
    // RunBootstrap.Awake(=판 시작)가 세우고, 승패는 안 가린다. static이라 씬 전환은 넘어가고
    // 게임을 껐다 켜면 false로 돌아온다(= 세션 기준. 사용자 결정).
    public static bool HasPlayedThisSession;
}

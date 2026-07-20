// 한 판의 선택값(맵·캐릭터)을 담는 static 홀더. 선택 화면이 씬 로드 전에 채우고,
// 게임 씬의 RunBootstrap이 읽어 적용한다. static이라 SceneManager.LoadScene를 넘어 값이 유지됨
// — 여기서 직접 초기화하지 말 것(선택값이 지워짐). MetaBonuses/MetaRun은 판마다 리셋되지만 이건 아님.
// null이면 RunBootstrap이 직렬화된 기본값으로 폴백 → SampleScene 단독 실행도 현재와 동일하게 동작.
public static class RunConfig
{
    public static MapDefinition Map;
    // Character는 Phase 3(캐릭터/스킬 데이터)에서 CharacterDefinition과 함께 추가.
}

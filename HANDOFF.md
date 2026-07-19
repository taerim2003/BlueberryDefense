# HANDOFF.md — 세션 인계 문서

<!--
📋 유지 가이드 — 이 문서는 "다음 세션이 지금 뭘 해야 하는가"만 담는다. 길어지지 않게 유지할 것.
- 담을 것: 현재 빌드 상태, 미해결 이슈, 다음 할 일. 그것뿐.
- 지울 것: 완료·검증된 피쳐/버그/이슈는 즉시 삭제 (git 히스토리에 이미 남음).
- 세션 로그를 누적하지 말 것. 매 세션 새로 쓰는 게 아니라, 끝난 항목은 지우고 새 항목만 갱신.
- 게임 스펙(스킬 수치·시스템 규칙 등)은 여기 말고 GDD.md에. 여기엔 "무엇을 확인/수정해야 하는지"만.
- 목표: 항상 한 화면 안에 읽힐 것.
-->

## 🧭 North Star
> **"스킬을 강화해 적들을 시원하게 쓸어버리자."**
> QWER 액티브 4 + 패시브 4를 진화 트리로 강화해 빌드를 만드는 벙커 디펜스. 상세 스펙은 `GDD.md` §4 참고.

---

## 현재 상태 (2026-07-19)

**빌드**: 컴파일 에러 없음. 아래 전부 **script-execute 로직테스트 + 플레이모드 스모크로 검증됨**. **사용자 실플레이(체감·밸런스)는 미검증** — 수치는 감으로 잡은 초기값.

### 밸런스/시스템 4건
1. **코어 루프 타이머→물량 기반**: `StageData.spawnCount`만큼 스폰 후 정지, **쿼터 소진+잔몹 0이면 클리어**(타이머 폐기). `EnemySpawner`가 SpawnRatio 소유, `GameManager` 폴링.
2. **클리어 20→15 + 대왕블루베리 보스**: `FinalStage=15`, 태양결정 15클리어만. 15라운드 마지막 물량으로 보스 1회 등장. **대왕블루베리 스프라이트 적용 완료**(3프레임 플립북·HP1500·이속0.6·flipX off). 죽으면 **일반+리젠트+UFO 30마리 팝콘 분출**(`Enemy.PopIn` 중력 아크) + **폭발 VFX**(`VFX_2D_Explosion_Big_01_Color`). `Enemy.deathSpawnPrefabs[]`/`deathSpawnCount`/`deathBurstVfxPrefab`.
3. **멀티히트 + 메이플식 데미지**: `Enemy.TakeSkillHit(base,crit,source)` — 자연타수+산탄버프, perHit=base/자연타수(버프 히트는 **추가 데미지**), 서브히트별 크리·첫 히트만 낙뢰. **전 스킬(Orb·Whirlwind·EagleDrop·Sniping·Homing)이 TakeSkillHit 통일**. `DamageNumber` 순번 오프셋+딜레이로 위로 주루룩.
4. **스탯 노드 레벨제**: `SkillTreeSave` HashSet→레벨맵(id:level, 구버전 lv1 흡수). Normal만 레벨(에셋 maxLevel), Gate/ActiveSkill 1회. 좌클릭 레벨업/우클릭 레벨환불. 다음레벨=기본×1.5^현재레벨.

### 신규 액티브 스킬 3종 (enum Sniping=5, Homing=6, Shotgun=7)
- **스나이핑**: 최고체력 적 5회 저격. Route1 타겟수↑ / Route2 피해+스플래시(`Effect_SplashSniping`) / Route3 자동시전. 이펙트는 타겟당 1회.
- **호밍 미사일**(`HomingMissile.cs`): 추적 미사일 5발·성장형(`EquippedSkill.GrowthStacks`, 판 한정). Route1 개수N배 / Route2 폭발 / Route3 성장률↑. 명중 즉시 소멸(수명 2.5s).
- **산탄 장착**: 5초 타수버프(`PlayerSkills.GlobalBonusHits`). Route1 타수추가 / Route2 최고공격력 1개·2배 / Route3 전체5회공격+기절.
- VFX 프리팹(`Effect_Sniping`/`Effect_SplashSniping`/`Homing_Missile`, `SpriteFlipbook.cs`)·아이콘 배열(LevelUpUI·HUD `activeIcons[5..7]`)·LevelUpUI 후보 배선 완료. 진화 연계조건 없음(`HasPathPrereq`가 연계 미지정 시 true).

---

## ★ 다음 세션 — 실플레이 밸런스 튜닝 (전부 미검증 초기값)
- [ ] **신규 스킬 밸런스**: 3종 데미지/쿨(`GetDefaultDamage/Cooldown`)·진화 **T1/T3 수치**(내가 채운 값, `DescribePathEffect`/`ApplyPathTierEffect`)·호밍 성장률·산탄 타수/지속.
- [ ] **호밍 아이콘 임시**(`Icon_Rewind`) — 전용 아이콘 없어 대체. 확정/교체 필요(`activeIcons[6]`).
- [ ] **밸런스 노브**: `spawnCount`(StageTable)·보스 HP1500/분출30/이속0.6·`BasicAttackHits=3`·`LevelCostGrowth=1.5`·per-level 효과.
- [ ] (기존) 트리 UI 조작감·인게임 체감(오버힐/ESC요약/방패빈도 등) 실플레이 확인.

---

## ⚠️ 미해결 이슈 / 주의
- **⚠️ 씬 데이터 손실 주의**: 이번 세션에 빌더 `script-execute`의 `EditorSceneManager.OpenScene(Single)`이 사용자의 **미저장 Title 씬 꾸밈을 덮어씀**(사용자가 복구). → **Title 씬은 스크립트로 열지 말 것**. 코드/에셋 경로 선호. 메모리 `[[dont-overwrite-user-scenes]]`.
- **에디터 인스펙터 예외(무해)**: 플레이 진입 시 `ObjectPreview.DrawPreview ... Image destroyed` — 인스펙터 프리뷰 표시 오류로 게임/빌드 무관. 하이라키 선택 해제 시 사라짐.
- **노션 토큰 노출**(기존): 폐기·재발급 권장.
- (기존) **사운드 볼륨 이중곱** — 우선순위 낮음. `SfxPlayer`/`AudioThrottle`/`ObjectPool`/Vefects.
- (참고) ONBOARDING의 `홈\.mcp.json` 노션 방식 자동로드 안 됨 → user scope 등록이 정답. 정정 필요.

---

## 🎨 스킬 이펙트 픽셀 크기 컨벤션
스킬 이펙트 스프라이트는 배경과 픽셀 밀도가 안 맞아, **그림은 작게 그리고 엔진에서 1.5배로 표시**한다(플레이어 딸기 프리팹 `localScale=1.5`에 맞춤).
- 새 이펙트 프리팹은 기존값에 곱하지 말고 `localScale`을 **직접 (1.5,1.5,1.5)로** 세팅.
- PPU **32**, Main Camera Orthographic Size 5(화면 세로 = 10유닛).
- 공식: `원본 그림 px = (목표 월드 유닛 × 32) ÷ 1.5` (예: 화면 세로 꽉 채우는 세로형 = 64 × 213px)
- 카메라 Size나 플레이어 localScale이 바뀌면 이 배율을 전부 다시 계산할 것.

---

## 보류 중인 결정 / 백로그
- UI 시스템: uGUI + TextMeshPro 확정 (한글 폰트 = `Galmuri11 SDF`)
- 배포(itch.io 등)는 아직 진행 안 함
- 아웃게임 컬렉션(캐릭터/스킬 해금) = 스킬트리 이후 Phase
- 진화 시스템·메타 신규 수치는 감으로 잡은 값 — 플레이테스트로 조정 필요

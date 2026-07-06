# HANDOFF.md — 세션 인계 문서

## 현재 상태 (2026-07-07)

**빌드 상태**: Must Have #1~#3 전부 완료 + Should Have 대부분 완료 (스테이지/몬스터 확장만 남음). 전 항목 플레이테스트로 검증 완료, 컴파일 에러 없음.

**마지막 작업**: 액티브 스킬 QWER 슬롯 획득 시스템, 패시브 5종, 스킬 젬 4종, 보물 블루베리, 배경/캐릭터 아트 적용까지 한 세션에 몰아서 완료. Unity-MCP로 Claude가 씬 작업을 직접 수행하는 워크플로가 완전히 자리잡음 (CLAUDE.md §6 참고).

> **다음 세션 = 스테이지 20개 + 몬스터 5종 확장.** 몬스터 아트(3종 추가 필요) 나오면 바로 붙일 수 있음.

---

## 🧭 North Star — 게임 비전

> **"스킬을 강화해 적들을 시원하게 쓸어버리자."**
> QWER 스킬 + 스킬 젬 강화로 빌드를 만드는 벙커 디펜스. 핵심 루프는 GDD.md §2 참고.

---

## ★ 다음 세션 시작 시 체크리스트

- [ ] 추가 몬스터 3종 아트 필요 (24×24~32×32, PPU 32) — 나오면 Enemy 프리팹 변형으로 추가
- [ ] 스테이지 20개 스폰 테이블 설계 (난이도 곡선은 플레이테스트로 조정 — SESSION_ZERO Known Unknown)
- [ ] (선택) 독수리 투하 스킬 비주얼 프리팹 (`eagleDropPrefab`, 현재 null이라 로직만 동작 — 데미지는 정상)
- [ ] (선택) 스킬 젬 4종의 "스킬별 호환 풀" 세부 매핑 — 현재는 임시로 전 스킬에 전 젬 호환 처리해둠. 원래 SESSION_ZERO §5 Known Unknown이었던 항목, 나중에 밸런싱 시 세분화 가능

---

## 🎨 도트 그래픽 제작 리스트

> PPU(Pixels Per Unit) 32 기준 — 카메라 Orthographic Size 5, 16:9라서 실효 해상도 320×180.

| 항목 | 상태 | 비고 |
|---|---|---|
| 플레이어 캐릭터 (32×48) | ✅ 완료 | `Player.png` + 공격 애니 3프레임 (`Player1~3.png`) |
| 일반 블루베리 (24×24) | ✅ 완료 | `Blueberry_basic.png` + 걷기 애니 4프레임 |
| 보물 블루베리 | ✅ 완료 | 일반과 동일, 코드에서 색만 골드로 틴트 (별도 그림 불필요) |
| 배경 | ✅ 완료 | 320×180 직접 그림, PPU 18로 화면 꽉 채움 |
| 추가 몬스터 3종 | ⬜ 미정 | Should Have, 스테이지 확장 시 필요 |
| 독수리 (스킬 이펙트) | ⬜ 미정 | 없어도 로직(데미지)은 정상 작동 |
| 스킬 아이콘 (QWER UI) | ⬜ 미정 | 지금은 텍스트 라벨로 대체 중 |

**플레이어/블루베리 둘 다 1.5배 스케일** 적용됨 (원본 32px 기준보다 화면에서 좀 더 크게 보이도록 사용자가 직접 조정).

---

## 코드 변경 요약 (이번 세션 — 대규모)

### 신규 스크립트
- `GameManager.cs`, `PlayerHealth.cs`, `Enemy.cs`, `EnemySpawner.cs` — Must Have #1 (스폰/이동/데미지/게임오버)
- `PlayerSkills.cs`, `Projectile.cs`, `Whirlwind.cs`, `Orb.cs` — 기본공격/회오리/오브 (Must Have #2 + Should Have)
- `LightningStorm.cs` — 낙뢰 (6초간 피격 시 30% 확률 추가 피해)
- `GemType.cs` — 스킬 젬 4종 enum
- `PlayerExperience.cs`, `LevelUpUI.cs` — 경험치/레벨업/3택1 UI (Must Have #3)
- `PlayerPassives.cs` — 패시브 5종 (힘/건강/지식/암살/리프레쉬)

### PlayerSkills 아키텍처 (중요, 다음 세션이 알아야 할 것)
- 스킬은 **키 고정이 아니라 획득 순서대로 Q→W→E→R 슬롯에 배정** (`EquippedSkill.Key`)
- 시작 시 기본공격만 Q에 장착, 나머지는 레벨업/보물 블루베리에서 획득
- `EquippedSkill.Level`이 5의 배수에 도달하면 그 배수만큼 젬이 없으면 추가 강화 불가 (`CanUpgradeSkill`)
- 젬 히트 이펙트(자수정=슬로우, 가넷=취약)는 Projectile/Whirlwind/Orb/EagleDrop에 `ApplyGemSlow`/`ApplyGemVulnerable` 플래그로 전달

### 씬/프리팹
- `Assets/Prefabs/`: Enemy_Blueberry, Enemy_TreasureBlueberry(변형), Projectile_BasicAttack, Whirlwind_Skill, Orb_Skill
- `Assets/Animations/`: Blueberry_Walk, Player_Idle, Player_Attack + 각 Animator Controller
- Canvas(Screen Space Overlay) + LevelUpPanel(3버튼) + EventSystem(InputSystemUIInputModule)
- `Assets/Sprites/`: Player.png(+공격3프레임), Blueberry_basic.png(+걷기4프레임), Background.png — 전부 PPU/Point Filter/무압축 설정 완료

### 설정/에셋
- `.mcp.json`, `UserSettings/AI-Game-Developer-Config.json` — Unity-MCP 로컬 연결
- `Assets/Vefects/Pixel Craft VFX URP/` — VFX 에셋 (2D URP 버전 포함, 아직 실제 스킬에는 미연결 — 플레이스홀더 도형 사용 중)

---

## 다음 할 일

1. 몬스터 아트 3종 + 스테이지 스폰 테이블로 콘텐츠 볼륨 확장 (Should Have 마지막 항목)
2. VFX_2D 프리팹을 실제 스킬 이펙트로 교체 (지금은 UISprite 사각형 플레이스홀더)
3. 스킬 아이콘 그림 → 3택1 UI 텍스트를 아이콘으로 교체

---

## 보류 중인 결정 / 백로그

- UI 시스템: uGUI 확정 사용 중
- 스킬 젬 호환 매핑: 임시로 전체 호환 처리 — 밸런싱 단계에서 스킬별로 제한할지 결정 필요
- 독수리 투하 비주얼: 로직은 완성, 프리팹만 비어있음 (아트 나오면 연결)

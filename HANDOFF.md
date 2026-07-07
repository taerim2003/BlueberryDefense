# HANDOFF.md — 세션 인계 문서

## 현재 상태 (2026-07-08)

**빌드 상태**: Must Have 전부 + Should Have 대부분 완료. UI/HUD를 TextMeshPro 기반으로 완전히 새로 구현 완료 (스테이지/HP/레벨/EXP/패시브/액티브 QWER 슬롯 전부 실동작). 스킬 5종에 타격 이펙트 연결 완료. 전 항목 플레이테스트로 검증, 컴파일 에러 없음.

**마지막 작업**: 지난 세션 잔여 버그(블루베리 스폰 위치, 투사체 크기) 수정 → HUD 전체를 uGUI Text에서 TextMeshPro(Galmuri11 한글 폰트)로 전환하며 새로 구축 → 사용자가 그린 아이콘 14종 + 프레임 아트를 HUD·레벨업 카드에 전부 연결 → 데미지 숫자 팝업, 쿨타임/글로벌쿨 마스크, EXP/HP 바 실동작까지 완성 → 스킬 이펙트(VFX) 여러 라운드 시행착오 끝에 "타격 이펙트만 유지, 트레일은 제거" 로 정리. 자세한 내용은 `d:\unity\prototyping-kit\journal\2026-07-07.md` 세션 2 참고.

> **다음 세션 시작 전 사용자 확인**: 사용자가 다음 세션 전까지 스프라이트 3종(기본공격/회오리/오브 실제 인게임 모양)을 직접 그리기로 함 — 아래 체크리스트 최상단 참고. 그 다음은 스테이지 20개 + 몬스터 5종 확장.

---

## 🧭 North Star — 게임 비전

> **"스킬을 강화해 적들을 시원하게 쓸어버리자."**
> QWER 스킬 + 스킬 젬 강화로 빌드를 만드는 벙커 디펜스. 핵심 루프는 GDD.md §2 참고.

---

## ★ 다음 세션 시작 시 체크리스트

- [ ] **사용자가 그릴 예정: 스킬 실제 인게임 스프라이트 3종** (지금은 Unity 기본 원 placeholder에 색만 다르게 칠해둔 상태 — UI 아이콘과는 별개임을 이번에 확인함)
  - 기본 공격 투사체: 32×32px, 1장
  - 회오리 투사체: 32×32px, 4장 (스핀 사이클 애니메이션)
  - 오브 투사체: 32×32px, 1~3장 (펄스 애니메이션 선택)
  - 공통: 투명 배경, 테두리 없음, 굵고 단순한 실루엣. 나오는 대로 `Projectile_BasicAttack` / `Whirlwind_Skill` / `Orb_Skill` 프리팹의 SpriteRenderer에 바로 교체
  - (선택) 독수리 투하 스킬용 실제 독수리 그림 — 지금은 메테오+폭발 VFX로만 표현 중, 없어도 무방
- [ ] 추가 몬스터 3종 아트 필요 (24×24~32×32, PPU 32) — 나오면 Enemy 프리팹 변형으로 추가
- [ ] 스테이지 20개 스폰 테이블 설계 (난이도 곡선은 플레이테스트로 조정 — SESSION_ZERO Known Unknown)
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
| 독수리 (스킬 이펙트) | ⬜ 미정 | 없어도 메테오+폭발 VFX로 대체 동작 중 |
| 스킬 아이콘 (QWER UI 등, 14종) | ✅ 완료 | 액티브5/패시브5/젬4, HUD·레벨업 카드에 전부 연결됨 |
| 아이콘 프레임 | ✅ 완료 | 쿨타임 마스크도 프레임 모양 그대로 적용 |
| **기본공격/회오리/오브 실제 인게임 스프라이트** | ⬜ **다음 세션 사용자 작업** | 지금은 Unity 기본 원 placeholder — 위 체크리스트 참고 |

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
- `Assets/Vefects/Pixel Craft VFX URP/` — VFX 에셋. **타격/발동 순간에 한 번 터지는 이펙트에만 사용** (아래 참고). 지속되는 트레일/모션 효과에는 이 팩의 픽셀 밀도가 우리 도트 스타일과 안 맞아서 전부 제거함 — 자세한 이유는 CLAUDE.md §6 또는 세션 일기 참고.

### 이번 세션 (2026-07-08) 추가 사항
- **TextMeshPro 전면 전환**: `Assets/Resources/TMP Settings.asset` 신규 생성(원래 없었음), 기본 폰트 Galmuri11 SDF로 설정. `HUDController.cs`, `LevelUpUI.cs` 등 전부 `TMP_Text`/`TextMeshProUGUI` 사용으로 변경.
- **HUD 완성** (`HUDController.cs`): 스테이지 텍스트(상단중앙), HP바(우상단), 레벨+패시브 슬롯(좌하단), EXP바+레벨텍스트(하단중앙), 액티브 QWER 슬롯(우하단, 쿨타임 마스크 + 글로벌쿨 표시 + 남은 쿨타임 초 표시, 프레임 모양 그대로 마스킹).
- **데미지 숫자 팝업**: `Enemy.cs` 하단에 `DamageNumber` 클래스 추가(새 .cs 파일이 컴파일러에 안 잡히는 버그 우회 — 기존 파일에 병합), `DamageNumber.prefab` 신설.
- **레벨업/젬 선택 카드**: 크기 확대, 아이콘 이미지 추가, 신규 스킬은 설명 텍스트·강화/젬은 수치 효과 텍스트 추가, 오토사이즈로 텍스트 넘침 방지.
- **EXP바/HP바 버그 수정**: Unity `Image` 컴포넌트가 `sprite == null`이면 `Filled` 타입이어도 fillAmount를 무시하고 항상 꽉 찬 사각형을 그리는 특성 때문에 진행이 안 보였음 — 흰색 단색 스프라이트(`UI_SolidFill.png`) 만들어서 해결.
- **스킬 타격 이펙트 5종 (전부 다른 이펙트)**: 기본공격=Impact Sparks, 회오리=Projectile Wind Impact, 오브=Magic Impact, 낙뢰(패시브 프록)=Lightning, 독수리투하=Explosion Big(적 위치마다 메테오 낙하 후 폭발). 몬스터 사망 시 Vanish, 플레이어 레벨업 시 Level Up 이펙트 추가.
- **VFX 트레일 시도 후 철회**: 회오리/오브/기본공격에 지속 이펙트(Vapor/Shield/Fireball 등)를 붙였다가 픽셀 밀도 불일치로 전부 제거. `Whirlwind.cs`/`Orb.cs`/`Projectile.cs`는 `impactVfxPrefab` 필드로 타격 시 1회성 이펙트만 스폰.
- **사망(Vanish)/독수리 메테오·임팩트(Explosion Big) 이펙트도 도트 크기 과대 문제 발견**: 원인은 이 VFX들의 원본 월드 스케일이 우리 캐릭터(약 1유닛)보다 훨씬 커서(Explosion Big 원본 약 7유닛) 같은 텍스처 픽셀이 훨씬 넓게 늘어나 보였던 것 (셰이더의 `_Pixelate` 옵션 자체는 꺼져 있어 무관함 — 순수 스케일 문제). `Enemy.cs`/`PlayerSkills.cs`에서 인스턴스 생성 직후 `localScale`을 0.2~0.25로 축소해서 캐릭터 스케일에 맞춤.
- **발견한 것**: `Projectile_BasicAttack`/`Whirlwind_Skill`/`Orb_Skill` 프리팹이 이번 세션 전까지 전부 Unity 기본 원(placeholder) 스프라이트였음 (UI 아이콘만 그려져 있었고 실제 인게임 모양은 방치돼 있었음) — 위 체크리스트 참고.

---

## 다음 할 일

1. **사용자가 그릴 스프라이트 3종(기본공격/회오리/오브) 받으면 바로 교체** — 체크리스트 최상단 참고
2. 몬스터 아트 3종 + 스테이지 스폰 테이블로 콘텐츠 볼륨 확장 (Should Have 마지막 항목)
3. (선택) 독수리 투하 실제 그림, 스킬별 트레일 이펙트(그리게 될 경우)

---

## 보류 중인 결정 / 백로그

- UI 시스템: uGUI + TextMeshPro 확정 사용 중
- 스킬 젬 호환 매핑: 임시로 전체 호환 처리 — 밸런싱 단계에서 스킬별로 제한할지 결정 필요
- 독수리 투하 비주얼: 메테오+폭발 VFX로 동작 중, 실제 독수리 그림은 선택 사항
- 외부 VFX 에셋팩 재사용 원칙: 타격/발동 순간의 1회성 이펙트에만 사용, 지속 트레일/모션은 픽셀 스타일 불일치 위험 크므로 직접 그리는 쪽으로 방향 전환

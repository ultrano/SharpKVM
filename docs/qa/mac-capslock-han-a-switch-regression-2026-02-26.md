# macOS CapsLock(한/A) 전환 회귀 QA 리뷰 패키지 (2026-02-26)

## 1. 범위 및 기준
- 프로젝트: `SharpKVM`
- 결함 ID: `DEFECT-MAC-CAPSLOCK-001`
- 결함 등급: `CRITICAL`
- 기준 명세: `docs/SharpKVM_상세명세서.md` (9.4 키보드 전송, 17. 알려진 구현 특성)
- UI 참조: `docs/images/ui-layout-main.svg`, `docs/images/ui-layout-stage.svg`

## 2. 자동화 재현 결과
- 실행 로그: `artifacts/qa/2026-02-26/test-mac-capslock-regression.log`
- 실행 시각(KST): `2026-02-26 21:40`
- 실행 명령:
  - `dotnet test SharpKVM.Tests/SharpKVM.Tests.csproj -c Release --filter "MacInputSourceHotkeyMapperTests"`
- 결과: `FAIL (3/14)`
- 실패 테스트:
  - `IsCapsLockPlainSwitch_True_WhenOnlyCapsLockFlagExists`
  - `ComputeCapsLockOptionEnabled_True_WhenCapsLockFlagOnlyHotkeyExists`
  - `Matches_True_ForCapsLockTrigger_WhenOnlyTriggerKeyIsPressed`
- 실패 시그니처(공통):
  - `Assert.True() Failure`
  - `Expected: True / Actual: False`

## 3. 재현 매트릭스 (QA 실행 기준)
| TC | macOS | 키보드 | 입력소스 | 전환 경로 | CapsLock 옵션 | 기대 결과 | 우선순위 |
|---|---|---|---|---|---|---|---|
| TC-01 | 15 (Sequoia) | 내장 | 2벌식+ABC | 핫키 | ON | 한/영 즉시 전환 | P0 |
| TC-02 | 15 (Sequoia) | 내장 | 2벌식+ABC | 마우스 | ON | 한/영 즉시 전환 | P0 |
| TC-03 | 15 (Sequoia) | 내장 | 2벌식+ABC | 자동 포커스 | ON | 한/영 즉시 전환 | P0 |
| TC-04 | 14 (Sonoma) | 내장 | 2벌식+ABC | 핫키 | ON | 한/영 즉시 전환 | P1 |
| TC-05 | 13 (Ventura) | 내장 | 2벌식+ABC | 핫키 | ON | 한/영 즉시 전환 | P1 |
| TC-06 | 15 (Sequoia) | 외장(USB) | 2벌식+ABC | 핫키 | ON | 한/영 즉시 전환 | P1 |
| TC-07 | 15 (Sequoia) | 내장 | 2벌식+ABC | 핫키 | OFF | 전환 차단(정상) | P1 |
| TC-08 | 15 (Sequoia) | 내장 | 2벌식+ABC | 핫키→마우스→핫키 | ON | 왕복 후 정상 유지 | P0 |
| TC-09 | 15 (Sequoia) | 내장 | 2벌식+ABC | 핫키 | ON | 100회 왕복 스트레스 통과 | P1 |

## 4. 실패→해결 로그 매핑 (CapsLock 이벤트/상태 동기화)
| 구분 | 실패(현재 코드) | 해결(수정 후 기대) | 증빙 |
|---|---|---|---|
| 옵션 판정 | `IsCapsLockPlainSwitch_True_WhenOnlyCapsLockFlagExists` 실패 (`Actual: False`) | 동일 테스트 PASS | `artifacts/qa/2026-02-26/test-mac-capslock-regression.log` |
| 상태 동기화 | `ComputeCapsLockOptionEnabled_True_WhenCapsLockFlagOnlyHotkeyExists` 실패 (`Actual: False`) | 동일 테스트 PASS | `artifacts/qa/2026-02-26/test-mac-capslock-regression.log` |
| 핫키 매칭 | `Matches_True_ForCapsLockTrigger_WhenOnlyTriggerKeyIsPressed` 실패 (`Actual: False`) | 동일 테스트 PASS | `artifacts/qa/2026-02-26/test-mac-capslock-regression.log` |
| 런타임 keyDown/flagsChanged | `capslock_enabled` false 또는 매칭 실패 시 전환 미발생 | `[MacInput][RX] KeyDown handled by input-source handler: VcCapsLock` + `Input Source Hotkey Triggered (CapsLock)` | 실기 로그 수집 대기 (개발 수정 후 30초 캡처) |

## 5. 개발 수정 요구사항(QA 확정)
- 대상 파일(개발팀 수정 지점): `Platform/MacInputSourceHotkeys.cs`
- 요구사항:
  - `TriggerKey == CapsLock`인 핫키는 `MacModifierFlags`의 CapsLock 자기 비트(`0x00010000`)를 plain 판단에서 허용해야 함
  - `ComputeCapsLockOptionEnabled`는 위 조건의 plain CapsLock 핫키를 `enabled=true`로 계산해야 함
  - `Matches` 비교 시 `CapsLock` 트리거 단독 입력이 `RequiredModifiers`와 정상 일치해야 함
- 완료 판정 기준:
  - `MacInputSourceHotkeyMapperTests` 14/14 PASS
  - 실기 로그에 `Input Source Hotkey Triggered (CapsLock)` 확인

## 6. 윈도우 영향도 회귀 자동화 결과
- 실행 로그: `artifacts/qa/2026-02-26/test-windows-impact-regression.log`
- 실행 시각(KST): `2026-02-26 21:40`
- 실행 명령:
  - `dotnet test SharpKVM.Tests/SharpKVM.Tests.csproj -c Release --filter "FullyQualifiedName!~MacInputSourceHotkeyMapperTests"`
- 결과: `PASS (40/40)`
- 해석:
  - mac CapsLock 결함 재현 테스트를 제외한 기존 회귀군은 모두 통과
  - 본 결함은 mac 입력소스 핫키 판정 로직에 국한되며, 윈도우 공통 경로 회귀는 자동화 기준에서 미검출

## 7. QA 게이트 판정
- 판정: `승인 보류 (BLOCKED)`
- 사유:
  - CapsLock 회귀 3건 자동화 FAIL
  - 전환 직후 30초 keyDown/flagsChanged 실기 로그의 해결 증빙 미제출
- 재심사 통과 조건:
  - 회귀 3건 PASS 로그 제출
  - TC-01~TC-09 실기 증빙(영상+로그) 제출
  - Windows 영향도 회귀 재실행 PASS 유지

## 8. 첨부 아티팩트
- `artifacts/qa/2026-02-26/test-baseline-without-new-cases.log`
- `artifacts/qa/2026-02-26/test-mac-capslock-regression.log`
- `artifacts/qa/2026-02-26/test-windows-impact-regression.log`
- `artifacts/qa/2026-02-26/test-run-summary.txt`

## 9. 최신 실행 요약 동기화
- 요약 파일: `artifacts/qa/2026-02-26/test-run-summary.txt`
- 동기화 항목:
  - `mac_capslock_regression_suite: failed=3, passed=11, total=14`
  - `windows_impact_regression_suite: failed=0, passed=40, total=40`
- 판정 반영:
  - QA 게이트 `승인 보류` 유지 (mac CapsLock 회귀 3건 미해결)

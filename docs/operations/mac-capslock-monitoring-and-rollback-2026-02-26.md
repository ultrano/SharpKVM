# macOS CapsLock(한/A) 핫픽스 운영 체크리스트 및 롤백 절차 (2026-02-26)

## 1. 문서 목적
- 대상 결함: `DEFECT-MAC-CAPSLOCK-001`
- 목적: 운영팀 조건부 승인 항목(배포 모니터링, 롤백, 성공률 지표)을 릴리스 패키지에 포함
- 기준 문서: `docs/SharpKVM_상세명세서.md` (9.4)

## 2. 배포 전 체크리스트
| 항목 | 기준 | 상태 |
|---|---|---|
| QA 자동화 회귀 | `MacInputSourceHotkeyMapperTests` 신규 3건 포함 PASS | 미충족(현재 3 FAIL) |
| 윈도우 영향도 회귀 | mac 회귀군 제외 `40/40 PASS` | 충족 |
| mac 실기 검증 | TC-01~TC-09(13/14/15 + 내장/외장 + 경로별) 완료 | 미충족 |
| 런타임 로그 증빙 | 전환 직후 30초 keyDown/flagsChanged + 입력소스 변경 로그 | 미충족 |
| 릴리스 빌드 | `dotnet build SharpKVM.csproj -c Release` 성공 | 개발 수정 후 재확인 필요 |
- 기준 실행 시각(KST): `2026-02-26 21:40`
- 기준 요약 파일: `artifacts/qa/2026-02-26/test-run-summary.txt`

## 3. 배포 후 모니터링 체크리스트
| 시간 구간 | 확인 항목 | 정상 기준 | 경고 기준 |
|---|---|---|---|
| T+0 ~ T+30분 | CapsLock 토글 성공률 | `>= 99%` | `< 99%` |
| T+0 ~ T+30분 | `CapsLock toggle execution failed` 로그 | `0건` | `1건 이상` |
| T+0 ~ T+30분 | `No input source hotkey matched` (CapsLock trigger) | `0건` | `1건 이상` |
| T+30 ~ T+24시간 | mac 전환 후 입력 불가 문의 | `0건` | `1건 이상` |
| T+30 ~ T+24시간 | 윈도우 연결/입력 회귀 신고 | `0건` | `1건 이상` |

## 4. CapsLock 성공률 로그 모니터링 정의
- 수집 대상 로그 키워드:
  - 성공: `Input Source Hotkey Triggered (CapsLock)`
  - 실패: `CapsLock toggle execution failed`
  - 매칭 실패: `No input source hotkey matched for trigger=VcCapsLock`
- 지표 공식:
  - `capslock_success_rate = success_count / (success_count + failure_count + mismatch_count) * 100`
- 알림 임계치:
  - `P1 알림`: 5분 윈도우에서 성공률 `< 99%`
  - `P0 알림`: 5분 내 실패 또는 매칭실패 로그 `>= 3건`

## 5. 핫픽스 롤백 절차
1. 배포 모니터링에서 P0 조건 발생 시 즉시 신규 배포 중단
2. 직전 안정 릴리스 태그(또는 배포 아티팩트)로 클라이언트/서버 바이너리 복원
3. 롤백 후 10분 내 재검증:
   - 윈도우↔맥 연결 상태
   - 기본 입력/마우스 전송
   - CapsLock 입력소스 전환(기존 동작 수준)
4. 운영 공지 채널에 롤백 시간/사유/영향 범위 공유
5. QA가 동일 시나리오(TC-01, TC-08, TC-09) 재실행 후 서비스 상태 재판정

## 6. 사용자 공지/가이드 반영안
### 6.1 공지 초안
- 제목: `macOS CapsLock(한/A) 입력소스 전환 안정성 개선 안내`
- 본문:
  - macOS 전환 직후 CapsLock 한/영 전환이 간헐적으로 동작하지 않던 문제를 수정 중입니다.
  - 배포 후 문제가 지속되면 `Client > Enable CapsLock Input Source Switch` 설정과 macOS 입력소스 옵션을 확인해 주세요.
  - 문제 재발 시 앱 로그(`[MacInput]`)와 macOS 버전 정보를 함께 전달해 주세요.

### 6.2 사용자 점검 가이드
1. macOS 시스템 설정에서 `CapsLock으로 입력 소스 전환` 옵션이 켜져 있는지 확인
2. SharpKVM Client 탭에서 `Enable CapsLock Input Source Switch`가 켜져 있는지 확인
3. 접근성 권한이 허용되어 있는지 확인
4. 이상 시 로그에 `Input Source Hotkey Triggered (CapsLock)` 존재 여부 확인

## 7. 현재 운영 판정
- 상태: `조건부 승인 유지`
- 차단 요인: QA 회귀 3건 FAIL + mac 실기 해결 로그 미제출
- 해제 조건: QA 게이트 통과 후 본 문서 2~4항목 재검증 완료
- 최신 동기화: `2026-02-26 21:40 KST` 재실행 결과 반영

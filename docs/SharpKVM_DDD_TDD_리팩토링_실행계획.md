# SharpKVM DDD/TDD 리팩토링 실행 계획서

문서 버전: 1.0  
작성일: 2026-02-28  
대상 저장소: `SharpKVM`  
기준 문서: `AGENTS.md`, `docs/SharpKVM_상세명세서.md`, `docs/images/ui-layout-main.svg`, `docs/images/ui-layout-stage.svg`

## 1. 문서 목적
이 문서는 SharpKVM 코드베이스를 DDD/TDD 친화 구조로 전환하기 위한 상세 실행 계획이다.  
핵심 목적은 다음 세 가지다.

1. 도메인 규칙을 UI/네트워크/OS 의존성에서 분리해 유지보수 가능성을 높인다.
2. 기능 추가 시 TDD(실패 테스트 작성 -> 구현 -> 리팩토링) 루프를 기본 개발 흐름으로 정착한다.
3. 리팩토링 중에도 기존 동작(명세서 기준)을 깨지 않도록 안전한 점진 전환(스트랭글러 패턴)을 적용한다.

## 2. 현재 상태 진단 (요약)
다음은 현재 코드베이스에서 리팩토링이 필요한 핵심 지점이다.

1. UI 오케스트레이터 비대화
- `UI/MainWindow.cs`가 서버/클라이언트 수명주기, 입력 라우팅, 레이아웃 정책, 클립보드 동기화를 동시에 담당한다.
- 규모: `MainWindow.cs` 2393 라인, `MainWindow.Clipboard.cs` 237 라인.

2. 의존성 방향 역전
- `Networking/ClientHandler.cs`가 `MainWindow`를 직접 참조하며 UI 메서드를 호출한다.
- 인프라 계층이 프레젠테이션 계층에 의존해 DDD 계층 분리 원칙을 위반한다.

3. 테스트 커버리지의 불균형
- 순수 로직 테스트는 존재하나(`LaunchArgumentParser`, `InputPacketSerializer`, 일부 Mac 매핑),
  핵심 시나리오(accept loop, handshake/stream read loop, layout orchestration, clipboard/file/image path) 테스트가 부족하다.
- 테스트 코드 총량 392 라인, 제품 코드 핵심 영역 3745 라인(주요 파일 기준)으로 보호망이 약하다.

4. 테스트 실행 환경 불일치
- 앱 타깃은 `net8.0`, 테스트 타깃은 `net9.0`이라 환경에 따라 즉시 `dotnet test`가 실패할 수 있다.

## 3. 리팩토링 목표 및 성공 지표 (KPI)
### 3.1 구조 목표
1. 계층 분리
- Domain: 비즈니스 규칙, 불변식, 값 객체
- Application: 유스케이스/오케스트레이션
- Infrastructure: TCP, 파일, OS, 프로세스, 클립보드, 훅
- Presentation: Avalonia 뷰 + 사용자 인터랙션 어댑터

2. 의존성 규칙
- `Presentation -> Application -> Domain`
- `Infrastructure -> Domain/Application contracts`
- `Domain`은 상위 계층 참조 금지

### 3.2 테스트 목표
1. Domain 계층 라인 커버리지 80% 이상
2. Application 계층 핵심 유스케이스 커버리지 70% 이상
3. 회귀 위험이 큰 6대 흐름 자동화
- 서버 시작/중지
- 클라이언트 연결/재연결
- Hello/PlatformInfo 핸드셰이크
- Clipboard Text/File/Image 송수신
- 레이아웃 Snap/Free 배치 전환
- 입력 전송(마우스/키보드) 경계 동작

4. PR 게이트
- `dotnet build -c Release` 성공
- `dotnet test` 성공
- 신규 유스케이스는 실패 테스트 선작성 증적 포함

### 3.3 코드 구조 목표
1. `MainWindow.cs` 책임 축소
- 목표: 2393 -> 700 라인 이하
- 나머지는 Application 서비스 및 어댑터로 분산

2. 직접 의존 제거
- `ClientHandler -> MainWindow` 직접 참조 제거
- 이벤트/포트 인터페이스 기반 통신으로 대체

## 4. 리팩토링 범위
### 4.1 포함 범위
1. 네트워크/프로토콜 처리 경계 재설계
2. 레이아웃 규칙 도메인화
3. 클립보드 동기화 정책 분리
4. Mac 입력소스/핫키 처리 분리 및 테스트 강화
5. 테스트 인프라(단위/통합/회귀) 확장
6. 문서 동기화 (`docs/SharpKVM_상세명세서.md`, SVG)

### 4.2 제외 범위 (이번 단계)
1. 기능 스펙 확장(신규 사용자 기능)
2. 네트워크 보안(TLS/인증)의 완성 구현
- 단, 설계 훅(포트/정책)만 선반영

## 5. 목표 아키텍처 (To-Be)
## 5.1 권장 프로젝트 구조
```text
src/
  SharpKVM.Domain/
    Layout/
    Clipboard/
    Session/
    InputRouting/
  SharpKVM.Application/
    Ports/
    UseCases/
    DTOs/
  SharpKVM.Infrastructure/
    Networking/
    Clipboard/
    Platform/
    Persistence/
  SharpKVM.Presentation.Avalonia/
    UI/
    ViewModels/
    Composition/
tests/
  SharpKVM.Domain.Tests/
  SharpKVM.Application.Tests/
  SharpKVM.Infrastructure.Tests/
  SharpKVM.IntegrationTests/
```

## 5.2 점진 이행 원칙
기존 폴더를 즉시 대이동하지 않고 아래 2단계로 진행한다.

1. 1차: 기존 프로젝트 내부에서 namespace/폴더 분리
- 예: `Core/Domain/*`, `Core/Application/*`, `Platform/Infrastructure/*`

2. 2차: 멀티 프로젝트 분리
- 컴파일 경계 강제 및 의존성 규칙 강화

## 5.3 도메인 경계(초안)
1. Session Context
- 개념: 원격 클라이언트 세션, 연결 상태, handshake 완료 여부
- Aggregate: `ClientSession`

2. Layout Context
- 개념: 화면 배치, 스냅/프리 모드, 인접/경계 규칙
- Aggregate: `WorkspaceLayout`

3. Clipboard Context
- 개념: 텍스트/파일/이미지 동기화 정책, 중복 방지 해시
- Aggregate: `ClipboardSyncState`

4. Input Routing Context
- 개념: 로컬 입력을 어떤 원격 클라이언트로 라우팅할지 결정
- Domain Service: `InputRoutePolicy`

5. Platform Context (Infrastructure 중심)
- 개념: OS별 입력소스 전환, 접근성 상태 조회, clipboard API
- Domain이 아닌 Adapter 책임

## 6. TDD 운영 원칙
1. 새 유스케이스 추가/변경 시 반드시 실패 테스트부터 작성한다.
2. 도메인 규칙 변경은 Domain 테스트에서 먼저 붉은불(red)을 만들고 구현한다.
3. 인프라 코드는 계약(포트) 기반 계약 테스트 + 최소 통합 테스트를 함께 작성한다.
4. 버그 수정은 "재현 테스트 -> 수정 -> 회귀 테스트" 순서로 진행한다.
5. PR 템플릿에 아래 항목을 강제한다.
- "먼저 작성한 실패 테스트 목록"
- "리팩토링 후 제거한 중복/결합"
- "명세서/문서 동기화 여부"

## 7. 단계별 실행 계획 (상세)
## 7.1 Phase 0: 베이스라인 및 안전망 구축 (1주)
목표: 리팩토링 전 동작을 고정하고 실패 지점을 빠르게 감지한다.

작업 항목
1. 테스트 타깃 프레임워크 정렬
- 옵션 A: 테스트를 `net8.0`으로 일치
- 옵션 B: CI/개발환경에 .NET 9 SDK 강제
- 산출물: 표준 개발환경 문서 + CI 검증

2. Characterization Test 작성
- 대상: `ClientHandler.HandshakeAsync`, `ReadPayloadAsync`, `MainWindow`의 clipboard 동작 핵심 분기
- 기존 동작을 "있는 그대로" 잠그는 테스트 작성

3. 아키텍처 규칙 테스트 도입
- Domain 계층에서 Avalonia/TcpClient 참조 금지 규칙 자동 검사

완료 기준
1. `dotnet build -c Release` 통과
2. `dotnet test` 통과
3. 주요 흐름 10개 이상 characterization test 확보

## 7.2 Phase 1: 포트(인터페이스) 도입 및 결합 해소 (1~2주)
목표: UI/Networking 직접 결합 제거 시작.

작업 항목
1. Application 포트 정의
- `IClipboardSink`, `IFileTransferSink`, `IImageSink`, `IClientConnectionEvents`
- `ClientHandler`는 `MainWindow` 대신 포트 인터페이스 사용

2. `ClientHandler` 어댑터화
- 패킷 수신 -> 이벤트 발행
- UI 호출 제거

3. Composition Root 구성
- `Program` 또는 별도 부트스트랩에서 포트 구현체 주입

완료 기준
1. `ClientHandler`에 `MainWindow` 타입 참조 없음
2. 클립보드/파일/이미지 수신 경로가 이벤트 기반으로 동작
3. 기존 동작 회귀 테스트 통과

## 7.3 Phase 2: Layout 도메인 추출 (2주)
목표: 배치 규칙을 UI 이벤트 코드에서 분리한다.

작업 항목
1. `WorkspaceLayout` Aggregate 설계
- 상태: 클라이언트 배치 맵, 모드(Snap/Free), anchor 정보
- 행위: place, move, snap, unsnap, remove, neighbor 탐색

2. `LayoutGeometry` 책임 재배치
- 순수 기하 규칙은 Domain 서비스로 유지
- UI 좌표/렌더링 관심사는 Presentation으로 이동

3. `MainWindow` 축소
- 드래그/드롭 이벤트 -> Application Command 호출
- 계산/검증 로직은 Domain/Application으로 위임

테스트
1. Snap 인접 규칙 경계값 테스트
2. Free 모드 겹침 보정 테스트
3. 화면 경계 진입/복귀 경로 테스트

완료 기준
1. 레이아웃 관련 규칙 테스트 30개 이상
2. `MainWindow`에서 레이아웃 계산 직접 수행 코드 60% 이상 제거

## 7.4 Phase 3: Clipboard 도메인/유스케이스 분리 (1~2주)
목표: 텍스트/파일/이미지 동기화 정책을 독립 유스케이스화.

작업 항목
1. `ClipboardSyncState` 모델 도입
- 캡처 해시, 마지막 송수신 상태, 중복 방지 정책 캡슐화

2. UseCase 분리
- `CaptureLocalClipboardUseCase`
- `SyncClipboardToRemoteUseCase`
- `ReceiveRemoteClipboardUseCase`

3. 인프라 어댑터 분리
- Avalonia clipboard 접근, 파일 zip 처리, 이미지 직렬화를 포트 뒤로 숨김

테스트
1. 텍스트 반복 송신 방지
2. 파일/이미지 우선순위 처리
3. payload limit 초과 처리

완료 기준
1. `MainWindow.Clipboard.cs`에서 정책 분기 70% 이상 제거
2. Clipboard 유스케이스 단위 테스트 20개 이상

## 7.5 Phase 4: 입력 라우팅/세션 관리 분리 (2주)
목표: 서버/클라이언트 루프와 입력 전달 결정 로직을 유스케이스화.

작업 항목
1. Session Aggregate 도입
- handshake 상태, platform 정보, 해상도, 연결 상태

2. 유스케이스 도입
- `StartServerUseCase`, `StopServerUseCase`, `AcceptClientUseCase`
- `StartClientUseCase`, `ReconnectPolicy`
- `RouteInputToRemoteUseCase`

3. 타이머/백그라운드 루프 분리
- mouse sender loop, reconnect loop를 인터페이스화된 스케줄러로 분리

테스트
1. 정상 handshake + 실패 handshake
2. 재연결 backoff 정책
3. remote active 전환 시 입력 suppress 동작

완료 기준
1. 서버/클라이언트 루프의 핵심 분기를 UI 코드 바깥으로 이동
2. 세션/라우팅 테스트 25개 이상 추가

## 7.6 Phase 5: 플랫폼 의존성 캡슐화 (1주)
목표: macOS/Windows 특화 코드를 인프라 어댑터 경계로 정리.

작업 항목
1. `MacInputSourceHotkeyProvider` 계약 분리
- plist 읽기/파싱, 명령 실행을 인터페이스로 분리

2. `CursorManager`, `ClipboardHelper` 추상화
- 운영체제별 구현 분리

3. 장애 격리
- OS 기능 실패 시 fallback 정책 명확화

테스트
1. plist 파싱 실패/키 누락/형식 변형 테스트
2. 외부 프로세스 실행 실패 핸들링 테스트

완료 기준
1. Application/Domain에서 `Process.Start`, `DllImport`, Avalonia Clipboard 직접 참조 제거
2. 플랫폼 어댑터 계약 테스트 추가

## 7.7 Phase 6: 통합 테스트 및 회귀 시나리오 구축 (1~2주)
목표: 리팩토링 완료 상태에서 실제 사용자 흐름 회귀 방지.

작업 항목
1. 로컬 루프백 통합 테스트
- server + virtual client + handshake + clipboard text/file/image

2. 레이아웃 통합 테스트
- Snap/Free 모드에서 배치/이동/해제 시나리오

3. 장애 주입 테스트
- 소켓 끊김, payload 불량, 재연결 지연

완료 기준
1. 핵심 E2E 시나리오 12개 이상 자동화
2. 릴리즈 전 회귀 테스트 자동 실행

## 7.8 Phase 7: 문서/운영 정리 및 하드닝 (1주)
목표: 코드-문서-운영 절차를 일치시켜 지속 가능 상태로 전환.

작업 항목
1. `docs/SharpKVM_상세명세서.md` 업데이트
- 계층 구조, 유스케이스 흐름, 신규 포트 반영

2. SVG 동기화
- `docs/images/ui-layout-main.svg`
- `docs/images/ui-layout-stage.svg`

3. 개발 가이드 추가
- TDD 체크리스트, 테스트 작성 템플릿, PR 규칙

완료 기준
1. 명세/코드 불일치 항목 0건
2. 신규 온보딩 문서로 개발자 1인 셋업 검증 통과

## 8. 파일 단위 리팩토링 매핑
### 8.1 현재 -> 목표 책임 매핑
1. `UI/MainWindow.cs`
- 현재: UI + 서버/클라이언트 + 라우팅 + 레이아웃 + 클립보드
- 목표: 뷰 이벤트 처리 + ViewModel 바인딩 + 상태 표시

2. `Networking/ClientHandler.cs`
- 현재: 네트워크 처리 + UI callback
- 목표: 네트워크 어댑터 + 이벤트 발행

3. `UI/MainWindow.Clipboard.cs`
- 현재: 캡처/정책/전송
- 목표: UI는 입력 이벤트만 전달, 정책은 Application으로 이동

4. `Core/LayoutGeometry.cs`
- 현재: 순수 규칙(강점)
- 목표: Domain 서비스로 유지 + 테스트 확장

5. `Platform/MacInputSourceHotkeyProvider.cs`
- 현재: 파싱 + 프로세스 호출 + 진단
- 목표: 인프라 어댑터 내부로 캡슐화 + 계약 기반 테스트

## 9. 테스트 상세 계획
## 9.1 테스트 피라미드 목표
1. Unit 70%
- Domain 규칙, Application 유스케이스

2. Integration 20%
- Infrastructure 어댑터 + loopback TCP

3. E2E/Scenario 10%
- 주요 사용자 흐름 회귀 시나리오

## 9.2 우선 작성할 테스트 목록 (상위 20)
1. Handshake: Hello 없는 첫 패킷 거부
2. Handshake: 비정상 해상도 거부
3. Clipboard: text payload upper bound 허용
4. Clipboard: upper bound+1 거부
5. Clipboard: 동일 텍스트 중복 송신 방지
6. Clipboard: file/image 우선순위 충돌 처리
7. Layout: snap 좌측 edge 인접 판정
8. Layout: snap 우측 edge 인접 판정
9. Layout: top/bottom 경계 판정
10. Layout: free mode 겹침 보정 반복 제한
11. Input: remote active true에서 local suppress
12. Input: remote active false에서 local passthrough
13. Session: disconnect event 정리 동작
14. Session: reconnect delay 정책 적용
15. Mac option: explicit key true 인식
16. Mac option: recursive key false 인식
17. Mac option: malformed json fallback
18. Virtual client: hello/platforminfo 순서 보장
19. Server lifecycle: start-stop 자원 정리
20. Client lifecycle: stop 시 상태 초기화

## 10. 마일스톤/PR 분할 계획
1. PR-01: Phase 0 (테스트 환경/베이스라인)
2. PR-02: Phase 1 (ClientHandler 결합 해소)
3. PR-03: Phase 2 (Layout 도메인 추출 1차)
4. PR-04: Phase 3 (Clipboard 유스케이스 분리)
5. PR-05: Phase 4 (Session/Input routing)
6. PR-06: Phase 5 (Platform adapter 정리)
7. PR-07: Phase 6 (통합/회귀 테스트)
8. PR-08: Phase 7 (문서/SVG 동기화)

각 PR 공통 완료 조건
1. 기능 회귀 없음
2. 신규/변경 규칙에 대응하는 테스트 존재
3. `dotnet build SharpKVM.csproj -c Release` 성공
4. 문서 업데이트 필요 시 같은 PR에 포함

## 11. 리스크 및 대응
1. 리스크: 장기 브랜치 충돌
- 대응: 주 2회 mainline 리베이스, PR 단위 최대 3일

2. 리스크: UI 이벤트와 도메인 상태 동기화 오류
- 대응: 상태 전이 로그 + 시나리오 테스트 우선 작성

3. 리스크: 네트워크/타이머 비동기 테스트 불안정
- 대응: 가상 시계/가짜 스케줄러/가짜 스트림 도입

4. 리스크: macOS 종속 기능 CI 재현 어려움
- 대응: 파서/정책은 unit으로 고정, OS 실행 경로는 adapter smoke test로 분리

## 12. 롤백 전략
1. Feature toggle 또는 어댑터 분기 유지
- 새 유스케이스 실패 시 기존 경로로 임시 복귀 가능하도록 유지

2. 단계별 릴리즈
- 각 Phase 종료 시점마다 태그 생성

3. Hotfix 절차
- 회귀 발견 시 재현 테스트 먼저 추가 후 최소 수정

## 13. 작업 관리 템플릿
아래 템플릿을 이슈/작업 카드에 사용한다.

```md
[작업명]
- 목적:
- 대상 파일:
- 선행 테스트(실패 상태):
- 구현 계획:
- 리팩토링 포인트:
- 완료 기준(DoD):
- 문서 반영 필요 여부:
```

## 14. Definition of Done (최종)
1. 계층 의존성 규칙이 테스트로 강제된다.
2. `MainWindow`는 UI 오케스트레이션 중심으로 축소된다.
3. `ClientHandler`는 UI 타입 참조를 제거한다.
4. 핵심 흐름 회귀 테스트 세트가 CI에서 안정적으로 동작한다.
5. 명세 문서와 UI SVG가 최신 구조를 반영한다.

## 15. 즉시 실행 체크리스트
1. Phase 0 킥오프 이슈 생성
2. 테스트 타깃 프레임워크 정책 확정 (`net8` or `net9`)
3. `ClientHandler` 이벤트 포트 초안 인터페이스 작성
4. 레이아웃 도메인 Aggregate 초안 모델링 세션 진행
5. 주간 점검 지표(테스트 수, MainWindow 라인 수, 결합도) 대시보드화

---

부록 A: 핵심 참조 파일
- `docs/SharpKVM_상세명세서.md`
- `docs/images/ui-layout-main.svg`
- `docs/images/ui-layout-stage.svg`
- `UI/MainWindow.cs`
- `UI/MainWindow.Clipboard.cs`
- `Networking/ClientHandler.cs`
- `Core/LayoutGeometry.cs`
- `Platform/MacInputSourceHotkeyProvider.cs`
- `SharpKVM.Tests/SharpKVM.Tests.csproj`

부록 B: 유지 원칙
1. 코드와 문서가 다르면 코드를 기준으로 판단하고 같은 작업에서 문서를 함께 수정한다.
2. 리팩토링 PR은 기능 추가 PR과 분리한다.
3. 모든 리팩토링 단계는 "테스트 추가 -> 구조 변경 -> 테스트 확장" 순서로 진행한다.

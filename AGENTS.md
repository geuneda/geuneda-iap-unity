# Geuneda.Iap.Unity - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.iap.unity`
- **Unity**: 2022.3+ (Unity IAP 5.4 의 최소 버전)
- **의존성**: `com.geuneda.iap` 0.1.0, `com.geuneda.core` 0.4.0, `com.cysharp.unitask`, `com.unity.purchasing` 5.4.4(UPM 의존성이라 함께 설치됨. analytics 어댑터처럼 SDK 가 없을 때 빼는 define 이 필요 없음)
- **역할**: `IIapStore` 를 Unity IAP 5 의 `StoreController` 로 구현합니다. 순서·중복·재시도·시간 제한은 `IapService` 몫이고 이 패키지는 SDK 호출과 형식 변환만 합니다

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/UnityIapStore.cs` | `IIapStore` 구현. 요청마다 완료 소스(상품·주문 조회는 하나씩, 확정은 거래 ID, 보유 조회는 상품 ID) |
| `Runtime/UnityIapMapping.cs` | 실패 이유·보유 상태·상품 종류 대응, 주문 변환(`ToStoreOrder`) |
| `Runtime/ConfirmTracker.cs` | 거래별 확정 요청(진행 중이면 붙음)과 닫힌 결과 기억 |

## 3. 규칙

- **Unity IAP 동작 근거**(5.4.4 소스로 확인, 2026-10-15): 연결 태스크는 성공이든 재시도 끝 실패든 예외 없이 끝나므로 끝난 뒤 `GetConnectionState()` 로 판정합니다. 상품 조회 기본 재시도 정책(`ExponentialBackOffRetryPolicy`)은 끝없이 재시도하고, `FetchProductsWithNoRetries` 도 null 정책을 넘겨 결국 기본 정책을 쓰므로 `new NoRetriesPolicy()` 를 직접 넘깁니다. 부분 조회는 성공 콜백 뒤 실패 콜백이 이어서 오고, 전부 실패면 실패 콜백만 옵니다. 상품 조회가 진행 중일 때 다시 부르면 실패 콜백이 와서 앞 요청과 섞이므로 하나씩만 합니다. 구매 조회는 마지막 요청의 콜백만 기억하므로 진행 중이면 붙습니다. `ConfirmPurchase` 의 검증 실패(연결 끊김 등)는 호출 안에서 `OnPurchaseConfirmed(FailedOrder)` 로 오고, 5.4.4 부터 `FailedOrder` 가 원래 주문의 `Info`(거래 ID)를 담습니다. Google 의 이미 승인된 주문 확정은 `DuplicateTransaction` 실패로 옵니다(5.4.3).
- **닫힌 확정 결과 기억**: 서비스가 확정을 시간 초과로 그만둔 뒤 성공 콜백이 오면 `ConfirmTracker` 가 그 결과(`Confirmed`/`AlreadyConfirmed`)를 기억하고 다음 확정 요청에 다시 확정하지 않고 돌려줍니다. Unity IAP 5.4.4 의 Google 확정은 이미 소비된 토큰을 찾지 못해 실패(Unknown)를 주므로, 버리면 서비스가 같은 거래를 계속 확정하려다 실패하고 열린 항목이 같은 상품 구매를 막습니다(2026-10-15 리뷰 1회차). 실패 결과는 기억하지 않습니다.
- **자동 재전달 끄기**: `ProcessPendingOrdersOnPurchasesFetched(false)` 는 생성자에서 합니다. 켜 두면 조회한 주문이 세션에 한 번만 `OnPurchasePending` 으로 다시 오고(같은 실행의 재조회로는 다시 안 옴), 서비스가 조회 결과로 처리하는 것과 겹칩니다.
- **실패 이유 대응**: `switch` 로 멤버마다 대응하고 정수 캐스트를 쓰지 않습니다. Unity 가 값을 추가하면 `EveryFailureReason_IsMapped`(값 개수)가 실패하니 대응을 다시 봅니다.
- **종류 판정**: 주문 첫 상품의 `definition.type` 이 `Unknown` 이 아니면 `IsKnownProduct` 입니다. 한 번에 상품 하나만 삽니다(장바구니 첫 항목).
- **이름 충돌**: `UnityEngine.Purchasing` 에도 `Result` 가 있어 `using Result = Geuneda.Core.Result;` 별칭을 씁니다.
- **복원**: Apple 플랫폼(iOS, macOS 플레이어, tvOS)만 `RestoreTransactions`. Unity 2022.3 초기 버전에 없는 `RuntimePlatform` 값(VisionOS)은 쓰지 않습니다.
- **스레드**: Unity IAP 콜백은 메인 스레드에서 옵니다(Android 는 `RunOnMainThread`). 연결 태스크를 기다린 뒤 메인 스레드가 아니면 돌아옵니다.

## 4. 테스트

- `Tests/EditMode/` (`Geuneda.Iap.Unity.Tests.EditMode`): 대응표, `ConfirmTrackerTest`. `Product` 생성자가 internal 이라 주문 변환은 단위 테스트하지 않고 PlayMode 로 확인합니다.
- `Tests/PlayMode/` (`Geuneda.Iap.Unity.Tests.PlayMode`): Unity 가짜 스토어로 서비스와 함께 전 과정. Unity IAP 는 `RuntimeInitializeOnLoadMethod` 로 스토어 팩토리를 등록해 EditMode 에서는 "Store Factory not present" 예외가 납니다. 가짜 스토어 기본 모드는 확인 창(`StandardUser`)이라 배치 모드에서 답이 오지 않으므로 scripting define `IAP_FAKE_STORE_DEFAULT` 가 없으면 건너뜁니다.
- Android·iOS 플레이어 스크립트 컴파일은 임시 프로젝트에서 `PlayerBuildInterface.CompilePlayerScripts` 로 확인했습니다(2026-10-15).

## 5. 문서

공개 API 를 바꾸면 `README.md`, `CHANGELOG.md`, 호스트 `claude-skills/geuneda-iap/` 스킬(이 패키지와 공유)을 함께 고칩니다.

# Geuneda IAP Unity

`com.geuneda.iap` 의 Unity IAP 5(`StoreController`) 어댑터입니다. `IapService` 에 스토어로 넘겨 씁니다.

| 타입 | 설명 |
|------|------|
| `UnityIapStore` | `IIapStore` 구현. 기본 생성자는 기본 스토어(Android Google Play, iOS App Store, 에디터 가짜 스토어)의 컨트롤러를 만듭니다 |

네임스페이스는 `Geuneda.Iap.Unity` 입니다.

## 설치

Unity IAP(`com.unity.purchasing` 5.4.4)는 이 패키지의 의존성으로 Unity 레지스트리에서 함께 설치됩니다. Unity 2022.3 이상이 필요합니다. Google Play 라이선스 키 등 Unity IAP 자체 설정은 Unity 문서(Services > In-App Purchasing > Configure)를 따릅니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.iap": "https://github.com/geuneda/geuneda-iap.git",
    "com.geuneda.iap.unity": "https://github.com/geuneda/geuneda-iap-unity.git"
  }
}
```

## 사용 예

```csharp
using Geuneda.Iap;
using Geuneda.Iap.Unity;

var store = new UnityIapStore();
var iap = new IapService(store, fulfiller, options);
await iap.InitializeAsync();

// 종료
iap.Dispose();
store.Dispose();
```

`UnityIapStore` 가 쓰는 `StoreController` 의 이벤트를 다른 곳에서 구독하거나 직접 확정하지 마세요. 이 어댑터가 모든 결과를 받아야 서비스가 지급 순서를 지킵니다.

## 동작

- **자동 재전달 끄기**: `ProcessPendingOrdersOnPurchasesFetched(false)`. Unity 는 미확정 주문을 세션에 거래마다 한 번만 `OnPurchasePending` 으로 다시 보내서, 같은 실행의 재조회(중복 거래 복구, 복원)로는 다시 처리되지 않습니다. 조회한 주문은 `FetchOrdersAsync` 결과로 서비스가 직접 처리합니다.
- **상품 조회 재시도 끄기**: 기본 정책은 실패하면 최대 30초 간격으로 끝없이 다시 시도합니다. `NoRetriesPolicy` 를 넘기고 시간 제한·재시도는 서비스가 맡습니다.
- **종류를 모르는 주문**: 주문의 상품 종류가 `ProductType.Unknown` 이면 `IsKnownProduct` 가 false 입니다(서비스가 확정하지 않음). Google 은 종류를 모르는 주문을 확정하면 소비 대신 승인만 합니다.
- **App Store 서명**: `IapOrder.SignedTransaction` 은 StoreKit 2 의 `jwsRepresentation` 입니다. `Receipt` 는 Unity 통합 영수증(App Store 는 앱 영수증을 감싼 것이라 비어 있을 수 있음)입니다.
- **확정 결과**: `OnPurchaseConfirmed` 를 거래 ID 로 맞춥니다. `ConfirmedOrder` 는 `Confirmed`, `FailedOrder` 의 `DuplicateTransaction`(서버나 다른 기기가 이미 승인)은 `AlreadyConfirmed`, 그 밖은 `Failed` 입니다. 닫힌 결과는 거래별로 기억해, 서비스가 시간 초과로 기다리기를 그만둔 뒤 성공 콜백이 와도 다음 확정 요청에 그 결과를 돌려줍니다(Google 은 이미 소비된 구매를 다시 확정하면 찾지 못해 실패함).
- **실패 이유 대응**: `PurchaseFailureReason` 멤버마다 대응합니다(정수로 옮기지 않음). `PurchasingUnavailable`·`StoreNotConnected`·`NotSupported`·`UserNotAuthenticated` 는 `StoreUnavailable`, `SignatureInvalid`·`ValidationFailure` 는 `ValidationFailed`, `PurchaseMissing`·`Unknown`·`OrderCancelled`·`OrderStateChanged` 는 `Unknown`.
- **복원**: iOS·macOS·tvOS 에서만 `RestoreTransactions` 를 부릅니다(복원된 거래는 미확정 주문으로 와서 지급자를 거침). 다른 스토어는 조회가 보유 주문을 돌려주므로 바로 성공입니다.

## 한계

- 실제 Google Play·App Store 결제, 구독 갱신, 승인 대기는 확인하지 않았습니다. 에디터의 Unity 가짜 스토어로 연결·조회·구매·확정·보유 조회를 확인했습니다.
- Unity IAP 의 원격 카탈로그·결제 대행(Payment Providers)·카탈로그 목록(여러 판매 조건)은 쓰지 않습니다. 상품 ID 하나에 판매 조건 하나입니다.

## 테스트

- `Geuneda.Iap.Unity.Tests.EditMode`: 실패 이유·보유 상태·상품 종류 대응표, 확정 결과 기억(`ConfirmTracker`).
- `Geuneda.Iap.Unity.Tests.PlayMode`: Unity 가짜 스토어로 `IapService` 와 함께 연결→조회→구매→지급→확정→보유 조회. Unity IAP 는 플레이 모드 시작 때 스토어를 등록하므로 PlayMode 입니다. 가짜 스토어의 기본은 확인 창 모드라 scripting define `IAP_FAKE_STORE_DEFAULT` 가 없으면 건너뜁니다.

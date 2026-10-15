# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.0] - 2026-10-15

**New**:
- `UnityIapStore`: `com.geuneda.iap` 0.1.0 의 Unity IAP 5.4.4(`StoreController`) 어댑터. 조회한 미확정 주문의 자동 재전달 끄기, 상품 조회 재시도 끄기(`NoRetriesPolicy`), 실패 이유·보유 상태를 멤버별로 대응, App Store 거래 서명(JWS)과 원래 거래 ID 를 주문에 담기, 스토어가 종류를 모르는 주문 표시, 확정 결과를 거래 ID 로 맞추고 이미 닫힌 거래(`DuplicateTransaction`)는 `AlreadyConfirmed`, 닫힌 확정 결과를 거래별로 기억해 시간 초과 뒤 재시도에 돌려줌, 복원은 Apple 플랫폼에서만 `RestoreTransactions`

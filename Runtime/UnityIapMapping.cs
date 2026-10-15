using UnityEngine.Purchasing;

// ReSharper disable once CheckNamespace

namespace Geuneda.Iap.Unity
{
	/// <summary>
	/// Unity IAP 값과 geuneda 값 사이의 대응입니다. 정수로 옮기지 않고 멤버마다 대응시킵니다(레퍼런스는 SDK 실패 이유를 정수로 넘겨
	/// 중복 거래를 7 로 비교했지만 Unity IAP v5 에서 7 은 검증 실패이고 중복 거래는 6 입니다).
	/// </summary>
	internal static class UnityIapMapping
	{
		public static ProductType ToProductType(IapProductType type)
		{
			switch (type)
			{
				case IapProductType.Consumable:
					return ProductType.Consumable;
				case IapProductType.NonConsumable:
					return ProductType.NonConsumable;
				case IapProductType.Subscription:
					return ProductType.Subscription;
				default:
					return ProductType.Unknown;
			}
		}

		public static IapStoreFailureReason ToFailureReason(PurchaseFailureReason reason)
		{
			switch (reason)
			{
				case PurchaseFailureReason.PurchasingUnavailable:
				case PurchaseFailureReason.StoreNotConnected:
				case PurchaseFailureReason.NotSupported:
				case PurchaseFailureReason.UserNotAuthenticated:
					return IapStoreFailureReason.StoreUnavailable;
				case PurchaseFailureReason.ExistingPurchasePending:
					return IapStoreFailureReason.ExistingPurchasePending;
				case PurchaseFailureReason.ProductUnavailable:
					return IapStoreFailureReason.ProductUnavailable;
				case PurchaseFailureReason.SignatureInvalid:
				case PurchaseFailureReason.ValidationFailure:
					return IapStoreFailureReason.ValidationFailed;
				case PurchaseFailureReason.UserCancelled:
					return IapStoreFailureReason.UserCancelled;
				case PurchaseFailureReason.PaymentDeclined:
					return IapStoreFailureReason.PaymentDeclined;
				case PurchaseFailureReason.DuplicateTransaction:
					return IapStoreFailureReason.DuplicateTransaction;
				default:
					// PurchaseMissing, Unknown, OrderCancelled, OrderStateChanged 와 앞으로 추가될 값
					return IapStoreFailureReason.Unknown;
			}
		}

		public static IapStoreFailureReason ToFailureReason(PurchasesFetchFailureReason reason)
		{
			switch (reason)
			{
				case PurchasesFetchFailureReason.PurchasingUnavailable:
				case PurchasesFetchFailureReason.StoreNotConnected:
				case PurchasesFetchFailureReason.NotSupported:
				case PurchasesFetchFailureReason.UserNotAuthenticated:
					return IapStoreFailureReason.StoreUnavailable;
				default:
					return IapStoreFailureReason.Unknown;
			}
		}

		public static IapEntitlementStatus ToEntitlementStatus(EntitlementStatus status)
		{
			switch (status)
			{
				case EntitlementStatus.NotEntitled:
					return IapEntitlementStatus.NotEntitled;
				case EntitlementStatus.EntitledUntilConsumed:
					return IapEntitlementStatus.EntitledUntilConsumed;
				case EntitlementStatus.EntitledButNotFinished:
					return IapEntitlementStatus.EntitledButNotFinished;
				case EntitlementStatus.FullyEntitled:
					return IapEntitlementStatus.FullyEntitled;
				default:
					return IapEntitlementStatus.Unknown;
			}
		}

		public static int ToResultCode(IapStoreFailureReason reason)
		{
			return reason == IapStoreFailureReason.StoreUnavailable ? IapResultCodes.StoreUnavailable : IapResultCodes.StoreError;
		}

		/// <summary>
		/// 주문을 서비스 형식으로 바꿉니다. 거래 ID 가 없으면 null 입니다(서비스가 중복을 거를 수 없음). 주문의 첫 상품을 씁니다(한 번에 하나만 삼).
		/// </summary>
		public static IapStoreOrder ToStoreOrder(Order order)
		{
			var info = order?.Info;

			if (info == null || string.IsNullOrEmpty(info.TransactionID))
			{
				return null;
			}

			var definition = FirstProduct(order)?.definition;
			// 스토어가 종류를 모르는 주문(상품 조회 전에 온 주문, 정의에 없는 상품)은 확정하면 Google 이 소비 대신 승인만 합니다
			var known = definition != null && definition.type != ProductType.Unknown;
			var apple = info.Apple;
			return new IapStoreOrder(definition?.id, info.TransactionID, info.Receipt, known, order, apple?.jwsRepresentation,
				apple?.OriginalTransactionID);
		}

		public static Product FirstProduct(Order order)
		{
			var items = order?.CartOrdered?.Items();
			return items != null && items.Count > 0 ? items[0]?.Product : null;
		}
	}
}

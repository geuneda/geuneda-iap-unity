using System;
using NUnit.Framework;
using UnityEngine.Purchasing;

// ReSharper disable once CheckNamespace

namespace Geuneda.Iap.Unity.Tests
{
	/// <summary>Unity IAP 값의 대응표를 확인합니다.</summary>
	public sealed class UnityIapStoreTest
	{
		[TestCase(PurchaseFailureReason.PurchasingUnavailable, IapStoreFailureReason.StoreUnavailable)]
		[TestCase(PurchaseFailureReason.StoreNotConnected, IapStoreFailureReason.StoreUnavailable)]
		[TestCase(PurchaseFailureReason.NotSupported, IapStoreFailureReason.StoreUnavailable)]
		[TestCase(PurchaseFailureReason.UserNotAuthenticated, IapStoreFailureReason.StoreUnavailable)]
		[TestCase(PurchaseFailureReason.ExistingPurchasePending, IapStoreFailureReason.ExistingPurchasePending)]
		[TestCase(PurchaseFailureReason.ProductUnavailable, IapStoreFailureReason.ProductUnavailable)]
		[TestCase(PurchaseFailureReason.SignatureInvalid, IapStoreFailureReason.ValidationFailed)]
		[TestCase(PurchaseFailureReason.ValidationFailure, IapStoreFailureReason.ValidationFailed)]
		[TestCase(PurchaseFailureReason.UserCancelled, IapStoreFailureReason.UserCancelled)]
		[TestCase(PurchaseFailureReason.PaymentDeclined, IapStoreFailureReason.PaymentDeclined)]
		[TestCase(PurchaseFailureReason.DuplicateTransaction, IapStoreFailureReason.DuplicateTransaction)]
		[TestCase(PurchaseFailureReason.PurchaseMissing, IapStoreFailureReason.Unknown)]
		[TestCase(PurchaseFailureReason.Unknown, IapStoreFailureReason.Unknown)]
		[TestCase(PurchaseFailureReason.OrderCancelled, IapStoreFailureReason.Unknown)]
		[TestCase(PurchaseFailureReason.OrderStateChanged, IapStoreFailureReason.Unknown)]
		public void FailureReasons_AreMappedByMember(PurchaseFailureReason unity, IapStoreFailureReason expected)
		{
			Assert.That(UnityIapMapping.ToFailureReason(unity), Is.EqualTo(expected));
		}

		[Test]
		public void EveryFailureReason_IsMapped()
		{
			// Unity 가 값을 추가하면 이 테스트로 대응을 다시 봅니다
			Assert.That(Enum.GetValues(typeof(PurchaseFailureReason)), Has.Length.EqualTo(15));
		}

		[TestCase(EntitlementStatus.Unknown, IapEntitlementStatus.Unknown)]
		[TestCase(EntitlementStatus.NotEntitled, IapEntitlementStatus.NotEntitled)]
		[TestCase(EntitlementStatus.EntitledUntilConsumed, IapEntitlementStatus.EntitledUntilConsumed)]
		[TestCase(EntitlementStatus.EntitledButNotFinished, IapEntitlementStatus.EntitledButNotFinished)]
		[TestCase(EntitlementStatus.FullyEntitled, IapEntitlementStatus.FullyEntitled)]
		public void EntitlementStatuses_AreMappedByMember(EntitlementStatus unity, IapEntitlementStatus expected)
		{
			Assert.That(UnityIapMapping.ToEntitlementStatus(unity), Is.EqualTo(expected));
		}

		[Test]
		public void ProductTypes_AreMapped()
		{
			Assert.That(UnityIapMapping.ToProductType(IapProductType.Consumable), Is.EqualTo(ProductType.Consumable));
			Assert.That(UnityIapMapping.ToProductType(IapProductType.NonConsumable), Is.EqualTo(ProductType.NonConsumable));
			Assert.That(UnityIapMapping.ToProductType(IapProductType.Subscription), Is.EqualTo(ProductType.Subscription));
		}
	}
}

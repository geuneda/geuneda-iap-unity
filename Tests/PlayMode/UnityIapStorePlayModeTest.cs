using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Iap.Unity.Tests
{
	/// <summary>
	/// 에디터의 Unity IAP 가짜 스토어(기본 모드는 창 없이 바로 답함)로 어댑터와 서비스를 함께 돌립니다.
	/// Unity IAP 는 플레이 모드 시작 때(RuntimeInitializeOnLoadMethod) 스토어를 등록하므로 PlayMode 테스트입니다.
	/// 가짜 스토어의 기본은 확인 창을 띄우는 모드라, scripting define <c>IAP_FAKE_STORE_DEFAULT</c> 가 없으면 건너뜁니다.
	/// </summary>
	public sealed class UnityIapStorePlayModeTest
	{
		private static readonly IapProductDefinition Gem = new IapProductDefinition("geuneda_test_gem", IapProductType.Consumable);
		private static readonly IapProductDefinition NoAds = new IapProductDefinition("geuneda_test_noads", IapProductType.NonConsumable);

		[UnityTest]
		public IEnumerator FakeStore_PurchaseIsFulfilledThenConfirmed()
		{
#if !IAP_FAKE_STORE_DEFAULT
			Assert.Ignore("Add the scripting define IAP_FAKE_STORE_DEFAULT so the Unity fake store answers without a dialog");
#endif
			return UniTask.ToCoroutine(async () =>
			{
				var store = new UnityIapStore();
				var fulfilled = new List<IapOrder>();
				var service = new IapService(store, new RecordingFulfiller(fulfilled), new IapServiceOptions { Products = new[] { Gem, NoAds } });
				var confirmed = new List<PurchaseSettlement>();
				service.PurchaseConfirmed += confirmed.Add;

				try
				{
					var initialized = await service.InitializeAsync();
					Assert.That(initialized.IsSuccess, Is.True, initialized.ToString());
					Assert.That(store.IsConnected, Is.True);
					Assert.That(service.TryGetProduct(Gem.Id, out var product), Is.True);
					Assert.That(product.IsAvailable, Is.True);

					var result = await service.PurchaseAsync(Gem.Id, "payload");

					Assert.That(result.IsSuccess, Is.True, result.ToString());
					Assert.That(result.Value.IsConfirmed, Is.True);
					Assert.That(fulfilled, Has.Count.EqualTo(1));
					Assert.That(fulfilled[0].Payload, Is.EqualTo("payload"));
					Assert.That(fulfilled[0].TransactionId, Is.Not.Empty);
					Assert.That(fulfilled[0].StoreName, Is.EqualTo(store.Name));
					Assert.That(confirmed, Has.Count.EqualTo(1));

					var entitlement = await service.CheckEntitlementAsync(NoAds.Id);
					Assert.That(entitlement.IsSuccess, Is.True, entitlement.ToString());
					Assert.That(entitlement.Value, Is.EqualTo(IapEntitlementStatus.NotEntitled));
				}
				finally
				{
					service.Dispose();
					store.Dispose();
				}
			});
		}

		private sealed class RecordingFulfiller : IPurchaseFulfiller
		{
			private readonly List<IapOrder> _orders;

			public RecordingFulfiller(List<IapOrder> orders)
			{
				_orders = orders;
			}

			public UniTask<FulfillmentResult> FulfillAsync(IapOrder order, CancellationToken cancellationToken)
			{
				_orders.Add(order);
				return UniTask.FromResult(FulfillmentResult.Granted());
			}
		}
	}
}

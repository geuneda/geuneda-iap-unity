using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using UnityEngine;
using UnityEngine.Purchasing;
using Result = Geuneda.Core.Result;

// ReSharper disable once CheckNamespace

namespace Geuneda.Iap.Unity
{
	/// <summary>
	/// Unity IAP 5 의 <see cref="StoreController"/> 를 <see cref="IIapStore"/> 로 감쌉니다. <see cref="IapService"/> 에 넘겨 씁니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 조회한 미확정 주문의 자동 재전달(<c>ProcessPendingOrdersOnPurchasesFetched</c>)을 끕니다. Unity 는 거래마다 세션에 한 번만 재전달해서 같은 실행의
	/// 재조회(중복 거래 복구, 복원)로는 다시 처리되지 않습니다. 조회한 주문은 <see cref="FetchOrdersAsync"/> 결과로 서비스가 직접 처리합니다.
	/// </para>
	/// <para>상품 조회 재시도를 끕니다. 기본 정책은 실패하면 최대 30초 간격으로 끝없이 다시 시도해, 서비스의 시간 제한과 재시도와 겹칩니다.</para>
	/// <para>메인 스레드에서 만들고, 서비스를 정리한 뒤 <see cref="Dispose"/> 합니다.</para>
	/// </remarks>
	public sealed class UnityIapStore : IIapStore, IDisposable
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Iap.Unity");

		private readonly StoreController _controller;
		private readonly ConfirmTracker _confirms = new ConfirmTracker();
		private readonly Dictionary<string, List<UniTaskCompletionSource<Result<IapEntitlementStatus>>>> _entitlements =
			new Dictionary<string, List<UniTaskCompletionSource<Result<IapEntitlementStatus>>>>();

		private UniTaskCompletionSource<Result<IReadOnlyList<IapProduct>>> _products;
		private IReadOnlyList<IapProductDefinition> _requestedProducts;
		private UniTaskCompletionSource<Result<IapStoreOrders>> _orders;
		private string _disconnectMessage;
		private bool _disposed;

		/// <summary>기본 스토어(Android 는 Google Play, iOS 는 App Store, 에디터는 가짜 스토어)의 컨트롤러로 만듭니다.</summary>
		public UnityIapStore()
			: this(UnityIAPServices.StoreController(), UnityIAPServices.GetDefaultStore())
		{
		}

		/// <param name="controller">쓸 컨트롤러. 다른 곳에서 이벤트를 구독하거나 확정하지 마세요(이 어댑터가 모든 결과를 받아야 함)</param>
		/// <param name="storeName">지급 요청에 넘길 스토어 이름</param>
		public UnityIapStore(StoreController controller, string storeName)
		{
			_controller = controller ?? throw new ArgumentNullException(nameof(controller));
			Name = string.IsNullOrEmpty(storeName) ? "unknown" : storeName;
			_controller.ProcessPendingOrdersOnPurchasesFetched(false);
			_controller.OnStoreConnected += OnStoreConnected;
			_controller.OnStoreDisconnected += OnStoreDisconnected;
			_controller.OnProductsFetched += OnProductsFetched;
			_controller.OnProductsFetchFailed += OnProductsFetchFailed;
			_controller.OnPurchasesFetched += OnPurchasesFetched;
			_controller.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
			_controller.OnPurchasePending += OnPurchasePending;
			_controller.OnPurchaseConfirmed += OnPurchaseConfirmed;
			_controller.OnPurchaseFailed += OnPurchaseFailed;
			_controller.OnPurchaseDeferred += OnPurchaseDeferred;
			_controller.OnCheckEntitlement += OnCheckEntitlement;
		}

		public string Name { get; }

		public bool IsConnected => !_disposed && _controller.GetConnectionState() == ConnectionState.Connected;

		public event Action<IapStoreOrder> OrderPending;

		public event Action<IapStorePurchaseFailure> PurchaseFailed;

		public event Action<string> PurchaseDeferred;

		public event Action<string> Disconnected;

		public async UniTask<Result> ConnectAsync(CancellationToken cancellationToken)
		{
			ThrowIfDisposed();
			_disconnectMessage = null;
			// Connect 의 태스크는 연결되거나 재시도가 끝나 실패했을 때 끝납니다(예외 없음). 연결 콜백은 메인 스레드에서 옵니다
			await _controller.Connect().AsUniTask(false);

			if (!PlayerLoopHelper.IsMainThread)
			{
				await UniTask.SwitchToMainThread();
			}

			return IsConnected
				? Result.Ok()
				: Result.Fail(IapResultCodes.StoreUnavailable, _disconnectMessage ?? $"Unity IAP is {_controller.GetConnectionState()}");
		}

		public UniTask<Result<IReadOnlyList<IapProduct>>> FetchProductsAsync(IReadOnlyList<IapProductDefinition> definitions,
			CancellationToken cancellationToken)
		{
			ThrowIfDisposed();

			if (_products != null)
			{
				// Unity 는 조회가 하나만 가능하고 두 번째 요청을 실패로 알려 앞 요청의 결과와 섞입니다
				return UniTask.FromResult(Result<IReadOnlyList<IapProduct>>.Fail(ResultCodes.InProgress, "A product fetch is in progress"));
			}

			var unityDefinitions = new List<ProductDefinition>(definitions.Count);

			foreach (var definition in definitions)
			{
				unityDefinitions.Add(new ProductDefinition(definition.Id, definition.StoreSpecificId, UnityIapMapping.ToProductType(definition.Type)));
			}

			var source = new UniTaskCompletionSource<Result<IReadOnlyList<IapProduct>>>();
			_products = source;
			_requestedProducts = definitions;
			_controller.FetchProducts(unityDefinitions, new NoRetriesPolicy());
			return source.Task;
		}

		public UniTask<Result<IapStoreOrders>> FetchOrdersAsync(CancellationToken cancellationToken)
		{
			ThrowIfDisposed();

			if (_orders != null)
			{
				// Unity 는 마지막 요청의 콜백만 기억하므로 진행 중인 조회에 붙습니다
				return _orders.Task;
			}

			var source = new UniTaskCompletionSource<Result<IapStoreOrders>>();
			_orders = source;
			_controller.FetchPurchases();
			return source.Task;
		}

		public void Purchase(IapProductDefinition product)
		{
			ThrowIfDisposed();
			var unityProduct = _controller.GetProductById(product.Id);

			if (unityProduct == null)
			{
				PurchaseFailed?.Invoke(new IapStorePurchaseFailure(product.Id, IapStoreFailureReason.ProductUnavailable,
					$"Unity IAP has not fetched {product.Id}"));
				return;
			}

			_controller.PurchaseProduct(unityProduct);
		}

		public UniTask<IapConfirmResult> ConfirmAsync(IapStoreOrder order, CancellationToken cancellationToken)
		{
			ThrowIfDisposed();

			if (!(order.Native is PendingOrder pending))
			{
				return UniTask.FromResult(IapConfirmResult.Failed(IapStoreFailureReason.Unknown, "The order is not a pending Unity IAP order"));
			}

			// 이미 닫힌 거래(앞 요청이 시간 초과된 뒤 성공 콜백이 옴)는 다시 확정하지 않고 그 결과를 돌려줍니다
			var task = _confirms.Begin(order.TransactionId, out var isNew);

			if (isNew)
			{
				// 검증 실패(연결 끊김 등)는 이 호출 안에서 OnPurchaseConfirmed 로 옵니다
				_controller.ConfirmPurchase(pending);
			}

			return task;
		}

		public UniTask<Result> RestoreAsync(CancellationToken cancellationToken)
		{
			ThrowIfDisposed();

			if (Application.platform != RuntimePlatform.IPhonePlayer && Application.platform != RuntimePlatform.OSXPlayer &&
			    Application.platform != RuntimePlatform.tvOS)
			{
				// Google Play 등은 보유 주문을 조회로 돌려주므로 따로 할 일이 없습니다
				return UniTask.FromResult(Result.Ok());
			}

			var source = new UniTaskCompletionSource<Result>();
			_controller.RestoreTransactions((success, message) =>
				source.TrySetResult(success ? Result.Ok() : Result.Fail(IapResultCodes.StoreUnavailable, message ?? "Restore failed")));
			return source.Task;
		}

		public UniTask<Result<IapEntitlementStatus>> CheckEntitlementAsync(IapProductDefinition product, CancellationToken cancellationToken)
		{
			ThrowIfDisposed();
			var unityProduct = _controller.GetProductById(product.Id);

			if (unityProduct == null)
			{
				return UniTask.FromResult(Result<IapEntitlementStatus>.Fail(IapResultCodes.ProductNotFound, $"Unity IAP has not fetched {product.Id}"));
			}

			if (!_entitlements.TryGetValue(product.Id, out var waiting))
			{
				waiting = new List<UniTaskCompletionSource<Result<IapEntitlementStatus>>>();
				_entitlements.Add(product.Id, waiting);
			}

			var source = new UniTaskCompletionSource<Result<IapEntitlementStatus>>();
			waiting.Add(source);

			if (waiting.Count == 1)
			{
				_controller.CheckEntitlement(unityProduct);
			}

			return source.Task;
		}

		/// <summary>이벤트 구독을 풀고 기다리는 요청을 취소로 끝냅니다. 컨트롤러는 Unity IAP 가 소유하므로 그대로 둡니다.</summary>
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_controller.OnStoreConnected -= OnStoreConnected;
			_controller.OnStoreDisconnected -= OnStoreDisconnected;
			_controller.OnProductsFetched -= OnProductsFetched;
			_controller.OnProductsFetchFailed -= OnProductsFetchFailed;
			_controller.OnPurchasesFetched -= OnPurchasesFetched;
			_controller.OnPurchasesFetchFailed -= OnPurchasesFetchFailed;
			_controller.OnPurchasePending -= OnPurchasePending;
			_controller.OnPurchaseConfirmed -= OnPurchaseConfirmed;
			_controller.OnPurchaseFailed -= OnPurchaseFailed;
			_controller.OnPurchaseDeferred -= OnPurchaseDeferred;
			_controller.OnCheckEntitlement -= OnCheckEntitlement;

			_products?.TrySetResult(Result<IReadOnlyList<IapProduct>>.Fail(ResultCodes.Cancelled, "UnityIapStore disposed"));
			_orders?.TrySetResult(Result<IapStoreOrders>.Fail(ResultCodes.Cancelled, "UnityIapStore disposed"));
			_products = null;
			_orders = null;

			_confirms.FailAll(IapConfirmResult.Failed(IapStoreFailureReason.Unknown, "UnityIapStore disposed"));

			foreach (var waiting in _entitlements.Values)
			{
				foreach (var source in waiting)
				{
					source.TrySetResult(Result<IapEntitlementStatus>.Fail(ResultCodes.Cancelled, "UnityIapStore disposed"));
				}
			}

			_entitlements.Clear();
		}

		private void OnStoreConnected()
		{
			Logger.Info($"Connected to {Name}");
		}

		private void OnStoreDisconnected(StoreConnectionFailureDescription description)
		{
			_disconnectMessage = description?.Message ?? "Disconnected";
			Disconnected?.Invoke(_disconnectMessage);
		}

		private void OnProductsFetched(List<Product> fetched)
		{
			var source = _products;

			if (source == null)
			{
				return;
			}

			_products = null;
			var products = new List<IapProduct>(_requestedProducts.Count);

			foreach (var definition in _requestedProducts)
			{
				var product = _controller.GetProductById(definition.Id);
				var metadata = product?.metadata;

				if (metadata == null)
				{
					continue;
				}

				products.Add(new IapProduct(definition, metadata.localizedPriceString, Math.Max(0m, metadata.localizedPrice), metadata.isoCurrencyCode,
					metadata.localizedTitle, metadata.localizedDescription, product.availableToPurchase));
			}

			// 일부만 받았으면 이어서 실패 콜백이 오고, 그것은 경고로만 남깁니다
			source.TrySetResult(Result<IReadOnlyList<IapProduct>>.Ok(products));
		}

		private void OnProductsFetchFailed(ProductFetchFailed failure)
		{
			var missing = failure?.FailedFetchProducts?.Count ?? 0;
			var source = _products;

			if (source == null)
			{
				Logger.Warning($"Unity IAP did not return {missing} product(s): {failure?.FailureReason}");
				return;
			}

			_products = null;
			source.TrySetResult(Result<IReadOnlyList<IapProduct>>.Fail(IapResultCodes.StoreError, $"Fetching products failed: {failure?.FailureReason}"));
		}

		private void OnPurchasesFetched(Orders fetched)
		{
			var source = _orders;

			if (source == null)
			{
				return;
			}

			_orders = null;
			var pending = new List<IapStoreOrder>(fetched.PendingOrders.Count);
			var owned = new List<IapStoreOrder>(fetched.ConfirmedOrders.Count);

			foreach (var order in fetched.PendingOrders)
			{
				Add(pending, order);
			}

			foreach (var order in fetched.ConfirmedOrders)
			{
				Add(owned, order);
			}

			source.TrySetResult(Result<IapStoreOrders>.Ok(new IapStoreOrders(pending, owned, fetched.DeferredOrders.Count)));
		}

		private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
		{
			var source = _orders;

			if (source == null)
			{
				return;
			}

			_orders = null;
			var reason = UnityIapMapping.ToFailureReason(failure.FailureReason);
			source.TrySetResult(Result<IapStoreOrders>.Fail(UnityIapMapping.ToResultCode(reason), $"{failure.FailureReason}: {failure.Message}"));
		}

		private void OnPurchasePending(PendingOrder order)
		{
			var mapped = UnityIapMapping.ToStoreOrder(order);

			if (mapped == null)
			{
				Logger.Warning("Ignoring a pending order without a transaction id");
				return;
			}

			OrderPending?.Invoke(mapped);
		}

		private void OnPurchaseConfirmed(Order order)
		{
			var transactionId = order?.Info?.TransactionID;

			if (string.IsNullOrEmpty(transactionId))
			{
				return;
			}

			_confirms.Complete(transactionId, ToConfirmResult(order));
		}

		private static IapConfirmResult ToConfirmResult(Order order)
		{
			switch (order)
			{
				case ConfirmedOrder _:
					return IapConfirmResult.Confirmed();
				case FailedOrder failed when failed.FailureReason == PurchaseFailureReason.DuplicateTransaction:
					// 이미 닫힌 거래(서버나 다른 기기가 승인, App Store 의 끝난 거래)입니다
					return IapConfirmResult.AlreadyConfirmed(failed.Details);
				case FailedOrder failed:
					return IapConfirmResult.Failed(UnityIapMapping.ToFailureReason(failed.FailureReason), failed.Details);
				default:
					return IapConfirmResult.Failed(IapStoreFailureReason.Unknown, $"Unexpected confirmation {order.GetType().Name}");
			}
		}

		private void OnPurchaseFailed(FailedOrder order)
		{
			var reason = UnityIapMapping.ToFailureReason(order.FailureReason);
			var items = order.CartOrdered?.Items();

			if (items == null || items.Count == 0)
			{
				PurchaseFailed?.Invoke(new IapStorePurchaseFailure(null, reason, order.Details));
				return;
			}

			foreach (var item in items)
			{
				PurchaseFailed?.Invoke(new IapStorePurchaseFailure(item?.Product?.definition?.id, reason, order.Details));
			}
		}

		private void OnPurchaseDeferred(DeferredOrder order)
		{
			var items = order.CartOrdered?.Items();

			if (items == null)
			{
				return;
			}

			foreach (var item in items)
			{
				PurchaseDeferred?.Invoke(item?.Product?.definition?.id);
			}
		}

		private void OnCheckEntitlement(Entitlement entitlement)
		{
			var productId = entitlement?.Product?.definition?.id;

			if (productId == null || !_entitlements.TryGetValue(productId, out var waiting))
			{
				return;
			}

			_entitlements.Remove(productId);
			var result = entitlement.Status == EntitlementStatus.Unknown && !string.IsNullOrEmpty(entitlement.ErrorMessage)
				? Result<IapEntitlementStatus>.Fail(IapResultCodes.StoreError, entitlement.ErrorMessage)
				: Result<IapEntitlementStatus>.Ok(UnityIapMapping.ToEntitlementStatus(entitlement.Status));

			foreach (var source in waiting)
			{
				source.TrySetResult(result);
			}
		}

		private static void Add(List<IapStoreOrder> target, Order order)
		{
			var mapped = UnityIapMapping.ToStoreOrder(order);

			if (mapped != null)
			{
				target.Add(mapped);
			}
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(UnityIapStore));
			}
		}
	}
}

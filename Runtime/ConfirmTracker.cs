using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Iap.Unity
{
	/// <summary>
	/// 거래별 확정 요청과 닫힌 결과를 기억합니다. 서비스가 확정을 기다리다 시간 초과로 그만둔 뒤 성공 콜백이 오면, 그 결과를 다음 확정 요청에 돌려줍니다.
	/// </summary>
	/// <remarks>
	/// Unity IAP 는 이미 소비된 Google 구매를 다시 확정하면 구매를 찾지 못해 실패(Unknown)를 줍니다. 늦게 온 성공을 버리면 서비스는 같은 거래를 계속 확정하려다
	/// 실패하고, 열린 항목이 남아 같은 상품 구매도 막힙니다.
	/// </remarks>
	internal sealed class ConfirmTracker
	{
		private readonly Dictionary<string, UniTaskCompletionSource<IapConfirmResult>> _pending =
			new Dictionary<string, UniTaskCompletionSource<IapConfirmResult>>();
		private readonly Dictionary<string, IapConfirmResult> _closed = new Dictionary<string, IapConfirmResult>();

		/// <summary>이미 닫힌 거래면 그 결과, 확정 중이면 그 요청, 아니면 새 요청입니다. 새 요청일 때만 <paramref name="isNew"/> 가 true 입니다.</summary>
		public UniTask<IapConfirmResult> Begin(string transactionId, out bool isNew)
		{
			isNew = false;

			if (_closed.TryGetValue(transactionId, out var closed))
			{
				return UniTask.FromResult(closed);
			}

			if (_pending.TryGetValue(transactionId, out var running))
			{
				return running.Task;
			}

			var source = new UniTaskCompletionSource<IapConfirmResult>();
			_pending.Add(transactionId, source);
			isNew = true;
			return source.Task;
		}

		/// <summary>확정 결과를 넣습니다. 닫힌 결과(확정, 이미 확정)는 기억합니다. 기다리는 요청이 없었으면 false 입니다.</summary>
		public bool Complete(string transactionId, IapConfirmResult result)
		{
			if (result.Status == IapConfirmStatus.Confirmed || result.Status == IapConfirmStatus.AlreadyConfirmed)
			{
				_closed[transactionId] = result;
			}

			if (!_pending.TryGetValue(transactionId, out var source))
			{
				return false;
			}

			_pending.Remove(transactionId);
			source.TrySetResult(result);
			return true;
		}

		/// <summary>기다리는 요청을 모두 이 결과로 끝냅니다(어댑터 종료).</summary>
		public void FailAll(IapConfirmResult result)
		{
			var pending = new List<UniTaskCompletionSource<IapConfirmResult>>(_pending.Values);
			_pending.Clear();

			foreach (var source in pending)
			{
				source.TrySetResult(result);
			}
		}
	}
}

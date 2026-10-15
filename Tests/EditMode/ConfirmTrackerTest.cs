using Cysharp.Threading.Tasks;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Iap.Unity.Tests
{
	public sealed class ConfirmTrackerTest
	{
		[Test]
		public void SecondRequestWhileConfirming_JoinsTheFirst()
		{
			var tracker = new ConfirmTracker();

			var first = tracker.Begin("tx", out var firstIsNew);
			var second = tracker.Begin("tx", out var secondIsNew);
			tracker.Complete("tx", IapConfirmResult.Confirmed());

			Assert.That(firstIsNew, Is.True);
			Assert.That(secondIsNew, Is.False);
			Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Succeeded));
			Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Succeeded));
		}

		[Test]
		public void LateSuccess_IsReturnedToTheNextRequestWithoutConfirmingAgain()
		{
			var tracker = new ConfirmTracker();
			tracker.Begin("tx", out _);
			// 서비스는 시간 초과로 첫 요청을 버렸고, 그 뒤 성공 콜백이 옵니다
			tracker.Complete("tx", IapConfirmResult.Confirmed());

			var retry = tracker.Begin("tx", out var isNew);

			Assert.That(isNew, Is.False, "the transaction is not confirmed again (Google cannot find a consumed purchase)");
			Assert.That(retry.GetAwaiter().GetResult().Status, Is.EqualTo(IapConfirmStatus.Confirmed));
		}

		[Test]
		public void AlreadyConfirmed_IsRemembered()
		{
			var tracker = new ConfirmTracker();
			tracker.Begin("tx", out _);
			tracker.Complete("tx", IapConfirmResult.AlreadyConfirmed("acknowledged elsewhere"));

			Assert.That(tracker.Begin("tx", out var isNew).GetAwaiter().GetResult().Status, Is.EqualTo(IapConfirmStatus.AlreadyConfirmed));
			Assert.That(isNew, Is.False);
		}

		[Test]
		public void Failure_IsNotRemembered()
		{
			var tracker = new ConfirmTracker();
			tracker.Begin("tx", out _);
			tracker.Complete("tx", IapConfirmResult.Failed(IapStoreFailureReason.StoreUnavailable));

			tracker.Begin("tx", out var isNew);

			Assert.That(isNew, Is.True, "a failed confirmation is attempted again");
		}

		[Test]
		public void CompletionWithoutARequest_IsRememberedWhenClosed()
		{
			var tracker = new ConfirmTracker();

			Assert.That(tracker.Complete("tx", IapConfirmResult.Confirmed()), Is.False);
			Assert.That(tracker.Begin("tx", out var isNew).GetAwaiter().GetResult().Status, Is.EqualTo(IapConfirmStatus.Confirmed));
			Assert.That(isNew, Is.False);
		}

		[Test]
		public void FailAll_EndsWaitingRequests()
		{
			var tracker = new ConfirmTracker();
			var pending = tracker.Begin("tx", out _);

			tracker.FailAll(IapConfirmResult.Failed(IapStoreFailureReason.Unknown, "disposed"));

			Assert.That(pending.GetAwaiter().GetResult().Status, Is.EqualTo(IapConfirmStatus.Failed));
		}
	}
}

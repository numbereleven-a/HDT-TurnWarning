using System;
using System.Threading;
using System.Windows.Threading;

namespace TurnWarning
{
	internal sealed class TurnNotificationCoordinator : IDisposable
	{
		private readonly Dispatcher _dispatcher;
		private readonly Func<PluginSettings> _settingsProvider;
		private int _generation;
		private int _testGeneration;
		private int _disposed;
		private DispatcherTimer? _pendingTimer;
		private TurnWarningWindow? _window;
		private bool _windowIsTest;

		public TurnNotificationCoordinator(
			Dispatcher dispatcher,
			Func<PluginSettings> settingsProvider)
		{
			_dispatcher = dispatcher;
			_settingsProvider = settingsProvider;
		}

		public void Schedule(NotificationContent content, Func<bool> validate)
		{
			if(Volatile.Read(ref _disposed) != 0)
				return;
			CloseTest();
			var generation = Interlocked.Increment(ref _generation);
			var settings = _settingsProvider();
			TryBeginInvoke(() => ScheduleOnUi(content, validate, settings, generation));
		}

		public void CancelPending()
		{
			if(Volatile.Read(ref _disposed) != 0)
				return;
			Interlocked.Increment(ref _generation);
			TryBeginInvoke(CancelPendingOnUi);
		}

		public void CancelAll()
		{
			if(Volatile.Read(ref _disposed) != 0)
				return;
			Interlocked.Increment(ref _generation);
			Interlocked.Increment(ref _testGeneration);
			TryBeginInvoke(() =>
			{
				CancelPendingOnUi();
				CloseCurrentWindowOnUi();
				NotificationServices.StopHearthstoneFlash();
			});
		}

		public void CloseTest()
		{
			Interlocked.Increment(ref _testGeneration);
			NotificationServices.ClearPreviewSound();
			if(Volatile.Read(ref _disposed) != 0)
				return;
			TryBeginInvoke(() =>
			{
				if(_windowIsTest)
					CloseCurrentWindowOnUi();
			});
		}

		public void ShowTest(PluginSettings settings)
		{
			if(Volatile.Read(ref _disposed) != 0)
				return;
			var snapshot = settings.Clone();
			snapshot.Normalize();
			var testGeneration = Interlocked.Increment(ref _testGeneration);
			var content = NotificationContent.ForTurn(snapshot,
				snapshot.ShowCombatResult ? new CombatResultSummary(CombatOutcome.Win, 8) : null);
			TryBeginInvoke(() =>
			{
				if(testGeneration == Volatile.Read(ref _testGeneration))
					Deliver(snapshot, content, true);
			});
		}

		private void ScheduleOnUi(NotificationContent content, Func<bool> validate, PluginSettings settings, int generation)
		{
			if(Volatile.Read(ref _disposed) != 0 || generation != Volatile.Read(ref _generation))
				return;
			CancelPendingOnUi();
			var deadline = DateTime.UtcNow.AddMilliseconds(settings.StabilizationDelayMs);
			_pendingTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
			{
				Interval = TimeSpan.FromMilliseconds(100)
			};
			_pendingTimer.Tick += (_, _) =>
			{
				if(Volatile.Read(ref _disposed) != 0 || generation != Volatile.Read(ref _generation))
				{
					CancelPendingOnUi();
					return;
				}
				if(HearthstoneWindowState.GetStatus() == HearthstoneWindowStatus.Focused)
				{
					CancelPendingOnUi();
					return;
				}
				if(DateTime.UtcNow < deadline)
					return;
				CancelPendingOnUi();
				try
				{
					if(validate())
						Deliver(settings, content, false);
				}
				catch
				{
					// A disappearing game process or shutting down dispatcher must not affect HDT.
				}
			};
			_pendingTimer.Start();
		}

		private void Deliver(PluginSettings settings, NotificationContent content, bool isTest)
		{
			if(Volatile.Read(ref _disposed) != 0)
				return;
			if(!isTest && !HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus()))
				return;
			CloseCurrentWindowOnUi();
			NotificationServices.FlashHearthstone(settings.FlashMode);
			if(isTest)
				NotificationServices.PlayPreviewSound(settings);
			else
				NotificationServices.PlaySound(settings);
			if(!settings.ShowWindow)
				return;
			var window = new TurnWarningWindow(settings, content, isTest);
			_window = window;
			_windowIsTest = isTest;
			window.Closed += (_, _) =>
			{
				if(ReferenceEquals(_window, window))
				{
					_window = null;
					_windowIsTest = false;
				}
			};
			window.Show();
		}

		private void CancelPendingOnUi()
		{
			_pendingTimer?.Stop();
			_pendingTimer = null;
		}

		private void CloseCurrentWindowOnUi()
		{
			var window = _window;
			_window = null;
			_windowIsTest = false;
			window?.Close();
		}

		public void Dispose()
		{
			if(Interlocked.Exchange(ref _disposed, 1) != 0)
				return;
			Interlocked.Increment(ref _generation);
			Interlocked.Increment(ref _testGeneration);
			NotificationServices.ClearPreviewSound();
			TryBeginInvoke(() =>
			{
				CancelPendingOnUi();
				CloseCurrentWindowOnUi();
			});
		}

		private void TryBeginInvoke(Action action)
		{
			if(_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
				return;
			try
			{
				_dispatcher.BeginInvoke(action);
			}
			catch(InvalidOperationException)
			{
				// HDT may be closing between the shutdown check and BeginInvoke.
			}
		}
	}
}

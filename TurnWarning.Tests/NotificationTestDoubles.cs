using System;
using System.Collections.Generic;

// Test doubles execute the production coordinator against a controllable UI queue.
namespace System.Windows.Threading
{
	internal enum DispatcherPriority { Normal }
	internal sealed class Dispatcher
	{
		public bool HasShutdownStarted => false;
		public bool HasShutdownFinished => false;
		public readonly List<Action> Queue = new List<Action>();
		public void BeginInvoke(Action action) => Queue.Add(action);
		public void Drain()
		{
			while(Queue.Count > 0) { var action = Queue[0]; Queue.RemoveAt(0); action(); }
		}
	}
	internal sealed class DispatcherTimer
	{
		public static readonly List<DispatcherTimer> Timers = new List<DispatcherTimer>();
		public DispatcherTimer(DispatcherPriority priority, Dispatcher dispatcher) { Timers.Add(this); }
		public TimeSpan Interval { get; set; }
		public event EventHandler? Tick;
		public bool Active;
		public void Start() => Active = true;
		public void Stop() => Active = false;
		public static void Advance()
		{
			foreach(var timer in Timers.ToArray()) if(timer.Active) timer.Tick?.Invoke(timer, EventArgs.Empty);
		}
	}
}
namespace Hearthstone_Deck_Tracker.Utility.Logging
{
	internal static class Log { public static void Error(string text, string source, string path) => throw new Exception(text); }
}
namespace TurnWarning
{
	internal enum HearthstoneWindowStatus { Focused, Unfocused }
	internal static class HearthstoneWindowState
	{
		public static HearthstoneWindowStatus GetStatus() => HearthstoneWindowStatus.Unfocused;
		public static bool IsAway(HearthstoneWindowStatus status) => status == HearthstoneWindowStatus.Unfocused;
	}
	internal static class NotificationServices
	{
		public static void ClearPreviewSound() { }
		public static void StopHearthstoneFlash() { }
		public static void FlashHearthstone(TaskbarFlashMode mode) { }
		public static void PlayPreviewSound(PluginSettings settings) { }
		public static void PlaySound(PluginSettings settings) { }
	}
	internal sealed class TurnWarningWindow
	{
		public static int Shown;
		public static int ClosedCount;
		public TurnWarningWindow(PluginSettings settings, NotificationContent content, bool isTest) { }
		public event EventHandler? Closed;
		public void Show() => Shown++;
		public void Close() { ClosedCount++; Closed?.Invoke(this, EventArgs.Empty); }
	}
}

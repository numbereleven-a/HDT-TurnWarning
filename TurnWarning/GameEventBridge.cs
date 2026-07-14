using System.Threading;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;

namespace TurnWarning
{
	internal static class GameEventBridge
	{
		private static TurnWarningPlugin? _target;
		private static int _subscribed;

		public static void Attach(TurnWarningPlugin target)
		{
			Volatile.Write(ref _target, target);
			if(Interlocked.Exchange(ref _subscribed, 1) != 0)
				return;

			// ActionList associates handlers with a plugin by the immediate caller type.
			// Subscribing through this bridge keeps one stable handler set across HDT
			// enable/disable cycles; the bridge itself holds no target while disabled.
			GameEvents.OnGameStart.Add(HandleGameStart);
			GameEvents.OnGameEnd.Add(HandleGameEnd);
			GameEvents.OnInMenu.Add(HandleGameEnd);
			GameEvents.OnTurnStart.Add(HandleTurnStart);
			GameEvents.OnEntityWillTakeDamage.Add(HandleEntityWillTakeDamage);
		}

		public static void Detach(TurnWarningPlugin target)
		{
			if(ReferenceEquals(Volatile.Read(ref _target), target))
				Volatile.Write(ref _target, null);
		}

		private static void HandleGameStart() => Volatile.Read(ref _target)?.HandleGameStart();
		private static void HandleGameEnd() => Volatile.Read(ref _target)?.HandleGameEnd();
		private static void HandleTurnStart(ActivePlayer player) => Volatile.Read(ref _target)?.HandleTurnStart(player);
		private static void HandleEntityWillTakeDamage(PredamageInfo info)
			=> Volatile.Read(ref _target)?.HandleEntityWillTakeDamage(info);
	}
}

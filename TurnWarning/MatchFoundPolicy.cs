namespace TurnWarning
{
	internal static class MatchFoundPolicy
	{
		public static bool CanDeliver(
			bool enabled,
			bool isBattlegrounds,
			bool isInMenu,
			bool isSpectator,
			bool isReconnect,
			bool heroPickingDone,
			bool isAway)
			=> enabled
				&& isBattlegrounds
				&& !isInMenu
				&& !isSpectator
				&& !isReconnect
				&& !heroPickingDone
				&& isAway;

		public static bool CanDeliverInitialRecruit(
			bool isBattlegrounds,
			bool isInMenu,
			bool isSpectator,
			bool isReconnect,
			bool heroPickingDone,
			bool isCombat,
			int turnNumber,
			bool isAway)
			=> isBattlegrounds
				&& !isInMenu
				&& !isSpectator
				&& !isReconnect
				&& heroPickingDone
				&& !isCombat
				&& turnNumber <= 1
				&& isAway;
	}
}

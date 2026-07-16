namespace TurnWarning
{
	internal static class CombatNotificationPolicy
	{
		public static bool CanDeliver(
			bool enabled,
			bool matchActive,
			bool isBattlegrounds,
			bool isSpectator,
			bool isCombat,
			bool isAway)
		{
			return enabled
				&& matchActive
				&& isBattlegrounds
				&& !isSpectator
				&& isCombat
				&& isAway;
		}
	}
}

namespace TurnWarning
{
	internal sealed class NotificationContent
	{
		public NotificationContent(string title, string message, CombatResultSummary? combatResult = null)
		{
			Title = title;
			Message = message;
			CombatResult = combatResult;
		}

		public string Title { get; }
		public string Message { get; }
		public CombatResultSummary? CombatResult { get; }

		public static NotificationContent ForTurn(PluginSettings settings, CombatResultSummary? result)
		{
			return new NotificationContent(settings.Title, settings.Message,
				settings.ShowCombatResult ? result : null);
		}

		public static NotificationContent ForMatchFound()
			=> new NotificationContent("Battlegrounds match found", "Choose your hero");

		public static NotificationContent ForInitialRecruit()
			=> new NotificationContent("Battlegrounds has started", "Hero selection is over - the Recruit phase has started");

		public static NotificationContent ForCombat()
			=> new NotificationContent("Combat has started", "The battle has begun");
	}
}

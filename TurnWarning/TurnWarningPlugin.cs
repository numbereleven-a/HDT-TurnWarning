using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Plugins;
using HdtCore = Hearthstone_Deck_Tracker.API.Core;

namespace TurnWarning
{
	public sealed class TurnWarningPlugin : IPlugin
	{
		internal const string DisplayVersion = "1.1";
		private readonly TurnBoundaryTracker _tracker = new TurnBoundaryTracker();
		private readonly CombatResultTracker _combatResult = new CombatResultTracker();
		private readonly MatchFoundRequestState _matchFound = new MatchFoundRequestState();
		private readonly object _settingsGate = new object();
		private volatile bool _enabled;
		private int _initialRecruitHandled;
		private PluginSettings _settings = new PluginSettings();
		private TurnNotificationCoordinator? _notifications;
		private SettingsWindow? _settingsWindow;
		private MenuItem? _menuItem;

		public string Name => "TurnWarning";
		public string Description => "Warns when a Battlegrounds match or Recruit phase starts while Hearthstone is not focused.";
		public string ButtonText => "Settings";
		public string Author => "numbereleven-a";
		public Version Version => new Version(1, 1);
		public MenuItem MenuItem
		{
			get
			{
				if(_menuItem != null)
					return _menuItem;
				_menuItem = new MenuItem { Header = "TurnWarning" };
				_menuItem.Click += (_, _) => OpenSettings();
				return _menuItem;
			}
		}

		public void OnLoad()
		{
			_enabled = true;
			lock(_settingsGate)
				_settings = SettingsStore.Load();
			NotificationServices.PrepareSound(GetSettingsSnapshot());
			var dispatcher = Application.Current?.Dispatcher;
			if(dispatcher != null)
				_notifications = new TurnNotificationCoordinator(dispatcher, GetSettingsSnapshot);

			var game = HdtCore.Game;
			_tracker.Initialize(
				matchActive: game != null && !game.IsInMenu,
				isBattlegrounds: game?.IsBattlegroundsMatch == true,
				isCombat: game?.IsBattlegroundsCombatPhase == true,
				isReconnect: game?.CurrentGameStats?.IsReconnect == true);
			Volatile.Write(ref _initialRecruitHandled, game != null && !game.IsInMenu ? 1 : 0);
			_combatResult.Reset();

			GameEventBridge.Attach(this);
		}

		public void OnUnload()
		{
			_enabled = false;
			GameEventBridge.Detach(this);
			_tracker.OnGameEnded();
			_combatResult.Reset();
			_matchFound.End();
			NotificationServices.StopHearthstoneFlash();
			NotificationServices.ClearPreparedSound();
			var notifications = Interlocked.Exchange(ref _notifications, null);
			notifications?.Dispose();
			var settingsWindow = _settingsWindow;
			try
			{
				if(settingsWindow != null && !settingsWindow.Dispatcher.HasShutdownStarted)
					settingsWindow.Dispatcher.BeginInvoke(new Action(settingsWindow.Close));
			}
			catch(InvalidOperationException)
			{
			}
			_settingsWindow = null;
		}

		public void OnButtonPress()
		{
			OpenSettings();
		}

		private void OpenSettings()
		{
			if(!_enabled)
				return;
			var dispatcher = Application.Current?.Dispatcher;
			if(dispatcher == null || dispatcher.HasShutdownStarted)
				return;
			dispatcher.BeginInvoke(new Action(ShowSettings));
		}

		public void OnUpdate()
		{
			if(!_enabled)
				return;
			var game = HdtCore.Game;
			if(game != null && game.CurrentGameMode == GameMode.Spectator)
			{
				_matchFound.CancelPending();
				_notifications?.CancelAll();
				_combatResult.Reset();
				_tracker.Observe(false, false, false);
				return;
			}
			if(game != null)
				TryHandlePendingMatchFound(game);
			var isCombat = game?.IsBattlegroundsCombatPhase == true;
			var transition = _tracker.Observe(
				matchActive: game != null && !game.IsInMenu,
				isBattlegrounds: game?.IsBattlegroundsMatch == true,
				isCombat: isCombat);
			if(transition == PhaseTransition.EnteredCombat && game != null)
			{
				_notifications?.CancelAll();
				var opponentHero = game.Opponent?.Hero;
				_combatResult.Begin(opponentHero != null && opponentHero.Health > 0);
				var combatToken = _tracker.OnCombatStarted();
				var settings = GetSettingsSnapshot();
				if(combatToken != null
					&& settings.NotifyCombatStarted
					&& HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus()))
				{
					_notifications?.Schedule(
						NotificationContent.ForCombat(),
						() => ValidatePendingCombat(combatToken));
				}
			}
		}

		internal void HandleGameStart()
		{
			if(!_enabled)
				return;
			var game = HdtCore.Game;
			_tracker.OnGameStarted(game?.IsBattlegroundsMatch == true);
			_combatResult.Reset();
			Volatile.Write(ref _initialRecruitHandled, 0);
			var settings = GetSettingsSnapshot();
			var shouldWait = game != null
				&& settings.NotifyMatchFound
				&& game.CurrentGameMode != GameMode.Spectator
				&& HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus());
			_matchFound.Start(shouldWait, DateTime.UtcNow.AddMinutes(1));
		}

		internal void HandleGameEnd()
		{
			if(!_enabled)
				return;
			_tracker.OnGameEnded();
			_combatResult.Reset();
			Volatile.Write(ref _initialRecruitHandled, 1);
			_matchFound.End();
			_notifications?.CancelAll();
			NotificationServices.StopHearthstoneFlash();
		}

		internal void HandleTurnStart(ActivePlayer player)
		{
			if(!_enabled)
				return;
			if(player != ActivePlayer.Player)
			{
				_notifications?.CancelAll();
				return;
			}

			var game = HdtCore.Game;
			if(game == null)
				return;
			if(game.CurrentGameMode == GameMode.Spectator)
			{
				_notifications?.CancelAll();
				return;
			}
			var turnNumber = game.GetTurnNumber();
			if(turnNumber <= 1 && game.IsBattlegroundsMatch
				&& Interlocked.Exchange(ref _initialRecruitHandled, 1) == 0)
			{
				if(game.CurrentGameStats?.IsReconnect == true
					|| !HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus()))
					return;
				var matchGeneration = _matchFound.CurrentGeneration;
				_notifications?.Schedule(
					NotificationContent.ForInitialRecruit(),
					() => ValidateInitialRecruit(matchGeneration));
				return;
			}
			var token = _tracker.OnPlayerTurnStarted(
				turnNumber,
				matchActive: !game.IsInMenu,
				isBattlegrounds: game.IsBattlegroundsMatch,
				isCombat: game.IsBattlegroundsCombatPhase);
			if(token == null)
				return;

			// Focus loss is checked only at the turn boundary. Minimizing later in the
			// same shopping phase therefore cannot create a warning.
			if(!HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus()))
				return;
			var settings = GetSettingsSnapshot();
			var content = NotificationContent.ForTurn(settings, _combatResult.Complete());
			_notifications?.Schedule(content, () => ValidatePendingTurn(token));
		}

		private void TryHandlePendingMatchFound(Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			if(!_matchFound.TryGetPending(out var matchGeneration, out var deadlineUtc))
				return;
			if(DateTime.UtcNow >= deadlineUtc
				|| game.CurrentGameStats?.IsReconnect == true
				|| HearthstoneWindowState.GetStatus() == HearthstoneWindowStatus.Focused)
			{
				_matchFound.CancelIfCurrent(matchGeneration);
				return;
			}
			var mode = game.CurrentGameMode;
			if(mode == GameMode.None)
				return;
			if(game.IsInMenu
				|| mode == GameMode.Spectator
				|| !game.IsBattlegroundsMatch
				|| game.GetTurnNumber() > 1
				|| game.IsBattlegroundsHeroPickingDone)
			{
				_matchFound.CancelIfCurrent(matchGeneration);
				return;
			}
			if(game.PlayerEntity == null)
				return;

			if(!_matchFound.TryComplete(matchGeneration))
				return;
			_notifications?.Schedule(
				NotificationContent.ForMatchFound(),
				() => ValidateMatchFound(matchGeneration));
		}

		private bool ValidateInitialRecruit(int matchGeneration)
		{
			if(!_enabled || !_matchFound.IsCurrent(matchGeneration))
				return false;
			var game = HdtCore.Game;
			if(game == null || game.PlayerEntity?.IsCurrentPlayer != true)
				return false;
			return MatchFoundPolicy.CanDeliverInitialRecruit(
				isBattlegrounds: game.IsBattlegroundsMatch,
				isInMenu: game.IsInMenu,
				isSpectator: game.CurrentGameMode == GameMode.Spectator,
				isReconnect: game.CurrentGameStats?.IsReconnect == true,
				heroPickingDone: game.IsBattlegroundsHeroPickingDone,
				isCombat: game.IsBattlegroundsCombatPhase,
				turnNumber: game.GetTurnNumber(),
				isAway: HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus()));
		}

		internal void HandleEntityWillTakeDamage(PredamageInfo info)
		{
			if(!_enabled || info?.Entity == null || !info.Entity.IsHero)
				return;
			var game = HdtCore.Game;
			if(game == null || !game.IsBattlegroundsMatch || !game.IsBattlegroundsCombatPhase
				|| game.CurrentGameMode == GameMode.Spectator)
				return;
			if(info.Entity.Id == game.Player?.Hero?.Id)
				_combatResult.RecordHeroDamage(CombatSide.Player, info.Value);
			else if(info.Entity.Id == game.Opponent?.Hero?.Id)
				_combatResult.RecordHeroDamage(CombatSide.Opponent, info.Value);
		}

		private bool ValidateMatchFound(int matchGeneration)
		{
			if(!_enabled || !_matchFound.IsCurrent(matchGeneration))
				return false;
			var game = HdtCore.Game;
			if(game == null || game.PlayerEntity == null)
				return false;
			return MatchFoundPolicy.CanDeliver(
				enabled: GetSettingsSnapshot().NotifyMatchFound,
				isBattlegrounds: game.IsBattlegroundsMatch,
				isInMenu: game.IsInMenu,
				isSpectator: game.CurrentGameMode == GameMode.Spectator,
				isReconnect: game.CurrentGameStats?.IsReconnect == true,
				heroPickingDone: game.IsBattlegroundsHeroPickingDone,
				isAway: HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus()));
		}

		private bool ValidatePendingTurn(TurnToken token)
		{
			if(!_enabled)
				return false;
			var game = HdtCore.Game;
			if(game == null || game.CurrentGameMode == GameMode.Spectator
				|| game.PlayerEntity?.IsCurrentPlayer != true)
				return false;
			if(!_tracker.IsCurrent(
				token,
				game.GetTurnNumber(),
				matchActive: !game.IsInMenu,
				isBattlegrounds: game.IsBattlegroundsMatch,
				isCombat: game.IsBattlegroundsCombatPhase))
				return false;
			return HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus());
		}

		private bool ValidatePendingCombat(CombatToken token)
		{
			if(!_enabled)
				return false;
			var game = HdtCore.Game;
			if(game == null)
				return false;
			if(!_tracker.IsCurrentCombat(
				token,
				matchActive: !game.IsInMenu,
				isBattlegrounds: game.IsBattlegroundsMatch,
				isCombat: game.IsBattlegroundsCombatPhase))
				return false;
			return CombatNotificationPolicy.CanDeliver(
				enabled: GetSettingsSnapshot().NotifyCombatStarted,
				matchActive: !game.IsInMenu,
				isBattlegrounds: game.IsBattlegroundsMatch,
				isSpectator: game.CurrentGameMode == GameMode.Spectator,
				isCombat: game.IsBattlegroundsCombatPhase,
				isAway: HearthstoneWindowState.IsAway(HearthstoneWindowState.GetStatus()));
		}

		private PluginSettings GetSettingsSnapshot()
		{
			lock(_settingsGate)
				return _settings.Clone();
		}

		private void SaveSettings(PluginSettings settings)
		{
			if(!_enabled)
				return;
			settings.Normalize();
			SettingsStore.Save(settings);
			lock(_settingsGate)
				_settings = settings.Clone();
			if(settings.FlashMode == TaskbarFlashMode.None)
				NotificationServices.StopHearthstoneFlash();
			_notifications?.CloseTest();
			NotificationServices.PrepareSound(settings, forceReload: true);
		}

		private void ShowSettings()
		{
			if(!_enabled)
				return;
			if(_settingsWindow != null)
			{
				_settingsWindow.Activate();
				return;
			}
			var window = new SettingsWindow(
				GetSettingsSnapshot(),
				SaveSettings,
				settings => _notifications?.ShowTest(settings));
			try
			{
				var owner = Application.Current?.MainWindow;
				if(owner != null && owner.IsVisible)
					window.Owner = owner;
			}
			catch
			{
			}
			window.Closed += (_, _) =>
			{
				_settingsWindow = null;
				_notifications?.CloseTest();
			};
			_settingsWindow = window;
			window.Show();
		}
	}
}

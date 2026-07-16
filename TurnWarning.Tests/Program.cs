using System;
using System.IO;
using System.Xml.Serialization;

namespace TurnWarning
{
	internal static class Program
	{
		private static int _passed;

		private static void Main()
		{
			Run("Combat -> Recruit creates one token", CombatToRecruitCreatesOneToken);
			Run("Duplicate turn event is suppressed", DuplicateTurnIsSuppressed);
			Run("Stale combat sample cannot rearm a handled boundary", StaleCombatSampleCannotRearmHandledBoundary);
			Run("Next combat allows next warning", NextCombatAllowsNextWarning);
			Run("Attach during Recruit creates no historical warning", AttachDuringRecruitIsQuiet);
			Run("Reconnect during Combat skips current boundary", ReconnectDuringCombatIsQuiet);
			Run("Normal attach during Combat can warn at the future boundary", NormalAttachDuringCombatCanWarn);
			Run("First shopping turn is suppressed", FirstTurnIsSuppressed);
			Run("Game end invalidates pending token", GameEndInvalidatesPendingToken);
			Run("New combat invalidates pending token", NewCombatInvalidatesPendingToken);
			Run("Old session token is rejected", OldSessionTokenIsRejected);
			Run("Unsupported mode stays quiet", UnsupportedModeStaysQuiet);
			Run("Combat boundary creates one notification token", CombatBoundaryCreatesOneNotificationToken);
			Run("Transient Combat state cannot duplicate notification", TransientCombatStateCannotDuplicateNotification);
			Run("Next valid Recruit phase rearms Combat notification", NextRecruitRearmsCombatNotification);
			Run("Attach or reconnect during Combat creates no notification", AttachDuringCombatCreatesNoNotification);
			Run("Late Battlegrounds classification arms future Combat", LateClassificationArmsFutureCombat);
			Run("Combat warning requires an active away Battlegrounds battle", CombatWarningRequiresActiveAwayBattle);
			Run("Default settings use English text and no sound", DefaultSettingsUseEnglishTextAndNoSound);
			Run("Settings normalization clamps unsafe values", SettingsNormalizationClampsValues);
			Run("Settings clone is independent", SettingsCloneIsIndependent);
			Run("Settings XML round-trip preserves options", SettingsXmlRoundTripPreservesOptions);
			Run("Older settings keep Combat warning disabled", OlderSettingsKeepCombatWarningDisabled);
			Run("Legacy focus option is ignored during upgrade", LegacyFocusOptionIsIgnoredDuringUpgrade);
			Run("Displayed version omits trailing zeros", DisplayedVersionOmitsTrailingZeros);
			Run("Combat result reports a win and exact damage", CombatResultReportsWin);
			Run("Combat result reports a loss and exact damage", CombatResultReportsLoss);
			Run("Duplicate player hero damage is counted once", DuplicatePlayerHeroDamageIsCountedOnce);
			Run("Duplicate opponent hero damage is counted once", DuplicateOpponentHeroDamageIsCountedOnce);
			Run("Living opponent with no hero damage reports a tie", CombatResultReportsTie);
			Run("Ghost combat does not invent a tie", GhostCombatDoesNotInventTie);
			Run("Concurrent combat damage is coalesced atomically", ConcurrentCombatDamageIsCoalescedAtomically);
			Run("Match found requires active hero selection while away", MatchFoundRequiresHeroSelectionWhileAway);
			Run("Match found suppresses reconnects and spectators", MatchFoundSuppressesReconnectsAndSpectators);
			Run("Old match observer cannot cancel a new request", OldMatchObserverCannotCancelNewRequest);
			Run("Game end invalidates match-found completion", GameEndInvalidatesMatchFoundCompletion);
			Run("Initial Recruit warns after hero selection while away", InitialRecruitWarnsAfterHeroSelection);
			Run("Initial Recruit rejects late or incomplete states", InitialRecruitRejectsInvalidStates);
			Run("Short PCM WAV passes validation", ShortPcmWavPassesValidation);
			Run("Long PCM WAV is rejected", LongPcmWavIsRejected);
			Run("Truncated WAV data is rejected", TruncatedWavDataIsRejected);
			Run("Invalid WAV path is rejected without throwing", InvalidWavPathIsRejectedWithoutThrowing);

			Console.WriteLine($"All {_passed} TurnWarning state-machine tests passed.");
		}

		private static void CombatToRecruitCreatesOneToken()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			var token = tracker.OnPlayerTurnStarted(2, true, true, true);
			Assert(token != null, "Expected a token");
			Assert(tracker.IsCurrent(token!, 2, true, true, false), "Token should be current after transition");
		}

		private static void DuplicateTurnIsSuppressed()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			Assert(tracker.OnPlayerTurnStarted(2, true, true, true) != null, "First event should pass");
			Assert(tracker.OnPlayerTurnStarted(2, true, true, true) == null, "Duplicate should be ignored");
		}

		private static void StaleCombatSampleCannotRearmHandledBoundary()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			var token = tracker.OnPlayerTurnStarted(2, true, true, true);
			Assert(token != null, "The Combat-to-Recruit boundary should create a token");
			tracker.Observe(true, true, true);
			Assert(tracker.IsCurrent(token!, 2, true, true, false),
				"A stale combat sample after OnTurnStart must not rearm or invalidate the boundary");
			tracker.Observe(true, true, false);
		}

		private static void NextCombatAllowsNextWarning()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			Assert(tracker.OnPlayerTurnStarted(2, true, true, true) != null, "Turn 2 should pass");
			tracker.Observe(true, true, false);
			tracker.Observe(true, true, true);
			Assert(tracker.OnPlayerTurnStarted(3, true, true, true) != null, "Turn 3 should pass");
		}

		private static void AttachDuringRecruitIsQuiet()
		{
			var tracker = NewRecruitTracker();
			Assert(tracker.OnPlayerTurnStarted(5, true, true, false) == null, "No combat boundary was observed");
		}

		private static void ReconnectDuringCombatIsQuiet()
		{
			var tracker = new TurnBoundaryTracker();
			tracker.Initialize(true, true, true, true);
			Assert(tracker.OnPlayerTurnStarted(5, true, true, true) == null, "Replay boundary must be suppressed");
		}

		private static void NormalAttachDuringCombatCanWarn()
		{
			var tracker = new TurnBoundaryTracker();
			tracker.Initialize(true, true, true, false);
			Assert(tracker.OnPlayerTurnStarted(5, true, true, true) != null, "Future boundary should be valid");
		}

		private static void FirstTurnIsSuppressed()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			Assert(tracker.OnPlayerTurnStarted(1, true, true, true) == null, "Initial shopping is not post-combat");
		}

		private static void GameEndInvalidatesPendingToken()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			var token = tracker.OnPlayerTurnStarted(2, true, true, true)!;
			tracker.OnGameEnded();
			Assert(!tracker.IsCurrent(token, 2, false, true, false), "Ended game must reject token");
		}

		private static void NewCombatInvalidatesPendingToken()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			var token = tracker.OnPlayerTurnStarted(2, true, true, true)!;
			tracker.Observe(true, true, true);
			Assert(!tracker.IsCurrent(token, 2, true, true, true), "Combat must cancel old notification");
		}

		private static void OldSessionTokenIsRejected()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			var token = tracker.OnPlayerTurnStarted(2, true, true, true)!;
			tracker.OnGameStarted(true);
			Assert(!tracker.IsCurrent(token, 2, true, true, false), "New session must reject old token");
		}

		private static void UnsupportedModeStaysQuiet()
		{
			var tracker = new TurnBoundaryTracker();
			tracker.Initialize(true, false, true, false);
			Assert(tracker.OnPlayerTurnStarted(4, true, false, true) == null, "Only Battlegrounds is supported");
		}

		private static void CombatBoundaryCreatesOneNotificationToken()
		{
			var tracker = NewRecruitTracker();
			Assert(tracker.Observe(true, true, true) == PhaseTransition.EnteredCombat,
				"Expected a Recruit-to-Combat transition");
			var token = tracker.OnCombatStarted();
			Assert(token != null, "Expected one Combat notification token");
			Assert(tracker.IsCurrentCombat(token!, true, true, true), "Combat token should remain current during Combat");
			Assert(tracker.OnCombatStarted() == null, "The same Combat boundary must not create another token");
		}

		private static void TransientCombatStateCannotDuplicateNotification()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			var token = tracker.OnCombatStarted()!;
			tracker.Observe(true, true, false);
			Assert(!tracker.IsCurrentCombat(token, true, true, false), "Leaving Combat must invalidate a pending warning");
			tracker.Observe(true, true, true);
			Assert(tracker.OnCombatStarted() == null, "A transient phase toggle must not rearm the same battle");
		}

		private static void NextRecruitRearmsCombatNotification()
		{
			var tracker = NewRecruitTracker();
			tracker.Observe(true, true, true);
			Assert(tracker.OnCombatStarted() != null, "First battle should create a warning token");
			Assert(tracker.OnPlayerTurnStarted(2, true, true, true) != null, "A valid Recruit boundary should be accepted");
			tracker.Observe(true, true, false);
			tracker.Observe(true, true, true);
			Assert(tracker.OnCombatStarted() != null, "The next battle should create a new warning token");
		}

		private static void AttachDuringCombatCreatesNoNotification()
		{
			var attached = new TurnBoundaryTracker();
			attached.Initialize(true, true, true, false);
			Assert(attached.OnCombatStarted() == null, "Attaching during an active battle must stay quiet");

			var reconnected = new TurnBoundaryTracker();
			reconnected.Initialize(true, true, true, true);
			Assert(reconnected.OnCombatStarted() == null, "Reconnect replay must stay quiet");
		}

		private static void LateClassificationArmsFutureCombat()
		{
			var tracker = new TurnBoundaryTracker();
			tracker.OnGameStarted(false);
			tracker.Observe(true, true, false);
			tracker.Observe(true, true, true);
			Assert(tracker.OnCombatStarted() != null, "Classification during Recruit should arm the future battle");
		}

		private static void CombatWarningRequiresActiveAwayBattle()
		{
			Assert(CombatNotificationPolicy.CanDeliver(true, true, true, false, true, true),
				"An enabled active Battlegrounds battle while away should deliver");
			Assert(!CombatNotificationPolicy.CanDeliver(false, true, true, false, true, true), "Disabled option must stay quiet");
			Assert(!CombatNotificationPolicy.CanDeliver(true, true, true, false, true, false), "Focused Hearthstone must stay quiet");
			Assert(!CombatNotificationPolicy.CanDeliver(true, true, true, true, true, true), "Spectator mode must stay quiet");
			Assert(!CombatNotificationPolicy.CanDeliver(true, true, false, false, true, true), "Other game modes must stay quiet");
			Assert(!CombatNotificationPolicy.CanDeliver(true, false, true, false, true, true), "Inactive matches must stay quiet");
			Assert(!CombatNotificationPolicy.CanDeliver(true, true, true, false, false, true), "Recruit phase must stay quiet");
		}

		private static void DefaultSettingsUseEnglishTextAndNoSound()
		{
			var settings = new PluginSettings();
			Assert(settings.ShowWindow, "Window should be enabled by default");
			Assert(!settings.PlaySound, "Sound should be disabled by default");
			Assert(settings.FlashMode == TaskbarFlashMode.None, "Taskbar flashing should be opt-in");
			Assert(settings.MonitorMode == NotificationMonitorMode.ActiveWindow, "Original active-monitor placement should remain the default");
			Assert(settings.Position == NotificationPosition.BottomRight, "Original corner should remain the default");
			Assert(settings.Title == "Your turn has started", "Default title should be English");
			Assert(settings.Message.StartsWith("Combat is over"), "Default message should be English");
			Assert(settings.NotifyMatchFound, "Match found notifications should be enabled by default");
			Assert(!settings.NotifyCombatStarted, "Combat-start notifications should be disabled by default");
			Assert(settings.ShowCombatResult, "Combat result should be enabled by default");
			Assert(settings.CombatResultStyle == CombatResultStyle.ResultPanel, "Result panel should be the default combat result style");
			Assert(settings.PulseMode == NotificationPulseMode.UntilFocused, "Notification should pulse until focused by default");
			Assert(settings.Style == NotificationStyle.Compact, "Compact notification should be the default style");
			Assert(settings.DisplaySeconds == 10, "Default display time should be 10 seconds");
		}

		private static void SettingsNormalizationClampsValues()
		{
			var settings = new PluginSettings
			{
				Title = " ",
				Message = string.Empty,
				DisplaySeconds = 100,
				StabilizationDelayMs = -10,
				SoundPath = "  sound.wav  "
			};
			settings.Normalize();
			Assert(settings.DisplaySeconds == 60, "Duration should be clamped");
			Assert(settings.StabilizationDelayMs == 0, "Delay should be clamped");
			Assert(settings.Title.Length > 0 && settings.Message.Length > 0, "Default text should be restored");
			Assert(settings.SoundPath == "sound.wav", "Paths should be trimmed");
		}

		private static void SettingsCloneIsIndependent()
		{
			var settings = new PluginSettings { Title = "Original" };
			var clone = settings.Clone();
			clone.Title = "Changed";
			Assert(settings.Title == "Original", "Changing a snapshot must not mutate live settings");
		}

		private static void SettingsXmlRoundTripPreservesOptions()
		{
			var source = new PluginSettings
			{
				ShowWindow = false,
				PlaySound = true,
				NotifyMatchFound = false,
				NotifyCombatStarted = true,
				ShowCombatResult = true,
				CombatResultStyle = CombatResultStyle.TwoColumn,
				SoundPath = "turn.wav",
				FlashMode = TaskbarFlashMode.UntilFocused,
				PulseMode = NotificationPulseMode.Brief,
				MonitorMode = NotificationMonitorMode.Specific,
				MonitorDeviceName = "DISPLAY2",
				Position = NotificationPosition.TopLeft,
				Style = NotificationStyle.Compact,
				Title = "Custom",
				Message = "Message",
				DisplaySeconds = 12,
				StabilizationDelayMs = 1200
			};
			var serializer = new XmlSerializer(typeof(PluginSettings));
			using(var stream = new MemoryStream())
			{
				serializer.Serialize(stream, source);
				stream.Position = 0;
				var restored = (PluginSettings)serializer.Deserialize(stream)!;
				Assert(restored.FlashMode == source.FlashMode, "Flash mode should survive serialization");
				Assert(restored.PulseMode == NotificationPulseMode.Brief, "Notification pulse mode should survive serialization");
				Assert(restored.MonitorDeviceName == source.MonitorDeviceName, "Monitor should survive serialization");
				Assert(restored.Title == source.Title && restored.DisplaySeconds == 12, "Text and duration should survive serialization");
				Assert(!restored.NotifyMatchFound && restored.NotifyCombatStarted && restored.ShowCombatResult,
					"Event options should survive serialization");
				Assert(restored.CombatResultStyle == CombatResultStyle.TwoColumn, "Combat result style should survive serialization");
			}
		}

		private static void DisplayedVersionOmitsTrailingZeros()
		{
			Assert(new Version(1, 1).ToString() == "1.1", "Plugin API version should be displayed without trailing zeros");
			Assert(new Version(1, 3, 1).ToString() == "1.3.1", "Patch versions should retain meaningful components");
		}

		private static void OlderSettingsKeepCombatWarningDisabled()
		{
			var serializer = new XmlSerializer(typeof(PluginSettings));
			string xml;
			using(var writer = new StringWriter())
			{
				serializer.Serialize(writer, new PluginSettings());
				xml = writer.ToString();
			}
			xml = xml.Replace("  <NotifyCombatStarted>false</NotifyCombatStarted>\r\n", string.Empty)
				.Replace("  <NotifyCombatStarted>false</NotifyCombatStarted>\n", string.Empty);
			Assert(!xml.Contains("NotifyCombatStarted"), "The compatibility fixture must omit the new setting");
			using(var reader = new StringReader(xml))
			{
				var restored = (PluginSettings)serializer.Deserialize(reader)!;
				Assert(!restored.NotifyCombatStarted, "Settings created before 1.1 must not enable Combat warnings");
			}
		}

		private static void LegacyFocusOptionIsIgnoredDuringUpgrade()
		{
			var serializer = new XmlSerializer(typeof(PluginSettings));
			string xml;
			using(var writer = new StringWriter())
			{
				serializer.Serialize(writer, new PluginSettings());
				xml = writer.ToString();
			}
			xml = xml.Replace("</PluginSettings>",
				"<CloseWhenHearthstoneFocused>false</CloseWhenHearthstoneFocused></PluginSettings>");
			using(var reader = new StringReader(xml))
				Assert(serializer.Deserialize(reader) is PluginSettings,
					"RC8 settings with the removed focus option must still load");
		}

		private static void CombatResultReportsWin()
		{
			var tracker = new CombatResultTracker();
			tracker.Begin(true);
			tracker.RecordHeroDamage(CombatSide.Opponent, 12);
			Assert(tracker.Complete()?.Text == "Previous combat: Win - 12 damage dealt", "Expected a win with damage dealt");
		}

		private static void CombatResultReportsLoss()
		{
			var tracker = new CombatResultTracker();
			tracker.Begin(true);
			tracker.RecordHeroDamage(CombatSide.Player, 7);
			Assert(tracker.Complete()?.Text == "Previous combat: Loss - 7 damage taken", "Expected a loss with damage taken");
		}

		private static void CombatResultReportsTie()
		{
			var tracker = new CombatResultTracker();
			tracker.Begin(true);
			Assert(tracker.Complete()?.Text == "Previous combat: Tie - no hero damage", "Expected a tie");
		}

		private static void DuplicatePlayerHeroDamageIsCountedOnce()
		{
			var tracker = new CombatResultTracker();
			tracker.Begin(true);
			tracker.RecordHeroDamage(CombatSide.Player, 5);
			tracker.RecordHeroDamage(CombatSide.Player, 5);
			var result = tracker.Complete();
			Assert(result?.Outcome == CombatOutcome.Loss && result.Damage == 5,
				"Mirrored player hero predamage must not be added twice");
		}

		private static void DuplicateOpponentHeroDamageIsCountedOnce()
		{
			var tracker = new CombatResultTracker();
			tracker.Begin(true);
			tracker.RecordHeroDamage(CombatSide.Opponent, 7);
			tracker.RecordHeroDamage(CombatSide.Opponent, 7);
			var result = tracker.Complete();
			Assert(result?.Outcome == CombatOutcome.Win && result.Damage == 7,
				"Mirrored opponent hero predamage must not be added twice");
		}

		private static void GhostCombatDoesNotInventTie()
		{
			var tracker = new CombatResultTracker();
			tracker.Begin(false);
			Assert(tracker.Complete() == null, "Ghost combat should be omitted when no result is observable");
		}

		private static void ConcurrentCombatDamageIsCoalescedAtomically()
		{
			var tracker = new CombatResultTracker();
			tracker.Begin(true);
			System.Threading.Tasks.Parallel.For(0, 10000, index =>
				tracker.RecordHeroDamage(CombatSide.Opponent, index % 17 + 1));
			var result = tracker.Complete();
			Assert(result?.Outcome == CombatOutcome.Win && result.Damage == 17,
				"Concurrent mirrored damage events must retain the highest final value");
		}

		private static void MatchFoundRequiresHeroSelectionWhileAway()
		{
			Assert(MatchFoundPolicy.CanDeliver(true, true, false, false, false, false, true), "Expected a valid match-found notification");
			Assert(!MatchFoundPolicy.CanDeliver(true, true, false, false, false, true, true), "Completed hero selection should be quiet");
			Assert(!MatchFoundPolicy.CanDeliver(true, true, false, false, false, false, false), "Focused Hearthstone should be quiet");
		}

		private static void MatchFoundSuppressesReconnectsAndSpectators()
		{
			Assert(!MatchFoundPolicy.CanDeliver(true, true, false, false, true, false, true), "Reconnect should be quiet");
			Assert(!MatchFoundPolicy.CanDeliver(true, true, false, true, false, false, true), "Spectator session should be quiet");
			Assert(!MatchFoundPolicy.CanDeliver(false, true, false, false, false, false, true), "Disabled option should be quiet");
		}

		private static void InitialRecruitWarnsAfterHeroSelection()
		{
			Assert(MatchFoundPolicy.CanDeliverInitialRecruit(true, false, false, false, true, false, 1, true),
				"Expected an initial Recruit notification after hero selection");
		}

		private static void OldMatchObserverCannotCancelNewRequest()
		{
			var state = new MatchFoundRequestState();
			state.Start(true, DateTime.UtcNow.AddMinutes(1));
			Assert(state.TryGetPending(out var oldGeneration, out _), "The first request should be pending");
			state.Start(true, DateTime.UtcNow.AddMinutes(1));
			Assert(!state.CancelIfCurrent(oldGeneration), "An observer from the old match must not cancel the new request");
			Assert(state.TryGetPending(out var currentGeneration, out _) && currentGeneration != oldGeneration,
				"The new request should remain pending with a new generation");
		}

		private static void GameEndInvalidatesMatchFoundCompletion()
		{
			var state = new MatchFoundRequestState();
			state.Start(true, DateTime.UtcNow.AddMinutes(1));
			Assert(state.TryGetPending(out var generation, out _), "The request should be pending");
			state.End();
			Assert(!state.TryComplete(generation), "A request from the ended game must not complete");
			Assert(!state.IsCurrent(generation), "Game end must invalidate the request generation");
		}

		private static void InitialRecruitRejectsInvalidStates()
		{
			Assert(!MatchFoundPolicy.CanDeliverInitialRecruit(true, false, false, false, false, false, 1, true),
				"Hero selection must be complete");
			Assert(!MatchFoundPolicy.CanDeliverInitialRecruit(true, false, false, false, true, false, 2, true),
				"Later Recruit turns must use the combat boundary flow");
			Assert(!MatchFoundPolicy.CanDeliverInitialRecruit(true, false, false, true, true, false, 1, true),
				"Reconnect should be quiet");
			Assert(!MatchFoundPolicy.CanDeliverInitialRecruit(true, false, false, false, true, false, 1, false),
				"Focused Hearthstone should be quiet");
		}

		private static void ShortPcmWavPassesValidation()
		{
			var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
			try
			{
				WritePcmWav(path, 1);
				Assert(WavFileValidator.TryValidate(path, out _), "A short PCM WAV should be accepted");
			}
			finally
			{
				if(File.Exists(path))
					File.Delete(path);
			}
		}

		private static void LongPcmWavIsRejected()
		{
			var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
			try
			{
				WritePcmWav(path, 11);
				Assert(!WavFileValidator.TryValidate(path, out var error) && error.Contains("10 seconds"),
					"A sound longer than the notification limit should be rejected");
			}
			finally
			{
				if(File.Exists(path))
					File.Delete(path);
			}
		}

		private static void TruncatedWavDataIsRejected()
		{
			var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
			try
			{
				WritePcmWav(path, 1);
				using(var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
					stream.SetLength(stream.Length - 100);
				Assert(!WavFileValidator.TryValidate(path, out var error) && error.Contains("truncated"),
					"A WAV whose data chunk exceeds the file must be rejected");
			}
			finally
			{
				if(File.Exists(path))
					File.Delete(path);
			}
		}

		private static void InvalidWavPathIsRejectedWithoutThrowing()
		{
			var result = WavFileValidator.TryValidate("invalid\0path.wav", out var error);
			Assert(!result && !string.IsNullOrEmpty(error), "An invalid path should return a validation error");
		}

		private static void WritePcmWav(string path, int seconds)
		{
			const int sampleRate = 8000;
			const short channels = 1;
			const short bits = 8;
			var dataLength = sampleRate * seconds;
			using(var stream = File.Create(path))
			using(var writer = new BinaryWriter(stream))
			{
				writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
				writer.Write(36 + dataLength);
				writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
				writer.Write(16);
				writer.Write((short)1);
				writer.Write(channels);
				writer.Write(sampleRate);
				writer.Write(sampleRate * channels * bits / 8);
				writer.Write((short)(channels * bits / 8));
				writer.Write(bits);
				writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
				writer.Write(dataLength);
				writer.Write(new byte[dataLength]);
			}
		}

		private static TurnBoundaryTracker NewRecruitTracker()
		{
			var tracker = new TurnBoundaryTracker();
			tracker.Initialize(true, true, false, false);
			return tracker;
		}

		private static void Run(string name, Action test)
		{
			try
			{
				test();
				_passed++;
				Console.WriteLine($"PASS: {name}");
			}
			catch(Exception ex)
			{
				Console.Error.WriteLine($"FAIL: {name}: {ex.Message}");
				Environment.ExitCode = 1;
				throw;
			}
		}

		private static void Assert(bool condition, string message)
		{
			if(!condition)
				throw new InvalidOperationException(message);
		}
	}
}

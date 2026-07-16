using System;
using System.IO;
using System.Xml.Serialization;

namespace TurnWarning
{
	public enum NotificationMonitorMode
	{
		ActiveWindow,
		Hearthstone,
		Primary,
		Specific
	}

	public enum NotificationPosition
	{
		TopLeft,
		TopRight,
		BottomLeft,
		BottomRight,
		Center
	}

	public enum NotificationStyle
	{
		Banner,
		Compact
	}

	public enum TaskbarFlashMode
	{
		None,
		Brief,
		UntilFocused
	}

	public enum NotificationPulseMode
	{
		None,
		Brief,
		UntilFocused
	}

	public enum CombatResultStyle
	{
		FullColoredLine,
		StatusLabel,
		ColoredMarker,
		ResultPanel,
		TwoColumn
	}

	public sealed class PluginSettings
	{
		public const string DefaultTitle = "Your turn has started";
		public const string DefaultMessage = "Combat is over - the Recruit phase has started";

		public bool ShowWindow { get; set; } = true;
		public bool PlaySound { get; set; } = false;
		public bool NotifyMatchFound { get; set; } = true;
		public bool NotifyCombatStarted { get; set; } = false;
		public bool ShowCombatResult { get; set; } = true;
		public CombatResultStyle CombatResultStyle { get; set; } = CombatResultStyle.ResultPanel;
		public string SoundPath { get; set; } = string.Empty;
		public TaskbarFlashMode FlashMode { get; set; } = TaskbarFlashMode.None;
		public NotificationPulseMode PulseMode { get; set; } = NotificationPulseMode.UntilFocused;
		public NotificationMonitorMode MonitorMode { get; set; } = NotificationMonitorMode.ActiveWindow;
		public string MonitorDeviceName { get; set; } = string.Empty;
		public NotificationPosition Position { get; set; } = NotificationPosition.BottomRight;
		public NotificationStyle Style { get; set; } = NotificationStyle.Compact;
		public string Title { get; set; } = DefaultTitle;
		public string Message { get; set; } = DefaultMessage;
		public int DisplaySeconds { get; set; } = 10;
		public int StabilizationDelayMs { get; set; } = 850;

		public PluginSettings Clone()
		{
			return new PluginSettings
			{
				ShowWindow = ShowWindow,
				PlaySound = PlaySound,
				NotifyMatchFound = NotifyMatchFound,
				NotifyCombatStarted = NotifyCombatStarted,
				ShowCombatResult = ShowCombatResult,
				CombatResultStyle = CombatResultStyle,
				SoundPath = SoundPath,
				FlashMode = FlashMode,
				PulseMode = PulseMode,
				MonitorMode = MonitorMode,
				MonitorDeviceName = MonitorDeviceName,
				Position = Position,
				Style = Style,
				Title = Title,
				Message = Message,
				DisplaySeconds = DisplaySeconds,
				StabilizationDelayMs = StabilizationDelayMs
			};
		}

		public void Normalize()
		{
			SoundPath = (SoundPath ?? string.Empty).Trim();
			MonitorDeviceName = (MonitorDeviceName ?? string.Empty).Trim();
			Title = string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title.Trim();
			Message = string.IsNullOrWhiteSpace(Message) ? DefaultMessage : Message.Trim();
			DisplaySeconds = Math.Max(2, Math.Min(60, DisplaySeconds));
			StabilizationDelayMs = Math.Max(0, Math.Min(3000, StabilizationDelayMs));
		}
	}

	internal static class SettingsStore
	{
		private static readonly string SettingsDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"HearthstoneDeckTracker",
			"TurnWarning");

		private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.xml");

		public static PluginSettings Load()
		{
			try
			{
				if(!File.Exists(SettingsPath))
					return new PluginSettings();
				var serializer = new XmlSerializer(typeof(PluginSettings));
				using(var stream = File.OpenRead(SettingsPath))
				{
					var settings = serializer.Deserialize(stream) as PluginSettings ?? new PluginSettings();
					settings.Normalize();
					return settings;
				}
			}
			catch
			{
				return new PluginSettings();
			}
		}

		public static void Save(PluginSettings settings)
		{
			settings.Normalize();
			Directory.CreateDirectory(SettingsDirectory);
			var temporaryPath = SettingsPath + ".tmp";
			var backupPath = SettingsPath + ".bak";
			var serializer = new XmlSerializer(typeof(PluginSettings));
			using(var stream = File.Create(temporaryPath))
				serializer.Serialize(stream, settings);

			if(File.Exists(SettingsPath))
				File.Replace(temporaryPath, SettingsPath, backupPath, true);
			else
				File.Move(temporaryPath, SettingsPath);
		}
	}
}

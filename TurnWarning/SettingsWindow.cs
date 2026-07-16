using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace TurnWarning
{
	internal sealed class SettingsWindow : Window
	{
		private readonly Action<PluginSettings> _save;
		private readonly Action<PluginSettings> _preview;
		private readonly CheckBox _showWindow = new CheckBox { Content = "Show notification window" };
		private readonly CheckBox _playSound = new CheckBox { Content = "Play sound" };
		private readonly CheckBox _notifyMatchFound = new CheckBox { Content = "Notify when a Battlegrounds match is found" };
		private readonly CheckBox _notifyCombatStarted = new CheckBox { Content = "Notify when Combat starts" };
		private readonly CheckBox _showCombatResult = new CheckBox { Content = "Include the previous combat result when available" };
		private readonly TextBox _soundPath = new TextBox();
		private readonly TextBox _title = new TextBox();
		private readonly TextBox _message = new TextBox();
		private readonly TextBox _displaySeconds = new TextBox();
		private readonly TextBox _delayMs = new TextBox();
		private readonly ComboBox _flashMode = NewCombo();
		private readonly ComboBox _pulseMode = NewCombo();
		private readonly ComboBox _monitor = NewCombo();
		private readonly ComboBox _position = NewCombo();
		private readonly ComboBox _style = NewCombo();
		private readonly ComboBox _combatResultStyle = NewCombo();
		private readonly TextBlock _status = new TextBlock
		{
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(Color.FromRgb(41, 128, 72)),
			Margin = new Thickness(16, 0, 16, 0)
		};
		private readonly List<MonitorChoice> _monitorChoices = new List<MonitorChoice>();
		private bool _soundOperationInProgress;
		private bool _closed;

		public SettingsWindow(PluginSettings settings, Action<PluginSettings> save, Action<PluginSettings> preview)
		{
			_save = save;
			_preview = preview;
			Title = "TurnWarning Settings";
			Width = 760;
			MinWidth = 580;
			MinHeight = 520;
			MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
			Height = Math.Max(MinHeight, Math.Min(860, SystemParameters.WorkArea.Height - 70));
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ResizeMode = ResizeMode.CanResize;
			Content = BuildContent();
			LoadSettings(settings);
			Closed += (_, _) =>
			{
				_closed = true;
				NotificationServices.ClearPreviewSound();
			};
		}

		private UIElement BuildContent()
		{
			var root = new Grid();
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			var content = new StackPanel { Margin = new Thickness(18, 14, 18, 8) };
			content.Children.Add(BuildEventsGroup());
			content.Children.Add(BuildDeliveryGroup());
			content.Children.Add(BuildPlacementGroup());
			content.Children.Add(BuildTextGroup());
			content.Children.Add(BuildTimingGroup());
			content.Children.Add(new TextBlock
			{
				Text = "Custom audio supports uncompressed PCM WAV files up to 5 MB and 10 seconds. Leave the path empty to use the Windows system sound. If a custom sound is unavailable or still loading, the Windows system sound is used instead. Taskbar flashing never activates the game.",
				TextWrapping = TextWrapping.Wrap,
				Foreground = Brushes.DimGray,
				Margin = new Thickness(2, 6, 2, 8)
			});
			var scroll = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Content = content
			};
			Grid.SetRow(scroll, 0);
			root.Children.Add(scroll);

			var footer = BuildFooter();
			Grid.SetRow(footer, 1);
			root.Children.Add(footer);
			return root;
		}

		private UIElement BuildFooter()
		{
			var grid = new Grid { Margin = new Thickness(18, 10, 18, 12) };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

			var version = new TextBlock
			{
				Text = "Version " + TurnWarningPlugin.DisplayVersion,
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = Brushes.DimGray
			};
			grid.Children.Add(version);
			Grid.SetColumn(_status, 1);
			grid.Children.Add(_status);

			var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
			var test = NewButton("TEST NOTIFICATION", 145);
			test.FontWeight = FontWeights.SemiBold;
			test.Background = new SolidColorBrush(Color.FromRgb(224, 218, 199));
			test.Foreground = new SolidColorBrush(Color.FromRgb(62, 58, 49));
			test.BorderBrush = new SolidColorBrush(Color.FromRgb(170, 158, 125));
			test.ToolTip = "Preview the currently displayed settings without saving them";
			test.Click += (_, _) => Preview();

			var ok = NewButton("OK", 72);
			ok.Margin = new Thickness(10, 0, 0, 0);
			ok.Click += (_, _) => Apply(closeAfterSave: true);
			var cancel = NewButton("Cancel", 82);
			cancel.Margin = new Thickness(8, 0, 0, 0);
			cancel.Click += (_, _) => Close();
			var apply = NewButton("Apply", 82);
			apply.Margin = new Thickness(8, 0, 0, 0);
			apply.Click += (_, _) => Apply(closeAfterSave: false);

			var reset = NewButton("Reset", 82);
			reset.Margin = new Thickness(8, 0, 0, 0);
			reset.ToolTip = "Load default values without saving them";
			reset.Click += (_, _) => ResetToDefaults();

			buttons.Children.Add(test);
			buttons.Children.Add(reset);
			buttons.Children.Add(ok);
			buttons.Children.Add(cancel);
			buttons.Children.Add(apply);
			Grid.SetColumn(buttons, 2);
			grid.Children.Add(buttons);

			return new Border
			{
				BorderBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
				BorderThickness = new Thickness(0, 1, 0, 0),
				Background = new SolidColorBrush(Color.FromRgb(247, 247, 247)),
				Child = grid
			};
		}

		private GroupBox BuildEventsGroup()
		{
			_combatResultStyle.ItemsSource = new[]
			{
				new Choice<CombatResultStyle>("Full colored line", CombatResultStyle.FullColoredLine),
				new Choice<CombatResultStyle>("Status label", CombatResultStyle.StatusLabel),
				new Choice<CombatResultStyle>("Colored marker", CombatResultStyle.ColoredMarker),
				new Choice<CombatResultStyle>("Result panel", CombatResultStyle.ResultPanel),
				new Choice<CombatResultStyle>("Two-column result", CombatResultStyle.TwoColumn)
			};
			var panel = NewGroupPanel();
			panel.Children.Add(_notifyMatchFound);
			panel.Children.Add(_notifyCombatStarted);
			panel.Children.Add(_showCombatResult);
			panel.Children.Add(Labeled("Combat result appearance", _combatResultStyle));
			return Group("Events", panel);
		}

		private GroupBox BuildDeliveryGroup()
		{
			_pulseMode.ItemsSource = new[]
			{
				new Choice<NotificationPulseMode>("No effect", NotificationPulseMode.None),
				new Choice<NotificationPulseMode>("Pulse 3 times", NotificationPulseMode.Brief),
				new Choice<NotificationPulseMode>("Pulse until Hearthstone is focused", NotificationPulseMode.UntilFocused)
			};
			_flashMode.ItemsSource = new[]
			{
				new Choice<TaskbarFlashMode>("Do not flash", TaskbarFlashMode.None),
				new Choice<TaskbarFlashMode>("Flash 3 times", TaskbarFlashMode.Brief),
				new Choice<TaskbarFlashMode>("Flash until Hearthstone is focused", TaskbarFlashMode.UntilFocused)
			};
			var panel = NewGroupPanel();
			panel.Children.Add(_showWindow);
			panel.Children.Add(_playSound);
			panel.Children.Add(Labeled("Notification window effect", _pulseMode));
			panel.Children.Add(Labeled("Hearthstone taskbar button", _flashMode));

			var browse = NewButton("Browse...", 90);
			browse.Margin = new Thickness(8, 0, 0, 0);
			browse.Click += (_, _) => BrowseSound();
			var soundRow = new DockPanel();
			DockPanel.SetDock(browse, Dock.Right);
			soundRow.Children.Add(browse);
			soundRow.Children.Add(_soundPath);
			panel.Children.Add(Labeled("Custom WAV file", soundRow));
			return Group("Notification methods", panel);
		}

		private GroupBox BuildPlacementGroup()
		{
			_monitorChoices.Add(new MonitorChoice("Monitor containing the active window", NotificationMonitorMode.ActiveWindow, string.Empty));
			_monitorChoices.Add(new MonitorChoice("Monitor containing Hearthstone", NotificationMonitorMode.Hearthstone, string.Empty));
			_monitorChoices.Add(new MonitorChoice("Primary monitor", NotificationMonitorMode.Primary, string.Empty));
			var index = 1;
			foreach(var screen in Forms.Screen.AllScreens)
			{
				var suffix = screen.Primary ? " (primary)" : string.Empty;
				_monitorChoices.Add(new MonitorChoice($"Monitor {index}: {screen.DeviceName}{suffix}", NotificationMonitorMode.Specific, screen.DeviceName));
				index++;
			}
			_monitor.ItemsSource = _monitorChoices;

			_position.ItemsSource = new[]
			{
				new Choice<NotificationPosition>("Top left", NotificationPosition.TopLeft),
				new Choice<NotificationPosition>("Top right", NotificationPosition.TopRight),
				new Choice<NotificationPosition>("Bottom left", NotificationPosition.BottomLeft),
				new Choice<NotificationPosition>("Bottom right", NotificationPosition.BottomRight),
				new Choice<NotificationPosition>("Center", NotificationPosition.Center)
			};
			_style.ItemsSource = new[]
			{
				new Choice<NotificationStyle>("Standard", NotificationStyle.Banner),
				new Choice<NotificationStyle>("Compact", NotificationStyle.Compact)
			};

			var panel = NewGroupPanel();
			panel.Children.Add(Labeled("Show on", _monitor));
			panel.Children.Add(Labeled("Position", _position));
			panel.Children.Add(Labeled("Size", _style));
			return Group("Screen and position", panel);
		}

		private GroupBox BuildTextGroup()
		{
			var panel = NewGroupPanel();
			panel.Children.Add(Labeled("Title", _title));
			panel.Children.Add(Labeled("Message", _message));
			return Group("Notification text", panel);
		}

		private GroupBox BuildTimingGroup()
		{
			var panel = NewGroupPanel();
			panel.Children.Add(Labeled("Display time, seconds (2-60)", _displaySeconds));
			panel.Children.Add(Labeled("Focus validation delay, ms (0-3000)", _delayMs));
			return Group("Timing", panel);
		}

		private void LoadSettings(PluginSettings settings)
		{
			_showWindow.IsChecked = settings.ShowWindow;
			_playSound.IsChecked = settings.PlaySound;
			_notifyMatchFound.IsChecked = settings.NotifyMatchFound;
			_notifyCombatStarted.IsChecked = settings.NotifyCombatStarted;
			_showCombatResult.IsChecked = settings.ShowCombatResult;
			Select(_combatResultStyle, settings.CombatResultStyle);
			_soundPath.Text = settings.SoundPath;
			_title.Text = settings.Title;
			_message.Text = settings.Message;
			_displaySeconds.Text = settings.DisplaySeconds.ToString();
			_delayMs.Text = settings.StabilizationDelayMs.ToString();
			Select(_flashMode, settings.FlashMode);
			Select(_pulseMode, settings.PulseMode);
			Select(_position, settings.Position);
			Select(_style, settings.Style);
			_monitor.SelectedItem = _monitorChoices.FirstOrDefault(x =>
				x.Mode == settings.MonitorMode
				&& (x.Mode != NotificationMonitorMode.Specific || string.Equals(x.DeviceName, settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase)))
				?? _monitorChoices[0];
		}

		private void ResetToDefaults()
		{
			LoadSettings(new PluginSettings());
			_status.Text = "Defaults loaded. Click Apply or OK to save.";
		}

		private void BrowseSound()
		{
			var dialog = new OpenFileDialog
			{
				Title = "Select a WAV file",
				Filter = "Wave audio (*.wav)|*.wav|All files (*.*)|*.*",
				CheckFileExists = true
			};
			if(dialog.ShowDialog(this) == true)
				_soundPath.Text = dialog.FileName;
		}

		private async void Preview()
		{
			if(_soundOperationInProgress)
				return;
			_soundOperationInProgress = true;
			try
			{
				_status.Text = string.Empty;
				if(!TryReadSettings(out var settings))
					return;
				if(!await ValidateSoundAsync(settings))
					return;
				await NotificationServices.PreparePreviewSoundAsync(settings);
				if(_closed)
					return;
				_preview(settings);
			}
			catch(Exception ex)
			{
				MessageBox.Show(this, "Could not test the notification: " + ex.Message, "TurnWarning", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				_soundOperationInProgress = false;
			}
		}

		private async void Apply(bool closeAfterSave)
		{
			if(_soundOperationInProgress)
				return;
			_soundOperationInProgress = true;
			try
			{
				if(!TryReadSettings(out var settings))
					return;
				if(!await ValidateSoundAsync(settings))
					return;
				if(_closed)
					return;
				_save(settings);
				if(closeAfterSave)
					Close();
				else
					_status.Text = "Settings applied.";
			}
			catch(Exception ex)
			{
				MessageBox.Show(this, "Could not save settings: " + ex.Message, "TurnWarning", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				_soundOperationInProgress = false;
			}
		}

		private bool TryReadSettings(out PluginSettings settings)
		{
			settings = new PluginSettings();
			if(!int.TryParse(_displaySeconds.Text, out var seconds) || seconds < 2 || seconds > 60)
				return ValidationError("Display time must be between 2 and 60 seconds.");
			if(!int.TryParse(_delayMs.Text, out var delay) || delay < 0 || delay > 3000)
				return ValidationError("Focus validation delay must be between 0 and 3000 ms.");
			var soundPath = _soundPath.Text.Trim();
			var monitor = _monitor.SelectedItem as MonitorChoice ?? _monitorChoices[0];
			settings = new PluginSettings
			{
				ShowWindow = _showWindow.IsChecked == true,
				PlaySound = _playSound.IsChecked == true,
				NotifyMatchFound = _notifyMatchFound.IsChecked == true,
				NotifyCombatStarted = _notifyCombatStarted.IsChecked == true,
				ShowCombatResult = _showCombatResult.IsChecked == true,
				CombatResultStyle = Selected<CombatResultStyle>(_combatResultStyle),
				SoundPath = soundPath,
				FlashMode = Selected<TaskbarFlashMode>(_flashMode),
				PulseMode = Selected<NotificationPulseMode>(_pulseMode),
				MonitorMode = monitor.Mode,
				MonitorDeviceName = monitor.DeviceName,
				Position = Selected<NotificationPosition>(_position),
				Style = Selected<NotificationStyle>(_style),
				Title = _title.Text,
				Message = _message.Text,
				DisplaySeconds = seconds,
				StabilizationDelayMs = delay
			};
			settings.Normalize();
			return true;
		}

		private async Task<bool> ValidateSoundAsync(PluginSettings settings)
		{
			if(!settings.PlaySound || string.IsNullOrWhiteSpace(settings.SoundPath))
				return true;
			_status.Text = "Checking sound...";
			var error = await Task.Run(() => ValidateSoundFile(settings.SoundPath));
			if(_closed)
				return false;
			_status.Text = string.Empty;
			return error == null || ValidationError(error);
		}

		private static string? ValidateSoundFile(string path)
		{
			try
			{
				if(!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
					return "Select a WAV file, or leave the path empty to use the Windows system sound.";
				return WavFileValidator.TryValidate(path, out var error) ? null : error;
			}
			catch(Exception ex) when(ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
			{
				return "The WAV file path is not valid.";
			}
		}

		private bool ValidationError(string message)
		{
			_status.Text = string.Empty;
			MessageBox.Show(this, message, "TurnWarning", MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}

		private static ComboBox NewCombo() => new ComboBox { MinWidth = 250, DisplayMemberPath = "Label" };
		private static Button NewButton(string text, double width) => new Button { Content = text, Width = width, Height = 30 };
		private static StackPanel NewGroupPanel() => new StackPanel { Margin = new Thickness(10, 6, 10, 8) };
		private static GroupBox Group(string title, UIElement content) => new GroupBox { Header = title, Content = content, Margin = new Thickness(0, 0, 0, 8) };

		private static FrameworkElement Labeled(string label, UIElement control)
		{
			var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
			Grid.SetColumn(control, 1);
			grid.Children.Add(text);
			grid.Children.Add(control);
			return grid;
		}

		private static void Select<T>(ComboBox combo, T value) where T : struct
		{
			combo.SelectedItem = combo.Items.Cast<object>().OfType<Choice<T>>().First(x => EqualityComparer<T>.Default.Equals(x.Value, value));
		}

		private static T Selected<T>(ComboBox combo) where T : struct
			=> ((Choice<T>)combo.SelectedItem).Value;

		private sealed class Choice<T> where T : struct
		{
			public Choice(string label, T value) { Label = label; Value = value; }
			public string Label { get; }
			public T Value { get; }
		}

		private sealed class MonitorChoice
		{
			public MonitorChoice(string label, NotificationMonitorMode mode, string deviceName)
			{
				Label = label;
				Mode = mode;
				DeviceName = deviceName;
			}
			public string Label { get; }
			public NotificationMonitorMode Mode { get; }
			public string DeviceName { get; }
		}
	}
}

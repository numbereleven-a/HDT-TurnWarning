using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TurnWarning
{
	internal sealed class TurnWarningWindow : Window
	{
		private const int GwlExStyle = -20;
		private const long WsExNoActivate = 0x08000000L;
		private const long WsExToolWindow = 0x00000080L;
		private const uint SwpNoSize = 0x0001;
		private const uint SwpNoActivate = 0x0010;
		private static readonly IntPtr HwndTopmost = new IntPtr(-1);
		private readonly PluginSettings _settings;
		private readonly bool _isTest;
		private readonly DispatcherTimer _closeTimer;
		private DispatcherTimer? _focusTimer;
		private DateTime? _focusedSince;
		private bool _focusTransitionStarted;

		public TurnWarningWindow(PluginSettings settings, NotificationContent content, bool isTest)
		{
			_settings = settings.Clone();
			_isTest = isTest;
			var compact = settings.Style == NotificationStyle.Compact;
			var hasResult = content.CombatResult != null;
			Width = compact ? (hasResult ? 340 : 300) : (hasResult ? 420 : 360);
			Height = compact ? (hasResult ? 132 : 86) : (hasResult ? 164 : 112);
			WindowStyle = WindowStyle.None;
			ResizeMode = ResizeMode.NoResize;
			ShowInTaskbar = false;
			ShowActivated = false;
			Topmost = true;
			AllowsTransparency = true;
			Background = Brushes.Transparent;
			Content = BuildContent(content, isTest, compact);

			_closeTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
			{
				Interval = TimeSpan.FromSeconds(settings.DisplaySeconds)
			};
			_closeTimer.Tick += CloseTimerOnTick;
			SourceInitialized += OnSourceInitialized;
			ContentRendered += OnContentRendered;
			Closed += OnClosed;
		}

		private UIElement BuildContent(NotificationContent content, bool isTest, bool compact)
		{
			var title = new TextBlock
			{
				Text = isTest ? "Test: " + content.Title : content.Title,
				FontSize = compact ? 18 : 22,
				FontWeight = FontWeights.SemiBold,
				Foreground = Brushes.White,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			var subtitle = new TextBlock
			{
				Text = content.Message,
				FontSize = compact ? 12 : 14,
				Margin = new Thickness(0, compact ? 3 : 7, 0, 0),
				Foreground = new SolidColorBrush(Color.FromRgb(220, 226, 235)),
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			var stack = new StackPanel { Margin = compact ? new Thickness(16, 11, 40, 10) : new Thickness(20, 16, 48, 16) };
			stack.Children.Add(title);
			stack.Children.Add(subtitle);
			if(content.CombatResult != null)
				stack.Children.Add(BuildCombatResult(content.CombatResult, compact));

			var close = new Button
			{
				Content = "×",
				Width = compact ? 30 : 34,
				Height = compact ? 30 : 34,
				FontSize = compact ? 18 : 22,
				Foreground = Brushes.White,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Focusable = false
			};
			close.Click += (_, _) => Close();

			var grid = new Grid();
			grid.Children.Add(stack);
			grid.Children.Add(close);

			return new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(246, 31, 38, 52)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(234, 171, 62)),
				BorderThickness = new Thickness(2),
				CornerRadius = new CornerRadius(10),
				Child = grid,
				Effect = new System.Windows.Media.Effects.DropShadowEffect
				{
					BlurRadius = 18,
					Opacity = 0.45,
					ShadowDepth = 3
				}
			};
		}

		private UIElement BuildCombatResult(CombatResultSummary result, bool compact)
		{
			var color = GetCombatResultColor(result.Outcome);
			var fontSize = compact ? 18 : 22;
			var margin = new Thickness(0, compact ? 7 : 10, 0, 0);
			var label = result.OutcomeLabel;
			var detail = result.Detail;

			switch(_settings.CombatResultStyle)
			{
				case CombatResultStyle.FullColoredLine:
					return ResultText($"{label.ToUpperInvariant()} · {detail}", color, fontSize, margin);

				case CombatResultStyle.ColoredMarker:
				{
					var row = ResultRow(fontSize);
					row.Children.Add(new Border
					{
						Width = 10,
						Height = 10,
						CornerRadius = new CornerRadius(5),
						Background = color,
						Margin = new Thickness(0, 0, 8, 0)
					});
					row.Children.Add(ResultText(label, color, fontSize));
					row.Children.Add(ResultText(" — " + detail, Brushes.White, fontSize));
					return new Border
					{
						BorderBrush = color,
						BorderThickness = new Thickness(4, 0, 0, 0),
						Padding = new Thickness(10, 0, 0, 0),
						Margin = margin,
						Child = row
					};
				}

				case CombatResultStyle.ResultPanel:
				{
					var row = ResultRow(fontSize);
					row.HorizontalAlignment = HorizontalAlignment.Stretch;
					row.Children.Add(ResultText(label, color, fontSize));
					row.Children.Add(ResultText("  " + detail, Brushes.White, fontSize));
					return new Border
					{
						Background = new SolidColorBrush(Color.FromArgb(42, color.Color.R, color.Color.G, color.Color.B)),
						BorderBrush = color,
						BorderThickness = new Thickness(0, 1, 0, 0),
						Padding = new Thickness(10, 7, 10, 7),
						Margin = margin,
						Child = row
					};
				}

				case CombatResultStyle.TwoColumn:
				{
					var grid = new Grid { Margin = margin };
					grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
					grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
					var outcome = ResultText(label.ToUpperInvariant(), color, fontSize);
					var damage = ResultText(detail.ToUpperInvariant(), color, fontSize);
					damage.HorizontalAlignment = HorizontalAlignment.Right;
					Grid.SetColumn(damage, 1);
					grid.Children.Add(outcome);
					grid.Children.Add(damage);
					return new Border
					{
						BorderBrush = color,
						BorderThickness = new Thickness(0, 1, 0, 0),
						Padding = new Thickness(0, 7, 0, 0),
						Child = grid
					};
				}

				default:
				{
					var row = ResultRow(fontSize);
					row.Margin = margin;
					row.Children.Add(new Border
					{
						Background = color,
						CornerRadius = new CornerRadius(9),
						Padding = new Thickness(8, 2, 8, 2),
						Margin = new Thickness(0, 0, 9, 0),
						Child = ResultText(label.ToUpperInvariant(), Brushes.Black, fontSize)
					});
					row.Children.Add(ResultText(detail, Brushes.White, fontSize));
					return row;
				}
			}
		}

		private static StackPanel ResultRow(double fontSize)
			=> new StackPanel
			{
				Orientation = Orientation.Horizontal,
				VerticalAlignment = VerticalAlignment.Center
			};

		private static TextBlock ResultText(string text, Brush color, double fontSize, Thickness? margin = null)
			=> new TextBlock
			{
				Text = text,
				FontSize = fontSize,
				FontWeight = FontWeights.SemiBold,
				Foreground = color,
				Margin = margin ?? new Thickness(0),
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis
			};

		private static SolidColorBrush GetCombatResultColor(CombatOutcome outcome)
			=> outcome switch
			{
				CombatOutcome.Win => new SolidColorBrush(Color.FromRgb(87, 214, 141)),
				CombatOutcome.Loss => new SolidColorBrush(Color.FromRgb(255, 107, 117)),
				_ => new SolidColorBrush(Color.FromRgb(255, 209, 102))
			};

		private void OnSourceInitialized(object? sender, EventArgs e)
		{
			var handle = new WindowInteropHelper(this).Handle;
			var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
			SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
			PositionWindow(handle);
		}

		private void OnContentRendered(object? sender, EventArgs e)
		{
			// Recheck after layout as a fallback. The initial position is already set
			// in SourceInitialized, before the window becomes visible.
			PositionWindow(new WindowInteropHelper(this).Handle);
			_closeTimer.Start();
			StartPulseEffect();
			_focusTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
			{
				Interval = TimeSpan.FromMilliseconds(200)
			};
			_focusTimer.Tick += FocusTimerOnTick;
			_focusTimer.Start();
		}

		private void PositionWindow(IntPtr handle)
		{
			if(GetWindowRect(handle, out var rect))
			{
				var area = ResolveScreen(_settings).WorkingArea;
				var width = rect.Right - rect.Left;
				var height = rect.Bottom - rect.Top;
				var point = ResolvePosition(area, width, height, _settings.Position);
				SetWindowPos(handle, HwndTopmost, point.X, point.Y, 0, 0, SwpNoSize | SwpNoActivate);
			}
		}

		private void FocusTimerOnTick(object? sender, EventArgs e)
		{
			var status = HearthstoneWindowState.GetStatus();
			if(status == HearthstoneWindowStatus.Unknown)
			{
				if(!_isTest)
					Close();
				else
					_focusedSince = null;
				return;
			}
			if(status != HearthstoneWindowStatus.Focused)
			{
				_focusedSince = null;
				return;
			}
			_focusedSince ??= DateTime.UtcNow;
			if(DateTime.UtcNow - _focusedSince.Value >= TimeSpan.FromSeconds(1))
			{
				FadeOutAndClose();
			}
		}

		private void StartPulseEffect()
		{
			if(_settings.PulseMode == NotificationPulseMode.None)
				return;
			var animation = new DoubleAnimation
			{
				From = 1.0,
				To = 0.35,
				Duration = TimeSpan.FromMilliseconds(_settings.PulseIntervalMs / 2.0),
				AutoReverse = true,
				RepeatBehavior = _settings.PulseMode == NotificationPulseMode.Brief
					? new RepeatBehavior(3)
					: RepeatBehavior.Forever
			};
			BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
		}

		private void StopPulseEffect()
		{
			BeginAnimation(OpacityProperty, null);
			Opacity = 1.0;
		}

		private void FadeOutAndClose()
		{
			if(_focusTransitionStarted)
				return;
			_focusTransitionStarted = true;
			_focusTimer?.Stop();
			_closeTimer.Stop();
			var currentOpacity = Opacity;
			BeginAnimation(OpacityProperty, null);
			Opacity = currentOpacity;
			var fade = new DoubleAnimation
			{
				From = currentOpacity,
				To = 0,
				Duration = TimeSpan.FromMilliseconds(450),
				FillBehavior = FillBehavior.HoldEnd
			};
			fade.Completed += (_, _) => Close();
			BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
		}

		private static Forms.Screen ResolveScreen(PluginSettings settings)
		{
			if(settings.MonitorMode == NotificationMonitorMode.Specific)
			{
				var selected = Forms.Screen.AllScreens.FirstOrDefault(x =>
					string.Equals(x.DeviceName, settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
				if(selected != null)
					return selected;
			}

			if(settings.MonitorMode == NotificationMonitorMode.Hearthstone)
			{
				var hearthstone = HearthstoneWindowState.GetMainWindowHandle();
				if(hearthstone != IntPtr.Zero)
					return Forms.Screen.FromHandle(hearthstone);
			}

			if(settings.MonitorMode == NotificationMonitorMode.ActiveWindow)
			{
				var foreground = GetForegroundWindow();
				if(foreground != IntPtr.Zero)
					return Forms.Screen.FromHandle(foreground);
			}

			return Forms.Screen.PrimaryScreen;
		}

		private static System.Drawing.Point ResolvePosition(System.Drawing.Rectangle area, int width, int height, NotificationPosition position)
		{
			const int padding = 18;
			return position switch
			{
				NotificationPosition.TopLeft => new System.Drawing.Point(area.Left + padding, area.Top + padding),
				NotificationPosition.TopRight => new System.Drawing.Point(area.Right - width - padding, area.Top + padding),
				NotificationPosition.BottomLeft => new System.Drawing.Point(area.Left + padding, area.Bottom - height - padding),
				NotificationPosition.Center => new System.Drawing.Point(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2),
				_ => new System.Drawing.Point(area.Right - width - padding, area.Bottom - height - padding)
			};
		}

		private void CloseTimerOnTick(object? sender, EventArgs e)
		{
			_closeTimer.Stop();
			StopPulseEffect();
			Close();
		}

		private void OnClosed(object? sender, EventArgs e)
		{
			_closeTimer.Stop();
			_closeTimer.Tick -= CloseTimerOnTick;
			if(_focusTimer != null)
			{
				_focusTimer.Stop();
				_focusTimer.Tick -= FocusTimerOnTick;
				_focusTimer = null;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
		private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

		[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
		private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int index);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
		private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

		[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
		private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int index, IntPtr value);

		private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index)
			=> IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : GetWindowLongPtr32(hWnd, index);

		private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value)
			=> IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, index, value) : SetWindowLongPtr32(hWnd, index, value);
	}
}

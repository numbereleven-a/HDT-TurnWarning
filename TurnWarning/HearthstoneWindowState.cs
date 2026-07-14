using System;
using System.Windows;
using Hearthstone_Deck_Tracker;

namespace TurnWarning
{
	internal enum HearthstoneWindowStatus
	{
		Unknown,
		Focused,
		Unfocused,
		Minimized
	}

	internal static class HearthstoneWindowState
	{
		public static IntPtr GetMainWindowHandle()
		{
			try
			{
				return User32.GetHearthstoneWindow();
			}
			catch
			{
			}
			return IntPtr.Zero;
		}

		public static HearthstoneWindowStatus GetStatus()
		{
			try
			{
				var handle = User32.GetHearthstoneWindow();
				if(handle == IntPtr.Zero)
					return HearthstoneWindowStatus.Unknown;
				if(User32.GetForegroundWindow() == handle)
					return HearthstoneWindowStatus.Focused;
				return User32.GetHearthstoneWindowState() == WindowState.Minimized
					? HearthstoneWindowStatus.Minimized
					: HearthstoneWindowStatus.Unfocused;
			}
			catch
			{
			}

			return HearthstoneWindowStatus.Unknown;
		}

		public static bool IsAway(HearthstoneWindowStatus status)
			=> status is HearthstoneWindowStatus.Unfocused or HearthstoneWindowStatus.Minimized;
	}
}

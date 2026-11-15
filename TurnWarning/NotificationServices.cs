using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TurnWarning
{
	internal static class NotificationServices
	{
		private static readonly PreparedSoundSlot CustomSound = new PreparedSoundSlot();
		private static readonly PreparedSoundSlot PreviewSound = new PreparedSoundSlot();
		private static int _flashActive;
		private const uint FlashwStop = 0x00000000;
		private const uint FlashwTray = 0x00000002;
		private const uint FlashwTimerNoForeground = 0x0000000C;

		public static void PlaySound(PluginSettings settings)
		{
			if(!settings.PlaySound)
				return;
			var requestedPath = (settings.SoundPath ?? string.Empty).Trim();
			if(requestedPath.Length == 0)
			{
				TryPlayFallbackSound();
				return;
			}
			if(CustomSound.TryPlay(requestedPath))
				return;
			TryPlayFallbackSound();
			PrepareSound(settings);
		}

		public static void PrepareSound(PluginSettings settings, bool forceReload = false)
			=> _ = PrepareSoundAsync(settings, forceReload);

		public static void PlayPreviewSound(PluginSettings settings)
		{
			if(!settings.PlaySound)
				return;
			var requestedPath = (settings.SoundPath ?? string.Empty).Trim();
			if(requestedPath.Length == 0)
			{
				TryPlayFallbackSound();
				return;
			}
			if(!PreviewSound.TryPlay(requestedPath))
				TryPlayFallbackSound();
		}

		public static Task<bool> PreparePreviewSoundAsync(PluginSettings settings)
			=> PreviewSound.PrepareAsync(settings, forceReload: false);

		public static Task<bool> PrepareSoundAsync(PluginSettings settings, bool forceReload = false)
			=> CustomSound.PrepareAsync(settings, forceReload);

		public static void ClearPreparedSound()
			=> CustomSound.Clear();

		public static void ClearPreviewSound()
			=> PreviewSound.Clear();

		private static void TryPlayFallbackSound()
		{
			try
			{
				SystemSounds.Exclamation.Play();
			}
			catch
			{
			}
		}

		public static void FlashHearthstone(TaskbarFlashMode mode)
		{
			if(mode == TaskbarFlashMode.None)
				return;
			var handle = HearthstoneWindowState.GetMainWindowHandle();
			if(handle == IntPtr.Zero)
				return;
			Volatile.Write(ref _flashActive, 1);
			var info = new FlashWindowInfo
			{
				Size = (uint)Marshal.SizeOf(typeof(FlashWindowInfo)),
				Window = handle,
				Flags = mode == TaskbarFlashMode.UntilFocused
					? FlashwTray | FlashwTimerNoForeground
					: FlashwTray,
				Count = mode == TaskbarFlashMode.Brief ? 3u : uint.MaxValue,
				Timeout = 0
			};
			FlashWindowEx(ref info);
		}

		public static void StopHearthstoneFlash()
		{
			if(Interlocked.Exchange(ref _flashActive, 0) == 0)
				return;
			var handle = HearthstoneWindowState.GetMainWindowHandle();
			if(handle == IntPtr.Zero)
				return;
			var info = new FlashWindowInfo
			{
				Size = (uint)Marshal.SizeOf(typeof(FlashWindowInfo)),
				Window = handle,
				Flags = FlashwStop,
				Count = 0,
				Timeout = 0
			};
			FlashWindowEx(ref info);
		}

		private sealed class PreparedSoundSlot
		{
			private readonly object _gate = new object();
			private SoundPlayer? _player;
			private string _requestedPath = string.Empty;
			private bool _ready;
			private bool _loading;
			private Task<bool> _loadingTask = Task.FromResult(false);
			private int _generation;

			public Task<bool> PrepareAsync(PluginSettings settings, bool forceReload)
			{
				var requestedPath = settings.PlaySound
					? (settings.SoundPath ?? string.Empty).Trim()
					: string.Empty;
				if(requestedPath.Length == 0)
				{
					Clear();
					return Task.FromResult(false);
				}

				int generation;
				SoundPlayer? previous;
				TaskCompletionSource<bool> completion;
				lock(_gate)
				{
					var sameRequest = string.Equals(
						_requestedPath,
						requestedPath,
						StringComparison.OrdinalIgnoreCase);
					if(!forceReload && sameRequest && _ready)
						return Task.FromResult(true);
					if(!forceReload && sameRequest && _loading)
						return _loadingTask;

					generation = ++_generation;
					previous = _player;
					_player = null;
					_requestedPath = requestedPath;
					_ready = false;
					_loading = true;
					completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
					_loadingTask = completion.Task;
				}
				Task.Run(() => completion.TrySetResult(PrepareCore(requestedPath, generation, previous)));
				return completion.Task;
			}

			public void Clear()
			{
				SoundPlayer? previous;
				lock(_gate)
				{
					_generation++;
					previous = _player;
					_player = null;
					_requestedPath = string.Empty;
					_ready = false;
					_loading = false;
					_loadingTask = Task.FromResult(false);
				}
				if(previous != null)
					Task.Run(previous.Dispose);
			}

			public bool TryPlay(string requestedPath)
			{
				lock(_gate)
				{
					if(!_ready || _player == null
						|| !string.Equals(_requestedPath, requestedPath, StringComparison.OrdinalIgnoreCase))
						return false;
					try
					{
						_player.Play();
						return true;
					}
					catch
					{
						return false;
					}
				}
			}

			private bool PrepareCore(string requestedPath, int generation, SoundPlayer? previous)
			{
				SoundPlayer? candidate = null;
				try
				{
					previous?.Dispose();
					if(!WavFileValidator.TryValidate(requestedPath, out _))
					{
						MarkFailed(generation);
						return false;
					}
					candidate = new SoundPlayer(new FileInfo(requestedPath).FullName);
					candidate.Load();
					lock(_gate)
					{
						if(generation != _generation)
							return false;
						_player = candidate;
						candidate = null;
						_ready = true;
						_loading = false;
					}
					return true;
				}
				catch
				{
					MarkFailed(generation);
					return false;
				}
				finally
				{
					candidate?.Dispose();
				}
			}

			private void MarkFailed(int generation)
			{
				SoundPlayer? previous = null;
				lock(_gate)
				{
					if(generation != _generation)
						return;
					previous = _player;
					_player = null;
					_ready = false;
					_loading = false;
					_loadingTask = Task.FromResult(false);
				}
				previous?.Dispose();
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct FlashWindowInfo
		{
			public uint Size;
			public IntPtr Window;
			public uint Flags;
			public uint Count;
			public uint Timeout;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool FlashWindowEx(ref FlashWindowInfo info);
	}
}

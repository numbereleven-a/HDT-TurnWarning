using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TurnWarning
{
	internal static class NotificationServices
	{
		private static readonly object SoundGate = new object();
		private static SoundPlayer? _customPlayer;
		private static string _customRequestedPath = string.Empty;
		private static bool _customReady;
		private static bool _customLoading;
		private static Task<bool> _customLoadingTask = Task.FromResult(false);
		private static int _soundGeneration;
		private static readonly object PreviewSoundGate = new object();
		private static SoundPlayer? _previewPlayer;
		private static string _previewRequestedPath = string.Empty;
		private static bool _previewReady;
		private static bool _previewLoading;
		private static Task<bool> _previewLoadingTask = Task.FromResult(false);
		private static int _previewSoundGeneration;
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
			if(TryPlayPreparedSound(requestedPath))
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
			if(!TryPlayPreparedPreviewSound(requestedPath))
				TryPlayFallbackSound();
		}

		public static Task<bool> PreparePreviewSoundAsync(PluginSettings settings)
		{
			var requestedPath = settings.PlaySound
				? (settings.SoundPath ?? string.Empty).Trim()
				: string.Empty;
			if(requestedPath.Length == 0)
			{
				ClearPreviewSound();
				return Task.FromResult(false);
			}

			int generation;
			SoundPlayer? previous;
			TaskCompletionSource<bool> completion;
			lock(PreviewSoundGate)
			{
				var sameRequest = string.Equals(
					_previewRequestedPath,
					requestedPath,
					StringComparison.OrdinalIgnoreCase);
				if(sameRequest && _previewReady)
					return Task.FromResult(true);
				if(sameRequest && _previewLoading)
					return _previewLoadingTask;

				generation = ++_previewSoundGeneration;
				previous = _previewPlayer;
				_previewPlayer = null;
				_previewRequestedPath = requestedPath;
				_previewReady = false;
				_previewLoading = true;
				completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				_previewLoadingTask = completion.Task;
			}
			Task.Run(() => completion.TrySetResult(PreparePreviewSoundCore(requestedPath, generation, previous)));
			return completion.Task;
		}

		public static Task<bool> PrepareSoundAsync(PluginSettings settings, bool forceReload = false)
		{
			var requestedPath = settings.PlaySound
				? (settings.SoundPath ?? string.Empty).Trim()
				: string.Empty;
			if(requestedPath.Length == 0)
			{
				ClearPreparedSound();
				return Task.FromResult(false);
			}

			int generation;
			SoundPlayer? previous;
			TaskCompletionSource<bool> completion;
			lock(SoundGate)
			{
				var sameRequest = string.Equals(
					_customRequestedPath,
					requestedPath,
					StringComparison.OrdinalIgnoreCase);
				if(!forceReload && sameRequest && _customReady)
					return Task.FromResult(true);
				if(!forceReload && sameRequest && _customLoading)
					return _customLoadingTask;

				generation = ++_soundGeneration;
				previous = _customPlayer;
				_customPlayer = null;
				_customRequestedPath = requestedPath;
				_customReady = false;
				_customLoading = true;
				completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				_customLoadingTask = completion.Task;
			}
			Task.Run(() => completion.TrySetResult(PrepareSoundCore(requestedPath, generation, previous)));
			return completion.Task;
		}

		private static bool PrepareSoundCore(string requestedPath, int generation, SoundPlayer? previous)
		{
			SoundPlayer? candidate = null;
			try
			{
				previous?.Dispose();
				if(!WavFileValidator.TryValidate(requestedPath, out _))
				{
					MarkSoundFailed(generation);
					return false;
				}
				var file = new FileInfo(requestedPath);
				candidate = new SoundPlayer(file.FullName);
				candidate.Load();
				lock(SoundGate)
				{
					if(generation != _soundGeneration)
						return false;
					_customPlayer = candidate;
					candidate = null;
					_customReady = true;
					_customLoading = false;
				}
				return true;
			}
			catch
			{
				MarkSoundFailed(generation);
				return false;
			}
			finally
			{
				candidate?.Dispose();
			}
		}

		private static void MarkSoundFailed(int generation)
		{
			SoundPlayer? previous = null;
			lock(SoundGate)
			{
				if(generation != _soundGeneration)
					return;
				previous = _customPlayer;
				_customPlayer = null;
				_customReady = false;
				_customLoading = false;
				_customLoadingTask = Task.FromResult(false);
			}
			previous?.Dispose();
		}

		public static void ClearPreparedSound()
		{
			SoundPlayer? previous;
			lock(SoundGate)
			{
				_soundGeneration++;
				previous = _customPlayer;
				_customPlayer = null;
				_customRequestedPath = string.Empty;
				_customReady = false;
				_customLoading = false;
				_customLoadingTask = Task.FromResult(false);
			}
			if(previous != null)
				Task.Run(previous.Dispose);
		}

		public static void ClearPreviewSound()
		{
			SoundPlayer? previous;
			lock(PreviewSoundGate)
			{
				_previewSoundGeneration++;
				previous = _previewPlayer;
				_previewPlayer = null;
				_previewRequestedPath = string.Empty;
				_previewReady = false;
				_previewLoading = false;
				_previewLoadingTask = Task.FromResult(false);
			}
			if(previous != null)
				Task.Run(previous.Dispose);
		}

		private static bool PreparePreviewSoundCore(string requestedPath, int generation, SoundPlayer? previous)
		{
			SoundPlayer? candidate = null;
			try
			{
				previous?.Dispose();
				if(!WavFileValidator.TryValidate(requestedPath, out _))
				{
					MarkPreviewSoundFailed(generation);
					return false;
				}
				candidate = new SoundPlayer(new FileInfo(requestedPath).FullName);
				candidate.Load();
				lock(PreviewSoundGate)
				{
					if(generation != _previewSoundGeneration)
						return false;
					_previewPlayer = candidate;
					candidate = null;
					_previewReady = true;
					_previewLoading = false;
				}
				return true;
			}
			catch
			{
				MarkPreviewSoundFailed(generation);
				return false;
			}
			finally
			{
				candidate?.Dispose();
			}
		}

		private static void MarkPreviewSoundFailed(int generation)
		{
			SoundPlayer? previous = null;
			lock(PreviewSoundGate)
			{
				if(generation != _previewSoundGeneration)
					return;
				previous = _previewPlayer;
				_previewPlayer = null;
				_previewReady = false;
				_previewLoading = false;
				_previewLoadingTask = Task.FromResult(false);
			}
			previous?.Dispose();
		}

		private static bool TryPlayPreparedPreviewSound(string requestedPath)
		{
			SoundPlayer? player;
			lock(PreviewSoundGate)
			{
				if(!_previewReady || _previewPlayer == null
					|| !string.Equals(_previewRequestedPath, requestedPath, StringComparison.OrdinalIgnoreCase))
					return false;
				player = _previewPlayer;
			}
			try
			{
				player.Play();
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static bool TryPlayPreparedSound(string requestedPath)
		{
			SoundPlayer? player;
			lock(SoundGate)
			{
				if(!_customReady || _customPlayer == null
					|| !string.Equals(_customRequestedPath, requestedPath, StringComparison.OrdinalIgnoreCase))
					return false;
				player = _customPlayer;
			}
			try
			{
				player.Play();
				return true;
			}
			catch
			{
				return false;
			}
		}

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

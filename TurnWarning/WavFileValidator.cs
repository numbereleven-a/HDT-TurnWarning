using System;
using System.IO;
using System.Security;
using System.Text;

namespace TurnWarning
{
	internal static class WavFileValidator
	{
		public const long MaxFileBytes = 5L * 1024 * 1024;
		public const double MaxDurationSeconds = 10;

		public static bool TryValidate(string path, out string error)
		{
			error = string.Empty;
			try
			{
				var file = new FileInfo(path);
				if(!file.Exists)
					return Fail("The selected WAV file does not exist.", out error);
				if(file.Length <= 44)
					return Fail("The selected WAV file is empty or incomplete.", out error);
				if(file.Length > MaxFileBytes)
					return Fail("The WAV file must not exceed 5 MB.", out error);

				using(var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
				using(var reader = new BinaryReader(stream, Encoding.ASCII, false))
				{
					if(ReadFourCc(reader) != "RIFF")
						return Fail("The selected file is not a RIFF WAV file.", out error);
					reader.ReadUInt32();
					if(ReadFourCc(reader) != "WAVE")
						return Fail("The selected file is not a WAV file.", out error);

					ushort format = 0;
					ushort channels = 0;
					uint sampleRate = 0;
					uint byteRate = 0;
					ushort bitsPerSample = 0;
					long dataBytes = -1;

					while(stream.Position + 8 <= stream.Length)
					{
						var chunkId = ReadFourCc(reader);
						var chunkSize = reader.ReadUInt32();
						var chunkStart = stream.Position;
						if(chunkSize > stream.Length - chunkStart)
							return Fail("The WAV file contains a truncated chunk.", out error);
						var chunkEnd = chunkStart + chunkSize;

						if(chunkId == "fmt " && chunkSize >= 16 && chunkEnd - chunkStart >= 16)
						{
							format = reader.ReadUInt16();
							channels = reader.ReadUInt16();
							sampleRate = reader.ReadUInt32();
							byteRate = reader.ReadUInt32();
							reader.ReadUInt16();
							bitsPerSample = reader.ReadUInt16();
							if(format == 0xFFFE && chunkSize >= 40 && chunkEnd - stream.Position >= 24)
							{
								reader.ReadUInt16();
								reader.ReadUInt16();
								reader.ReadUInt32();
								format = reader.ReadUInt16();
							}
						}
						else if(chunkId == "data")
							dataBytes = chunkSize;

						stream.Position = chunkEnd;
						if((chunkSize & 1) != 0 && stream.Position < stream.Length)
							stream.Position++;
					}

					if(format != 1)
						return Fail("Use an uncompressed PCM WAV file.", out error);
					if(channels < 1 || channels > 2)
						return Fail("The WAV file must use mono or stereo audio.", out error);
					if(sampleRate < 8000 || sampleRate > 96000)
						return Fail("The WAV sample rate must be between 8 and 96 kHz.", out error);
					if(bitsPerSample != 8 && bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32)
						return Fail("The WAV file must use 8, 16, 24, or 32-bit PCM audio.", out error);
					if(byteRate == 0 || dataBytes < 0)
						return Fail("The WAV file does not contain valid audio data.", out error);
					if(dataBytes / (double)byteRate > MaxDurationSeconds)
						return Fail("The WAV sound must not be longer than 10 seconds.", out error);
				}
				return true;
			}
			catch(EndOfStreamException)
			{
				return Fail("The WAV file is incomplete.", out error);
			}
			catch(IOException)
			{
				return Fail("The WAV file could not be read.", out error);
			}
			catch(UnauthorizedAccessException)
			{
				return Fail("The WAV file could not be accessed.", out error);
			}
			catch(Exception ex) when(ex is ArgumentException || ex is NotSupportedException || ex is SecurityException)
			{
				return Fail("The WAV file could not be read.", out error);
			}
		}

		private static string ReadFourCc(BinaryReader reader)
			=> Encoding.ASCII.GetString(reader.ReadBytes(4));

		private static bool Fail(string message, out string error)
		{
			error = message;
			return false;
		}
	}
}

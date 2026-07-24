using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TurnWarning
{
	internal sealed class VersionCheckResult
	{
		public VersionCheckResult(bool updateAvailable, string latestDisplayVersion)
		{
			UpdateAvailable = updateAvailable;
			LatestDisplayVersion = latestDisplayVersion;
		}

		public bool UpdateAvailable { get; }
		public string LatestDisplayVersion { get; }
	}

	internal static class VersionChecker
	{
		private static readonly HttpClient Client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(15)
		};

		public static async Task<VersionCheckResult> CheckAsync(
			string repository,
			string? token,
			Version installedVersion,
			CancellationToken cancellationToken)
		{
			if(!IsValidRepository(repository))
				throw new ArgumentException("Repository must use the owner/repository format.", nameof(repository));
			var validatedRepository = repository!;

			using(var request = new HttpRequestMessage(
				HttpMethod.Get,
				"https://api.github.com/repos/" + validatedRepository + "/releases/latest"))
			{
				request.Headers.TryAddWithoutValidation("User-Agent", "HDT-TurnWarning");
				request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
				request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
				if(!string.IsNullOrWhiteSpace(token))
					request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token!.Trim());

				using(var response = await Client.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken).ConfigureAwait(false))
				{
					response.EnsureSuccessStatusCode();
					using(var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
					{
						cancellationToken.ThrowIfCancellationRequested();
						var serializer = new DataContractJsonSerializer(typeof(LatestReleaseResponse));
						var release = serializer.ReadObject(stream) as LatestReleaseResponse;
						if(release == null || !TryParseReleaseVersion(release.TagName, out var latest, out var display))
							throw new InvalidDataException("The latest release tag is not a supported version.");
						return new VersionCheckResult(Compare(latest, installedVersion) > 0, display);
					}
				}
			}
		}

		internal static bool IsValidRepository(string? repository)
		{
			if(string.IsNullOrWhiteSpace(repository))
				return false;
			var parts = repository!.Split('/');
			return parts.Length == 2
				&& IsRepositoryPart(parts[0])
				&& IsRepositoryPart(parts[1]);
		}

		internal static bool TryParseReleaseVersion(string? tag, out Version version, out string display)
		{
			version = new Version(0, 0);
			display = string.Empty;
			if(string.IsNullOrWhiteSpace(tag))
				return false;
			var value = tag!.Trim();
			if(value.Length > 0 && (value[0] == 'v' || value[0] == 'V'))
				value = value.Substring(1);
			if(value.Length == 0)
				return false;
			display = value;
			var suffix = value.IndexOfAny(new[] { '-', '+' });
			var numeric = suffix >= 0 ? value.Substring(0, suffix) : value;
			if(!Version.TryParse(numeric, out var parsed) || parsed == null)
			{
				display = string.Empty;
				return false;
			}
			version = parsed;
			return true;
		}

		internal static int Compare(Version left, Version right)
		{
			var result = left.Major.CompareTo(right.Major);
			if(result != 0)
				return result;
			result = left.Minor.CompareTo(right.Minor);
			if(result != 0)
				return result;
			result = Component(left.Build).CompareTo(Component(right.Build));
			return result != 0 ? result : Component(left.Revision).CompareTo(Component(right.Revision));
		}

		internal static string ToDisplayVersion(Version version)
		{
			if(version.Revision > 0)
				return version.ToString(4);
			if(version.Build > 0)
				return version.ToString(3);
			return version.ToString(2);
		}

		private static int Component(int value) => value < 0 ? 0 : value;

		private static bool IsRepositoryPart(string value)
		{
			if(value.Length == 0 || value == "." || value == "..")
				return false;
			foreach(var character in value)
			{
				if(!char.IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.')
					return false;
			}
			return true;
		}

		[DataContract]
		private sealed class LatestReleaseResponse
		{
			[DataMember(Name = "tag_name")]
			public string? TagName { get; set; }
		}
	}
}

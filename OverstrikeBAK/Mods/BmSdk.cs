// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json.Linq;
using OverstrikeBAK.Game;
using OverstrikeBAK.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace OverstrikeBAK.Mods {
	/// <summary>
	/// BmSDK-AK (https://github.com/Team-BmSDK/BmSDK-AK), the C# modding framework script mods run on.
	/// Its release zip mirrors the game folder (Binaries\Win64\dinput8.dll + plugins\ + sdk\, BmGame\...),
	/// so installing it is extracting that zip into the game folder.
	/// </summary>
	internal static class BmSdk {
		public const string RepositoryUrl = "https://github.com/Team-BmSDK/BmSDK-AK";
		private const string LatestReleaseApi = "https://api.github.com/repos/Team-BmSDK/BmSDK-AK/releases/latest";
		private const string FallbackVersion = "v0.21.0";
		private const string FallbackZipUrl = "https://github.com/Team-BmSDK/BmSDK-AK/releases/download/v0.21.0/BmSDK-AK-v0.21.0.zip";

		public static string GetSdkDllPath(string gamePath) => Path.Combine(GameInstall.GetBinariesPath(gamePath), "sdk", "BmSDK.dll");
		public static string GetLogPath(string gamePath) => Path.Combine(GameInstall.GetBinariesPath(gamePath), "BmSDK.log");

		public static bool IsInstalled(string gamePath) {
			var bin = GameInstall.GetBinariesPath(gamePath);
			return File.Exists(GetSdkDllPath(gamePath))
				&& File.Exists(Path.Combine(bin, "dinput8.dll"))
				&& File.Exists(Path.Combine(bin, "plugins", "BmSDK.Host.asi"));
		}

		/// <summary>"0.21.0", or null if BmSDK isn't installed.</summary>
		public static string GetInstalledVersion(string gamePath) {
			try {
				if (!IsInstalled(gamePath)) return null;
				var version = FileVersionInfo.GetVersionInfo(GetSdkDllPath(gamePath)).FileVersion ?? "";
				return version.EndsWith(".0") && version.Count(c => c == '.') == 3 ? version[..^2] : version;
			} catch {
				return "unknown version";
			}
		}

		/// <summary>Downloads the latest BmSDK-AK release zip (v0.21.0 if GitHub can't be asked) and extracts it into the game folder.</summary>
		public static async Task<string> Install(string gamePath, Action<string> onProgress) {
			using var http = new HttpClient();
			http.DefaultRequestHeaders.UserAgent.ParseAdd("Overstrike-BAK");
			http.Timeout = TimeSpan.FromMinutes(10);

			var (version, url) = (FallbackVersion, FallbackZipUrl);
			try {
				onProgress("Looking for the latest BmSDK release...");
				var release = JObject.Parse(await http.GetStringAsync(LatestReleaseApi));
				var asset = (release["assets"] as JArray)?
					.FirstOrDefault(a => ((string)a["name"] ?? "").StartsWith("BmSDK-AK", StringComparison.OrdinalIgnoreCase)
						&& ((string)a["name"]).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
				if (asset != null) {
					version = (string)release["tag_name"] ?? version;
					url = (string)asset["browser_download_url"];
				}
			} catch {}

			var zipPath = Path.Combine(Path.GetTempPath(), $"Overstrike-BAK_BmSDK-AK-{version}.zip");
			try {
				using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)) {
					response.EnsureSuccessStatusCode();
					var total = response.Content.Headers.ContentLength ?? 0;

					using var input = await response.Content.ReadAsStreamAsync();
					using var output = new FileStream(zipPath, FileMode.Create);
					var buffer = new byte[1 << 16];
					long done = 0;
					var lastReport = DateTime.MinValue;
					int read;
					while ((read = await input.ReadAsync(buffer)) > 0) {
						await output.WriteAsync(buffer.AsMemory(0, read));
						done += read;
						if ((DateTime.Now - lastReport).TotalMilliseconds > 200) {
							lastReport = DateTime.Now;
							onProgress(total > 0 ? $"Downloading BmSDK-AK {version}... {done * 100 / total}% ({done >> 20} / {total >> 20} MB)" : $"Downloading BmSDK-AK {version}... {done >> 20} MB");
						}
					}
				}

				onProgress($"Installing BmSDK-AK {version}...");
				var root = Path.GetFullPath(gamePath);
				using var zip = ZipFile.OpenRead(zipPath);
				foreach (var entry in zip.Entries) {
					if (string.IsNullOrEmpty(entry.Name)) continue; // directory
					if (!entry.FullName.Contains('/') && !entry.FullName.Contains('\\')) continue; // README.md, LICENSE.md

					var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', '\\')));
					if (!FileUtils.IsInside(target, root)) continue;

					Directory.CreateDirectory(Path.GetDirectoryName(target));
					if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
					entry.ExtractToFile(target, true);
				}
			} finally {
				try { File.Delete(zipPath); } catch {}
			}

			return version;
		}
	}
}

// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Game {
	/// <summary>Paths and facts about a Batman: Arkham Knight installation.</summary>
	internal static class GameInstall {
		public const string UserFriendlyName = "Batman: Arkham Knight";
		public const string SteamAppId = "208650";
		public const string ProcessName = "BatmanAK";

		/// <summary>The DLC folder most DLC mods use: it's known not to cause instabilities.</summary>
		public const string DefaultDlcFolder = "356474";

		public static string GetExecutablePath(string gamePath) => Path.Combine(gamePath, "Binaries", "Win64", "BatmanAK.exe");
		public static string GetBinariesPath(string gamePath) => Path.Combine(gamePath, "Binaries", "Win64");
		public static string GetDlcPath(string gamePath) => Path.Combine(gamePath, "DLC");
		public static string GetScriptModsPath(string gamePath) => Path.Combine(gamePath, "BmGame", "Mods");
		public static string GetConfigPath(string gamePath) => Path.Combine(gamePath, "BmGame", "Config");
		public static string GetCookedPath(string gamePath) => Path.Combine(gamePath, "BmGame", "CookedPCConsole");
		public static string GetLocalizationPath(string gamePath) => Path.Combine(gamePath, "BmGame", "Localization");

		public static bool IsGameInstallation(string gamePath) {
			try {
				return File.Exists(GetExecutablePath(gamePath)) && Directory.Exists(Path.Combine(gamePath, "BmGame"));
			} catch {
				return false;
			}
		}

		/// <summary>Accepts the game folder or a folder inside it (Binaries, Binaries\Win64, BmGame, DLC...).</summary>
		public static string FindGameRoot(string path) {
			try {
				var dir = new DirectoryInfo(path);
				for (var i = 0; i < 4 && dir != null; ++i) {
					if (IsGameInstallation(dir.FullName)) return dir.FullName;
					dir = dir.Parent;
				}
			} catch {}

			return null;
		}

		/// <summary>"Steam", "Epic Games", "GOG" or "unknown".</summary>
		public static string GetStore(string gamePath) {
			try {
				if (Directory.Exists(Path.Combine(gamePath, ".egstore"))) return "Epic Games";
				if (Directory.GetFiles(gamePath, "goggame-*.info").Length > 0) return "GOG";
				if (gamePath.Contains("steamapps", StringComparison.OrdinalIgnoreCase)) return "Steam";
				if (File.Exists(Path.Combine(GetBinariesPath(gamePath), "steam_api64.dll"))) return "Steam";
			} catch {}

			return "unknown";
		}

		public static bool IsSteamInstallation(string gamePath) => GetStore(gamePath) == "Steam";

		/// <summary>Numeric folders inside DLC\, sorted.</summary>
		public static List<string> GetDlcFolders(string gamePath) {
			try {
				return Directory.GetDirectories(GetDlcPath(gamePath))
					.Select(Path.GetFileName)
					.Where(name => name.All(char.IsDigit))
					.OrderBy(name => name, StringComparer.Ordinal)
					.ToList();
			} catch {
				return new List<string>();
			}
		}

		public static bool IsRunning() {
			try {
				return Process.GetProcessesByName(ProcessName).Length > 0;
			} catch {
				return false;
			}
		}

		public static void Launch(string gamePath) {
			if (IsSteamInstallation(gamePath)) {
				Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true });
				return;
			}

			Process.Start(new ProcessStartInfo() {
				FileName = GetExecutablePath(gamePath),
				WorkingDirectory = GetBinariesPath(gamePath),
				UseShellExecute = false
			});
		}
	}
}

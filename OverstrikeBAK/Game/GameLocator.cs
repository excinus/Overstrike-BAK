// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Microsoft.Win32;
using OverstrikeBAK.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace OverstrikeBAK.Game {
	/// <summary>Finds Batman: Arkham Knight installations: Steam libraries first, then the usual places on every drive.</summary>
	internal static class GameLocator {
		public class Found {
			public string Name;
			public string Path;
		}

		public static List<Found> FindInstallations() {
			var result = new List<Found>();

			void TryAdd(string name, string path) {
				try {
					if (!GameInstall.IsGameInstallation(path)) return;
					foreach (var f in result) {
						if (FileUtils.SamePath(f.Path, path)) return;
					}
					result.Add(new Found() { Name = name, Path = Path.GetFullPath(path) });
				} catch {}
			}

			foreach (var library in GetSteamLibraries()) {
				TryAdd("Batman Arkham Knight (Steam)", Path.Combine(library, "steamapps", "common", "Batman Arkham Knight"));
			}

			var usual = new (string, string)[] {
				("Batman Arkham Knight (Steam)", ":\\Program Files (x86)\\Steam\\steamapps\\common\\Batman Arkham Knight"),
				("Batman Arkham Knight (Steam)", ":\\Program Files\\Steam\\steamapps\\common\\Batman Arkham Knight"),
				("Batman Arkham Knight (Steam)", ":\\SteamLibrary\\steamapps\\common\\Batman Arkham Knight"),
				("Batman Arkham Knight (Steam)", ":\\Steam\\steamapps\\common\\Batman Arkham Knight"),
				("Batman Arkham Knight (Epic)", ":\\Program Files\\Epic Games\\BatmanArkhamKnight"),
				("Batman Arkham Knight (Epic)", ":\\Program Files (x86)\\Epic Games\\BatmanArkhamKnight"),
				("Batman Arkham Knight (Epic)", ":\\Epic Games\\BatmanArkhamKnight"),
				("Batman Arkham Knight (GOG)", ":\\GOG Games\\Batman Arkham Knight"),
				("Batman Arkham Knight (GOG)", ":\\Program Files (x86)\\GOG Galaxy\\Games\\Batman Arkham Knight"),
			};

			foreach (var drive in DriveInfo.GetDrives()) {
				try {
					if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
				} catch {
					continue;
				}

				var letter = drive.Name.Substring(0, 1);
				foreach (var (name, path) in usual) {
					TryAdd(name, letter + path);
				}
			}

			return result;
		}

		private static List<string> GetSteamLibraries() {
			var libraries = new List<string>();
			var steamPaths = new List<string>();

			try {
				using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
				if (key?.GetValue("SteamPath") is string p) steamPaths.Add(p.Replace('/', '\\'));
			} catch {}

			try {
				using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
				if (key?.GetValue("InstallPath") is string p) steamPaths.Add(p);
			} catch {}

			foreach (var steam in steamPaths) {
				libraries.Add(steam);

				try {
					var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
					if (!File.Exists(vdf)) continue;

					foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) {
						libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
					}
				} catch {}
			}

			return libraries;
		}
	}
}

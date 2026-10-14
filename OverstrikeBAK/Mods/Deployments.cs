// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json.Linq;
using OverstrikeBAK.Data;
using OverstrikeBAK.Utils;
using System;
using System.Collections.Generic;
using System.IO;

namespace OverstrikeBAK.Mods {
	/// <summary>
	/// The state of one game folder: which library mods are installed in it and where (rebuilt from the library's
	/// overstrike-bak.json files on every scan), and which profile's suit order its files have (saved in
	/// Profiles\Deployments.json, per game folder, because profiles pointing at the same game share its files).
	/// </summary>
	public class Deployments {
		public string GamePath;

		/// <summary>Library mod path -> installed folder, relative to the game folder (forward slashes).</summary>
		public Dictionary<string, string> Mods = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Name of the profile whose suit order is written in the game files, or null if they have the original order.</summary>
		public string SuitOrderProfile = null;

		private static string StatePath => Path.Combine(App.BaseDirectory, "Profiles", "Deployments.json");

		private static string Key(string gamePath) => Path.GetFullPath(gamePath).TrimEnd('\\', '/').ToLowerInvariant();

		public static Deployments Load(string gamePath) {
			var result = new Deployments() { GamePath = gamePath };

			try {
				if (!File.Exists(StatePath)) return result;

				var json = JObject.Parse(File.ReadAllText(StatePath));
				if (json[Key(gamePath)] is JObject game) {
					result.SuitOrderProfile = (string)game["suit_order_profile"];
				}
			} catch {}

			return result;
		}

		public void Save() {
			JObject json;
			try {
				json = File.Exists(StatePath) ? JObject.Parse(File.ReadAllText(StatePath)) : new JObject();
			} catch {
				json = new JObject();
			}

			json[Key(GamePath)] = new JObject() {
				["path"] = GamePath,
				["suit_order_profile"] = SuitOrderProfile,
			};

			File.WriteAllText(StatePath, json.ToString());
		}

		/// <summary>Rebuilds the installed mods map from the library.</summary>
		public void SetMods(IEnumerable<ModEntry> mods) {
			Mods.Clear();
			foreach (var mod in mods) {
				if (mod.IsInstalledIn(GamePath)) Mods[mod.Path] = mod.InstalledPath;
			}
		}

		public string GetDeployedFullPath(string modPath) {
			return Mods.TryGetValue(modPath, out var rel) ? Path.Combine(GamePath, rel.Replace('/', '\\')) : null;
		}

		/// <summary>If 'fullPath' is inside an installed library mod's folder: that mod's library path and the path inside the mod.</summary>
		public (string modPath, string relativeInMod) FindOwner(string fullPath) {
			foreach (var (modPath, rel) in Mods) {
				var root = Path.Combine(GamePath, rel.Replace('/', '\\'));
				if (FileUtils.IsInside(fullPath, root)) {
					return (modPath, Path.GetRelativePath(root, fullPath));
				}
			}
			return (null, null);
		}
	}
}

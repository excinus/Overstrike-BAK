// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Data {
	/// <summary>
	/// "Mod Profiles\&lt;name&gt;.json": a saved set of ticked mods, by library path (the name is only for reading the file).
	/// Loading one sets the ticks; Apply then installs and removes mods to match.
	/// </summary>
	public static class ModProfiles {
		public static string Directory => Path.Combine(App.BaseDirectory, "Mod Profiles");

		public static List<string> List() {
			try {
				System.IO.Directory.CreateDirectory(Directory);
				return System.IO.Directory.GetFiles(Directory, "*.json")
					.Select(Path.GetFileNameWithoutExtension)
					.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
					.ToList();
			} catch {
				return new List<string>();
			}
		}

		public static string GetPath(string name) => Path.Combine(Directory, name + ".json");

		public static bool Exists(string name) => File.Exists(GetPath(name));

		/// <summary>Library paths of the mods ticked in this profile.</summary>
		public static HashSet<string> Load(string name) {
			var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var json = JObject.Parse(File.ReadAllText(GetPath(name)));
			foreach (var mod in (json["mods"] as JArray) ?? new JArray()) {
				var path = mod is JObject o ? (string)o["path"] : (string)mod;
				if (!string.IsNullOrEmpty(path)) result.Add(path);
			}
			return result;
		}

		public static void Save(string name, IEnumerable<ModEntry> ticked) {
			var mods = new JArray();
			foreach (var mod in ticked) {
				mods.Add(new JObject() { ["path"] = mod.Path, ["name"] = mod.Name });
			}

			System.IO.Directory.CreateDirectory(Directory);
			File.WriteAllText(GetPath(name), new JObject() { ["mods"] = mods }.ToString());
		}

		public static void Delete(string name) {
			File.Delete(GetPath(name));
		}
	}
}

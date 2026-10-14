// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json.Linq;
using OverstrikeBAK.Game;
using OverstrikeBAK.Suits;
using System;
using System.Collections.Generic;
using System.IO;

namespace OverstrikeBAK.Data {
	public class ProfileMod {
		public string Path;
		public bool Install;
	}

	public class Profile {
		// meta
		public string FullPath;
		public string Name;

		// game
		public string GamePath;

		// mods (library-relative paths), in list order
		public List<ProfileMod> Mods = new();

		// settings
		public string Settings_DlcFolder = GameInstall.DefaultDlcFolder;

		// the suits order this profile wants in game; null = the game's original order
		public SuitOrder SuitOrder = null;

		public static string ProfilesDirectory => System.IO.Path.Combine(App.BaseDirectory, "Profiles");

		public Profile(string filename) {
			FullPath = filename;
			Name = System.IO.Path.GetFileNameWithoutExtension(filename);

			var json = JObject.Parse(File.ReadAllText(FullPath));
			GamePath = (string)json["path"];
			if (GamePath == null) throw new Exception("bad profile");

			if (json["mods"] is JArray mods) {
				foreach (var mod in mods) {
					var path = (string)mod[0];
					var install = (bool?)mod[1];
					if (path == null || install == null) continue;
					Mods.Add(new ProfileMod() { Path = path, Install = (bool)install });
				}
			}

			if (json["settings"] is JObject settings) {
				Settings_DlcFolder = (string)settings["dlc_folder"] ?? Settings_DlcFolder;
			}

			if (json["suit_order"] is JObject order) {
				SuitOrder = SuitOrder.FromJson(order);
			}
		}

		public Profile(string name, string gamePath) {
			Name = name;
			FullPath = System.IO.Path.Combine(ProfilesDirectory, Name + ".json");
			GamePath = gamePath;
		}

		public bool Save() {
			try {
				var mods = new JArray();
				foreach (var mod in Mods) {
					mods.Add(new JArray { mod.Path, mod.Install });
				}

				var j = new JObject {
					["game"] = "BAK",
					["path"] = GamePath,
					["mods"] = mods,
					["settings"] = new JObject() {
						["dlc_folder"] = Settings_DlcFolder,
					},
					["suit_order"] = SuitOrder?.ToJson(),
				};

				File.WriteAllText(FullPath, j.ToString());
				return true;
			} catch {}

			return false;
		}

		public static bool IsValidName(string name) {
			return System.Text.RegularExpressions.Regex.IsMatch(name ?? "", "^[A-Za-z0-9 _()-]+$");
		}
	}
}

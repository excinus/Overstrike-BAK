// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json.Linq;
using OverstrikeBAK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Mods {
	/// <summary>What Overstrike-BAK knows about one mod of a library entry.</summary>
	public class ManifestMod {
		public string Path = "";        // inside the entry folder ("" = the entry folder itself)
		public ModType Type;
		public string Name;
		public string Folder;           // folder name used in the game
		public string Description;
		public string ShowcaseName;     // DLC mods: the Suits Menu name of the one item it adds ("" = none or several; null = not read yet)

		public string InstalledGame;    // game folder it's installed in, or null if it's in the library
		public string InstalledPath;    // relative to that game folder

		public NexusInfo Nexus;

		public bool IsInstalled => InstalledGame != null && InstalledPath != null;
	}

	/// <summary>
	/// "Mods Library\&lt;entry&gt;\overstrike-bak.json". Installed mods are moved out of the library into the game, so this
	/// file is what remembers them (and where to move them back from) while the library folder is empty.
	/// </summary>
	public class LibraryManifest {
		public const string FileName = "overstrike-bak.json";

		public List<ManifestMod> Mods = new();
		public DateTime? Added;         // when the folder was added to the library (UTC)

		public ManifestMod Find(string path) {
			return Mods.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
		}

		public static LibraryManifest Load(string entryFolder) {
			var result = new LibraryManifest();
			var file = System.IO.Path.Combine(entryFolder, FileName);
			if (!File.Exists(file)) return result;

			try {
				var json = JObject.Parse(File.ReadAllText(file));
				result.Added = (DateTime?)json["added"];
				foreach (var m in (json["mods"] as JArray) ?? new JArray()) {
					if (m is not JObject o) continue;

					var mod = new ManifestMod() {
						Path = (string)o["path"] ?? "",
						Type = Enum.TryParse<ModType>((string)o["type"], out var type) ? type : ModType.DLC,
						Name = (string)o["name"],
						Folder = (string)o["folder"],
						Description = (string)o["description"],
						ShowcaseName = (string)o["showcase_name"],
					};

					if (o["installed"] is JObject installed) {
						mod.InstalledGame = (string)installed["game"];
						mod.InstalledPath = (string)installed["path"];
					}

					if (o["nexus"] is JObject nexus) {
						mod.Nexus = NexusInfo.FromJson(nexus);
					}

					result.Mods.Add(mod);
				}
			} catch {}

			return result;
		}

		public void Save(string entryFolder) {
			var mods = new JArray();
			foreach (var m in Mods) {
				var o = new JObject() {
					["path"] = m.Path,
					["type"] = m.Type.ToString(),
					["name"] = m.Name,
					["folder"] = m.Folder,
					["description"] = m.Description,
					["showcase_name"] = m.ShowcaseName,
				};
				if (m.IsInstalled) {
					o["installed"] = new JObject() { ["game"] = m.InstalledGame, ["path"] = m.InstalledPath };
				}
				if (m.Nexus != null) {
					o["nexus"] = m.Nexus.ToJson();
				}
				mods.Add(o);
			}

			Directory.CreateDirectory(entryFolder);
			var j = new JObject() { ["mods"] = mods };
			if (Added != null) j["added"] = Added.Value.ToUniversalTime();
			File.WriteAllText(System.IO.Path.Combine(entryFolder, FileName), j.ToString());
		}
	}
}

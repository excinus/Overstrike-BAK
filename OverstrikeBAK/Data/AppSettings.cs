// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json.Linq;
using System.IO;

namespace OverstrikeBAK.Data {
	public class AppSettings {
		public string CurrentProfile = null;
		public string Theme = Utils.Theme.Dark;
		public string ModsSort = ModsSortName; // how the Mods list is sorted
		public bool ModsSortReversed = false;  // the arrow down: Z-A, oldest first, unchecked first, SDK mods first

		public const string ModsSortName = "name";
		public const string ModsSortAdded = "added";
		public const string ModsSortChecked = "checked";
		public const string ModsSortType = "type";

		public AppSettings() {}

		public AppSettings(string file) {
			var json = JObject.Parse(File.ReadAllText(file));
			CurrentProfile = (string)json["profile"];
			Theme = (string)json["theme"] ?? Theme;
			ModsSort = (string)json["mods_sort"] ?? ModsSort;
			ModsSortReversed = (bool?)json["mods_sort_reversed"] ?? ModsSortReversed;
		}

		public void Save(string file) {
			var j = new JObject() {
				["profile"] = CurrentProfile,
				["theme"] = Theme,
				["mods_sort"] = ModsSort,
				["mods_sort_reversed"] = ModsSortReversed,
			};
			File.WriteAllText(file, j.ToString());
		}
	}
}

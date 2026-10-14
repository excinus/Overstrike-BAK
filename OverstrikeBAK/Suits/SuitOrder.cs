// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Suits {
	/// <summary>
	/// An order of Showcase items (by Item key) for each Showcase page ("Players", "Batmobiles").
	/// Saved in profiles (the order applied to the game) and in suit profiles (presets users save and load).
	/// </summary>
	public class SuitOrder {
		public Dictionary<string, List<string>> Pages = new(System.StringComparer.OrdinalIgnoreCase);

		public List<string> Get(string page) {
			return Pages.TryGetValue(page, out var list) ? list : null;
		}

		public SuitOrder Clone() {
			var result = new SuitOrder();
			foreach (var (page, items) in Pages) {
				result.Pages[page] = new List<string>(items);
			}
			return result;
		}

		public JObject ToJson() {
			var pages = new JObject();
			foreach (var (page, items) in Pages) {
				pages[page] = new JArray(items);
			}
			return new JObject() { ["pages"] = pages };
		}

		public static SuitOrder FromJson(JObject json) {
			var result = new SuitOrder();
			if (json["pages"] is JObject pages) {
				foreach (var (page, items) in pages) {
					if (items is JArray array) {
						result.Pages[page] = array.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList();
					}
				}
			}
			return result;
		}

		public static SuitOrder Load(string file) {
			return FromJson(JObject.Parse(File.ReadAllText(file)));
		}

		public void Save(string file) {
			File.WriteAllText(file, ToJson().ToString());
		}

		/// <summary>
		/// The full order to write for each page 'wanted' has, fitted (see Merge) to the items installed now.
		/// Pages 'wanted' doesn't mention are left out, so their files aren't touched.
		/// </summary>
		public static Dictionary<string, List<string>> Resolve(ShowcaseModel model, SuitOrder wanted) {
			var result = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
			if (wanted == null) return result;

			foreach (var page in ShowcaseModel.Pages) {
				var list = wanted.Get(page);
				if (list == null) continue;

				var suits = model.GetPage(page);
				var characters = new Dictionary<string, string>();
				foreach (var suit in suits) characters[suit.Item] = suit.Character;

				result[page] = Merge(suits.Select(s => s.Item).ToList(), list, item => characters.TryGetValue(item, out var c) ? c : "");
			}

			return result;
		}

		/// <summary>
		/// Fits a wanted order to the items that exist now: items no longer present are dropped, and
		/// items the wanted order doesn't know (e.g. a newly enabled mod) are placed after the last item of
		/// the same character (or after the item before them in the current order).
		/// </summary>
		public static List<string> Merge(IReadOnlyList<string> current, IReadOnlyList<string> wanted, System.Func<string, string> characterOf) {
			var present = new HashSet<string>(current);
			var result = new List<string>();
			var inResult = new HashSet<string>();
			foreach (var item in wanted) {
				if (present.Contains(item) && inResult.Add(item)) {
					result.Add(item);
				}
			}

			for (var i = 0; i < current.Count; ++i) {
				var item = current[i];
				if (inResult.Contains(item)) continue;

				var insertAt = -1;

				var character = characterOf(item);
				for (var j = result.Count - 1; j >= 0; --j) {
					if (characterOf(result[j]) == character) {
						insertAt = j + 1;
						break;
					}
				}

				if (insertAt == -1) {
					insertAt = 0;
					for (var j = i - 1; j >= 0; --j) {
						if (inResult.Contains(current[j])) {
							insertAt = result.IndexOf(current[j]) + 1;
							break;
						}
					}
				}

				result.Insert(insertAt, item);
				inResult.Add(item);
			}

			return result;
		}
	}
}

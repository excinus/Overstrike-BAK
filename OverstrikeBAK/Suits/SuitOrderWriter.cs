// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Game;
using OverstrikeBAK.Mods;
using OverstrikeBAK.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Suits {
	/// <summary>
	/// Writes a suit order into the game: the Showcase sorts by SortIndex only, so every item gets its own
	/// SortIndex (inside its page's range), written into every ShowcaseItems line for that item, in every config
	/// file that has one. Lines of the same item keep sharing one value, so duplicates (the "BatmanMk2" filler rows
	/// most skin mods carry) still sort next to each other and the Showcase still shows them once.
	/// Hidden items have all their lines commented out (see ShowcaseIni.HiddenTag), so the game doesn't list them.
	/// </summary>
	internal static class SuitOrderWriter {
		public class Result {
			public int FilesChanged;
			public List<string> Warnings = new();
		}

		/// <param name="hidden">The items to hide on each page; the others are shown. Null: leave hidden items as they are.</param>
		public static Result Apply(ShowcaseModel model, Dictionary<string, List<string>> order, Dictionary<string, HashSet<string>> hidden = null) {
			var result = new Result();
			var indexes = new Dictionary<(string, string), int>();

			foreach (var (page, items) in order) {
				var (start, end) = ShowcaseModel.PageRange(page);
				var capacity = end - start + 1;
				if (items.Count > capacity) {
					throw new Exception($"There are {items.Count} items on the {page} page, but the game only has room for {capacity} different positions there.");
				}

				var step = Math.Clamp(capacity / Math.Max(items.Count, 1), 1, 5);
				for (var i = 0; i < items.Count; ++i) {
					indexes[(page.ToLowerInvariant(), items[i])] = start + i * step;
				}
			}

			var files = model.Sources.Select(s => s.FullPath).ToList();
			var defaultGameIni = Path.Combine(GameInstall.GetConfigPath(model.GamePath), "DefaultGame.ini");
			if (File.Exists(defaultGameIni) && !files.Any(f => FileUtils.SamePath(f, defaultGameIni))) {
				files.Insert(0, defaultGameIni); // keep it in sync with BmGame.ini, in case the game regenerates BmGame.ini from it
			}

			var backups = new SuitBackups(model.GamePath);
			var warned = new HashSet<string>();

			foreach (var file in files) {
				TextFile text;
				try {
					text = TextFile.Load(file);
				} catch (Exception ex) {
					result.Warnings.Add($"Couldn't read '{file}': {ex.Message}");
					continue;
				}

				var newText = ShowcaseIni.Process(text.Text, (op, value, fields, line, isHidden) => {
					if (op == ConfigOp.Clear) return null;

					var newLine = line;
					if (indexes.TryGetValue((fields.Page.ToLowerInvariant(), fields.Item), out var index)) {
						if (fields.SortIndex == null) {
							if (warned.Add(fields.Item)) result.Warnings.Add($"'{fields.Item}' in '{file}' has no SortIndex, so it can't be moved.");
						} else if (fields.SortIndex != index) {
							newLine = ShowcaseIni.WithSortIndex(line, index);
						}
					}

					// only lines that add an item get hidden (removals stay as they are)
					var hide = isHidden;
					if (hidden != null && op != ConfigOp.Remove) {
						hide = hidden.TryGetValue(fields.Page, out var items) && items.Contains(fields.Item);
					}

					return hide ? ShowcaseIni.Hide(newLine) : newLine;
				});

				if (newText == null) continue;

				backups.EnsureBackup(file);

				File.SetAttributes(file, FileAttributes.Normal);
				text.Text = newText;
				text.Save(file);
				++result.FilesChanged;
			}

			return result;
		}

		/// <summary>Puts the original config files back from the backups.</summary>
		public static int RestoreOriginal(string gamePath) {
			return new SuitBackups(gamePath).RestoreAll().Count;
		}
	}
}

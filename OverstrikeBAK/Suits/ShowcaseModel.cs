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
using System.Text.RegularExpressions;

namespace OverstrikeBAK.Suits {
	public enum SourceKind { BaseGame, OfficialDlc, ManagedMod, OtherMod }

	/// <summary>A config file that contributes ShowcaseItems: the base game's BmGame.ini, or a BmGame.ini inside DLC\.</summary>
	public class ConfigSource {
		public string FullPath;      // the file in the game folder
		public string RelativePath;  // relative to the game folder
		public string ReadPath;      // the file actually read (an original copy when building the original order)
		public SourceKind Kind;
		public string Label;
		public string ModPath;       // library path of the managed mod it belongs to
	}

	/// <summary>One entry of a Showcase page, as the main menu Showcase shows it.</summary>
	public class ShowcaseSuit {
		public string Page;
		public string Item;
		public string Skin;
		public string Character;      // the game's skin id: "batman", "robin", "dlcharleyquinn", "batmobile"...
		public string CharacterName;
		public string Name;
		public string Description;
		public int SortIndex;
		public bool Unlocked;
		public bool DLC;
		public bool WBPlay;
		public string Condition;
		public string SourceLabel;    // where the shown entry comes from
		public List<string> Sources = new();
		public bool IsOfficial;       // defined by the base game or an official DLC (mods may repeat its line)
		public List<string> ModPaths = new(); // library paths of the managed mods whose configs define it
		public List<string> NameFiles = new(); // the mods' GFxUI files (inside DLC\, not official DLC) that name it
		public int Appearances = 1;   // >1: the same item shows up several times in game (same Item, different SortIndex)
		public bool Tied;             // shares its SortIndex with another item: their order depends on the order mod folders are loaded
		public bool Hidden;           // all its lines are commented out by "Hide": the game doesn't show it
		public string ThumbnailPackage;
	}

	/// <summary>
	/// The Showcase as the game builds it: RGameInfo.ShowcaseItems from the base config plus every DLC config,
	/// sorted like RGameInfo._ShowcaseItemsSort (SortIndex ascending, unlocked first on ties; the sort itself
	/// isn't stable, so ties come out in no particular order) and listed like RGFxMovieUI_BatCave.XI_BatCaveEnumerate
	/// (an entry is skipped when its Item equals the previous entry's).
	/// </summary>
	public class ShowcaseModel {
		public const string PagePlayers = "Players";
		public const string PageBatmobiles = "Batmobiles";
		public static readonly string[] Pages = { PagePlayers, PageBatmobiles };

		public string GamePath;
		public List<ConfigSource> Sources = new();
		public Dictionary<string, List<ShowcaseSuit>> Suits = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>The SortIndex range of a page. Pages must not overlap: the game expects each page's items to be contiguous after sorting.</summary>
		public static (int start, int end) PageRange(string page) {
			return page.Equals(PageBatmobiles, StringComparison.OrdinalIgnoreCase) ? (0, 99) : (100, 999);
		}

		public List<ShowcaseSuit> GetPage(string page) {
			return Suits.TryGetValue(page, out var list) ? list : new List<ShowcaseSuit>();
		}

		#region building

		/// <param name="originals">Read the original files (backups, and the library copies of managed mods) instead of the current ones.</param>
		public static ShowcaseModel Build(string gamePath, Deployments deployments, Func<string, string> modName, bool originals) {
			var model = new ShowcaseModel() { GamePath = gamePath };
			model.CollectSources(deployments, modName, originals);

			var allLines = new List<ShowcaseLine>();
			var effective = new List<ShowcaseLine>();
			var order = 0;

			foreach (var source in model.Sources) {
				string text;
				try {
					text = TextFile.ReadAllText(source.ReadPath);
				} catch {
					continue;
				}

				foreach (var (op, value, fields, hidden) in ShowcaseIni.Read(text)) {
					var line = new ShowcaseLine() { Source = source, Order = order++, Op = op, Hidden = hidden, RawValue = value, Fields = fields };
					allLines.Add(line);
					if (hidden) continue; // commented out: the game doesn't read it

					switch (op) {
						case ConfigOp.Add:
						case ConfigOp.Set:
							effective.Add(line);
							break;

						case ConfigOp.AddUnique:
							if (!effective.Any(e => SameValue(e.RawValue, value))) effective.Add(line);
							break;

						case ConfigOp.Remove:
							var index = effective.FindIndex(e => SameValue(e.RawValue, value));
							if (index != -1) effective.RemoveAt(index);
							break;

						case ConfigOp.Clear:
							effective.Clear();
							break;
					}
				}
			}

			var nameFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			var strings = Localization.LoadShowcaseStrings(gamePath, nameFiles);
			var thumbnails = FindThumbnailPackages(gamePath);

			foreach (var page in Pages) {
				model.Suits[page] = MakePage(page, effective, allLines, strings, thumbnails);
				foreach (var suit in model.Suits[page]) {
					if (nameFiles.TryGetValue(suit.Item, out var files)) suit.NameFiles = files.Where(f => !IsOfficialDlcFile(gamePath, f)).ToList();
				}
			}

			return model;
		}

		private static bool SameValue(string a, string b) {
			return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
		}

		private void CollectSources(Deployments deployments, Func<string, string> modName, bool originals) {
			var backups = new SuitBackups(GamePath);

			string ReadPathFor(string fullPath, string modPath, string relativeInMod) {
				if (!originals) return fullPath;

				var backup = backups.GetBackupPath(fullPath);
				if (File.Exists(backup)) return backup;

				if (modPath != null) {
					var source = Path.Combine(ModsLibrary.LibraryPath, modPath.Replace('/', '\\'), relativeInMod);
					if (File.Exists(source)) return source;
				}

				return fullPath;
			}

			// base game: BmGame.ini is what the game reads (DefaultGame.ini is its source)
			var configDir = GameInstall.GetConfigPath(GamePath);
			var baseIni = Path.Combine(configDir, "BmGame.ini");
			if (!File.Exists(baseIni)) baseIni = Path.Combine(configDir, "DefaultGame.ini");
			if (File.Exists(baseIni)) {
				Sources.Add(new ConfigSource() {
					FullPath = baseIni,
					RelativePath = Path.GetRelativePath(GamePath, baseIni),
					ReadPath = ReadPathFor(baseIni, null, null),
					Kind = SourceKind.BaseGame,
					Label = "Base game",
				});
			}

			// DLC: every BmGame.ini anywhere inside DLC\<folder>\, folders in name order
			var dlcRoot = GameInstall.GetDlcPath(GamePath);
			if (!Directory.Exists(dlcRoot)) return;

			foreach (var dlcFolder in Directory.GetDirectories(dlcRoot).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)) {
				var dlcName = Path.GetFileName(dlcFolder);
				var files = FileUtils.EnumerateFilesSafe(dlcFolder, "*.ini")
					.Where(f => Path.GetFileName(f).Equals("BmGame.ini", StringComparison.OrdinalIgnoreCase))
					.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

				foreach (var file in files) {
					var inside = Path.GetRelativePath(dlcFolder, file);
					var top = inside.Split('\\')[0];
					var (modPath, relativeInMod) = deployments.FindOwner(file);

					var source = new ConfigSource() {
						FullPath = file,
						RelativePath = Path.GetRelativePath(GamePath, file),
						ReadPath = ReadPathFor(file, modPath, relativeInMod),
						ModPath = modPath,
					};

					if (modPath != null) {
						source.Kind = SourceKind.ManagedMod;
						source.Label = modName(modPath) ?? modPath;
					} else if (top.Equals("Content", StringComparison.OrdinalIgnoreCase)) {
						source.Kind = SourceKind.OfficialDlc;
						source.Label = $"Official DLC ({dlcName})";
					} else {
						source.Kind = SourceKind.OtherMod;
						source.Label = GetModFolderLabel(dlcName, inside);
					}

					Sources.Add(source);
				}
			}
		}

		/// <summary>DLC\&lt;number&gt;\Content\...: the game's own DLC.</summary>
		private static bool IsOfficialDlcFile(string gamePath, string file) {
			var parts = Path.GetRelativePath(GameInstall.GetDlcPath(gamePath), file).Split('\\');
			return parts.Length > 2 && parts[1].Equals("Content", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsOfficialSource(ConfigSource source) => source.Kind == SourceKind.BaseGame || source.Kind == SourceKind.OfficialDlc;

		/// <summary>"DLC\356474\Some Mod" for DLC\356474\Some Mod\Config\BmGame.ini.</summary>
		private static string GetModFolderLabel(string dlcName, string inside) {
			var parts = inside.Split('\\').ToList();
			parts.RemoveAt(parts.Count - 1); // BmGame.ini
			while (parts.Count > 0 && (parts[^1].Equals("Config", StringComparison.OrdinalIgnoreCase) || parts[^1].Equals("BmGame", StringComparison.OrdinalIgnoreCase))) {
				parts.RemoveAt(parts.Count - 1);
			}
			return "DLC\\" + dlcName + (parts.Count > 0 ? "\\" + string.Join("\\", parts) : "");
		}

		private static List<ShowcaseSuit> MakePage(string page, List<ShowcaseLine> effective, List<ShowcaseLine> allLines, Dictionary<string, string> strings, Dictionary<string, string> thumbnails) {
			// hidden items (every line commented out) are listed where they'd be, so they can be shown again
			var onPage = effective.Where(e => e.Fields.Page.Equals(page, StringComparison.OrdinalIgnoreCase)).ToList();
			var shownItems = new HashSet<string>(onPage.Select(e => e.Fields.Item));
			var hiddenLines = allLines.Where(l => l.Hidden && l.Op != ConfigOp.Remove && l.Op != ConfigOp.Clear)
				.Where(l => l.Fields.Page.Equals(page, StringComparison.OrdinalIgnoreCase) && !shownItems.Contains(l.Fields.Item));

			var entries = onPage.Concat(hiddenLines)
				.OrderBy(e => e.Fields.SortIndex ?? 0)
				.ThenBy(e => e.Fields.Unlocked ? 0 : 1)
				.ThenBy(e => e.Order)
				.ToList();

			// which SortIndex values are shared by different items
			var itemsPerIndex = new Dictionary<int, HashSet<string>>();
			foreach (var e in entries.Where(e => !e.Hidden)) {
				var index = e.Fields.SortIndex ?? 0;
				if (!itemsPerIndex.TryGetValue(index, out var set)) {
					set = new HashSet<string>();
					itemsPerIndex[index] = set;
				}
				set.Add(e.Fields.Item);
			}

			var result = new List<ShowcaseSuit>();
			var byItem = new Dictionary<string, ShowcaseSuit>();
			string previous = null;

			foreach (var e in entries) {
				var item = e.Fields.Item;
				if (!e.Hidden) {
					if (item == previous) continue; // same check as XI_BatCaveEnumerate (case-sensitive)
					previous = item;
				}

				if (byItem.TryGetValue(item, out var existing)) {
					if (!e.Hidden) ++existing.Appearances;
					continue;
				}

				var skin = e.Fields.Skin;
				if (item.Equals("BatmanMk2", StringComparison.OrdinalIgnoreCase)) skin = "Playable_Batman_Oldsuit"; // XI_BatCaveEnumerate does this too

				var character = CharacterOf(skin, page);
				var sortIndex = e.Fields.SortIndex ?? 0;

				var suit = new ShowcaseSuit() {
					Page = page,
					Item = item,
					Skin = skin,
					Character = character,
					CharacterName = CharacterDisplayName(character),
					Name = strings.TryGetValue(item, out var name) && name.Length > 0 ? name : SplitWords(item),
					Description = strings.TryGetValue(item + "_Desc", out var desc) ? desc : "",
					SortIndex = sortIndex,
					Unlocked = e.Fields.Unlocked,
					DLC = e.Fields.DLC,
					WBPlay = e.Fields.WBPlay,
					Condition = e.Fields.Condition,
					SourceLabel = e.Source.Label,
					Tied = !e.Hidden && itemsPerIndex[sortIndex].Count > 1,
					Hidden = e.Hidden,
					ThumbnailPackage = thumbnails.TryGetValue(item, out var package) ? package : null,
				};

				// official DLCs and skin mods repeat base game lines (the first suit, the first Batmobile) as filler: an
				// item's real source is the base game if it defines it, else the official DLCs that do, else the mods
				var defining = allLines
					.Where(l => l.Op != ConfigOp.Remove && l.Op != ConfigOp.Clear)
					.Where(l => l.Fields.Page.Equals(page, StringComparison.OrdinalIgnoreCase) && l.Fields.Item == item)
					.Select(l => l.Source)
					.ToList();
				suit.IsOfficial = defining.Any(IsOfficialSource);
				suit.ModPaths = defining.Where(d => d.ModPath != null).Select(d => d.ModPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				var shown = defining.Any(d => d.Kind == SourceKind.BaseGame) ? defining.Where(d => d.Kind == SourceKind.BaseGame)
					: suit.IsOfficial ? defining.Where(IsOfficialSource)
					: defining;
				foreach (var source in shown) {
					if (!suit.Sources.Contains(source.Label)) suit.Sources.Add(source.Label);
				}
				if (suit.Sources.Count > 0) suit.SourceLabel = suit.Sources[0]; // the first config (in load order) that defines it

				byItem[item] = suit;
				result.Add(suit);
			}

			return result;
		}

		#endregion
		#region characters and names

		/// <summary>Same rule as RPersistentOptions.GetActivePlayerSkinId: "Playable_Batman_Noel" -> "batman".</summary>
		public static string CharacterOf(string skin, string page) {
			if (page.Equals(PageBatmobiles, StringComparison.OrdinalIgnoreCase)) return "batmobile";

			var s = skin ?? "";
			if (s.StartsWith("Playable_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(9);
			if (s.StartsWith("Batmobile", StringComparison.OrdinalIgnoreCase)) return "batmobile";

			var underscore = s.LastIndexOf('_');
			if (underscore != -1) s = s.Substring(0, underscore);

			return s.ToLowerInvariant();
		}

		private static readonly Dictionary<string, string> CharacterNames = new(StringComparer.OrdinalIgnoreCase) {
			["batman"] = "Batman",
			["robin"] = "Robin",
			["nightwing"] = "Nightwing",
			["catwoman"] = "Catwoman",
			["azrael"] = "Azrael",
			["dlcbatgirl"] = "Batgirl",
			["dlcredhood"] = "Red Hood",
			["dlcharleyquinn"] = "Harley Quinn",
			["batmobile"] = "Batmobile",
		};

		public static string CharacterDisplayName(string character) {
			if (CharacterNames.TryGetValue(character, out var name)) return name;
			if (character.Length == 0) return "Other";

			var id = character.StartsWith("dlc", StringComparison.OrdinalIgnoreCase) ? character.Substring(3) : character;
			return id.Length == 0 ? character : char.ToUpperInvariant(id[0]) + id.Substring(1);
		}

		private static readonly Regex WordBoundary = new("(?<=[a-z])(?=[A-Z0-9])");

		public static string SplitWords(string key) {
			return WordBoundary.Replace(key, " ").Replace('_', ' ').Trim();
		}

		#endregion
		#region thumbnails

		/// <summary>Item -> Showcase_UI_&lt;Item&gt;_SF.upk (the Showcase grid thumbnail). DLC packages override the base game's.</summary>
		private static Dictionary<string, string> FindThumbnailPackages(string gamePath) {
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			void Scan(string dir) {
				foreach (var file in FileUtils.EnumerateFilesSafe(dir, "Showcase_UI_*_SF.upk").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
					var name = Path.GetFileNameWithoutExtension(file);
					var item = name.Substring("Showcase_UI_".Length, name.Length - "Showcase_UI_".Length - "_SF".Length);
					result[item] = file;
				}
			}

			Scan(GameInstall.GetCookedPath(gamePath));
			Scan(GameInstall.GetDlcPath(gamePath));
			return result;
		}

		#endregion
	}
}

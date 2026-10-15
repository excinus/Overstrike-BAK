// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Data;
using OverstrikeBAK.Suits;
using OverstrikeBAK.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverstrikeBAK.Mods {
	public class DetectedMod {
		public string RelativeRoot; // "" = the scanned folder itself
		public ModType Type;
		public string Name;
		public string Description;
		public string ShowcaseName; // DLC mods: the in-game name of the one item it adds ("" = none, or several items)
		public List<string> Files = new(); // files for the game folder itself (a replacer's, a script mod's extras), relative to it
		public string FilesRoot = "";      // where they are: the folder that stands for the game folder
	}

	public class DetectionResult {
		public List<DetectedMod> Mods = new();
		public List<string> TfcInstallerFiles = new();
		public bool IsTfcInstallerMod => TfcInstallerFiles.Count > 0;
		public List<string> ResoRepFiles = new();
		public bool IsResoRepMod => ResoRepFiles.Count > 0;
	}

	/// <summary>
	/// Finds mods inside a folder (an extracted archive, a Mods Library entry, a DLC folder...):
	/// - DLC: a folder with Config\ and/or CookedPCConsole\ (optionally under BmGame\), the same layout as the
	///   game's own DLC\&lt;number&gt;\Content. It goes to DLC\&lt;number&gt;\&lt;folder&gt;.
	/// - BmSDK script mod: a folder with mod.toml. It goes to BmGame\Mods\&lt;folder&gt;. Files next to it for the game folder
	///   (in a copy of the game's layout, like Binaries\Console.txt next to BmGame\Mods\&lt;folder&gt;) go there too.
	/// - Loose BmSDK scripts: BmGame\Scripts\*.cs in a game layout (ModsLibrary lays out archives that only have .cs
	///   files this way). They go to BmGame\Scripts. They count as script mods.
	/// - Replacer: a folder laid out like the game folder (BmGame\CookedPCConsole, and/or DLC\&lt;number&gt;\Content for
	///   "hybrid" ones) that doesn't register a DLC. Its files go in place of the game's own.
	/// - TFC Installer mod: *.upk.PackagePatch / *.TFCMapping files, or .tfc files outside any DLC. Not supported.
	/// - ResoRep mod: nothing but loose .dds textures (read by ResoRep while the game runs). Not supported.
	/// </summary>
	internal static class ModDetector {
		private static readonly Regex DlcPathSegment = new(@"(^|\\)DLC\\\d+\\", RegexOptions.IgnoreCase);

		/// <summary>A readme, a preview picture...: comes with mods but isn't part of them.</summary>
		public static bool IsDocument(string file) => DocumentExtensions.Contains(Path.GetExtension(file));

		/// <summary>Top folders of the game folder a replacer can put files in.</summary>
		private static readonly HashSet<string> GameTopFolders = new(StringComparer.OrdinalIgnoreCase) { "BmGame", "DLC", "Engine", "Binaries" };

		/// <summary>Files that come with mods but aren't part of them (readmes, previews...).</summary>
		private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase) {
			".txt", ".md", ".rtf", ".pdf", ".nfo", ".url", ".html", ".htm", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
		};

		public static DetectionResult Detect(string folder, string folderName) {
			var result = new DetectionResult();

			var files = FileUtils.EnumerateFilesSafe(folder)
				.Select(f => Path.GetRelativePath(folder, f))
				.Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
				.ToList();

			// directories (relative), with their child directory names
			var children = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase) { [""] = new(StringComparer.OrdinalIgnoreCase) };
			foreach (var file in files) {
				var dir = Path.GetDirectoryName(file) ?? "";
				while (dir.Length > 0) {
					var parent = Path.GetDirectoryName(dir) ?? "";
					if (!children.TryGetValue(parent, out var set)) {
						set = new(StringComparer.OrdinalIgnoreCase);
						children[parent] = set;
					}
					set.Add(Path.GetFileName(dir));
					if (!children.ContainsKey(dir)) children[dir] = new(StringComparer.OrdinalIgnoreCase);
					dir = parent;
				}
			}

			// script mods
			var scriptRoots = files
				.Where(f => Path.GetFileName(f).Equals("mod.toml", StringComparison.OrdinalIgnoreCase))
				.Select(f => Path.GetDirectoryName(f) ?? "")
				.ToList();

			foreach (var root in RemoveNested(scriptRoots)) {
				var (name, description) = ReadModToml(Path.Combine(folder, root, "mod.toml"));
				result.Mods.Add(new DetectedMod() {
					RelativeRoot = root,
					Type = ModType.SCRIPT,
					Name = name ?? (root.Length > 0 ? Path.GetFileName(root) : folderName),
					Description = description,
				});
			}

			string Child(string dir, string name) => dir.Length > 0 ? dir + "\\" + name : name;

			// loose scripts: BmGame\Scripts\*.cs (and nothing for CookedPCConsole: that's a replacer)
			var looseRoots = new List<string>();
			foreach (var (dir, subdirs) in children) {
				if (scriptRoots.Any(s => IsInsideRelative(dir, s) || IsInsideRelative(s, dir))) continue;
				if (!subdirs.Contains("BmGame") || !children.TryGetValue(Child(dir, "BmGame"), out var bm) || !bm.Contains("Scripts") || bm.Contains("CookedPCConsole")) continue;
				var scriptsDir = Child(Child(dir, "BmGame"), "Scripts");
				if (!files.Any(f => IsInsideRelative(Path.GetDirectoryName(f) ?? "", scriptsDir) && f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))) continue;
				looseRoots.Add(dir);
			}
			foreach (var root in RemoveNested(looseRoots)) {
				var gameFiles = files
					.Where(f => IsInsideRelative(Path.GetDirectoryName(f) ?? "", root))
					.Select(f => root.Length > 0 ? f.Substring(root.Length + 1) : f)
					.Where(f => f.Contains('\\') && GameTopFolders.Contains(f.Split('\\')[0]))
					.ToList();
				var scripts = gameFiles.Where(f => f.StartsWith("BmGame\\Scripts\\", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).ToList();
				result.Mods.Add(new DetectedMod() {
					RelativeRoot = root,
					Type = ModType.SCRIPT,
					Name = root.Length > 0 ? Path.GetFileName(root) : folderName,
					Description = "BmSDK scripts: " + string.Join(", ", scripts.Take(4)) + (scripts.Count > 4 ? $" (and {scripts.Count - 4} more)" : ""),
					Files = gameFiles,
					FilesRoot = root,
				});
				scriptRoots.Add(root); // not a replacer or DLC mod too
			}

			// replacers: the game's own layout, BmGame\CookedPCConsole and/or DLC\<number>\Content (official DLC files)
			var replacerCandidates = new List<string>();
			foreach (var (dir, subdirs) in children) {
				if (scriptRoots.Any(s => IsInsideRelative(dir, s) || IsInsideRelative(s, dir))) continue; // a script mod's game layout isn't a replacer

				var bmGame = Child(dir, "BmGame");
				var hasCooked = subdirs.Contains("BmGame") && children.TryGetValue(bmGame, out var bm) && bm.Contains("CookedPCConsole");
				var dlc = Child(dir, "DLC");
				var hasOfficialDlc = subdirs.Contains("DLC") && children.TryGetValue(dlc, out var numbers)
					&& numbers.Any(n => n.All(char.IsDigit) && children.TryGetValue(Child(dlc, n), out var inside) && inside.Contains("Content"));
				if (!hasCooked && !hasOfficialDlc) continue;

				// a DLC mod in a BmGame folder (Config\BmGame.ini registering it) isn't a replacer
				var registers = files.Any(f => IsInsideRelative(Path.GetDirectoryName(f) ?? "", bmGame) && f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && LooksLikeDlcConfig(Path.Combine(folder, f)));
				if (registers && !hasOfficialDlc) continue;

				replacerCandidates.Add(dir);
			}

			var replacerRoots = RemoveNested(replacerCandidates);
			foreach (var root in replacerRoots) {
				var gameFiles = files
					.Where(f => IsInsideRelative(Path.GetDirectoryName(f) ?? "", root))
					.Select(f => root.Length > 0 ? f.Substring(root.Length + 1) : f)
					.Where(f => f.Contains('\\') && GameTopFolders.Contains(f.Split('\\')[0])) // readmes next to BmGame\ stay out
					.ToList();
				if (gameFiles.Count == 0) continue;

				var tops = gameFiles.Select(f => f.Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase);
				result.Mods.Add(new DetectedMod() {
					RelativeRoot = root,
					Type = ModType.REPLACER,
					Name = root.Length > 0 ? Path.GetFileName(root) : folderName,
					Description = $"Replaces {gameFiles.Count} game file{(gameFiles.Count == 1 ? "" : "s")} (in {string.Join(", ", tops)})",
					Files = gameFiles,
					FilesRoot = root,
				});
			}

			// DLC
			var candidates = new List<string>();
			foreach (var (dir, subdirs) in children) {
				if (!subdirs.Contains("CookedPCConsole") && !subdirs.Contains("Config")) continue;
				if (scriptRoots.Any(s => IsInsideRelative(dir, s))) continue;
				if (replacerRoots.Any(r => IsInsideRelative(dir, r))) continue;

				var candidate = dir;
				if (Path.GetFileName(dir).Equals("BmGame", StringComparison.OrdinalIgnoreCase)) {
					candidate = Path.GetDirectoryName(dir) ?? "";
				}

				if (!candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase)) candidates.Add(candidate);
			}

			var dlcRoots = new List<string>();
			foreach (var candidate in RemoveNested(candidates)) {
				var inside = files.Where(f => IsInsideRelative(Path.GetDirectoryName(f) ?? "", candidate)).ToList();
				var registersDlc = inside.Any(f => f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && LooksLikeDlcConfig(Path.Combine(folder, f)));
				var underDlcFolder = DlcPathSegment.IsMatch(candidate + "\\");
				if (!registersDlc && !underDlcFolder) continue; // e.g. BmGame\CookedPCConsole replacing base game files

				dlcRoots.Add(candidate);
				result.Mods.Add(new DetectedMod() {
					RelativeRoot = candidate,
					Type = ModType.DLC,
					Name = candidate.Length > 0 ? Path.GetFileName(candidate) : folderName,
					Description = DescribeDlc(inside.Select(f => Path.Combine(folder, f)), out var showcaseName),
					ShowcaseName = showcaseName,
				});
			}

			// script mods' extra files: in BmGame\Mods\<mod>'s game layout (or next to the mod's folder), the files of the
			// game's top folders (Binaries\Console.txt...) that aren't part of any mod found above
			var modRoots = result.Mods.Select(m => m.RelativeRoot).ToList();
			foreach (var script in result.Mods.Where(m => m.Type == ModType.SCRIPT && !looseRoots.Contains(m.RelativeRoot))) {
				var m = Regex.Match(script.RelativeRoot, @"^(?:(.*)\\)?BmGame\\Mods\\[^\\]+$", RegexOptions.IgnoreCase);
				var gameRoot = m.Success ? m.Groups[1].Value : (Path.GetDirectoryName(script.RelativeRoot) ?? "");
				if (gameRoot.Equals(script.RelativeRoot, StringComparison.OrdinalIgnoreCase)) continue;

				script.FilesRoot = gameRoot;
				script.Files = files
					.Where(f => IsInsideRelative(Path.GetDirectoryName(f) ?? "", gameRoot))
					.Where(f => !modRoots.Any(r => r.Length > 0 && IsInsideRelative(Path.GetDirectoryName(f) ?? "", r)))
					.Select(f => gameRoot.Length > 0 ? f.Substring(gameRoot.Length + 1) : f)
					.Where(f => f.Contains('\\') && GameTopFolders.Contains(f.Split('\\')[0]) && !f.StartsWith("BmGame\\Mods\\", StringComparison.OrdinalIgnoreCase))
					.ToList();
			}

			// TFC Installer
			foreach (var file in files) {
				var name = Path.GetFileName(file);
				var isTfcInstallerFile = name.EndsWith(".PackagePatch", StringComparison.OrdinalIgnoreCase)
					|| name.EndsWith(".TFCMapping", StringComparison.OrdinalIgnoreCase)
					|| (name.EndsWith(".tfc", StringComparison.OrdinalIgnoreCase) && !dlcRoots.Concat(replacerRoots).Any(r => IsInsideRelative(Path.GetDirectoryName(file) ?? "", r)));
				if (isTfcInstallerFile) result.TfcInstallerFiles.Add(file);
			}

			// ResoRep: only loose textures (and maybe a readme or preview pictures)
			if (result.Mods.Count == 0 && !result.IsTfcInstallerMod) {
				var textures = files.Where(f => f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)).ToList();
				var others = files.Where(f => !f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) && !DocumentExtensions.Contains(Path.GetExtension(f))).ToList();
				if (textures.Count > 0 && others.Count == 0) result.ResoRepFiles = textures;
			}

			return result;
		}

		private static bool IsInsideRelative(string path, string root) {
			if (root.Length == 0) return true;
			return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Keeps only the outermost roots.</summary>
		private static List<string> RemoveNested(List<string> roots) {
			return roots
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Where(r => !roots.Any(other => !other.Equals(r, StringComparison.OrdinalIgnoreCase) && IsInsideRelative(r, other)))
				.OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private static bool LooksLikeDlcConfig(string iniPath) {
			try {
				var text = TextFile.ReadAllText(iniPath);
				return text.Contains("ShowcaseItems", StringComparison.OrdinalIgnoreCase)
					|| text.Contains("DLCBundlesInstalled", StringComparison.OrdinalIgnoreCase)
					|| text.Contains("[BmGame.RGameInfo]", StringComparison.OrdinalIgnoreCase);
			} catch {
				return false;
			}
		}

		private static readonly HashSet<string> FillerItems = new(StringComparer.OrdinalIgnoreCase) {
			"BatmanMk2", "BatmanMk8", "Robin", "Catwoman", "Nightwing", "Batmobile", "DLCBatgirl", "Azrael",
		};

		/// <summary>The same description (and in-game name) for a DLC mod's folder anywhere: in the game, for a mod imported from there.</summary>
		public static string DescribeDlcFolder(string folder, out string showcaseName) => DescribeDlc(FileUtils.EnumerateFilesSafe(folder), out showcaseName);

		/// <summary>
		/// "Adds: Batman Kenpach 2022 (Players)" from the mod's ShowcaseItems. 'showcaseName' is the name the Suits Menu
		/// shows for it (from the mod's GFxUI.int) when the mod adds a single item, else "".
		/// </summary>
		private static string DescribeDlc(IEnumerable<string> files, out string showcaseName) {
			var list = files.ToList();
			var items = new List<string>();
			var itemIds = new List<string>();
			foreach (var file in list) {
				if (!Path.GetFileName(file).Equals("BmGame.ini", StringComparison.OrdinalIgnoreCase)) continue;
				try {
					foreach (var (op, _, fields, _) in ShowcaseIni.Read(TextFile.ReadAllText(file))) {
						if (op == ConfigOp.Remove || op == ConfigOp.Clear) continue;
						if (fields.Item.Length == 0 || FillerItems.Contains(fields.Item)) continue;
						var text = $"{fields.Item} ({fields.Page})";
						if (!items.Contains(text)) items.Add(text);
						if (!itemIds.Contains(fields.Item, StringComparer.OrdinalIgnoreCase)) itemIds.Add(fields.Item);
					}
				} catch {}
			}

			showcaseName = "";
			if (itemIds.Count == 1) {
				var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var file in list.Where(f => Path.GetFileName(f).Equals("GFxUI.int", StringComparison.OrdinalIgnoreCase))) {
					Suits.Localization.Parse(file, names);
				}
				// no name in the mod's files: the Suits Menu shows the item's id split into words, and so does this
				showcaseName = names.TryGetValue(itemIds[0], out var name) && name.Length > 0 ? name : Suits.ShowcaseModel.SplitWords(itemIds[0]);
			}

			return items.Count > 0 ? "Adds: " + string.Join(", ", items) : null;
		}

		private static (string name, string description) ReadModToml(string path) {
			try {
				string name = null, description = null;
				foreach (var line in File.ReadAllLines(path)) {
					var m = Regex.Match(line, "^\\s*(name|description)\\s*=\\s*\"(.*)\"\\s*$");
					if (!m.Success) continue;
					if (m.Groups[1].Value == "name") name = m.Groups[2].Value;
					else description = m.Groups[2].Value;
				}
				return (name, description);
			} catch {
				return (null, null);
			}
		}
	}
}

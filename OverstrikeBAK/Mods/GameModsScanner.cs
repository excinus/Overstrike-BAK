// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Data;
using OverstrikeBAK.Game;
using OverstrikeBAK.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Mods {
	/// <summary>A mod found in the game folder that Overstrike-BAK doesn't manage (installed by hand or by another tool).</summary>
	public class FoundGameMod: INotifyPropertyChanged {
		public event PropertyChangedEventHandler PropertyChanged;

		public string FullPath { get; set; }
		public string RelativePath { get; set; }
		public ModType Type { get; set; }
		public string Name { get; set; }
		public string TypeText => Type == ModType.SCRIPT ? "Script" : "DLC";

		private bool _import;
		public bool Import {
			get => _import;
			set {
				_import = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Import)));
			}
		}
	}

	internal static class GameModsScanner {
		/// <summary>
		/// DLC mods anywhere inside DLC\&lt;number&gt;\ (except the official DLC\&lt;number&gt;\Content folders and the
		/// Community Patch's folders, which TFC Installer manages) and script mods in BmGame\Mods\.
		/// </summary>
		public static List<FoundGameMod> Scan(string gamePath, Deployments deployments) {
			var result = new List<FoundGameMod>();
			var managed = deployments.Mods.Values.Select(rel => Path.Combine(gamePath, rel.Replace('/', '\\'))).ToList();

			bool IsManaged(string path) => managed.Any(m => FileUtils.IsInside(path, m) || FileUtils.IsInside(m, path));

			var dlcRoot = GameInstall.GetDlcPath(gamePath);
			if (Directory.Exists(dlcRoot)) {
				foreach (var dlc in Directory.GetDirectories(dlcRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
					foreach (var folder in Directory.GetDirectories(dlc).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
						var name = Path.GetFileName(folder);
						if (name.Equals("Content", StringComparison.OrdinalIgnoreCase)) continue;
						if (name.Contains("COMMUNITYPATCH", StringComparison.OrdinalIgnoreCase)) continue;

						foreach (var mod in ModDetector.Detect(folder, name).Mods) {
							if (mod.Type != ModType.DLC) continue;

							var full = Path.Combine(folder, mod.RelativeRoot);
							if (IsManaged(full)) continue;

							result.Add(new FoundGameMod() {
								FullPath = full,
								RelativePath = Path.GetRelativePath(gamePath, full),
								Type = ModType.DLC,
								Name = mod.Name,
								Import = true,
							});
						}
					}
				}
			}

			// "Binks' AC Suit\Damaged" or "Micecury\IM1" would be confusing as just "Damaged" or "IM1" in the library
			var ambiguous = new HashSet<string>(result.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
			foreach (var mod in result) {
				if (ambiguous.Contains(mod.Name) || IsGenericName(mod.Name)) {
					mod.Name = Path.GetFileName(Path.GetDirectoryName(mod.FullPath)) + " - " + mod.Name;
				}
			}

			var modsRoot = GameInstall.GetScriptModsPath(gamePath);
			if (Directory.Exists(modsRoot)) {
				foreach (var folder in Directory.GetDirectories(modsRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
					if (!File.Exists(Path.Combine(folder, "mod.toml"))) continue;
					if (IsManaged(folder)) continue;

					var name = Path.GetFileName(folder);
					result.Add(new FoundGameMod() {
						FullPath = folder,
						RelativePath = Path.GetRelativePath(gamePath, folder),
						Type = ModType.SCRIPT,
						Name = name,
						Import = !name.Equals("ModTemplate", StringComparison.OrdinalIgnoreCase), // BmSDK's example
					});
				}
			}

			return result;
		}

		private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase) {
			"Damaged", "Pristine", "Clean", "Default", "Alt", "Alternate", "Optional", "Main", "Mod", "Content", "Normal",
		};

		private static bool IsGenericName(string name) {
			return name.Length <= 4 || GenericNames.Contains(name);
		}

		/// <summary>
		/// Adds the mod to the library without copying anything: its library folder only gets an overstrike-bak.json
		/// saying it's installed in the game, where its files stay. Returns the new library path.
		/// </summary>
		public static string Import(FoundGameMod mod, string gamePath) {
			Directory.CreateDirectory(ModsLibrary.LibraryPath);
			var folder = FileUtils.MakeUniquePath(ModsLibrary.LibraryPath, FileUtils.SanitizeFileName(mod.Name));

			string showcaseName = null;
			var description = mod.Type == ModType.DLC ? ModDetector.DescribeDlcFolder(mod.FullPath, out showcaseName) : null;

			var manifest = new LibraryManifest();
			manifest.Mods.Add(new ManifestMod() {
				Path = "",
				Type = mod.Type,
				Name = mod.Name,
				Folder = Path.GetFileName(mod.FullPath),
				Description = description,
				ShowcaseName = showcaseName,
				InstalledGame = gamePath,
				InstalledPath = FileUtils.NormalizeRelative(mod.RelativePath),
			});
			manifest.Save(folder);

			return Path.GetFileName(folder);
		}
	}
}

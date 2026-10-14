// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Data;
using OverstrikeBAK.Game;
using OverstrikeBAK.Suits;
using OverstrikeBAK.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Mods {
	/// <summary>
	/// Moves mods between the Mods Library and the game (DLC mods -> DLC\&lt;number&gt;\&lt;folder&gt;, script mods ->
	/// BmGame\Mods\&lt;folder&gt;). A mod's files are only ever in one of the two places; its library folder keeps
	/// overstrike-bak.json so it can be found and moved back. Moving within a drive is instant; across drives the
	/// files are copied, then deleted.
	/// </summary>
	internal class Deployer {
		private readonly string _gamePath;

		public Deployer(string gamePath) {
			_gamePath = gamePath;
		}

		public string GetTargetParent(ModType type, string dlcFolder) {
			return type == ModType.SCRIPT
				? GameInstall.GetScriptModsPath(_gamePath)
				: Path.Combine(GameInstall.GetDlcPath(_gamePath), dlcFolder);
		}

		/// <summary>Moves the mod's files from the library into the game.</summary>
		/// <param name="installedPath">Where to put it (relative to the game folder), like the version it replaces; null = the usual place.</param>
		public void Install(ModEntry mod, string dlcFolder, Action<string> onFile = null, string installedPath = null) {
			if (!mod.IsSupported) throw new InvalidOperationException("TFC Installer mods can't be installed by Overstrike-BAK.");
			if (mod.IsInstalledIn(_gamePath)) return;
			if (mod.InstalledGame != null) throw new InvalidOperationException($"'{mod.Name}' is installed in another game folder ({mod.InstalledGame}).");

			var source = ModsLibrary.GetModFolder(mod);
			if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"'{mod.Name}' is missing from the Mods Library.");

			string target;
			if (installedPath != null && !Directory.Exists(Path.Combine(_gamePath, installedPath.Replace('/', '\\')))) {
				target = Path.Combine(_gamePath, installedPath.Replace('/', '\\'));
				Directory.CreateDirectory(Path.GetDirectoryName(target));
			} else {
				var parent = GetTargetParent(mod.Type, dlcFolder);
				Directory.CreateDirectory(parent);
				target = FileUtils.MakeUniquePath(parent, FileUtils.SanitizeFileName(mod.FolderName));
			}

			MoveContents(source, target, deleteSource: mod.PathInEntry.Length > 0, onFile);
			if (mod.PathInEntry.Length > 0) RemoveEmptyLibraryFolders(source, ModsLibrary.GetEntryFolder(mod.Entry));

			mod.InstalledGame = _gamePath;
			mod.InstalledPath = FileUtils.NormalizeRelative(Path.GetRelativePath(_gamePath, target));
			ModsLibrary.UpdateManifest(mod);
		}

		/// <summary>Moves the mod's files from the game back into the library.</summary>
		public void Uninstall(ModEntry mod, Action<string> onFile = null) {
			if (!mod.IsInstalledIn(_gamePath)) return;

			var installed = Path.Combine(_gamePath, mod.InstalledPath.Replace('/', '\\'));
			if (Directory.Exists(installed)) {
				if (!IsSafeToMove(installed)) throw new InvalidOperationException($"'{installed}' isn't a mod folder.");

				// the Suits Menu may have rewritten SortIndex values in its configs: the library keeps the originals
				new SuitBackups(_gamePath).RestoreInside(installed);

				MoveContents(installed, ModsLibrary.GetModFolder(mod), deleteSource: true, onFile);
				RemoveEmptyParents(installed);
			}

			mod.InstalledGame = null;
			mod.InstalledPath = null;
			ModsLibrary.UpdateManifest(mod);
		}

		/// <summary>Deletes an installed mod's files from the game (the user is deleting the mod).</summary>
		public void Delete(ModEntry mod) {
			if (!mod.IsInstalledIn(_gamePath)) return;

			var installed = Path.Combine(_gamePath, mod.InstalledPath.Replace('/', '\\'));
			if (Directory.Exists(installed)) {
				if (!IsSafeToMove(installed)) throw new InvalidOperationException($"'{installed}' isn't a mod folder.");

				var backups = new SuitBackups(_gamePath).GetBackupPath(installed);
				if (Directory.Exists(backups)) FileUtils.DeleteDirectory(backups);

				FileUtils.DeleteDirectory(installed);
				RemoveEmptyParents(installed);
			}

			mod.InstalledGame = null;
			mod.InstalledPath = null;
		}

		/// <summary>Moves installed DLC mods into another DLC\&lt;number&gt; folder.</summary>
		public void MoveDlcMods(IEnumerable<ModEntry> mods, string newDlcFolder) {
			var parent = GetTargetParent(ModType.DLC, newDlcFolder);
			Directory.CreateDirectory(parent);

			foreach (var mod in mods.Where(m => m.Type == ModType.DLC && m.IsInstalledIn(_gamePath))) {
				var installed = Path.Combine(_gamePath, mod.InstalledPath.Replace('/', '\\'));
				if (!Directory.Exists(installed) || FileUtils.SamePath(Path.GetDirectoryName(installed), parent)) continue;

				var target = FileUtils.MakeUniquePath(parent, Path.GetFileName(installed));
				new SuitBackups(_gamePath).MoveInside(installed, target);
				MoveContents(installed, target, deleteSource: true);
				RemoveEmptyParents(installed);

				mod.InstalledPath = FileUtils.NormalizeRelative(Path.GetRelativePath(_gamePath, target));
				ModsLibrary.UpdateManifest(mod);
			}
		}

		/// <summary>
		/// Moves everything inside 'source' into 'target' (created if needed). The library's overstrike-bak.json never
		/// moves. Whole folders/files are moved when on the same drive, copied and deleted otherwise.
		/// </summary>
		private static void MoveContents(string source, string target, bool deleteSource, Action<string> onFile = null) {
			Directory.CreateDirectory(target);

			foreach (var dir in Directory.GetDirectories(source)) {
				var to = Path.Combine(target, Path.GetFileName(dir));
				onFile?.Invoke(Path.GetFileName(dir));
				if (Directory.Exists(to) || !TryMoveDirectory(dir, to)) {
					FileUtils.CopyDirectory(dir, to, onFile);
					FileUtils.DeleteDirectory(dir);
				}
			}

			foreach (var file in Directory.GetFiles(source)) {
				if (Path.GetFileName(file).Equals(LibraryManifest.FileName, StringComparison.OrdinalIgnoreCase)) continue;

				var to = Path.Combine(target, Path.GetFileName(file));
				onFile?.Invoke(Path.GetFileName(file));
				if (File.Exists(to)) File.SetAttributes(to, FileAttributes.Normal);
				File.Move(file, to, true);
			}

			if (deleteSource && !Directory.EnumerateFileSystemEntries(source).Any()) {
				Directory.Delete(source);
			}
		}

		private static bool TryMoveDirectory(string from, string to) {
			if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase)) {
				return false;
			}

			try {
				Directory.Move(from, to);
				return true;
			} catch (IOException) {
				return false;
			}
		}

		/// <summary>DLC\&lt;n&gt;\something or BmGame\Mods\something, never the DLC folders themselves.</summary>
		private bool IsSafeToMove(string path) {
			var dlc = GameInstall.GetDlcPath(_gamePath);
			var mods = GameInstall.GetScriptModsPath(_gamePath);

			if (FileUtils.IsInside(path, mods)) return !FileUtils.SamePath(path, mods);

			if (FileUtils.IsInside(path, dlc)) {
				var parts = Path.GetRelativePath(dlc, path).Split('\\', StringSplitOptions.RemoveEmptyEntries);
				return parts.Length >= 2 && !parts[1].Equals("Content", StringComparison.OrdinalIgnoreCase);
			}

			return false;
		}

		/// <summary>After moving "Mods Library\Entry\Wrapper\Mod" into the game, removes "Wrapper" if it became empty (never the entry folder).</summary>
		private static void RemoveEmptyLibraryFolders(string moved, string entryFolder) {
			try {
				var dir = Path.GetDirectoryName(moved);
				while (dir != null && FileUtils.IsInside(dir, entryFolder) && !FileUtils.SamePath(dir, entryFolder)) {
					if (Directory.EnumerateFileSystemEntries(dir).Any()) break;
					Directory.Delete(dir);
					dir = Path.GetDirectoryName(dir);
				}
			} catch {}
		}

		/// <summary>After moving DLC\356474\Character Mods\X away, removes "Character Mods" if it became empty (never DLC\356474 itself).</summary>
		private void RemoveEmptyParents(string removed) {
			try {
				var dlc = GameInstall.GetDlcPath(_gamePath);
				var dir = Path.GetDirectoryName(removed);
				while (dir != null && FileUtils.IsInside(dir, dlc)) {
					var depth = Path.GetRelativePath(dlc, dir).Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
					if (depth < 2) break;
					if (Directory.EnumerateFileSystemEntries(dir).Any()) break;
					Directory.Delete(dir);
					dir = Path.GetDirectoryName(dir);
				}
			} catch {}
		}
	}
}

// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Utils;
using System.Collections.Generic;
using System.IO;

namespace OverstrikeBAK.Suits {
	/// <summary>
	/// Original copies of the game config files Overstrike-BAK edits to reorder suits (the base game's BmGame.ini,
	/// official DLC and mods). A file is copied the first time it's about to change, so the copy is always the
	/// untouched original. Layout: Backups\&lt;game folder&gt;\files\&lt;path inside the game folder&gt;.
	/// </summary>
	internal class SuitBackups {
		private readonly string _gamePath;

		/// <summary>Backups\&lt;game folder&gt;: the backed up files (in files\) and the original order snapshot.</summary>
		public string Root { get; }

		private string FilesRoot => Path.Combine(Root, "files");

		public SuitBackups(string gamePath) {
			_gamePath = gamePath;
			Root = Path.Combine(App.BaseDirectory, "Backups", FileUtils.SanitizeFileName(Path.GetFullPath(gamePath).Replace(':', '_').Replace('\\', '_').Replace('/', '_')));
		}

		public string GetBackupPath(string gameFile) {
			return Path.Combine(FilesRoot, Path.GetRelativePath(_gamePath, gameFile));
		}

		public bool HasBackups => FileUtils.EnumerateFilesSafe(FilesRoot).Length > 0;

		public void EnsureBackup(string gameFile) {
			var backup = GetBackupPath(gameFile);
			if (File.Exists(backup)) return;

			Directory.CreateDirectory(Path.GetDirectoryName(backup));
			File.Copy(gameFile, backup);
		}

		/// <summary>
		/// Puts back the original copies of files inside a game folder that's about to leave the game (a mod going back
		/// to the library), and forgets those backups: the library always holds the original files.
		/// </summary>
		public void RestoreInside(string gameFolder) {
			var backupFolder = GetBackupPath(gameFolder);
			if (!Directory.Exists(backupFolder)) return;

			foreach (var backup in FileUtils.EnumerateFilesSafe(backupFolder)) {
				var target = Path.Combine(gameFolder, Path.GetRelativePath(backupFolder, backup));
				if (File.Exists(target)) {
					File.SetAttributes(target, FileAttributes.Normal);
					File.Copy(backup, target, true);
				}
			}

			FileUtils.DeleteDirectory(backupFolder);
		}

		/// <summary>Keeps backups attached to a mod folder that moves inside the game (another DLC folder).</summary>
		public void MoveInside(string oldGameFolder, string newGameFolder) {
			var from = GetBackupPath(oldGameFolder);
			if (!Directory.Exists(from)) return;

			var to = GetBackupPath(newGameFolder);
			Directory.CreateDirectory(Path.GetDirectoryName(to));
			if (Directory.Exists(to)) FileUtils.DeleteDirectory(to);
			Directory.Move(from, to);
		}

		/// <summary>Puts every backed up file back. Files whose folder no longer exists in the game (mod removed) are skipped.</summary>
		public List<string> RestoreAll() {
			var restored = new List<string>();

			foreach (var backup in FileUtils.EnumerateFilesSafe(FilesRoot)) {
				var target = Path.Combine(_gamePath, Path.GetRelativePath(FilesRoot, backup));
				if (!Directory.Exists(Path.GetDirectoryName(target))) continue;

				if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
				File.Copy(backup, target, true);
				restored.Add(target);
			}

			return restored;
		}
	}
}

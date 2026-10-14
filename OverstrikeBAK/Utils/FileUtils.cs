// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Utils {
	internal static class FileUtils {
		public static void CopyDirectory(string source, string destination, Action<string> onFile = null) {
			Directory.CreateDirectory(destination);

			foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)) {
				Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
			}

			foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
				var relative = Path.GetRelativePath(source, file);
				onFile?.Invoke(relative);

				var target = Path.Combine(destination, relative);
				if (File.Exists(target)) {
					File.SetAttributes(target, FileAttributes.Normal);
				}
				File.Copy(file, target, true);
			}
		}

		public static void DeleteDirectory(string path) {
			if (!Directory.Exists(path)) return;

			foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) {
				File.SetAttributes(file, FileAttributes.Normal);
			}
			Directory.Delete(path, true);
		}

		public static long GetDirectorySize(string path) {
			try {
				return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
			} catch {
				return 0;
			}
		}

		/// <summary>True if 'path' is 'root' itself or is inside it.</summary>
		public static bool IsInside(string path, string root) {
			var p = Path.GetFullPath(path).TrimEnd('\\', '/') + "\\";
			var r = Path.GetFullPath(root).TrimEnd('\\', '/') + "\\";
			return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
		}

		public static bool SamePath(string a, string b) {
			return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Forward slashes, no leading/trailing separators. Used for paths stored in profiles.</summary>
		public static string NormalizeRelative(string path) {
			return path.Replace('\\', '/').Trim('/');
		}

		public static string SanitizeFileName(string name) {
			var invalid = Path.GetInvalidFileNameChars();
			var result = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
			return (result.Length == 0 ? "Mod" : result);
		}

		/// <summary>'parent/name', or 'parent/name (2)', 'parent/name (3)'... whichever doesn't exist yet.</summary>
		public static string MakeUniquePath(string parent, string name) {
			var path = Path.Combine(parent, name);
			var index = 2;
			while (Directory.Exists(path) || File.Exists(path)) {
				path = Path.Combine(parent, $"{name} ({index})");
				++index;
			}
			return path;
		}

		public static string[] EnumerateFilesSafe(string path, string pattern = "*") {
			try {
				if (!Directory.Exists(path)) return Array.Empty<string>();
				return Directory.GetFiles(path, pattern, new EnumerationOptions() { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive });
			} catch {
				return Array.Empty<string>();
			}
		}
	}
}

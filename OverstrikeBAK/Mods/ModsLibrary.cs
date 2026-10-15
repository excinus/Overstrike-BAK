// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Data;
using OverstrikeBAK.Utils;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Mods {
	/// <summary>
	/// The "Mods Library" folder next to the app. Each added mod gets a folder there. A mod that isn't installed keeps its
	/// files there; an installed one is moved into the game and only its overstrike-bak.json stays behind, so the library
	/// never doubles the space mods take. Archives are extracted when added (or on the next refresh if dropped in by hand).
	/// </summary>
	internal static class ModsLibrary {
		public static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar" };

		public const string TfcInstallerUrl = "https://www.nexusmods.com/site/mods/588";

		public static string LibraryPath => Path.Combine(App.BaseDirectory, "Mods Library");

		private const string TempPrefix = ".adding_";

		public static bool IsArchive(string path) => ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

		public static string GetEntryFolder(string entry) => Path.Combine(LibraryPath, entry);

		/// <summary>Where the files it puts in the game folder itself are while it's not installed.</summary>
		public static string GetGameFilesFolder(ModEntry mod) {
			return mod.GameFilesRoot.Length > 0 ? Path.Combine(GetEntryFolder(mod.Entry), mod.GameFilesRoot.Replace('/', '\\')) : GetEntryFolder(mod.Entry);
		}

		/// <summary>Where the mod's files are while it's not installed.</summary>
		public static string GetModFolder(ModEntry mod) {
			return mod.PathInEntry.Length > 0
				? Path.Combine(GetEntryFolder(mod.Entry), mod.PathInEntry.Replace('/', '\\'))
				: GetEntryFolder(mod.Entry);
		}

		#region scanning

		public static List<ModEntry> Scan(Action<string> onProgress, List<string> warnings) {
			Directory.CreateDirectory(LibraryPath);

			// archives put into the folder by hand
			foreach (var archive in Directory.GetFiles(LibraryPath).Where(IsArchive)) {
				var name = Path.GetFileNameWithoutExtension(archive);
				var folder = Path.Combine(LibraryPath, name);
				if (Directory.Exists(folder)) continue;

				onProgress?.Invoke(Path.GetFileName(archive));
				try {
					Extract(archive, folder);
					LayOutLooseScripts(folder);
					AssignNexusFromFileName(folder, name);
				} catch (Exception ex) {
					warnings.Add($"Couldn't extract {Path.GetFileName(archive)}: {ex.Message}");
					try { FileUtils.DeleteDirectory(folder); } catch {}
				}
			}

			var result = new List<ModEntry>();
			foreach (var folder in Directory.GetDirectories(LibraryPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
				var name = Path.GetFileName(folder);
				if (name.StartsWith(".")) continue; // ".adding_*" temporary folders

				onProgress?.Invoke(name);
				try {
					result.AddRange(ScanEntry(folder, name));
				} catch (Exception ex) {
					warnings.Add($"{name}: {ex.Message}");
				}
			}

			return result;
		}

		/// <summary>Mods whose files are in the folder, plus the installed ones its manifest remembers. Updates the manifest.</summary>
		private static List<ModEntry> ScanEntry(string folder, string entry) {
			var result = new List<ModEntry>();
			var manifest = LibraryManifest.Load(folder);
			var detection = ModDetector.Detect(folder, entry);
			var known = new List<ManifestMod>();
			var added = manifest.Added ?? Directory.GetCreationTimeUtc(folder); // older manifests: the folder's date

			foreach (var found in detection.Mods) {
				var path = FileUtils.NormalizeRelative(found.RelativeRoot);
				var m = manifest.Find(path) ?? new ManifestMod() { Path = path };
				m.Type = found.Type;
				m.Name = found.Name;
				m.Folder = found.RelativeRoot.Length > 0 ? Path.GetFileName(found.RelativeRoot) : (m.Folder ?? entry);
				m.Description = found.Description;
				m.ShowcaseName = found.ShowcaseName;
				m.Files = found.Files.Count > 0 ? found.Files : null;
				m.FilesRoot = found.Files.Count > 0 ? FileUtils.NormalizeRelative(found.FilesRoot) : null;
				m.InstalledGame = null; // its files are here, so it's not installed
				m.InstalledPath = null;
				known.Add(m);
			}

			// installed mods: only the manifest knows them
			foreach (var m in manifest.Mods) {
				if (!m.IsInstalled || known.Any(k => string.Equals(k.Path, m.Path, StringComparison.OrdinalIgnoreCase))) continue;

				// imported before these were read from the game folder: read them there now
				if (m.Type == ModType.DLC && (m.Description == null || m.ShowcaseName == null)) {
					var installed = Path.Combine(m.InstalledGame, m.InstalledPath.Replace('/', '\\'));
					if (Directory.Exists(installed)) {
						m.Description = ModDetector.DescribeDlcFolder(installed, out var showcaseName);
						m.ShowcaseName = showcaseName;
					}
				}
				known.Add(m);
			}

			// the TFC Installer mods ship with nothing Overstrike-BAK can install; remember their Nexus page anyway
			var shares = known.Count > 1;
			foreach (var m in known) {
				result.Add(new ModEntry(entry, m.Path, m.Type, MakeDisplayName(entry, m, shares), m.Folder ?? entry) {
					Description = m.Description,
					ShowcaseName = m.ShowcaseName,
					GameFiles = m.Files ?? new List<string>(),
					GameFilesRoot = m.FilesRoot ?? m.Path,
					InstalledGame = m.InstalledGame,
					InstalledPath = m.InstalledPath,
					Nexus = m.Nexus ?? manifest.Mods.FirstOrDefault(x => x.Nexus != null)?.Nexus,
					SharesEntry = shares,
					Added = added,
				});
			}

			// keep the manifest in step with the folder
			var nexus = manifest.Mods.FirstOrDefault(x => x.Nexus != null)?.Nexus;
			foreach (var m in known) m.Nexus ??= nexus;
			var updated = new LibraryManifest() { Mods = known, Added = added };
			if (known.Count > 0 || File.Exists(Path.Combine(folder, LibraryManifest.FileName))) {
				updated.Save(folder);
			}

			return result;
		}

		private static string MakeDisplayName(string entry, ManifestMod m, bool shares) {
			var name = m.Name ?? (m.Path.Length > 0 ? Path.GetFileName(m.Path) : entry);
			if (shares && !name.Equals(entry, StringComparison.OrdinalIgnoreCase)) name = $"{entry} / {name}";
			return name;
		}

		/// <summary>Saves a mod's install location and Nexus info into its library folder's manifest.</summary>
		public static void UpdateManifest(ModEntry mod) {
			var folder = GetEntryFolder(mod.Entry);
			var manifest = LibraryManifest.Load(folder);
			var m = manifest.Find(mod.PathInEntry);
			if (m == null) {
				m = new ManifestMod() { Path = mod.PathInEntry };
				manifest.Mods.Add(m);
			}

			m.Type = mod.Type;
			m.Name = mod.LocalName;
			m.Folder = mod.FolderName;
			m.Description = mod.Description;
			m.ShowcaseName = mod.ShowcaseName;
			m.Files = mod.GameFiles.Count > 0 ? mod.GameFiles : null;
			m.FilesRoot = mod.GameFiles.Count > 0 ? mod.GameFilesRoot : null;
			m.InstalledGame = mod.InstalledGame;
			m.InstalledPath = mod.InstalledPath;
			m.Nexus = mod.Nexus;
			manifest.Save(folder);
		}

		/// <summary>Sets the Nexus page of every mod in a library folder (they come from the same download).</summary>
		public static void SetNexusForEntry(string entry, NexusInfo nexus) {
			var folder = GetEntryFolder(entry);
			var manifest = LibraryManifest.Load(folder);
			foreach (var m in manifest.Mods) m.Nexus = nexus;
			manifest.Save(folder);
		}

		#endregion
		#region adding

		public class AddResult {
			public List<string> AddedFolders = new();
			public List<string> ReplacedFolders = new(); // a mod with the same folder name was there: this is its new version
			public List<string> TfcInstallerMods = new();
			public List<string> ResoRepMods = new();
			public List<string> Unsupported = new();
			public List<string> Errors = new();

			/// <summary>Before replacing a library folder: returns why it can't be replaced, or null.</summary>
			public Func<string, string> CheckReplace;

			/// <summary>Once the new version is in the library: takes the old version's installed copies out of the game.</summary>
			public Action<string> RemoveOldVersion;
		}

		/// <summary>
		/// Copies a folder, or extracts an archive, into the library. TFC Installer and ResoRep mods aren't added. A mod with the
		/// same folder name is replaced by it completely (it's a new version of that mod). Returns true if a mod from it
		/// is now in the library.
		/// </summary>
		public static bool Add(string source, AddResult result, Action<string> onProgress = null) {
			var sourceName = Path.GetFileName(source.TrimEnd('\\', '/'));
			var isDirectory = Directory.Exists(source);

			if (!isDirectory && !IsArchive(source)) {
				result.Unsupported.Add(sourceName);
				return false;
			}

			Directory.CreateDirectory(LibraryPath);
			var temp = Path.Combine(LibraryPath, TempPrefix + Guid.NewGuid().ToString("N"));

			try {
				onProgress?.Invoke(sourceName);
				if (isDirectory) {
					FileUtils.CopyDirectory(source, temp);
				} else {
					Extract(source, temp);
				}
				LayOutLooseScripts(temp);

				var name = isDirectory ? sourceName : Path.GetFileNameWithoutExtension(source);
				var detection = ModDetector.Detect(temp, name);

				if (detection.IsTfcInstallerMod) {
					result.TfcInstallerMods.Add(sourceName);
				}
				if (detection.IsResoRepMod) {
					result.ResoRepMods.Add(sourceName);
				}

				if (detection.Mods.Count == 0) {
					if (!detection.IsTfcInstallerMod && !detection.IsResoRepMod) result.Unsupported.Add(sourceName);
					FileUtils.DeleteDirectory(temp);
					return false;
				}

				var entry = FileUtils.SanitizeFileName(CleanNexusFileName(name));
				var target = Path.Combine(LibraryPath, entry);
				if (Directory.Exists(target)) {
					return Replace(target, temp, name, result);
				}

				Directory.Move(temp, target);
				AssignNexusFromFileName(target, name);
				result.AddedFolders.Add(entry);
				return true;
			} catch (Exception ex) {
				result.Errors.Add($"{sourceName}: {ex.Message}");
				try { FileUtils.DeleteDirectory(temp); } catch {}
				return false;
			}
		}

		/// <summary>Puts a new version (in 'temp') in place of the library folder 'target'. The old one is deleted only once the new one is in.</summary>
		private static bool Replace(string target, string temp, string sourceName, AddResult result) {
			var entry = Path.GetFileName(target);
			var problem = result.CheckReplace?.Invoke(entry);
			if (problem != null) {
				result.Errors.Add(problem);
				FileUtils.DeleteDirectory(temp);
				return false;
			}

			var old = LibraryManifest.Load(target);
			var oldNexus = old.Mods.FirstOrDefault(m => m.Nexus != null)?.Nexus;

			var previous = Path.Combine(LibraryPath, TempPrefix + Guid.NewGuid().ToString("N"));
			Directory.Move(target, previous);
			try {
				Directory.Move(temp, target);
			} catch {
				Directory.Move(previous, target);
				throw;
			}

			result.RemoveOldVersion?.Invoke(entry);
			try { FileUtils.DeleteDirectory(previous); } catch {}

			// keep its Nexus page and the date it was first added. The version is the download's, or else taken to be
			// the latest one
			var id = Nexus.ParseModIdFromFileName(sourceName);
			var version = Nexus.ParseVersionFromFileName(sourceName);
			NexusInfo nexus = null;
			if (oldNexus != null && (id == 0 || id == oldNexus.ModId)) {
				nexus = new NexusInfo() {
					ModId = oldNexus.ModId, Name = oldNexus.Name, Author = oldNexus.Author, Version = oldNexus.Version,
					Summary = oldNexus.Summary, Fetched = oldNexus.Fetched, InstalledVersion = version ?? oldNexus.Version,
				};
			} else if (id != 0) {
				nexus = new NexusInfo() { ModId = id, InstalledVersion = version };
			}

			var manifest = new LibraryManifest() { Added = old.Added ?? DateTime.UtcNow };
			if (nexus != null) manifest.Mods.Add(new ManifestMod() { Path = "", Name = entry, Nexus = nexus });
			manifest.Save(target);

			result.ReplacedFolders.Add(entry);
			return true;
		}

		/// <summary>
		/// A download with nothing but BmSDK .cs scripts (and maybe a readme): they belong in BmGame\Scripts, so they're
		/// moved into BmGame\Scripts\ inside the folder, where ModDetector finds them.
		/// </summary>
		private static void LayOutLooseScripts(string folder) {
			if (ModDetector.Detect(folder, Path.GetFileName(folder)).Mods.Count > 0) return;

			var files = FileUtils.EnumerateFilesSafe(folder).Where(f => !Path.GetFileName(f).Equals(LibraryManifest.FileName, StringComparison.OrdinalIgnoreCase)).ToList();
			var scripts = files.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();
			if (scripts.Count == 0 || files.Except(scripts).Any(f => !ModDetector.IsDocument(f))) return;
			if (scripts.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != scripts.Count) return; // two scripts with one name

			var target = Path.Combine(folder, "BmGame", "Scripts");
			Directory.CreateDirectory(target);
			foreach (var script in scripts) File.Move(script, Path.Combine(target, Path.GetFileName(script)));

			// folders left empty
			foreach (var dir in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length)) {
				if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
			}
		}

		/// <summary>"Mod Name-1234-1-0-1699999999" -> "Mod Name".</summary>
		private static string CleanNexusFileName(string name) {
			if (Nexus.ParseModIdFromFileName(name) == 0) return name;
			var index = name.IndexOf('-');
			var cleaned = name.Substring(0, index).Trim();
			return cleaned.Length > 0 ? cleaned : name;
		}

		private static void AssignNexusFromFileName(string folder, string fileName) {
			var id = Nexus.ParseModIdFromFileName(fileName);
			if (id == 0) return;

			var detection = ModDetector.Detect(folder, Path.GetFileName(folder));
			var manifest = LibraryManifest.Load(folder);
			foreach (var found in detection.Mods) {
				var path = FileUtils.NormalizeRelative(found.RelativeRoot);
				var m = manifest.Find(path);
				if (m == null) {
					m = new ManifestMod() { Path = path, Type = found.Type, Name = found.Name };
					manifest.Mods.Add(m);
				}
				m.Nexus ??= new NexusInfo() { ModId = id, InstalledVersion = Nexus.ParseVersionFromFileName(fileName) };
			}
			manifest.Save(folder);
		}

		private static void Extract(string archivePath, string destination) {
			Directory.CreateDirectory(destination);
			var root = Path.GetFullPath(destination);

			using var file = File.OpenRead(archivePath);
			using var archive = ArchiveFactory.OpenArchive(file);
			foreach (var entry in archive.Entries) {
				if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key)) continue;

				var target = Path.GetFullPath(Path.Combine(root, entry.Key.Replace('/', '\\').TrimStart('\\')));
				if (!FileUtils.IsInside(target, root)) continue; // no "..\" tricks

				Directory.CreateDirectory(Path.GetDirectoryName(target));
				using var input = entry.OpenEntryStream();
				using var output = File.Create(target);
				input.CopyTo(output);
			}
		}

		#endregion
	}
}

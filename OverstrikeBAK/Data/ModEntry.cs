// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Mods;
using OverstrikeBAK.Utils;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;

namespace OverstrikeBAK.Data {
	public enum ModType {
		DLC,            // DLC\<number>\<ModFolder>: add-on skins for Batman, the Batmobile and the other characters
		SCRIPT,         // BmGame\Mods\<ModFolder>: BmSDK script mods (folder with mod.toml)
		TFC_INSTALLER,  // .upk.PackagePatch / .TFCMapping / loose .tfc: not supported, installed with TFC Installer
	}

	public class ModEntry: INotifyPropertyChanged {
		public event PropertyChangedEventHandler PropertyChanged;

		private void Notify(params string[] names) {
			foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}

		// identity: "<library folder>/<path inside it>", forward slashes
		public string Path { get; set; }
		public string Entry { get; set; }        // library folder name
		public string PathInEntry { get; set; }  // "" = the library folder itself
		public ModType Type { get; set; }

		public string LocalName { get; set; }
		public string FolderName { get; set; }   // folder name in the game
		public string Description { get; set; }
		// DLC mods adding one item: its name in the Suits Menu
		private string _showcaseName;
		public string ShowcaseName {
			get => _showcaseName;
			set {
				_showcaseName = value;
				Notify(nameof(ShowcaseName), nameof(Name));
			}
		}

		// another mod is linked to a page with the same title (variants of one suit, a pack...)
		private bool _nexusNameShared;
		public bool NexusNameShared {
			get => _nexusNameShared;
			set {
				if (_nexusNameShared == value) return;
				_nexusNameShared = value;
				Notify(nameof(NexusNameShared), nameof(Name));
			}
		}
		public bool SharesEntry { get; set; }    // the library folder holds several mods
		public System.DateTime Added { get; set; } // when its library folder was added (UTC), for sorting

		// where it is now
		public string InstalledGame { get; set; }
		public string InstalledPath { get; set; }

		public bool IsInstalledIn(string gamePath) {
			return InstalledGame != null && InstalledPath != null && FileUtils.SamePath(InstalledGame, gamePath);
		}

		// Nexus Mods
		private NexusInfo _nexus;
		public NexusInfo Nexus {
			get => _nexus;
			set {
				_nexus = value;
				Notify(nameof(Nexus), nameof(Name), nameof(ToolTip), nameof(NexusVisibility), nameof(HasNexus), nameof(UpdateAvailable), nameof(UpdateVisibility), nameof(UpdateToolTip));
			}
		}

		/// <summary>Nexus Mods has a newer version than the one in Overstrike-BAK.</summary>
		public bool UpdateAvailable => _nexus != null && _nexus.UpdateAvailable;

		public Visibility UpdateVisibility => UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

		public string UpdateToolTip => UpdateAvailable ? $"Version {_nexus.Version} is on Nexus Mods (you have {_nexus.InstalledVersion}). Click to open the page, then add the download here: it replaces this version." : null;

		public bool HasNexus => _nexus != null;

		public Visibility NexusVisibility => _nexus != null ? Visibility.Visible : Visibility.Collapsed;

		/// <summary>
		/// The Nexus Mods title when it's linked, unless other mods have the same title (one library folder with
		/// several mods, or variants of a suit on one page): then a DLC mod adding one suit or Batmobile is named like
		/// in the Suits Menu (and the game), anything else by its own name.
		/// </summary>
		public string Name => !SharesEntry && !_nexusNameShared && !string.IsNullOrEmpty(_nexus?.Name) ? _nexus.Name
			: Type == ModType.DLC && !string.IsNullOrEmpty(_showcaseName) ? _showcaseName
			: LocalName;

		// the tick: what should be installed after Apply
		private bool _install;
		public bool Install {
			get => _install;
			set {
				if (_install == value) return;
				_install = value;
				Notify(nameof(Install));
			}
		}

		private string _status = "";
		public string Status {
			get => _status;
			set {
				_status = value;
				Notify(nameof(Status));
			}
		}

		/// <summary>"" normal, "pending" (changes on Apply), "problem".</summary>
		private string _statusKind = "";
		public string StatusKind {
			get => _statusKind;
			set {
				_statusKind = value;
				Notify(nameof(StatusKind));
			}
		}

		public bool IsSupported => Type != ModType.TFC_INSTALLER;

		/// <summary>The Showcase page a DLC mod adds items to, from its description ("Adds: X (Players)"), or null.</summary>
		public string ShowcasePage => Type != ModType.DLC || Description == null ? null
			: Description.Contains("(Players)") ? "Players"
			: Description.Contains("(Batmobiles)") ? "Batmobiles"
			: null;

		public string ShowInMenuHeader => ShowcasePage == "Batmobiles" ? "Show in Batmobiles menu" : "Show in Suits menu";

		public Visibility ShowInMenuVisibility => ShowcasePage != null ? Visibility.Visible : Visibility.Collapsed;

		// installed in the profile's game folder (only those are in its Suits Menu)
		private bool _installedHere;
		public bool InstalledHere {
			get => _installedHere;
			set {
				if (_installedHere == value) return;
				_installedHere = value;
				Notify(nameof(InstalledHere), nameof(ShowInMenuToolTip));
			}
		}

		public string ShowInMenuToolTip => _installedHere ? null : "Install it first (tick it and Apply)";

		public string ToolTip {
			get {
				var lines = new List<string>();
				if (_nexus != null && !string.IsNullOrEmpty(_nexus.Name)) {
					var by = string.IsNullOrEmpty(_nexus.Author) ? "" : $" by {_nexus.Author}";
					var installed = _nexus.InstalledVersion ?? _nexus.Version;
					var version = string.IsNullOrEmpty(installed) ? "" : $" v{installed}";
					lines.Add($"{_nexus.Name}{version}{by}");
					if (!string.IsNullOrEmpty(_nexus.Summary)) lines.Add(_nexus.Summary);
				}
				if (!string.IsNullOrEmpty(Description)) lines.Add(Description);
				if (Type == ModType.TFC_INSTALLER) lines.Add("TFC Installer mod: install it manually with TFC Installer.");
				lines.Add("Mods Library/" + Path);
				return string.Join("\n", lines);
			}
		}

		public string BadgeText => Type switch {
			ModType.DLC => "DLC",
			ModType.SCRIPT => "SDK",
			_ => "TFC",
		};

		public string BadgeColor => Type switch {
			ModType.DLC => "#1E6FB8",
			ModType.SCRIPT => "#D98200",
			_ => "#77838C",
		};

		public ModEntry(string entry, string pathInEntry, ModType type, string name, string folderName) {
			Entry = entry;
			PathInEntry = pathInEntry ?? "";
			Path = FileUtils.NormalizeRelative(PathInEntry.Length > 0 ? entry + "/" + PathInEntry : entry);
			Type = type;
			LocalName = name;
			FolderName = folderName;
		}
	}
}

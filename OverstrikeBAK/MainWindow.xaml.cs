// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Microsoft.Win32;
using OverstrikeBAK.Data;
using OverstrikeBAK.Game;
using OverstrikeBAK.Mods;
using OverstrikeBAK.Suits;
using OverstrikeBAK.Tabs;
using OverstrikeBAK.Utils;
using OverstrikeBAK.Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace OverstrikeBAK {
	public partial class MainWindow: Window, ISuitsHost {
		private readonly AppSettings _settings;
		private List<Profile> _profiles;
		private Profile _selectedProfile;

		private List<ModEntry> _libraryMods = new();
		private readonly ObservableCollection<ModEntry> _modsList = new();

		private Deployments _deployments;
		private Deployer _deployer;

		private class ProfileItem {
			public string Text { get; set; }
			public Profile Profile { get; set; }
		}

		private readonly ObservableCollection<ProfileItem> _profilesItems = new();

		private bool _statusMessageErrorShown = false;
		private int _busy = 0;
		private bool _propagatingInstallChange = false;
		private bool _reactToSettingsChanges = false;

		private string GamePath => _selectedProfile.GamePath;

		public MainWindow(AppSettings settings, List<Profile> profiles, bool firstLaunch) {
			InitializeComponent();

			_settings = settings;
			_profiles = profiles;

			AboutText.Text = $"Overstrike-BAK v{App.Version} by excinus";
			Title = "Overstrike-BAK";

			ModsList.ItemsSource = _modsList;
			ApplySort();
			SuitsMenuContent.Init(this);
			MakeProfileItems();

			Loaded += async (s, e) => await Startup(firstLaunch);
		}

		private async Task Startup(bool firstLaunch) {
			await ScanLibrary();

			var profile = _profiles.FirstOrDefault(p => p.Name == _settings.CurrentProfile) ?? _profiles[0];
			SwitchToProfile(profile);

			if (firstLaunch) {
				await OfferImport(true);
			}

			RefreshNexusInfo();
		}

		#region ISuitsHost

		public void ShowModInList(string modPath) {
			var mod = _modsList.FirstOrDefault(m => m.Path.Equals(modPath, StringComparison.OrdinalIgnoreCase));
			if (mod == null) {
				ShowStatusMessage("That mod isn't in the Mods Library.");
				return;
			}

			MainTabs.SelectedItem = ModsTab;
			if (!VisibleMods.Contains(mod)) FilterTextBox.Text = ""; // the search hides it
			ModsList.SelectedItems.Clear();
			ModsList.SelectedItems.Add(mod);
			ModsList.ScrollIntoView(mod);
			ModsList.Focus();
		}

		public void OnSuitRenamed(IEnumerable<string> modPaths, string oldName, string newName) {
			var paths = new HashSet<string>(modPaths, StringComparer.OrdinalIgnoreCase);
			foreach (var mod in _libraryMods.Where(m => paths.Contains(m.Path) && m.ShowcaseName == oldName)) {
				mod.ShowcaseName = newName;
				try { ModsLibrary.UpdateManifest(mod); } catch {}
			}
		}

		public Profile Profile => _selectedProfile;
		public Deployments Deployments => _deployments;

		public string ModName(string modPath) {
			return _libraryMods.FirstOrDefault(m => m.Path.Equals(modPath, StringComparison.OrdinalIgnoreCase))?.Name;
		}

		/// <summary>Saves the profile's settings and suit order (the ticks are saved by Apply).</summary>
		public void SaveProfile() {
			_selectedProfile.Save();
		}

		public void ShowStatus(string text, bool error = false) {
			if (error) ShowStatusMessageError(text);
			else ShowStatusMessage(text);
		}

		public Task<bool> RunTask(string header, Action<Action<string>> work) {
			return RunTaskAsync(header, progress => {
				work(progress);
				return Task.CompletedTask;
			});
		}

		/// <summary>Runs 'work' off the UI thread with the overlay shown. Errors are logged and reported. Returns true on success.</summary>
		public async Task<bool> RunTaskAsync(string header, Func<Action<string>, Task> work) {
			++_busy;
			Overlay.Visibility = Visibility.Visible;
			OverlayHeaderLabel.Text = header;
			OverlayOperationLabel.Text = "";

			void Progress(string text) {
				Dispatcher.BeginInvoke(() => OverlayOperationLabel.Text = text);
			}

			try {
				await Task.Run(() => work(Progress));
				return true;
			} catch (Exception ex) {
				App.LogError(header, ex);
				ShowStatusMessageError("Something went wrong. Click here for details.");
				new ErrorLogWindow() { Owner = this }.ShowDialog();
				return false;
			} finally {
				--_busy;
				if (_busy == 0) Overlay.Visibility = Visibility.Collapsed;
			}
		}

		#endregion
		#region profiles

		private void MakeProfileItems() {
			_profilesItems.Clear();
			foreach (var profile in _profiles) {
				_profilesItems.Add(new ProfileItem() { Text = profile.Name, Profile = profile });
			}
			_profilesItems.Add(new ProfileItem() { Text = "Add new profile...", Profile = null });

			ProfileComboBox.ItemsSource = new CompositeCollection {
				new CollectionContainer() { Collection = _profilesItems }
			};
		}

		private void UpdateSelectedProfileItem() {
			ProfileComboBox.SelectedItem = _profilesItems.FirstOrDefault(i => i.Profile == _selectedProfile);
		}

		private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if (e.AddedItems.Count <= 0) return;

			var item = (ProfileItem)e.AddedItems[0];
			if (item.Profile == _selectedProfile) return;

			if (!ConfirmDiscardPending() || !SuitsMenuContent.ConfirmDiscardChanges()) {
				UpdateSelectedProfileItem();
				return;
			}

			if (item.Profile == null) {
				var window = new CreateProfile() { Owner = this };
				window.ShowDialog();

				var p = window.GetProfile();
				if (p == null || !p.Save()) {
					UpdateSelectedProfileItem();
					return;
				}

				_profiles = ((App)Application.Current).ReloadProfiles();
				MakeProfileItems();
				SwitchToProfile(_profiles.FirstOrDefault(x => x.Name == p.Name) ?? _selectedProfile);
				return;
			}

			SwitchToProfile(item.Profile);
		}

		/// <summary>Shows the profile's ticks and settings. Differences with the game become pending changes (Apply).</summary>
		private void SwitchToProfile(Profile profile) {
			_selectedProfile = profile;
			UpdateSelectedProfileItem();

			_settings.CurrentProfile = profile.Name;
			((App)Application.Current).WriteSettings();

			ProfileGamePath.Text = profile.GamePath;
			_deployments = Deployments.Load(profile.GamePath);
			_deployments.SetMods(_libraryMods);
			_deployer = new Deployer(profile.GamePath);

			MakeModsItems();
			UpdateSettingsTab();
			SuitsMenuContent.RequestReload();

			if (!GameInstall.IsGameInstallation(profile.GamePath)) {
				Dialog.Warning($"Batman: Arkham Knight isn't in this folder anymore:\n{profile.GamePath}");
			}
		}

		#endregion
		#region mods list

		private async Task ScanLibrary() {
			var warnings = new List<string>();
			List<ModEntry> mods = null;

			await RunTask("Scanning the Mods Library...", progress => {
				mods = ModsLibrary.Scan(progress, warnings);
			});

			if (mods != null) _libraryMods = mods;
			_deployments?.SetMods(_libraryMods);

			if (warnings.Count > 0) {
				Dialog.Warning(string.Join("\n", warnings));
			}
		}

		private void MakeModsItems() {
			foreach (var mod in _modsList) mod.PropertyChanged -= OnModPropertyChanged;
			_modsList.Clear();

			var ticks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach (var mod in _selectedProfile.Mods) {
				ticks[mod.Path] = mod.Install;
			}

			foreach (var mod in _libraryMods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)) {
				mod.PropertyChanged -= OnModPropertyChanged;
				// mods the profile doesn't know yet keep their current state
				mod.Install = mod.IsSupported && (ticks.TryGetValue(mod.Path, out var install) ? install : mod.IsInstalledIn(GamePath));
				mod.PropertyChanged += OnModPropertyChanged;
				_modsList.Add(mod);
			}

			UpdateNexusNameConflicts();
			UpdateModStatuses();
			UpdateFilter();
		}

		/// <summary>Mods linked to pages with the same title don't use it as their name: they'd look like duplicates.</summary>
		private void UpdateNexusNameConflicts() {
			var counts = _libraryMods.Where(m => !string.IsNullOrEmpty(m.Nexus?.Name))
				.GroupBy(m => m.Nexus.Name.Trim(), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
			foreach (var mod in _libraryMods) {
				mod.NexusNameShared = !string.IsNullOrEmpty(mod.Nexus?.Name) && counts[mod.Nexus.Name.Trim()] > 1;
			}
		}

		private bool IsPendingInstall(ModEntry m) => m.IsSupported && m.Install && !m.IsInstalledIn(GamePath) && m.InstalledGame == null;
		private bool IsPendingRemoval(ModEntry m) => !m.Install && m.IsInstalledIn(GamePath);

		private bool IsSuitOrderPending() {
			if (_deployments == null) return false;
			return _selectedProfile.SuitOrder == null
				? _deployments.SuitOrderProfile != null
				: _deployments.SuitOrderProfile != _selectedProfile.Name;
		}

		private int PendingCount => _modsList.Count(m => IsPendingInstall(m) || IsPendingRemoval(m)) + (IsSuitOrderPending() ? 1 : 0);

		private void UpdateModStatuses() {
			var bmSdkInstalled = BmSdk.IsInstalled(GamePath);

			foreach (var mod in _modsList) {
				var here = mod.IsInstalledIn(GamePath);
				mod.InstalledHere = here;

				if (!mod.IsSupported) {
					(mod.Status, mod.StatusKind) = ("Install it with TFC Installer", "");
				} else if (mod.InstalledGame != null && !here) {
					(mod.Status, mod.StatusKind) = ("Installed in another game folder", "problem");
				} else if (IsPendingInstall(mod)) {
					(mod.Status, mod.StatusKind) = ("Will be installed", "pending");
				} else if (IsPendingRemoval(mod)) {
					(mod.Status, mod.StatusKind) = ("Will be removed", "pending");
				} else if (here && !Directory.Exists(Path.Combine(GamePath, mod.InstalledPath))) {
					(mod.Status, mod.StatusKind) = ("Missing from the game folder", "problem");
				} else if (here && mod.Type == ModType.SCRIPT && !bmSdkInstalled) {
					(mod.Status, mod.StatusKind) = (mod.InstalledPath.Replace('/', '\\') + " · BmSDK not installed", "problem");
				} else if (here) {
					(mod.Status, mod.StatusKind) = (mod.InstalledPath.Replace('/', '\\'), "");
				} else {
					(mod.Status, mod.StatusKind) = ("", "");
				}
			}

			var installed = _modsList.Count(m => m.IsInstalledIn(GamePath));
			ModsCountLabel.Text = _modsList.Count == 0 ? "" : $"{installed} of {_modsList.Count} installed";

			var pending = PendingCount;
			PendingLabel.Text = pending == 0 ? "" : (pending == 1 ? "1 change to apply" : $"{pending} changes to apply");
			ApplyButton.IsEnabled = pending > 0;
			RevertButton.IsEnabled = _modsList.Any(m => IsPendingInstall(m) || IsPendingRemoval(m));
			UpdateCheckAllButton();
		}

		private void OnModPropertyChanged(object sender, PropertyChangedEventArgs e) {
			if (e.PropertyName != nameof(ModEntry.Install)) return;
			if (_propagatingInstallChange) return;

			var changed = (ModEntry)sender;
			var affected = new List<ModEntry> { changed };

			// ticking one of several selected mods ticks all of them
			_propagatingInstallChange = true;
			if (ModsList.SelectedItems.Contains(changed)) {
				foreach (ModEntry item in ModsList.SelectedItems) {
					if (item == changed) continue;
					item.Install = changed.Install;
					affected.Add(item);
				}
			}

			var tfc = affected.Where(m => !m.IsSupported && m.Install).ToList();
			var elsewhere = affected.Where(m => m.Install && m.InstalledGame != null && !m.IsInstalledIn(GamePath)).ToList();
			foreach (var mod in tfc.Concat(elsewhere)) mod.Install = false;
			_propagatingInstallChange = false;

			UpdateModStatuses();

			if (tfc.Count > 0) ShowTfcInstallerMessage(tfc.Select(m => m.Name));
			if (elsewhere.Count > 0) Dialog.Warning($"{elsewhere[0].Name} is installed in another game folder:\n{elsewhere[0].InstalledGame}");
		}

		private static void ShowTfcInstallerMessage(IEnumerable<string> names) {
			var list = string.Join("\n", names);
			Dialog.Warning($"{list}\n\nThis is a TFC Installer mod. Install it manually with [TFC Installer]({ModsLibrary.TfcInstallerUrl}).", "TFC Installer mod");
		}

		private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e) {
			UpdateFilter();
		}

		private void UpdateFilter() {
			var view = CollectionViewSource.GetDefaultView(_modsList);
			var words = FilterTextBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (words.Length == 0) {
				view.Filter = null;
			} else {
				// the name shown, the mod's own (folder) name or its Nexus Mods name
				view.Filter = (item) => {
					var mod = (ModEntry)item;
					return words.All(w => mod.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
						|| mod.LocalName.Contains(w, StringComparison.OrdinalIgnoreCase)
						|| (mod.Nexus?.Name?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false));
				};
			}
			UpdateCheckAllButton();
		}

		private async void RefreshButton_Click(object sender, RoutedEventArgs e) {
			await RefreshMods();
		}

		/// <summary>Rescans the library, keeping the ticks the user set but didn't apply yet.</summary>
		private async Task RefreshMods() {
			var ticks = _modsList.ToDictionary(m => m.Path, m => m.Install, StringComparer.OrdinalIgnoreCase);

			await ScanLibrary();
			MakeModsItems();

			_propagatingInstallChange = true;
			foreach (var mod in _modsList) {
				if (ticks.TryGetValue(mod.Path, out var install) && mod.IsSupported) mod.Install = install;
			}
			_propagatingInstallChange = false;

			UpdateModStatuses();
			ResortIfByChecked();
			SuitsMenuContent.RequestReload();
		}

		#endregion
		#region check all, sorting, mod profiles

		/// <summary>The mods the list shows right now (filtered and sorted).</summary>
		private List<ModEntry> VisibleMods => CollectionViewSource.GetDefaultView(_modsList).Cast<ModEntry>().ToList();

		/// <summary>Can be ticked: not a TFC Installer mod, not installed in another game folder.</summary>
		private bool CanTick(ModEntry m) => m.IsSupported && !(m.InstalledGame != null && !m.IsInstalledIn(GamePath));

		private void UpdateCheckAllButton() {
			var visible = VisibleMods;
			var anyChecked = visible.Any(m => m.Install);
			CheckAllButton.Content = anyChecked ? "Uncheck all" : "Check all";
			CheckAllButton.IsEnabled = anyChecked || visible.Any(CanTick);
			CheckAllButton.ToolTip = FilterTextBox.Text.Trim() == "" ? null : "Only the mods the search shows";
		}

		private void CheckAllButton_Click(object sender, RoutedEventArgs e) {
			var visible = VisibleMods;
			var check = !visible.Any(m => m.Install);

			_propagatingInstallChange = true;
			foreach (var mod in visible) {
				mod.Install = check && CanTick(mod);
			}
			_propagatingInstallChange = false;

			UpdateModStatuses();
			ResortIfByChecked();
		}

		/// <summary>The sort modes: arrow up is the first way, arrow down (choosing it again) the other.</summary>
		private static readonly (string key, string label, string up, string down)[] SortModes = {
			(AppSettings.ModsSortName, "Name", "A to Z", "Z to A"),
			(AppSettings.ModsSortAdded, "Date added", "newest first", "oldest first"),
			(AppSettings.ModsSortChecked, "Checked", "checked first", "unchecked first"),
			(AppSettings.ModsSortType, "Type", "DLC mods first", "SDK mods first"),
		};

		private void SortButton_Click(object sender, RoutedEventArgs e) {
			var menu = SortButton.ContextMenu;
			menu.Items.Clear();

			foreach (var (key, label, up, down) in SortModes) {
				var active = key == _settings.ModsSort;
				var reversed = active && _settings.ModsSortReversed;
				var item = new MenuItem() {
					Header = active ? $"{label}  {(reversed ? "↓" : "↑")}" : label,
					IsChecked = active,
					ToolTip = active ? $"{(reversed ? down : up)}. Choose it again for {(reversed ? up : down)}." : $"{up} (choose it again for {down})",
				};
				item.Click += (s, a) => {
					_settings.ModsSortReversed = key == _settings.ModsSort && !_settings.ModsSortReversed;
					_settings.ModsSort = key;
					((App)Application.Current).WriteSettings();
					ApplySort();
				};
				menu.Items.Add(item);
			}

			menu.PlacementTarget = SortButton;
			menu.Placement = PlacementMode.Bottom;
			menu.IsOpen = true;
		}

		private void ApplySort() {
			var mode = SortModes.FirstOrDefault(m => m.key == _settings.ModsSort);
			if (mode.key == null) mode = SortModes[0];
			var reversed = _settings.ModsSortReversed;
			var up = reversed ? ListSortDirection.Descending : ListSortDirection.Ascending;
			var down = reversed ? ListSortDirection.Ascending : ListSortDirection.Descending;

			var view = CollectionViewSource.GetDefaultView(_modsList);
			using (view.DeferRefresh()) {
				view.SortDescriptions.Clear();
				switch (mode.key) {
					case AppSettings.ModsSortAdded:
						view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.Added), down));
						break;
					case AppSettings.ModsSortChecked:
						view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.Install), down));
						break;
					case AppSettings.ModsSortType:
						// DLC before script mods (or the other way round); TFC Installer mods always last
						view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.IsSupported), ListSortDirection.Descending));
						view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.Type), up));
						break;
				}
				view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.Name), mode.key == AppSettings.ModsSortName ? up : ListSortDirection.Ascending));
			}

			SortButtonLabel.Text = $"Sort by: {mode.label} {(reversed ? "↓" : "↑")}";
			SortButton.ToolTip = reversed ? mode.down : mode.up;
		}

		/// <summary>
		/// "Checked first" isn't re-sorted on every tick (the row would jump away from the mouse), only after
		/// actions that change many ticks at once.
		/// </summary>
		private void ResortIfByChecked() {
			if (_settings.ModsSort == AppSettings.ModsSortChecked) {
				CollectionViewSource.GetDefaultView(_modsList).Refresh();
			}
		}

		private void ModProfilesButton_Click(object sender, RoutedEventArgs e) {
			var menu = ModProfilesButton.ContextMenu;
			menu.Items.Clear();

			var names = ModProfiles.List();
			if (names.Count == 0) {
				menu.Items.Add(new MenuItem() { Header = "No presets yet", IsEnabled = false });
			}
			foreach (var name in names) {
				var item = new MenuItem() { Header = name, ToolTip = "Tick the mods of this preset (then Apply)" };
				item.Click += (s, a) => LoadModProfile(name);
				menu.Items.Add(item);
			}

			menu.Items.Add(new Separator());
			var save = new MenuItem() { Header = "Save current preset" };
			save.Click += (s, a) => SaveModProfile();
			menu.Items.Add(save);

			if (names.Count > 0) {
				var delete = new MenuItem() { Header = "Delete" };
				foreach (var name in names) {
					var item = new MenuItem() { Header = name };
					item.Click += (s, a) => DeleteModProfile(name);
					delete.Items.Add(item);
				}
				menu.Items.Add(delete);
			}

			menu.PlacementTarget = ModProfilesButton;
			menu.Placement = PlacementMode.Bottom;
			menu.IsOpen = true;
		}

		private void LoadModProfile(string name) {
			HashSet<string> ticked;
			try {
				ticked = ModProfiles.Load(name);
			} catch (Exception ex) {
				Dialog.Error($"Couldn't read the preset {name}: {ex.Message}");
				return;
			}

			_propagatingInstallChange = true;
			foreach (var mod in _modsList) {
				mod.Install = ticked.Contains(mod.Path) && CanTick(mod);
			}
			_propagatingInstallChange = false;

			UpdateModStatuses();
			ResortIfByChecked();

			var missing = ticked.Count(p => !_modsList.Any(m => m.Path.Equals(p, StringComparison.OrdinalIgnoreCase)));
			var pending = _modsList.Any(m => IsPendingInstall(m) || IsPendingRemoval(m));
			var text = $"Loaded '{name}'." + (pending ? " Press Apply to switch to it." : " It matches what's installed.");
			if (missing > 0) text += missing == 1 ? " 1 of its mods isn't in the library." : $" {missing} of its mods aren't in the library.";
			ShowStatusMessage(text);
		}

		private void SaveModProfile() {
			var prompt = new TextPromptWindow("Save preset", "Preset name:", "", name => {
				if (name.Length == 0) return "Enter a name.";
				if (name.IndexOfAny(Path.GetInvalidFileNameChars()) != -1) return "The name can't contain \\ / : * ? \" < > |";
				return null;
			}) { Owner = this };

			if (prompt.ShowDialog() != true) return;

			var name = prompt.Value;
			if (ModProfiles.Exists(name) && !Dialog.Confirm($"Replace the preset {name}?", "Replace", "Cancel")) return;

			var ticked = _modsList.Where(m => m.Install && m.IsSupported).ToList();
			try {
				ModProfiles.Save(name, ticked);
			} catch (Exception ex) {
				Dialog.Error($"Couldn't save {name}: {ex.Message}");
				return;
			}

			ShowStatusMessage($"Preset '{name}' saved ({(ticked.Count == 1 ? "1 mod" : $"{ticked.Count} mods")}).");
		}

		private void DeleteModProfile(string name) {
			if (!Dialog.Confirm($"Delete the preset {name}?", "Delete", "Cancel", "Overstrike-BAK", DialogIcon.Warning)) return;

			try {
				ModProfiles.Delete(name);
			} catch (Exception ex) {
				Dialog.Error($"Couldn't delete {name}: {ex.Message}");
				return;
			}

			ShowStatusMessage($"Preset '{name}' deleted.");
		}

		#endregion
		#region apply

		private void RevertButton_Click(object sender, RoutedEventArgs e) {
			_propagatingInstallChange = true;
			foreach (var mod in _modsList) {
				mod.Install = mod.IsSupported && mod.IsInstalledIn(GamePath);
			}
			_propagatingInstallChange = false;
			UpdateModStatuses();
			ResortIfByChecked();
		}

		private async void ApplyButton_Click(object sender, RoutedEventArgs e) {
			await ApplyChanges();
		}

		/// <summary>Moves ticked mods into the game and unticked ones back into the library, then writes the suit order.</summary>
		private async Task<bool> ApplyChanges() {
			var toInstall = _modsList.Where(IsPendingInstall).ToList();
			var toRemove = _modsList.Where(IsPendingRemoval).ToList();
			var suitOrder = IsSuitOrderPending();
			if (toInstall.Count == 0 && toRemove.Count == 0 && !suitOrder) return true;

			if (GameInstall.IsRunning() && !Dialog.Confirm("The game is running, so its files may be locked.", "Apply anyway", "Cancel", icon: DialogIcon.Warning)) {
				return false;
			}

			if (toInstall.Any(m => m.Type == ModType.SCRIPT)) {
				if (!GameInstall.IsSteamInstallation(GamePath)) {
					Dialog.Warning($"Script mods need BmSDK, which only works with the Steam version of the game. {DescribeStore()}, so they won't run.");
				} else if (!BmSdk.IsInstalled(GamePath)) {
					if (Dialog.Confirm("Script mods need BmSDK, which isn't installed.", "Install BmSDK", "Skip", icon: DialogIcon.Warning)) {
						await InstallBmSdk();
					}
				}
			}

			var dlcFolder = _selectedProfile.Settings_DlcFolder;
			var failed = new List<string>();
			var installed = 0;
			var removed = 0;

			var ok = await RunTask("Applying changes...", progress => {
				foreach (var mod in toRemove) {
					progress($"Removing {mod.Name}...");
					try {
						_deployer.Uninstall(mod, file => progress($"Removing {mod.Name}: {file}"));
						++removed;
					} catch (Exception ex) {
						App.LogError($"Removing {mod.Name}", ex);
						failed.Add($"{mod.Name}: {ex.Message}");
					}
				}

				foreach (var mod in toInstall) {
					progress($"Installing {mod.Name}...");
					try {
						_deployer.Install(mod, dlcFolder, file => progress($"Installing {mod.Name}: {file}"));
						++installed;
					} catch (Exception ex) {
						App.LogError($"Installing {mod.Name}", ex);
						failed.Add($"{mod.Name}: {ex.Message}");
					}
				}

				_deployments.SetMods(_libraryMods);

				if (suitOrder || ((installed > 0 || removed > 0) && _selectedProfile.SuitOrder != null)) {
					WriteProfileSuitOrder(progress);
				}
			});

			SaveTicksToProfile();
			UpdateModStatuses();
			ResortIfByChecked();
			SuitsMenuContent.RequestReload();

			if (failed.Count > 0) {
				Dialog.Error("Some mods couldn't be moved:\n\n" + string.Join("\n", failed.Take(10)));
				return false;
			}

			if (ok) {
				var parts = new List<string>();
				if (installed > 0) parts.Add($"{installed} installed");
				if (removed > 0) parts.Add($"{removed} removed");
				if (parts.Count == 0) parts.Add("suit order updated");
				ShowStatusMessage("Done! " + string.Join(", ", parts) + ".");
			}
			return ok;
		}

		/// <summary>Writes the profile's suit order (fitted to what's installed now), or restores the original one. Runs off the UI thread.</summary>
		private void WriteProfileSuitOrder(Action<string> progress) {
			var profile = _selectedProfile;

			if (profile.SuitOrder == null) {
				if (_deployments.SuitOrderProfile != null) {
					progress("Restoring the original suit order...");
					SuitOrderWriter.RestoreOriginal(profile.GamePath);
					_deployments.SuitOrderProfile = null;
					_deployments.Save();
				}
				return;
			}

			progress("Writing the suit order...");
			var model = ShowcaseModel.Build(profile.GamePath, _deployments, ModName, false);
			SuitOrderWriter.Apply(model, SuitOrder.Resolve(model, profile.SuitOrder));
			_deployments.SuitOrderProfile = profile.Name;
			_deployments.Save();
		}

		private void SaveTicksToProfile() {
			// keep entries of mods missing from the library right now, so they come back if the mod does
			var known = new HashSet<string>(_modsList.Select(m => m.Path), StringComparer.OrdinalIgnoreCase);
			var mods = _selectedProfile.Mods.Where(m => !known.Contains(m.Path)).ToList();
			foreach (var mod in _modsList.Where(m => m.IsSupported)) {
				mods.Add(new ProfileMod() { Path = mod.Path, Install = mod.Install });
			}
			_selectedProfile.Mods = mods;
			_selectedProfile.Save();
		}

		/// <summary>Asks what to do with ticks that weren't applied. Returns false to stay.</summary>
		private bool ConfirmDiscardPending() {
			if (!_modsList.Any(m => IsPendingInstall(m) || IsPendingRemoval(m))) return true;

			var choice = Dialog.Show("You have changes that weren't applied.", "Overstrike-BAK", DialogIcon.Question, "Discard them", "Cancel");
			if (choice != 0) return false;

			RevertButton_Click(null, null);
			return true;
		}

		#endregion
		#region adding and deleting mods

		private void AddMods_Click(object sender, RoutedEventArgs e) {
			AddMods.ContextMenu.PlacementTarget = AddMods;
			AddMods.ContextMenu.Placement = PlacementMode.Bottom;
			AddMods.ContextMenu.IsOpen = true;
		}

		private async void AddArchives_Click(object sender, RoutedEventArgs e) {
			var dialog = new OpenFileDialog() {
				Title = "Add mods",
				Multiselect = true,
				Filter = "Mod archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|All files (*.*)|*.*",
			};
			if (dialog.ShowDialog(this) != true) return;

			await AddModsFrom(dialog.FileNames);
		}

		private async void AddFolders_Click(object sender, RoutedEventArgs e) {
			var dialog = new OpenFolderDialog() { Title = "Add mod folders", Multiselect = true };
			if (dialog.ShowDialog(this) != true) return;

			await AddModsFrom(dialog.FolderNames);
		}

		private void ModsList_DragEnter(object sender, DragEventArgs e) {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		}

		private async void ModsList_Drop(object sender, DragEventArgs e) {
			if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) {
				await AddModsFrom(paths);
			}
		}

		private async Task AddModsFrom(IEnumerable<string> paths) {
			var sources = paths.ToList();
			var reinstall = new List<(string entry, string pathInEntry, string installedPath)>();
			var dlcFolder = _selectedProfile.Settings_DlcFolder;

			var result = new ModsLibrary.AddResult() {
				// a new version of a mod that's installed in another game folder would leave that copy behind
				CheckReplace = entry => _libraryMods.FirstOrDefault(m => m.Entry.Equals(entry, StringComparison.OrdinalIgnoreCase) && m.InstalledGame != null && !m.IsInstalledIn(GamePath)) is { } elsewhere
					? $"{elsewhere.Name}: it's installed in another game folder ({elsewhere.InstalledGame})."
					: null,
				// the old version's files in the game go; the new one is installed in the same place below
				RemoveOldVersion = entry => {
					foreach (var mod in _libraryMods.Where(m => m.Entry.Equals(entry, StringComparison.OrdinalIgnoreCase) && m.IsInstalledIn(GamePath)).ToList()) {
						reinstall.Add((entry, mod.PathInEntry, mod.InstalledPath));
						_deployer.Delete(mod);
					}
				},
			};

			await RunTask("Adding mods...", progress => {
				foreach (var source in sources) {
					ModsLibrary.Add(source, result, name => progress($"Adding {name}..."));
				}
			});

			if (result.TfcInstallerMods.Count > 0) {
				ShowTfcInstallerMessage(result.TfcInstallerMods);
			}

			if (result.Unsupported.Count > 0) {
				Dialog.Warning("No DLC or script mod found in:\n" + string.Join("\n", result.Unsupported));
			}

			if (result.Errors.Count > 0) {
				Dialog.Error("Couldn't add:\n" + string.Join("\n", result.Errors));
			}

			if (result.AddedFolders.Count == 0 && result.ReplacedFolders.Count == 0) return;

			await RefreshMods();

			// updated mods that were installed: the new version goes where the old one was
			if (reinstall.Count > 0) {
				var reinstalled = new List<ModEntry>();
				await RunTask("Installing the new versions...", progress => {
					foreach (var (entry, pathInEntry, installedPath) in reinstall) {
						var inEntry = _libraryMods.Where(m => m.Entry.Equals(entry, StringComparison.OrdinalIgnoreCase) && m.IsSupported).ToList();
						var mod = inEntry.FirstOrDefault(m => m.PathInEntry.Equals(pathInEntry, StringComparison.OrdinalIgnoreCase));
						// the new version moved things around: one mod before, one mod now, so it's the same one
						if (mod == null && inEntry.Count == 1 && reinstall.Count(r => r.entry.Equals(entry, StringComparison.OrdinalIgnoreCase)) == 1) mod = inEntry[0];
						if (mod == null) continue; // the new version doesn't have it anymore
						progress($"Installing {mod.Name}...");
						_deployer.Install(mod, dlcFolder, null, installedPath);
						reinstalled.Add(mod);
					}
					_deployments.SetMods(_libraryMods);
					if (_selectedProfile.SuitOrder != null) WriteProfileSuitOrder(progress);
				});
				// installed means ticked (the rescan didn't know a mod whose path inside its folder changed)
				_propagatingInstallChange = true;
				foreach (var mod in reinstalled) mod.Install = true;
				_propagatingInstallChange = false;

				SaveTicksToProfile();
				UpdateModStatuses();
				SuitsMenuContent.RequestReload();
			}

			var entries = result.AddedFolders.Concat(result.ReplacedFolders).ToList();
			var added = _modsList.Where(m => entries.Contains(m.Entry, StringComparer.OrdinalIgnoreCase)).ToList();
			ModsList.SelectedItems.Clear();
			foreach (var mod in added) ModsList.SelectedItems.Add(mod);
			if (added.Count > 0) ModsList.ScrollIntoView(added[0]);

			var parts = new List<string>();
			if (result.AddedFolders.Count > 0) parts.Add($"{result.AddedFolders.Count} mod{(result.AddedFolders.Count == 1 ? "" : "s")} added (tick and Apply to install)");
			if (result.ReplacedFolders.Count > 0) parts.Add($"{result.ReplacedFolders.Count} updated");
			ShowStatusMessage(string.Join(", ", parts) + ".");
			RefreshNexusInfo();
		}

		private void ModsList_KeyUp(object sender, KeyEventArgs e) {
			if (e.Key == Key.Delete) DeleteSelectedMods();
		}

		private void DeleteMod_Click(object sender, RoutedEventArgs e) {
			SelectClickedMod(sender);
			DeleteSelectedMods();
		}

		private void SelectClickedMod(object sender) {
			if (((FrameworkElement)sender).DataContext is ModEntry mod && !ModsList.SelectedItems.Contains(mod)) {
				ModsList.SelectedItems.Clear();
				ModsList.SelectedItems.Add(mod);
			}
		}

		private async void DeleteSelectedMods() {
			var selected = ModsList.SelectedItems.Cast<ModEntry>().ToList();
			if (selected.Count == 0) return;

			// a library folder holds every mod of a download: deleting one deletes them all
			var entries = selected.Select(m => m.Entry).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var affected = _libraryMods.Where(m => entries.Contains(m.Entry, StringComparer.OrdinalIgnoreCase)).ToList();
			var installed = affected.Where(m => m.IsInstalledIn(GamePath)).ToList();

			var message = affected.Count == 1 ? $"Delete {affected[0].Name}?" : $"Delete {affected.Count} mods?";
			if (affected.Count > selected.Count) message += $" ({affected.Count - selected.Count} more share their folder.)";
			if (installed.Count > 0) message += "\nInstalled ones are deleted from the game too.";

			if (!Dialog.Confirm(message, "Delete", "Cancel", icon: DialogIcon.Warning)) return;

			var ok = await RunTask("Deleting mods...", progress => {
				foreach (var mod in installed) {
					progress($"Deleting {mod.Name} from the game...");
					_deployer.Delete(mod);
				}

				foreach (var entry in entries) {
					progress($"Deleting {entry}...");
					FileUtils.DeleteDirectory(ModsLibrary.GetEntryFolder(entry));
				}
			});

			await RefreshMods();
			if (ok) ShowStatusMessage($"Done! {affected.Count} mod{(affected.Count == 1 ? "" : "s")} deleted.");
		}

		private void ShowInSuitsMenu_Click(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is not ModEntry mod) return;
			MainTabs.SelectedItem = SuitsMenuTab;
			SuitsMenuContent.ShowMod(mod.Path);
		}

		private void OpenModFolder_Click(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is not ModEntry mod) return;

			if (mod.InstalledGame != null) {
				var folder = Path.Combine(mod.InstalledGame, mod.InstalledPath.Replace('/', '\\'));
				OpenFolder(Directory.Exists(folder) ? folder : mod.InstalledGame);
			} else {
				OpenFolder(ModsLibrary.GetModFolder(mod));
			}
		}

		private void OpenLibrary_Click(object sender, RoutedEventArgs e) {
			OpenFolder(ModsLibrary.LibraryPath);
		}

		#endregion
		#region Nexus Mods

		private void OpenNexus_Click(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is ModEntry { Nexus: not null } mod) {
				Dialog.OpenUrl(mod.Nexus.Url);
			}
		}

		private async void SetNexus_Click(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is not ModEntry mod) return;

			var initial = mod.Nexus?.Url ?? $"https://www.nexusmods.com/{Nexus.GameDomain}/mods/";
			var prompt = new TextPromptWindow("Nexus Mods page", "Link to the mod's Nexus Mods page:", initial,
				text => Nexus.ParseModId(text) == 0 ? "Paste a nexusmods.com/batmanarkhamknight/mods/... link." : null) { Owner = this };
			if (prompt.ShowDialog() != true) return;

			await FetchNexus(mod.Entry, Nexus.ParseModId(prompt.Value), true);
		}

		private async void RefreshNexus_Click(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is ModEntry { Nexus: not null } mod) {
				await FetchNexus(mod.Entry, mod.Nexus.ModId, true);
			}
		}

		private void RemoveNexus_Click(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is not ModEntry mod) return;
			SetEntryNexus(mod.Entry, null);
		}

		/// <summary>Reads the mod's info from Nexus Mods and stores it for every mod of the library folder.</summary>
		private async Task FetchNexus(string entry, int modId, bool showResult) {
			NexusInfo info = null;
			string error = null;

			await RunTaskAsync("Reading Nexus Mods...", async progress => {
				try {
					info = await Nexus.Fetch(modId);
				} catch (Exception ex) {
					error = ex.Message;
				}
			});

			var old = _libraryMods.FirstOrDefault(m => m.Entry.Equals(entry, StringComparison.OrdinalIgnoreCase))?.Nexus;
			if (info == null) {
				SetEntryNexus(entry, old?.ModId == modId ? old : new NexusInfo() { ModId = modId });
				if (showResult) Dialog.Warning($"Couldn't read the Nexus Mods page ({error}). The link was saved.");
				return;
			}

			SetEntryNexus(entry, Nexus.Merge(old, info));
			if (showResult) ShowStatusMessage($"Linked to {info.Name} on Nexus Mods.");
		}

		private void SetEntryNexus(string entry, NexusInfo info) {
			try {
				ModsLibrary.SetNexusForEntry(entry, info);
			} catch {}

			foreach (var mod in _libraryMods.Where(m => m.Entry.Equals(entry, StringComparison.OrdinalIgnoreCase))) {
				mod.Nexus = info;
			}
			UpdateNexusNameConflicts();
		}

		/// <summary>
		/// Quietly reads every linked mod from Nexus Mods (one request per 50 mods): fills in mods linked by their
		/// download's file name, and shows the update icon next to mods that have a newer version.
		/// </summary>
		private async void RefreshNexusInfo() {
			var entries = _libraryMods.Where(m => m.Nexus != null && m.Nexus.ModId > 0)
				.GroupBy(m => m.Entry, StringComparer.OrdinalIgnoreCase)
				.Select(g => (entry: g.Key, nexus: g.First().Nexus))
				.ToList();
			if (entries.Count == 0) return;

			Dictionary<int, NexusInfo> fresh;
			try {
				fresh = await Nexus.FetchMany(entries.Select(e => e.nexus.ModId));
			} catch {
				return; // offline: try again next time
			}

			foreach (var (entry, nexus) in entries) {
				if (fresh.TryGetValue(nexus.ModId, out var info)) SetEntryNexus(entry, Nexus.Merge(nexus, info));
			}
		}

		private void OpenNexusUpdate_Click(object sender, MouseButtonEventArgs e) {
			if (((FrameworkElement)sender).DataContext is ModEntry { Nexus: not null } mod) {
				Dialog.OpenUrl(mod.Nexus.Url);
				e.Handled = true;
			}
		}


		#endregion
		#region importing mods from the game folder

		private async void ImportMods_Click(object sender, RoutedEventArgs e) {
			await OfferImport(false);
		}

		private async Task OfferImport(bool silentIfNone) {
			if (!GameInstall.IsGameInstallation(GamePath)) return;

			List<FoundGameMod> found = null;
			await RunTask("Looking for mods in the game folder...", progress => {
				found = GameModsScanner.Scan(GamePath, _deployments);
			});

			if (found == null) return;
			if (found.Count == 0) {
				if (!silentIfNone) Dialog.Info("No new mods in the game folder.");
				return;
			}

			var window = new ImportModsWindow(found) { Owner = this };
			if (window.ShowDialog() != true) return;

			var selected = window.Selected;
			if (selected.Count == 0) return;

			var imported = new List<string>();
			await RunTask("Importing mods...", progress => {
				foreach (var mod in selected) {
					progress($"Importing {mod.Name}...");
					imported.Add(GameModsScanner.Import(mod, GamePath));
				}
			});

			await RefreshMods();
			SaveTicksToProfile();

			ShowStatusMessage($"Done! {imported.Count} mod{(imported.Count == 1 ? "" : "s")} imported.");
		}

		#endregion
		#region BmSDK

		private async Task<bool> InstallBmSdk() {
			string version = null;

			var ok = await RunTaskAsync("Installing BmSDK...", async progress => {
				version = await BmSdk.Install(GamePath, progress);
			});

			UpdateSettingsTab();
			UpdateModStatuses();

			if (ok) ShowStatusMessage($"Done! BmSDK {version} installed.");
			return ok;
		}

		private async void InstallBmSdk_Click(object sender, RoutedEventArgs e) {
			if (GameInstall.IsRunning()) {
				Dialog.Warning("Close the game first.");
				return;
			}

			if (!GameInstall.IsSteamInstallation(GamePath)
				&& !Dialog.Confirm($"BmSDK only works with the Steam version of the game. {DescribeStore()}.", "Install anyway", "Cancel", icon: DialogIcon.Warning)) {
				return;
			}

			await InstallBmSdk();
		}

		/// <summary>"This is the Epic Games version" / "This doesn't look like the Steam version".</summary>
		private string DescribeStore() {
			var store = GameInstall.GetStore(GamePath);
			return store == "unknown" ? "This doesn't look like the Steam version" : $"This is the {store} version";
		}

		#endregion
		#region settings tab

		private void UpdateSettingsTab() {
			_reactToSettingsChanges = false;

			var profile = _selectedProfile;
			Settings_Theme.ItemsSource = new[] { Theme.Dark, Theme.Light };
			Settings_Theme.SelectedItem = Theme.Current;

			Settings_GamePath.Text = profile.GamePath;
			Settings_Store.Text = GameInstall.GetStore(profile.GamePath) switch {
				"unknown" => "Unknown",
				var store => store,
			};

			var dlcFolders = GameInstall.GetDlcFolders(profile.GamePath);
			if (!dlcFolders.Contains(profile.Settings_DlcFolder)) dlcFolders.Add(profile.Settings_DlcFolder);
			Settings_DlcFolder.ItemsSource = dlcFolders;
			Settings_DlcFolder.SelectedItem = profile.Settings_DlcFolder;

			var version = BmSdk.GetInstalledVersion(profile.GamePath);
			var steam = GameInstall.IsSteamInstallation(profile.GamePath);
			Settings_BmSdkStatus.Text = version != null ? $"Installed (v{version})" : (steam ? "Not installed" : "Not installed (needs the Steam version)");
			Settings_BmSdkStatus.SetResourceReference(TextBlock.ForegroundProperty, version != null ? "SuccessBrush" : "ErrorBrush");
			Settings_BmSdkInstallButton.Content = version != null ? "Update / reinstall BmSDK" : "Install BmSDK";
			_reactToSettingsChanges = true;
		}

		private void Settings_Theme_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if (!_reactToSettingsChanges || Settings_Theme.SelectedItem is not string theme) return;
			Theme.Apply(theme);
			_settings.Theme = theme;
			((App)Application.Current).WriteSettings();
		}

		private async void Settings_DlcFolder_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if (!_reactToSettingsChanges) return;
			if (Settings_DlcFolder.SelectedItem is not string folder || folder == _selectedProfile.Settings_DlcFolder) return;

			var installedDlcMods = _modsList.Where(m => m.Type == ModType.DLC && m.IsInstalledIn(GamePath)).ToList();
			if (installedDlcMods.Count > 0) {
				var choice = Dialog.Show($"Move the {installedDlcMods.Count} installed DLC mods to DLC\\{folder}?", "DLC folder", DialogIcon.Question, "Move them", "Leave them", "Cancel");
				if (choice == 2) {
					_reactToSettingsChanges = false;
					Settings_DlcFolder.SelectedItem = _selectedProfile.Settings_DlcFolder;
					_reactToSettingsChanges = true;
					return;
				}

				if (choice == 0) {
					await RunTask("Moving DLC mods...", progress => {
						_deployer.MoveDlcMods(installedDlcMods, folder);
						_deployments.SetMods(_libraryMods);
					});
				}
			}

			_selectedProfile.Settings_DlcFolder = folder;
			SaveProfile();
			UpdateModStatuses();
			SuitsMenuContent.RequestReload();
		}

		private void OpenGameFolder_Click(object sender, RoutedEventArgs e) {
			OpenFolder(GamePath);
		}

		private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
			Dialog.OpenUrl(e.Uri.AbsoluteUri);
			e.Handled = true;
		}

		#endregion
		#region misc UI

		private void LaunchGameButton_Click(object sender, RoutedEventArgs e) {
			if (!SuitsMenuContent.ConfirmDiscardChanges()) return;
			if (PendingCount > 0 && !Dialog.Confirm("Some changes weren't applied yet.", "Launch anyway", "Cancel", icon: DialogIcon.Warning)) return;

			try {
				GameInstall.Launch(GamePath);
			} catch (Exception ex) {
				Dialog.Error("Couldn't start the game: " + ex.Message);
			}
		}

		private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if (e.Source != MainTabs) return;
			if (MainTabs.SelectedItem == SuitsMenuTab) {
				SuitsMenuContent.OnOpen();
			}
		}

		private void ProfileGamePath_MouseDown(object sender, MouseButtonEventArgs e) {
			if (e.ClickCount < 2) return;
			OpenFolder(e.ChangedButton == MouseButton.Right ? App.BaseDirectory : GamePath);
		}

		private void Window_Closing(object sender, CancelEventArgs e) {
			if (_busy > 0) {
				e.Cancel = true;
				return;
			}

			if (_modsList.Any(m => IsPendingInstall(m) || IsPendingRemoval(m))) {
				var choice = Dialog.Show("You have changes that weren't applied.", "Overstrike-BAK", DialogIcon.Question, "Apply", "Discard", "Cancel");
				if (choice == 2) {
					e.Cancel = true;
					return;
				}

				if (choice == 0) {
					e.Cancel = true;
					Dispatcher.BeginInvoke(async () => {
						if (await ApplyChanges()) Close();
					});
					return;
				}
			}

			if (!SuitsMenuContent.ConfirmDiscardChanges()) {
				e.Cancel = true;
			}
		}

		private void Window_KeyUp(object sender, KeyEventArgs e) {
			if ((e.Key == Key.PrintScreen || e.SystemKey == Key.PrintScreen) && _statusMessageErrorShown) {
				var w = new ErrorLogWindow() { Owner = this };
				w.CopyErrorText();
				w.ShowDialog();
			}
		}

		private void StatusMessage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
			if (!_statusMessageErrorShown) return;
			new ErrorLogWindow() { Owner = this }.ShowDialog();
			_statusMessageErrorShown = false;
			PlayAnimation("HideStatusMessage");
		}

		private void ShowStatusMessage(string text) {
			StatusMessage.Text = text;
			StatusMessage.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
			StatusMessage.Cursor = null;
			_statusMessageErrorShown = false;
			PlayAnimation("ShowStatusMessage");
		}

		private void ShowStatusMessageError(string text) {
			StatusMessage.Text = text;
			StatusMessage.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
			StatusMessage.Cursor = Cursors.Hand;
			_statusMessageErrorShown = true;
			PlayAnimation("ShowStatusMessageError");
		}

		private void PlayAnimation(string name) {
			BeginStoryboard((System.Windows.Media.Animation.Storyboard)FindResource(name));
		}

		private static void OpenFolder(string path) {
			try {
				Process.Start("explorer.exe", path);
			} catch {}
		}

		#endregion
	}
}

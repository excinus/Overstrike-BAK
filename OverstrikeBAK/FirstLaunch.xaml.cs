// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Data;
using OverstrikeBAK.Game;
using OverstrikeBAK.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace OverstrikeBAK {
	public partial class FirstLaunch: Window {
		private readonly ObservableCollection<FirstLaunchProfile> _profilesList = new();

		public FirstLaunch() {
			InitializeComponent();

			foreach (var found in GameLocator.FindInstallations()) {
				AddToProfilesList(found.Name, found.Path);
			}

			if (_profilesList.Count > 0) {
				Hint.Text = (_profilesList.Count == 1 ? "Found the game" : $"Found {_profilesList.Count} installations") + " in the usual places. Add more manually, if you want:";
			}

			ProfilesList.ItemsSource = _profilesList;
			RefreshButton();
		}

		private void AddToProfilesList(string name, string path) {
			if (_profilesList.Any(p => FileUtils.SamePath(p.GamePath, path))) return;

			var baseName = name;
			var index = 2;
			while (NameIsTaken(name)) {
				name = $"{baseName} ({index})";
				++index;
			}

			var profile = new FirstLaunchProfile() { Create = true, Name = name, GamePath = path };
			profile.PropertyChanged += (s, e) => RefreshButton();
			_profilesList.Add(profile);
		}

		private bool NameIsTaken(string name) {
			return File.Exists(Path.Combine(Profile.ProfilesDirectory, name + ".json")) || _profilesList.Any(p => p.Name == name);
		}

		private void RefreshButton() {
			CreateProfilesButton.IsEnabled = _profilesList.Any(p => p.Create);
		}

		private void AddGameButton_Click(object sender, RoutedEventArgs e) {
			var window = new CreateProfile(NameIsTaken) { Owner = this };
			window.ShowDialog();

			var p = window.GetProfile();
			if (p != null) {
				AddToProfilesList(p.Name, p.GamePath);
				RefreshButton();
			}
		}

		private void CreateProfilesButton_Click(object sender, RoutedEventArgs e) {
			foreach (var profile in _profilesList.Where(p => p.Create)) {
				new Profile(profile.Name, profile.GamePath).Save();
			}

			Close();
		}
	}

	public class FirstLaunchProfile: INotifyPropertyChanged {
		public event PropertyChangedEventHandler PropertyChanged;

		private bool _create;
		public bool Create {
			get => _create;
			set {
				_create = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Create)));
			}
		}

		public string Name { get; set; }
		public string GamePath { get; set; }
	}
}

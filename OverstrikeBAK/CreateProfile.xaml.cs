// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Microsoft.Win32;
using OverstrikeBAK.Data;
using OverstrikeBAK.Game;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace OverstrikeBAK {
	public partial class CreateProfile: Window {
		private readonly bool _initializing = true;

		private string _profileName = null;
		private bool _isNameValid = false;
		private bool _isNameTaken = false;

		private string _gamePath = null;

		/// <param name="nameTaken">Extra check for names that aren't saved yet (first launch list).</param>
		public CreateProfile(System.Func<string, bool> nameTaken = null) {
			InitializeComponent();
			_nameTaken = nameTaken;
			_initializing = false;
		}

		private readonly System.Func<string, bool> _nameTaken;

		private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e) {
			if (_initializing) return;

			_profileName = NameTextBox.Text.Trim();
			_isNameValid = Profile.IsValidName(_profileName);
			_isNameTaken = File.Exists(Path.Combine(Profile.ProfilesDirectory, _profileName + ".json")) || (_nameTaken?.Invoke(_profileName) ?? false);

			UpdateErrorAndButton();
		}

		private void BrowseButton_Click(object sender, RoutedEventArgs e) {
			var dialog = new OpenFolderDialog() { Title = "Select the Batman Arkham Knight folder..." };
			var result = dialog.ShowDialog(this);
			Activate();

			if (result != true) return;

			_gamePath = GameInstall.FindGameRoot(dialog.FolderName);
			PathTextBox.Text = _gamePath ?? dialog.FolderName;
			if (_gamePath == null) _gamePath = "";

			if (string.IsNullOrEmpty(NameTextBox.Text) && _gamePath.Length > 0) {
				NameTextBox.Text = GameInstall.IsSteamInstallation(_gamePath) ? "Batman Arkham Knight (Steam)" : "Batman Arkham Knight";
			}

			UpdateErrorAndButton();
		}

		private void UpdateErrorAndButton() {
			var message = "Select the 'Batman Arkham Knight' game folder.";
			var pathOk = false;
			var nameOk = false;

			if (_gamePath != null) {
				if (_gamePath.Length == 0) {
					message = "Couldn't find Batman: Arkham Knight there! (looking for Binaries\\Win64\\BatmanAK.exe)";
				} else {
					pathOk = true;
					message = "Detected game: " + GameInstall.UserFriendlyName;
				}
			}

			if (_profileName != null) {
				if (!_isNameValid) {
					message = "Profile name is not valid! (letters, digits, spaces, _ - ( ) only)";
				} else if (_isNameTaken) {
					message = "Profile with such name already exists!";
				} else {
					nameOk = true;
				}
			}

			ErrorMessage.Content = message;
			CreateProfileButton.IsEnabled = nameOk && pathOk;
		}

		private void CreateProfileButton_Click(object sender, RoutedEventArgs e) {
			DialogResult = true;
		}

		internal Profile GetProfile() {
			return (DialogResult == true) ? new Profile(_profileName, _gamePath) : null;
		}
	}
}

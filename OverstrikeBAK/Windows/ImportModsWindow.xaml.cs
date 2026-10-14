// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Mods;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace OverstrikeBAK.Windows {
	public partial class ImportModsWindow: Window {
		private readonly List<FoundGameMod> _mods;

		public List<FoundGameMod> Selected => _mods.Where(m => m.Import).ToList();

		public ImportModsWindow(List<FoundGameMod> mods) {
			InitializeComponent();
			_mods = mods;
			ModsList.ItemsSource = _mods;
		}

		private void SelectAll_Click(object sender, RoutedEventArgs e) {
			foreach (var mod in _mods) mod.Import = true;
		}

		private void SelectNone_Click(object sender, RoutedEventArgs e) {
			foreach (var mod in _mods) mod.Import = false;
		}

		private void Import_Click(object sender, RoutedEventArgs e) {
			DialogResult = true;
		}
	}
}

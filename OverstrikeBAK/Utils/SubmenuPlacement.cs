// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System.Windows;
using System.Windows.Controls.Primitives;

namespace OverstrikeBAK.Utils {
	/// <summary>
	/// Opens a submenu to the right of its item, and to the left only when there's no room on the screen.
	/// (Placement="Right" alone opens to the left on PCs set up for right-handed pen use: Windows' menu drop alignment.)
	/// </summary>
	public static class SubmenuPlacement {
		public static readonly DependencyProperty OpenToTheRightProperty = DependencyProperty.RegisterAttached(
			"OpenToTheRight", typeof(bool), typeof(SubmenuPlacement), new PropertyMetadata(false, OnChanged));

		public static bool GetOpenToTheRight(DependencyObject obj) => (bool)obj.GetValue(OpenToTheRightProperty);
		public static void SetOpenToTheRight(DependencyObject obj, bool value) => obj.SetValue(OpenToTheRightProperty, value);

		private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if (d is not Popup popup || e.NewValue is not true) return;

			popup.Placement = PlacementMode.Custom;
			popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[] {
				new CustomPopupPlacement(new Point(targetSize.Width - 1, -4), PopupPrimaryAxis.Horizontal),
				new CustomPopupPlacement(new Point(-popupSize.Width + 1, -4), PopupPrimaryAxis.Horizontal),
			};
		}
	}
}

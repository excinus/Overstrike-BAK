// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace OverstrikeBAK.Utils {
	/// <summary>
	/// Opens right-click menus to the bottom right of the mouse, flipping only when there's no room on the screen.
	/// (The default placement opens them to the left on PCs set up for right-handed pen use: Windows' menu drop alignment.)
	/// </summary>
	public static class ContextMenuPlacement {
		/// <summary>Call once at startup: every element's context menu gets this placement just before it opens.</summary>
		public static void Enable() {
			EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ContextMenuOpeningEvent,
				new ContextMenuEventHandler((sender, _) => {
					// buttons open their menus below themselves (in code)
					if (sender is FrameworkElement { ContextMenu: ContextMenu menu } owner && sender is not ButtonBase) AtMouse(owner, menu);
				}));
		}

		private static void AtMouse(FrameworkElement owner, ContextMenu menu) {
			// opening a menu with the mouse resets its Placement; the owner's ContextMenuService.Placement is kept
			ContextMenuService.SetPlacement(owner, PlacementMode.Custom);
			menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => {
				var mouse = new Point();
				if (menu.PlacementTarget is FrameworkElement target) {
					mouse = Mouse.GetPosition(target);
					// the callback works in device pixels, the mouse position is in WPF units
					if (target.ActualWidth > 0) {
						var scale = targetSize.Width / target.ActualWidth;
						mouse = new Point(mouse.X * scale, mouse.Y * scale);
					}
				}

				return new[] {
					new CustomPopupPlacement(new Point(mouse.X + 1, mouse.Y + 1), PopupPrimaryAxis.Vertical),
					new CustomPopupPlacement(new Point(mouse.X - popupSize.Width, mouse.Y + 1), PopupPrimaryAxis.Vertical),
					new CustomPopupPlacement(new Point(mouse.X + 1, mouse.Y - popupSize.Height), PopupPrimaryAxis.Vertical),
					new CustomPopupPlacement(new Point(mouse.X - popupSize.Width, mouse.Y - popupSize.Height), PopupPrimaryAxis.Vertical),
				};
			};
		}
	}
}

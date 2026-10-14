// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Windows.Media;
using System.Windows;

namespace OverstrikeBAK.Utils {
	internal static class DragDrop {
		// Helper to search up the VisualTree
		internal static T FindAncestor<T>(DependencyObject current)
			where T : DependencyObject {
			try {
				do {
					if (current is T) {
						return (T)current;
					}
					current = VisualTreeHelper.GetParent(current);
				}
				while (current != null);
			} catch (Exception) {
				// happens when listview is filtered
			}
			return null;
		}
	}
}

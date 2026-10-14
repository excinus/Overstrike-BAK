// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OverstrikeBAK.Utils {
	/// <summary>Switches between the Dark and Light color dictionaries (Themes\*.xaml) and matches the window title bars.</summary>
	internal static class Theme {
		public const string Dark = "Dark";
		public const string Light = "Light";

		public static string Current { get; private set; } = Dark;

		private static bool _hooked = false;

		public static void Apply(string name) {
			Current = (name == Light) ? Light : Dark;

			var colors = new ResourceDictionary() { Source = new Uri($"pack://application:,,,/Themes/{Current}.xaml") };
			var dictionaries = Application.Current.Resources.MergedDictionaries;
			if (dictionaries.Count > 0) dictionaries[0] = colors;
			else dictionaries.Add(colors);

			if (!_hooked) {
				_hooked = true;
				EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) => ApplyTitleBar((Window)sender)));
			}

			foreach (Window window in Application.Current.Windows) {
				ApplyTitleBar(window);
			}
		}

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

		private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

		public static void ApplyTitleBar(Window window) {
			try {
				var hwnd = new WindowInteropHelper(window).Handle;
				if (hwnd == IntPtr.Zero) return;
				var dark = (Current == Dark) ? 1 : 0;
				DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
			} catch {}
		}
	}
}

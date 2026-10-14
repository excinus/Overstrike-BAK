// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace OverstrikeBAK.Windows {
	public enum DialogIcon { None, Info, Warning, Error, Question }

	/// <summary>
	/// A themed message box. Messages can contain links written as [text](https://...).
	/// The first button is the default (Enter), the last one is the cancel button (Esc).
	/// </summary>
	public partial class Dialog: Window {
		private int _result = -1;

		private Dialog(string title, string message, DialogIcon icon, string[] buttons) {
			InitializeComponent();
			Title = title;
			SetMessage(message);
			SetIcon(icon);

			for (var i = 0; i < buttons.Length; ++i) {
				var index = i;
				var button = new Button() {
					Content = buttons[i],
					MinWidth = 84,
					Height = 26,
					Margin = new Thickness(8, 0, 0, 0),
					IsDefault = (i == 0),
					IsCancel = (i == buttons.Length - 1),
				};
				if (i == 0) button.Style = (Style)FindResource("AccentButton");
				button.Click += (s, e) => {
					_result = index;
					DialogResult = true;
				};
				ButtonsPanel.Children.Add(button);
			}

			Loaded += (s, e) => (ButtonsPanel.Children[0] as Button)?.Focus();
		}

		private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\((https?://[^)\s]+)\)");

		private void SetMessage(string message) {
			var position = 0;
			foreach (Match m in LinkRegex.Matches(message)) {
				if (m.Index > position) MessageText.Inlines.Add(new Run(message.Substring(position, m.Index - position)));

				var url = m.Groups[2].Value;
				var link = new Hyperlink(new Run(m.Groups[1].Value)) { NavigateUri = new Uri(url), ToolTip = url };
				link.RequestNavigate += (s, e) => OpenUrl(url);
				MessageText.Inlines.Add(link);

				position = m.Index + m.Length;
			}
			if (position < message.Length) MessageText.Inlines.Add(new Run(message.Substring(position)));
		}

		private void SetIcon(DialogIcon icon) {
			var (glyph, brush) = icon switch {
				DialogIcon.Info => ("", "AccentBrush"),
				DialogIcon.Warning => ("", "WarningBrush"),
				DialogIcon.Error => ("", "ErrorBrush"),
				DialogIcon.Question => ("", "AccentBrush"),
				_ => ("", "TextBrush"),
			};
			IconGlyph.Text = glyph;
			IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
			IconGlyph.Visibility = (icon == DialogIcon.None) ? Visibility.Collapsed : Visibility.Visible;
		}

		public static void OpenUrl(string url) {
			try {
				Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			} catch {}
		}

		/// <summary>Shows the dialog and returns the index of the clicked button (the last one if closed).</summary>
		public static int Show(string message, string title, DialogIcon icon, params string[] buttons) {
			if (buttons.Length == 0) buttons = new[] { "OK" };

			var dialog = new Dialog(title, message, icon, buttons);
			var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != dialog)
				?? Application.Current?.MainWindow;
			if (owner != null && owner.IsVisible) {
				dialog.Owner = owner;
			} else {
				dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
			}

			dialog.ShowDialog();
			return dialog._result == -1 ? buttons.Length - 1 : dialog._result;
		}

		public static void Info(string message, string title = "Overstrike-BAK") => Show(message, title, DialogIcon.Info, "OK");
		public static void Warning(string message, string title = "Overstrike-BAK") => Show(message, title, DialogIcon.Warning, "OK");
		public static void Error(string message, string title = "Overstrike-BAK") => Show(message, title, DialogIcon.Error, "OK");

		/// <summary>True if the first button ("yes") was clicked.</summary>
		public static bool Confirm(string message, string yes = "Yes", string no = "No", string title = "Overstrike-BAK", DialogIcon icon = DialogIcon.Question) {
			return Show(message, title, icon, yes, no) == 0;
		}
	}
}

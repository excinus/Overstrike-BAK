// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Windows;
using System.Windows.Controls;

namespace OverstrikeBAK.Windows {
	public partial class TextPromptWindow: Window {
		private readonly Func<string, string> _validate;

		public string Value => ValueTextBox.Text.Trim();

		/// <param name="validate">Returns an error message, or null if the value is fine.</param>
		public TextPromptWindow(string title, string prompt, string initial, Func<string, string> validate) {
			InitializeComponent();
			Title = title;
			PromptLabel.Text = prompt;
			_validate = validate;
			ValueTextBox.Text = initial ?? "";
			ValueTextBox.SelectAll();
			ValueTextBox.Focus();
			Validate();
		}

		private bool Validate() {
			var error = _validate?.Invoke(Value);
			ErrorLabel.Text = error ?? "";
			OkButton.IsEnabled = (error == null);
			return error == null;
		}

		private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e) {
			Validate();
		}

		private void OkButton_Click(object sender, RoutedEventArgs e) {
			if (Validate()) DialogResult = true;
		}
	}
}

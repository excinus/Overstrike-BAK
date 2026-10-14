// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System.IO;
using System.Text;

namespace OverstrikeBAK.Utils {
	/// <summary>
	/// A text file read with its original encoding, so editing a few characters and writing it back
	/// keeps everything else (BOM, UTF-16, line endings) byte-for-byte the same.
	/// UE3 .ini/.int files come in ANSI, UTF-8 (with or without BOM) and UTF-16 LE.
	/// </summary>
	internal class TextFile {
		public string Text;
		private readonly Encoding _encoding;
		private readonly byte[] _preamble;

		private TextFile(string text, Encoding encoding, byte[] preamble) {
			Text = text;
			_encoding = encoding;
			_preamble = preamble;
		}

		public static TextFile Load(string path) {
			return FromBytes(File.ReadAllBytes(path));
		}

		public static TextFile FromBytes(byte[] bytes) {
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) {
				return new TextFile(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode, new byte[] { 0xFF, 0xFE });
			}

			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) {
				return new TextFile(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode, new byte[] { 0xFE, 0xFF });
			}

			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
				var utf8 = new UTF8Encoding(false);
				return new TextFile(utf8.GetString(bytes, 3, bytes.Length - 3), utf8, new byte[] { 0xEF, 0xBB, 0xBF });
			}

			// no BOM: UTF-8 if it decodes cleanly, otherwise Latin-1 (keeps every byte as-is when written back)
			try {
				var strict = new UTF8Encoding(false, true);
				return new TextFile(strict.GetString(bytes), new UTF8Encoding(false), new byte[0]);
			} catch {
				return new TextFile(Encoding.Latin1.GetString(bytes), Encoding.Latin1, new byte[0]);
			}
		}

		public static string ReadAllText(string path) {
			return Load(path).Text;
		}

		public void Save(string path) {
			using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
			stream.Write(_preamble, 0, _preamble.Length);
			var bytes = _encoding.GetBytes(Text);
			stream.Write(bytes, 0, bytes.Length);
		}
	}
}

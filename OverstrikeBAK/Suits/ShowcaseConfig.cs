// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OverstrikeBAK.Suits {
	/// <summary>
	/// How a config line changes the RGameInfo.ShowcaseItems array, following UE3 .ini prefixes:
	/// '.' adds (duplicates allowed), '+' adds unless an identical value exists, '-' removes one identical value,
	/// '!' clears the array, no prefix sets/adds.
	/// </summary>
	public enum ConfigOp { Add, AddUnique, Remove, Clear, Set }

	/// <summary>The fields of one ( Page="Players", Item="...", Skin="...", SortIndex=100, ... ) struct.</summary>
	public class ShowcaseFields {
		public string Page = "";
		public string Item = "";
		public string Skin = "";
		public string Camera = "";
		public string Condition = "";
		public string Prerequisite = "";
		public int? SortIndex = null;
		public bool Unlocked = false;
		public bool DLC = false;
		public bool WBPlay = false;

		public static ShowcaseFields Parse(string value) {
			var result = new ShowcaseFields();

			var text = value.Trim();
			if (text.StartsWith("(")) text = text.Substring(1);
			if (text.EndsWith(")")) text = text.Substring(0, text.Length - 1);

			foreach (var part in SplitTopLevel(text)) {
				var eq = part.IndexOf('=');
				if (eq <= 0) continue;

				var key = part.Substring(0, eq).Trim();
				var val = Unquote(part.Substring(eq + 1).Trim());

				switch (key.ToLowerInvariant()) {
					case "page": result.Page = val; break;
					case "item": result.Item = val; break;
					case "skin": result.Skin = val; break;
					case "camera": result.Camera = val; break;
					case "condition": result.Condition = val; break;
					case "prerequisite": result.Prerequisite = val; break;
					case "sortindex":
						if (int.TryParse(val, out var index)) result.SortIndex = index;
						break;
					case "unlocked": result.Unlocked = IsTrue(val); break;
					case "dlc": result.DLC = IsTrue(val); break;
					case "wbplay": result.WBPlay = IsTrue(val); break;
				}
			}

			return result;
		}

		private static bool IsTrue(string value) {
			return value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value == "1";
		}

		private static string Unquote(string value) {
			if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\"")) {
				return value.Substring(1, value.Length - 2);
			}
			return value;
		}

		private static List<string> SplitTopLevel(string text) {
			var parts = new List<string>();
			var current = new StringBuilder();
			var depth = 0;
			var inQuotes = false;

			foreach (var c in text) {
				if (c == '"') inQuotes = !inQuotes;
				if (!inQuotes) {
					if (c == '(') ++depth;
					if (c == ')') --depth;
					if (c == ',' && depth == 0) {
						parts.Add(current.ToString());
						current.Clear();
						continue;
					}
				}
				current.Append(c);
			}

			if (current.Length > 0) parts.Add(current.ToString());
			return parts;
		}
	}

	/// <summary>One "ShowcaseItems=..." line of a config file.</summary>
	public class ShowcaseLine {
		public ConfigSource Source;
		public int Order; // position in load order (across all sources)
		public ConfigOp Op;
		public bool Hidden; // commented out by "Hide": the game doesn't read it
		public string RawValue;
		public ShowcaseFields Fields;
	}

	/// <summary>Reads and rewrites the [BmGame.RGameInfo] ShowcaseItems lines of BmGame.ini-style files.</summary>
	public static class ShowcaseIni {
		public const string Section = "BmGame.RGameInfo";
		public const string Key = "ShowcaseItems";

		/// <summary>
		/// "Hide" comments a line out with this in front, so the game skips it and it can be put back as it was.
		/// </summary>
		public const string HiddenTag = ";[Hidden by Overstrike-BAK]";

		private static readonly Regex SortIndexRegex = new(@"(SortIndex\s*=\s*)(-?\d+)", RegexOptions.IgnoreCase);

		/// <summary>The line commented out by "Hide".</summary>
		public static string Hide(string line) => HiddenTag + " " + line.TrimStart();

		/// <summary>
		/// Calls 'onLine(op, value, fields, line, hidden)' for every ShowcaseItems line ('line' without the HiddenTag of
		/// a hidden one). It can return a new text for that line (or null to keep it). Returns the new file text, or null
		/// if nothing changed.
		/// </summary>
		public static string Process(string text, Func<ConfigOp, string, ShowcaseFields, string, bool, string> onLine) {
			var lines = text.Split('\n');
			var section = "";
			var changed = false;

			for (var i = 0; i < lines.Length; ++i) {
				var raw = lines[i];
				var line = raw.TrimEnd('\r').Trim();
				var hidden = line.StartsWith(HiddenTag, StringComparison.OrdinalIgnoreCase);
				if (hidden) line = line.Substring(HiddenTag.Length).Trim();
				if (line.Length == 0 || line.StartsWith(";")) continue;

				if (line.StartsWith("[") && line.EndsWith("]")) {
					section = line.Substring(1, line.Length - 2).Trim();
					continue;
				}

				if (!section.Equals(Section, StringComparison.OrdinalIgnoreCase)) continue;

				var op = ConfigOp.Set;
				var body = line;
				switch (line[0]) {
					case '.': op = ConfigOp.Add; body = line.Substring(1); break;
					case '+': op = ConfigOp.AddUnique; body = line.Substring(1); break;
					case '-': op = ConfigOp.Remove; body = line.Substring(1); break;
					case '!': op = ConfigOp.Clear; body = line.Substring(1); break;
				}

				var eq = body.IndexOf('=');
				if (eq <= 0) continue;
				if (!body.Substring(0, eq).Trim().Equals(Key, StringComparison.OrdinalIgnoreCase)) continue;

				var value = body.Substring(eq + 1).Trim();
				var original = raw.TrimEnd('\r');
				var replacement = onLine(op, value, ShowcaseFields.Parse(value), hidden ? line : original, hidden);
				if (replacement != null && replacement != original) {
					lines[i] = replacement + (raw.EndsWith("\r") ? "\r" : "");
					changed = true;
				}
			}

			return changed ? string.Join("\n", lines) : null;
		}

		/// <summary>Returns the line (as found in the file) with its SortIndex value replaced, keeping everything else.</summary>
		public static string WithSortIndex(string line, int sortIndex) {
			return SortIndexRegex.Replace(line, m => m.Groups[1].Value + sortIndex, 1);
		}

		public static List<(ConfigOp op, string value, ShowcaseFields fields, bool hidden)> Read(string text) {
			var result = new List<(ConfigOp, string, ShowcaseFields, bool)>();
			Process(text, (op, value, fields, line, hidden) => {
				result.Add((op, value, fields, hidden));
				return null;
			});
			return result;
		}
	}
}

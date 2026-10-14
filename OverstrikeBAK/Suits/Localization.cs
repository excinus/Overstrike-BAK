// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Game;
using OverstrikeBAK.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverstrikeBAK.Suits {
	/// <summary>Suit names and descriptions: the [Showcase] section of GFxUI.int (English; base game and every DLC/mod).</summary>
	internal static class Localization {
		/// <param name="dlcSources">Filled with the DLC\ files that name each item, in load order (base game names aren't listed).</param>
		public static Dictionary<string, string> LoadShowcaseStrings(string gamePath, Dictionary<string, List<string>> dlcSources = null) {
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			Load(gamePath, "INT", result, dlcSources);
			return result;
		}

		private static void Load(string gamePath, string language, Dictionary<string, string> result, Dictionary<string, List<string>> dlcSources) {
			var ext = "." + language.ToLowerInvariant();

			var baseFile = Path.Combine(GameInstall.GetLocalizationPath(gamePath), language, "GFxUI" + ext);
			if (File.Exists(baseFile)) Parse(baseFile, result);

			var dlcFiles = FileUtils.EnumerateFilesSafe(GameInstall.GetDlcPath(gamePath), "GFxUI" + ext)
				.Where(f => Path.GetExtension(f).Equals(ext, StringComparison.OrdinalIgnoreCase))
				.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
			foreach (var file in dlcFiles) {
				Parse(file, result, dlcSources);
			}
		}

		/// <summary>Sets an item's name in the [Showcase] section of a GFxUI file (adds the line, or the section, if missing).</summary>
		public static void SetShowcaseName(string file, string item, string name) {
			var text = TextFile.Load(file);
			var nl = text.Text.Contains("\r\n") ? "\r\n" : "\n";
			var lines = text.Text.Replace("\r\n", "\n").Split('\n').ToList();
			var entry = $"{item}=\"{name}\"";

			int sectionStart = -1, sectionEnd = lines.Count;
			for (var i = 0; i < lines.Count; ++i) {
				var line = lines[i].Trim().Trim('﻿');
				if (line.StartsWith("[") && line.EndsWith("]")) {
					if (sectionStart >= 0) { sectionEnd = i; break; }
					if (line.Equals("[Showcase]", StringComparison.OrdinalIgnoreCase)) sectionStart = i;
					continue;
				}
				if (sectionStart < 0) continue;

				var eq = line.IndexOf('=');
				if (eq > 0 && line.Substring(0, eq).Trim().Equals(item, StringComparison.OrdinalIgnoreCase)) {
					lines[i] = entry;
					text.Text = string.Join(nl, lines);
					text.Save(file);
					return;
				}
			}

			if (sectionStart < 0) {
				lines.Add("[Showcase]");
				lines.Add(entry);
			} else {
				var at = sectionEnd;
				while (at > sectionStart + 1 && lines[at - 1].Trim().Length == 0) --at; // before the blank lines ending the section
				lines.Insert(at, entry);
			}
			text.Text = string.Join(nl, lines);
			text.Save(file);
		}

		/// <summary>Reads the [Showcase] names of one GFxUI file (a mod's) into 'result' (and where each one came from into 'sources').</summary>
		public static void Parse(string file, Dictionary<string, string> result, Dictionary<string, List<string>> sources = null) {
			string text;
			try {
				text = TextFile.ReadAllText(file);
			} catch {
				return;
			}

			var inShowcase = false;
			foreach (var rawLine in text.Split('\n')) {
				var line = rawLine.Trim().Trim('﻿');
				if (line.Length == 0 || line.StartsWith(";")) continue;

				if (line.StartsWith("[") && line.EndsWith("]")) {
					inShowcase = line.Equals("[Showcase]", StringComparison.OrdinalIgnoreCase);
					continue;
				}

				if (!inShowcase) continue;

				var eq = line.IndexOf('=');
				if (eq <= 0) continue;

				var key = line.Substring(0, eq).Trim();
				var value = line.Substring(eq + 1).Trim();
				if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\"")) {
					value = value.Substring(1, value.Length - 2);
				}

				result[key] = value.Replace("\\n", " ").Trim();
				if (sources != null) {
					if (!sources.TryGetValue(key, out var files)) sources[key] = files = new List<string>();
					if (!files.Contains(file)) files.Add(file);
				}
			}
		}
	}
}

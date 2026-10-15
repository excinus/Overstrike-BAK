// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OverstrikeBAK.Mods {
	/// <summary>A mod's Nexus Mods page and what was last read from it.</summary>
	public class NexusInfo {
		public int ModId;
		public string Name;
		public string Author;
		public string Version;          // the latest version on Nexus Mods (when last read)
		public string Summary;
		public DateTime? Fetched;
		public string InstalledVersion; // the version in Overstrike-BAK: from the download's file name, or the page's when it was linked

		public string Url => Nexus.GetModUrl(ModId);

		public bool UpdateAvailable => Nexus.IsNewer(Version, InstalledVersion);

		public JObject ToJson() {
			return new JObject() {
				["mod_id"] = ModId,
				["name"] = Name,
				["author"] = Author,
				["version"] = Version,
				["summary"] = Summary,
				["fetched"] = Fetched,
				["installed_version"] = InstalledVersion,
			};
		}

		public static NexusInfo FromJson(JObject json) {
			return new NexusInfo() {
				ModId = (int?)json["mod_id"] ?? 0,
				Name = (string)json["name"],
				Author = (string)json["author"],
				Version = (string)json["version"],
				Summary = (string)json["summary"],
				Fetched = (DateTime?)json["fetched"],
				InstalledVersion = (string)json["installed_version"],
			};
		}
	}

	internal static class Nexus {
		public const string GameDomain = "batmanarkhamknight";
		public const int AppModId = 3570; // Overstrike-BAK's own page
		private const string GraphQlUrl = "https://api.nexusmods.com/v2/graphql";

		public static string GetModUrl(int modId) => $"https://www.nexusmods.com/{GameDomain}/mods/{modId}";

		/// <summary>The mod id from a Nexus Mods page link (or a plain number), or 0.</summary>
		public static int ParseModId(string text) {
			text = (text ?? "").Trim();
			if (int.TryParse(text, out var plain) && plain > 0) return plain;

			var m = Regex.Match(text, @"nexusmods\.com/" + GameDomain + @"/mods/(\d+)", RegexOptions.IgnoreCase);
			return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : 0;
		}

		private static readonly Regex FileNamePattern = new(@"-(\d+)-([0-9A-Za-z]+(?:-[0-9A-Za-z]+)*)-(\d{9,11})$");

		/// <summary>Nexus Mods names downloads "Mod Name-1234-1-0-1699999999.zip": returns 1234, or 0.</summary>
		public static int ParseModIdFromFileName(string fileName) {
			var m = FileNamePattern.Match(fileName ?? "");
			return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : 0;
		}

		/// <summary>The file's version from a Nexus Mods download name ("Mod Name-1234-1-0-1699999999" -> "1.0"), or null.</summary>
		public static string ParseVersionFromFileName(string fileName) {
			var m = FileNamePattern.Match(fileName ?? "");
			return m.Success ? m.Groups[2].Value.Replace('-', '.') : null;
		}

		/// <summary>
		/// Is 'latest' a newer version than 'installed'? Compares the numbers in them ("1.10" > "1.9", "v2" > "1.5");
		/// versions without numbers count as newer when they're different. Unknown versions are never newer.
		/// </summary>
		public static bool IsNewer(string latest, string installed) {
			latest = Normalize(latest);
			installed = Normalize(installed);
			if (latest.Length == 0 || installed.Length == 0 || latest == installed) return false;

			var a = Regex.Matches(latest, @"\d+").Select(m => long.TryParse(m.Value, out var n) ? n : 0).ToList();
			var b = Regex.Matches(installed, @"\d+").Select(m => long.TryParse(m.Value, out var n) ? n : 0).ToList();
			if (a.Count == 0 || b.Count == 0) return a.Count == 0 && b.Count == 0;

			for (var i = 0; i < Math.Max(a.Count, b.Count); ++i) {
				var x = i < a.Count ? a[i] : 0;
				var y = i < b.Count ? b[i] : 0;
				if (x != y) return x > y;
			}
			return false; // same numbers, different text ("1.0" / "1.0 final"): not worth flagging

			static string Normalize(string v) {
				v = (v ?? "").Trim().ToLowerInvariant();
				return v.StartsWith("v") ? v.Substring(1).Trim() : v;
			}
		}

		/// <summary>
		/// Fresh info from Nexus Mods for a mod that was linked to 'old': keeps the version the user has. When that
		/// isn't known (linked by hand, or a download without the version in its name), it's the latest one.
		/// </summary>
		public static NexusInfo Merge(NexusInfo old, NexusInfo fresh) {
			fresh.InstalledVersion = (old != null && old.ModId == fresh.ModId ? old.InstalledVersion : null) ?? fresh.Version;
			return fresh;
		}

		/// <summary>Name, author, version and summary from the public Nexus Mods API (no account needed).</summary>
		public static async Task<NexusInfo> Fetch(int modId) {
			var result = await FetchMany(new[] { modId });
			return result.TryGetValue(modId, out var info) ? info : throw new Exception("Mod not found on Nexus Mods.");
		}

		/// <summary>Several mods in as few requests as possible. Mods Nexus Mods doesn't return (removed, hidden) are left out.</summary>
		public static async Task<Dictionary<int, NexusInfo>> FetchMany(IEnumerable<int> modIds) {
			using var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(20) };
			http.DefaultRequestHeaders.UserAgent.ParseAdd("Overstrike-BAK");

			var result = new Dictionary<int, NexusInfo>();
			const string query = "query($ids: [CompositeDomainWithIdInput!]!) { legacyModsByDomain(ids: $ids) { nodes { modId name author version summary } } }";

			foreach (var chunk in modIds.Distinct().Where(id => id > 0).Chunk(50)) {
				var ids = new JArray();
				foreach (var id in chunk) ids.Add(new JObject() { ["gameDomain"] = GameDomain, ["modId"] = id });
				var body = new JObject() { ["query"] = query, ["variables"] = new JObject() { ["ids"] = ids } };

				var response = await http.PostAsync(GraphQlUrl, new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
				response.EnsureSuccessStatusCode();

				var json = JObject.Parse(await response.Content.ReadAsStringAsync());
				foreach (var node in (json["data"]?["legacyModsByDomain"]?["nodes"] as JArray) ?? new JArray()) {
					var id = (int?)node["modId"] ?? 0;
					if (id == 0) continue;
					result[id] = new NexusInfo() {
						ModId = id,
						Name = (string)node["name"],
						Author = (string)node["author"],
						Version = (string)node["version"],
						Summary = (string)node["summary"],
						Fetched = DateTime.UtcNow,
					};
				}
			}

			return result;
		}
	}
}

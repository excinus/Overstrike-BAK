// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Data;
using OverstrikeBAK.Utils;
using OverstrikeBAK.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;

namespace OverstrikeBAK {
	public partial class App: Application {
		public static string BaseDirectory => AppContext.BaseDirectory.TrimEnd('\\', '/');

		public static string Version {
			get {
				var v = Assembly.GetExecutingAssembly().GetName().Version;
				return v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
			}
		}

		public AppSettings Settings = new();
		public List<Profile> Profiles = new();

		private static string SettingsPath => Path.Combine(BaseDirectory, "Profiles", "Settings.json");

		private static System.Threading.Mutex _singleInstance;

		protected override void OnStartup(StartupEventArgs e) {
			Directory.SetCurrentDirectory(BaseDirectory);

			// two instances would both copy, delete and rewrite the same game files
			_singleInstance = new System.Threading.Mutex(true, "OverstrikeBAK_" + BaseDirectory.ToLowerInvariant().Replace('\\', '_').Replace(':', '_'), out var isFirst);
			if (!isFirst) {
				ReadSettings();
				Theme.Apply(Settings.Theme);
				Dialog.Info("Overstrike-BAK is already running.");
				Shutdown();
				return;
			}

			DispatcherUnhandledException += (sender, args) => {
				LogError("Unexpected error", args.Exception);
				Dialog.Error($"Something went wrong: {args.Exception.Message}\nDetails are in errors.log.");
				args.Handled = true;
			};

			if (!CreateSubdirectories()) {
				Dialog.Error("Can't write to this folder. Move Overstrike-BAK somewhere you have write access.");
				Shutdown();
				return;
			}

			ReadSettings();
			Theme.Apply(Settings.Theme);
			LoadProfiles();

			ShutdownMode = ShutdownMode.OnExplicitShutdown;
			var firstLaunch = (Profiles.Count == 0);
			if (firstLaunch) {
				new FirstLaunch().ShowDialog();
				LoadProfiles();
			}

			if (Profiles.Count == 0) {
				Shutdown();
				return;
			}

			ShutdownMode = ShutdownMode.OnMainWindowClose;
			var window = new MainWindow(Settings, Profiles, firstLaunch);
			MainWindow = window;
			window.Show();
		}

		private static bool CreateSubdirectories() {
			try {
				Directory.CreateDirectory(Path.Combine(BaseDirectory, "Mods Library"));
				Directory.CreateDirectory(Path.Combine(BaseDirectory, "Profiles"));
				Directory.CreateDirectory(Path.Combine(BaseDirectory, "Suit Profiles"));
				Directory.CreateDirectory(Path.Combine(BaseDirectory, "Mod Profiles"));
				return true;
			} catch {
				return false;
			}
		}

		private void ReadSettings() {
			try {
				if (File.Exists(SettingsPath)) Settings = new AppSettings(SettingsPath);
			} catch {}
		}

		public void WriteSettings() {
			try {
				Settings.Save(SettingsPath);
			} catch {}
		}

		private void LoadProfiles() {
			Profiles.Clear();

			foreach (var file in Directory.GetFiles(Profile.ProfilesDirectory, "*.json", SearchOption.TopDirectoryOnly)) {
				var name = Path.GetFileName(file);
				if (name.Equals("Settings.json", StringComparison.OrdinalIgnoreCase) || name.Equals("Deployments.json", StringComparison.OrdinalIgnoreCase)) continue;

				try {
					Profiles.Add(new Profile(file));
				} catch {}
			}
		}

		public List<Profile> ReloadProfiles() {
			LoadProfiles();
			return Profiles;
		}

		public static void LogError(string context, Exception ex) {
			ErrorLogger.StartSession();
			ErrorLogger.WriteInfo($"Overstrike-BAK v{Version}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");
			ErrorLogger.WriteError($"{context}\n{ex}\n");
			ErrorLogger.EndSession();
		}
	}
}

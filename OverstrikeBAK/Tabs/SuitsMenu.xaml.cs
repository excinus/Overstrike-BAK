// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Data;
using OverstrikeBAK.Mods;
using OverstrikeBAK.Suits;
using OverstrikeBAK.Utils;
using OverstrikeBAK.Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OverstrikeBAK.Tabs {
	/// <summary>What the Suits Menu needs from the main window.</summary>
	public interface ISuitsHost {
		Profile Profile { get; }
		Deployments Deployments { get; }
		string ModName(string modPath);
		Task<bool> RunTask(string header, Action<Action<string>> work);
		void SaveProfile();
		void ShowStatus(string text, bool error = false);
		void ShowModInList(string modPath);
		void OnSuitRenamed(IEnumerable<string> modPaths, string oldName, string newName);
	}

	#region view models

	public class SuitView: INotifyPropertyChanged {
		public event PropertyChangedEventHandler PropertyChanged;

		private static readonly Brush NormalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xF8)));
		private static readonly Brush MovedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3F, 0xD8, 0xE8)));

		private static Brush Freeze(Brush b) {
			b.Freeze();
			return b;
		}

		public ShowcaseSuit Suit { get; }

		public string Item => Suit.Item;
		public string Name => Suit.Name;
		public string Initial => string.IsNullOrEmpty(Suit.CharacterName) ? "?" : Suit.CharacterName.Substring(0, 1);

		public string ToolTip => $"{Suit.Name}\n{Suit.CharacterName} · {Suit.Item}\nFrom: {Suit.SourceLabel}";

		public Visibility LockVisibility => (!Suit.Unlocked || Suit.WBPlay) ? Visibility.Visible : Visibility.Collapsed;
		public string LockToolTip => Suit.WBPlay ? "Needs a WB Play unlock" : "Unlocked by playing the game";
		public Visibility ModVisibility => IsFromMod ? Visibility.Visible : Visibility.Collapsed;
		public Visibility TiedVisibility => Suit.Tied ? Visibility.Visible : Visibility.Collapsed;

		// right-click menu
		public bool CanShowInMods => !Suit.IsOfficial && Suit.ModPaths.Count > 0;
		public string ShowInModsToolTip => CanShowInMods ? null : Suit.IsOfficial ? "It's part of the game" : "Its mod wasn't installed with Overstrike-BAK (Import from game adds it)";
		public bool CanRename => !Suit.IsOfficial && Suit.NameFiles.Count > 0;
		public string RenameToolTip => CanRename ? null : Suit.IsOfficial ? "It's part of the game" : "Its mod has no name file (GFxUI.int) to change";

		/// <summary>Batmobile thumbnails are landscape (484x224), suits are portrait.</summary>
		public bool IsWide => string.Equals(Suit.Page, ShowcaseModel.PageBatmobiles, StringComparison.OrdinalIgnoreCase);

		/// <summary>Only mods define it (not the base game or official DLC).</summary>
		public bool IsFromMod { get; set; }

		private int _position;
		public int Position {
			get => _position;
			set {
				if (_position == value) return;
				_position = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Position)));
			}
		}

		private bool _moved;
		public bool Moved {
			get => _moved;
			set {
				if (_moved == value) return;
				_moved = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PositionBrush)));
			}
		}

		public Brush PositionBrush => _moved ? MovedBrush : NormalBrush;

		private BitmapSource _thumbnail;
		public BitmapSource Thumbnail {
			get => _thumbnail;
			set {
				_thumbnail = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
			}
		}

		public SuitView(ShowcaseSuit suit) {
			Suit = suit;
		}
	}

	public class CharacterChip {
		public string Key { get; set; } // null = all characters
		public string Label { get; set; }
		public bool IsSelected { get; set; }
	}

	public class PresetItem {
		public string Name { get; set; }
		public string FilePath { get; set; }
		public bool IsOriginal { get; set; }
		public bool IsVanillaFirst { get; set; }
		public bool IsBuiltIn => IsOriginal || IsVanillaFirst; // always there, can't be deleted or overwritten
	}

	#endregion

	/// <summary>
	/// The main menu Showcase, as a grid you can reorder: same pages, same order, same 5-column layout.
	/// Saving writes a unique SortIndex for every item (see SuitOrderWriter).
	/// </summary>
	public partial class SuitsMenu: UserControl {
		private const string OriginalPresetName = "Original (backup)";
		private const string VanillaFirstPresetName = "Vanilla first, Mods to bottom";

		private ISuitsHost _host;

		private bool _needsReload = true;
		private string _pendingShowMod = null; // a mod whose suits to select once the Showcase is loaded
		private bool _loading = false;
		private int _generation = 0;

		private ShowcaseModel _model;
		private string _page = ShowcaseModel.PagePlayers;
		private string _character = null;

		private Dictionary<string, List<string>> _savedOrder = new(StringComparer.OrdinalIgnoreCase);
		private Dictionary<string, List<string>> _order = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _forcedPages = new(StringComparer.OrdinalIgnoreCase); // "Pin this order" without moving anything
		private bool _pendingRestoreOriginal = false;

		private readonly Dictionary<string, Dictionary<string, SuitView>> _views = new(StringComparer.OrdinalIgnoreCase);
		private readonly ObservableCollection<SuitView> _visible = new();
		private readonly ObservableCollection<CharacterChip> _chips = new();
		private static readonly Dictionary<string, BitmapSource> ThumbnailCache = new(StringComparer.OrdinalIgnoreCase);

		private static string PresetsDirectory => Path.Combine(App.BaseDirectory, "Suit Profiles");

		public SuitsMenu() {
			InitializeComponent();
			SuitsGrid.ItemsSource = _visible;
			CharacterChips.ItemsSource = _chips;
		}

		public void Init(ISuitsHost host) {
			_host = host;
		}

		#region loading

		public void RequestReload() {
			_needsReload = true;
			if (IsVisible) Reload();
		}

		public void OnOpen() {
			if (_needsReload) Reload();
		}

		/// <summary>Opens the page with a mod's suits (or Batmobiles) and selects them; waits for the Showcase to load if needed.</summary>
		public void ShowMod(string modPath) {
			_pendingShowMod = modPath;
			if (_needsReload || _loading) {
				if (IsVisible && !_loading) Reload();
				return;
			}
			ApplyPendingShowMod();
		}

		private void ApplyPendingShowMod() {
			var modPath = _pendingShowMod;
			_pendingShowMod = null;
			if (modPath == null || _model == null) return;

			var suits = ShowcaseModel.Pages.SelectMany(_model.GetPage)
				.Where(s => !s.IsOfficial && s.ModPaths.Contains(modPath, StringComparer.OrdinalIgnoreCase))
				.ToList();
			if (suits.Count == 0) {
				_host.ShowStatus("None of this mod's suits are in the Suits Menu.");
				return;
			}

			var page = suits[0].Page;
			var button = page.Equals(ShowcaseModel.PageBatmobiles, StringComparison.OrdinalIgnoreCase) ? PageBatmobilesButton : PagePlayersButton;
			if (button.IsChecked == true) {
				_character = null;
				RefreshChips();
				RefreshGrid(null);
			} else {
				button.IsChecked = true; // PageButton_Checked shows every character of that page
			}

			var views = _views.TryGetValue(page, out var v) ? v : new Dictionary<string, SuitView>();
			SuitsGrid.SelectedItems.Clear();
			foreach (var suit in suits.Where(s => s.Page == page)) {
				if (views.TryGetValue(suit.Item, out var view) && _visible.Contains(view)) SuitsGrid.SelectedItems.Add(view);
			}
			if (SuitsGrid.SelectedItems.Count > 0) {
				SuitsGrid.ScrollIntoView(SuitsGrid.SelectedItems[0]);
				SuitsGrid.Focus();
			}
		}

		public void ClearThumbnails() {
			lock (ThumbnailCache) ThumbnailCache.Clear();
		}

		private async void Reload() {
			if (_loading || _host?.Profile == null) return;
			_needsReload = false;
			_loading = true;
			var generation = ++_generation;

			ShowGridMessage("Reading the Showcase...");
			var profile = _host.Profile;
			var deployments = _host.Deployments;

			ShowcaseModel model = null;
			string error = null;
			try {
				model = await Task.Run(() => ShowcaseModel.Build(profile.GamePath, deployments, _host.ModName, false));
			} catch (Exception ex) {
				App.LogError("Reading the Showcase", ex);
				error = ex.Message;
			}

			_loading = false;
			if (_needsReload) { // something changed while loading
				Reload();
				return;
			}

			if (model == null) {
				ShowGridMessage("Couldn't read the game's Showcase:\n" + error);
				return;
			}

			var hadChanges = HasChanges;
			_model = model;
			_savedOrder = new(StringComparer.OrdinalIgnoreCase);
			foreach (var page in ShowcaseModel.Pages) {
				_savedOrder[page] = model.GetPage(page).Select(s => s.Item).ToList();
			}

			if (hadChanges) {
				// keep the user's unsaved edits, fitted to what's installed now
				foreach (var page in ShowcaseModel.Pages) {
					_order[page] = _order.TryGetValue(page, out var edited) ? Merge(page, edited) : new List<string>(_savedOrder[page]);
				}
			} else {
				_order = CloneOrder(_savedOrder);
				_pendingRestoreOriginal = false;
				_forcedPages.Clear();
			}

			_views.Clear();
			foreach (var page in ShowcaseModel.Pages) {
				var views = new Dictionary<string, SuitView>();
				foreach (var suit in model.GetPage(page)) {
					views[suit.Item] = new SuitView(suit) { IsFromMod = suit.Sources.Count > 0 && !suit.IsOfficial };
				}
				_views[page] = views;
			}

			PagePlayersButton.Content = $"SUITS ({_savedOrder[ShowcaseModel.PagePlayers].Count})";
			PageBatmobilesButton.Content = $"BATMOBILES ({_savedOrder[ShowcaseModel.PageBatmobiles].Count})";

			HideGridMessage();
			RefreshChips();
			RefreshGrid(null);
			RefreshPresets();
			UpdateModifiedState();
			LoadThumbnails(generation);
			ApplyPendingShowMod();
		}

		private void LoadThumbnails(int generation) {
			var pending = _views.Values.SelectMany(v => v.Values).Where(v => v.Suit.ThumbnailPackage != null).ToList();

			// visible page first
			pending = pending.OrderBy(v => v.Suit.Page.Equals(_page, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();

			Task.Run(() => {
				foreach (var view in pending) {
					if (generation != _generation) return;

					BitmapSource bitmap;
					lock (ThumbnailCache) {
						ThumbnailCache.TryGetValue(view.Suit.ThumbnailPackage, out bitmap);
					}

					if (bitmap == null) {
						bitmap = Thumbnails.Load(view.Suit.ThumbnailPackage);
						if (bitmap == null) continue;
						lock (ThumbnailCache) {
							ThumbnailCache[view.Suit.ThumbnailPackage] = bitmap;
						}
					}

					Dispatcher.BeginInvoke(() => view.Thumbnail = bitmap);
				}
			});
		}

		private void ShowGridMessage(string text) {
			GridMessage.Text = text;
			GridMessage.Visibility = Visibility.Visible;
			SuitsGrid.Visibility = Visibility.Hidden;
		}

		private void HideGridMessage() {
			GridMessage.Visibility = Visibility.Collapsed;
			SuitsGrid.Visibility = Visibility.Visible;
		}

		#endregion
		#region order helpers

		private static Dictionary<string, List<string>> CloneOrder(Dictionary<string, List<string>> order) {
			var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var (page, items) in order) result[page] = new List<string>(items);
			return result;
		}

		private List<string> Merge(string page, IReadOnlyList<string> wanted) {
			var current = _savedOrder.TryGetValue(page, out var saved) ? saved : new List<string>();
			var characters = new Dictionary<string, string>();
			foreach (var suit in _model.GetPage(page)) characters[suit.Item] = suit.Character;
			return SuitOrder.Merge(current, wanted, item => characters.TryGetValue(item, out var c) ? c : "");
		}

		private bool PageChanged(string page) {
			if (_forcedPages.Contains(page)) return true;
			if (!_order.TryGetValue(page, out var order) || !_savedOrder.TryGetValue(page, out var saved)) return false;
			return !order.SequenceEqual(saved);
		}

		public bool HasChanges => _model != null && (_pendingRestoreOriginal || ShowcaseModel.Pages.Any(PageChanged));

		/// <summary>The Batmobiles page is a single column (like in game), the suits page a 5-column grid.</summary>
		private bool IsSingleColumn => _page.Equals(ShowcaseModel.PageBatmobiles, StringComparison.OrdinalIgnoreCase);

		private List<string> FullOrder => _order.TryGetValue(_page, out var list) ? list : new List<string>();

		private List<string> VisibleItems() {
			var views = _views.TryGetValue(_page, out var v) ? v : new Dictionary<string, SuitView>();
			return FullOrder.Where(item => views.ContainsKey(item) && (_character == null || views[item].Suit.Character == _character)).ToList();
		}

		#endregion
		#region grid

		private void RefreshChips() {
			_chips.Clear();
			if (!_page.Equals(ShowcaseModel.PagePlayers, StringComparison.OrdinalIgnoreCase) || !_views.TryGetValue(_page, out var views)) {
				_character = null;
				CharacterChips.Visibility = Visibility.Collapsed;
				return;
			}

			CharacterChips.Visibility = Visibility.Visible;
			var characters = new List<(string key, string name, int count)>();
			foreach (var item in FullOrder) {
				if (!views.TryGetValue(item, out var view)) continue;
				var index = characters.FindIndex(c => c.key == view.Suit.Character);
				if (index == -1) characters.Add((view.Suit.Character, view.Suit.CharacterName, 1));
				else characters[index] = (characters[index].key, characters[index].name, characters[index].count + 1);
			}

			if (_character != null && !characters.Any(c => c.key == _character)) _character = null;

			_chips.Add(new CharacterChip() { Key = null, Label = $"ALL  {FullOrder.Count}", IsSelected = _character == null });
			foreach (var (key, name, count) in characters) {
				_chips.Add(new CharacterChip() { Key = key, Label = $"{name.ToUpperInvariant()}  {count}", IsSelected = _character == key });
			}
		}

		private void RefreshGrid(IEnumerable<string> select) {
			var views = _views.TryGetValue(_page, out var v) ? v : new Dictionary<string, SuitView>();
			var saved = _savedOrder.TryGetValue(_page, out var s) ? s : new List<string>();
			var full = FullOrder;

			for (var i = 0; i < full.Count; ++i) {
				if (!views.TryGetValue(full[i], out var view)) continue;
				view.Position = i + 1;
				view.Moved = _pendingRestoreOriginal || saved.IndexOf(full[i]) != i;
			}

			var visible = VisibleItems().Select(item => views[item]).ToList();
			if (!_visible.SequenceEqual(visible)) {
				_visible.Clear();
				foreach (var view in visible) _visible.Add(view);
			}

			if (select != null) {
				SuitsGrid.SelectedItems.Clear();
				foreach (var item in select) {
					if (views.TryGetValue(item, out var view) && _visible.Contains(view)) SuitsGrid.SelectedItems.Add(view);
				}
				if (SuitsGrid.SelectedItems.Count > 0) SuitsGrid.ScrollIntoView(SuitsGrid.SelectedItems[0]);
			}

			var ties = views.Values.Count(x => x.Suit.Tied);
			FixTiesButton.Visibility = (ties > 0 && !PageChanged(_page) && !_pendingRestoreOriginal) ? Visibility.Visible : Visibility.Collapsed;
			// the bar under the grid only shows up for suits that share a SortIndex
			GridHint.Text = (ties > 0 && !PageChanged(_page))
				? $"{ties} suits share a SortIndex with another suit (TIE). The game orders those by the order it loads mod folders, so they can shift when you add, remove or move mods. Saving pins every suit in place."
				: "";
			GridHintBar.Visibility = GridHint.Text.Length > 0 || FixTiesButton.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

			if (_visible.Count == 0 && _model != null) {
				ShowGridMessage("Nothing on this page.");
			} else if (_model != null) {
				HideGridMessage();
			}

			UpdateDetails();
		}

		private void PageButton_Checked(object sender, RoutedEventArgs e) {
			if (_model == null) return;
			_page = (sender == PageBatmobilesButton) ? ShowcaseModel.PageBatmobiles : ShowcaseModel.PagePlayers;
			SuitsGrid.ItemsPanel = (ItemsPanelTemplate)FindResource(IsSingleColumn ? "BatmobilesPanel" : "SuitsPanel");
			_character = null;
			RefreshChips();
			RefreshGrid(null);
		}

		private void CharacterChip_Checked(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is not CharacterChip chip) return;
			if (_character == chip.Key) return;
			_character = chip.Key;
			foreach (var c in _chips) c.IsSelected = (c == chip);
			RefreshGrid(SelectedItems());
		}

		private void FixTiesButton_Click(object sender, RoutedEventArgs e) {
			_forcedPages.Add(_page);
			RefreshGrid(SelectedItems());
			UpdateModifiedState();
		}

		#endregion
		#region moving suits

		private List<string> SelectedItems() {
			return SuitsGrid.SelectedItems.Cast<SuitView>().Select(v => v.Item).ToList();
		}

		/// <summary>Moves 'moving' so they start at 'insertIndex' of the visible list (index counted before removing them).</summary>
		private void MoveItems(List<string> moving, int insertIndex) {
			var visible = VisibleItems();
			moving = visible.Where(moving.Contains).ToList(); // keep their current relative order
			if (moving.Count == 0) return;

			var index = insertIndex - moving.Count(m => visible.IndexOf(m) < insertIndex);
			var newVisible = visible.Where(item => !moving.Contains(item)).ToList();
			index = Math.Clamp(index, 0, newVisible.Count);
			newVisible.InsertRange(index, moving);

			if (newVisible.SequenceEqual(visible)) return;

			if (_character == null) {
				_order[_page] = newVisible;
			} else {
				// the selected character's suits are reordered among the positions they occupy; other characters stay put
				var full = FullOrder;
				var visibleSet = new HashSet<string>(visible);
				var slots = Enumerable.Range(0, full.Count).Where(i => visibleSet.Contains(full[i])).ToList();
				var newFull = new List<string>(full);
				for (var k = 0; k < slots.Count; ++k) newFull[slots[k]] = newVisible[k];
				_order[_page] = newFull;
			}

			_pendingRestoreOriginal = false;
			RefreshGrid(moving);
			UpdateModifiedState();
		}

		private void MoveTop_Click(object sender, RoutedEventArgs e) => MoveItems(SelectedItems(), 0);

		private void MoveBottom_Click(object sender, RoutedEventArgs e) => MoveItems(SelectedItems(), VisibleItems().Count);

		private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveBy(-1);

		private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveBy(1);

		private void MoveBy(int delta) {
			var selected = SelectedItems();
			if (selected.Count == 0) return;

			var visible = VisibleItems();
			var indexes = selected.Select(item => visible.IndexOf(item)).Where(i => i >= 0).ToList();
			if (indexes.Count == 0) return;

			if (delta < 0) MoveItems(selected, Math.Max(0, indexes.Min() + delta));
			else MoveItems(selected, Math.Min(visible.Count, indexes.Max() + 1 + delta));
		}

		private void SuitsGrid_PreviewKeyDown(object sender, KeyEventArgs e) {
			if (Keyboard.Modifiers != ModifierKeys.Control) return;

			if (e.Key == Key.Left || e.Key == Key.Up) {
				MoveBy(e.Key == Key.Up && !IsSingleColumn ? -5 : -1);
				e.Handled = true;
			} else if (e.Key == Key.Right || e.Key == Key.Down) {
				MoveBy(e.Key == Key.Down && !IsSingleColumn ? 5 : 1);
				e.Handled = true;
			} else if (e.Key == Key.Home) {
				MoveTop_Click(null, null);
				e.Handled = true;
			} else if (e.Key == Key.End) {
				MoveBottom_Click(null, null);
				e.Handled = true;
			}
		}

		#endregion
		#region drag and drop

		private Point _dragStart;
		private SuitView _dragStartItem;
		private InsertionAdorner _insertionAdorner;

		private bool _deferredSelect;

		private void SuitsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
			_dragStart = e.GetPosition(SuitsGrid);
			_dragStartItem = Utils.DragDrop.FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as SuitView;
			_deferredSelect = false;

			// pressing on one of several selected suits keeps the selection (to drag them all); a plain click selects just it on release
			if (_dragStartItem != null && SuitsGrid.SelectedItems.Contains(_dragStartItem) && Keyboard.Modifiers == ModifierKeys.None && SuitsGrid.SelectedItems.Count > 1) {
				_deferredSelect = true;
				e.Handled = true;
				SuitsGrid.Focus();
			}
		}

		private void SuitsGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
			if (_deferredSelect && _dragStartItem != null) {
				SuitsGrid.SelectedItems.Clear();
				SuitsGrid.SelectedItems.Add(_dragStartItem);
			}
			_deferredSelect = false;
			_dragStartItem = null;
		}

		private void SuitsGrid_PreviewMouseMove(object sender, MouseEventArgs e) {
			if (e.LeftButton != MouseButtonState.Pressed || _dragStartItem == null) return;

			var position = e.GetPosition(SuitsGrid);
			if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
				Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) {
				return;
			}

			var items = SuitsGrid.SelectedItems.Contains(_dragStartItem) ? SelectedItems() : new List<string> { _dragStartItem.Item };
			if (!SuitsGrid.SelectedItems.Contains(_dragStartItem)) {
				SuitsGrid.SelectedItems.Clear();
				SuitsGrid.SelectedItems.Add(_dragStartItem);
			}
			_dragStartItem = null;
			_deferredSelect = false;

			System.Windows.DragDrop.DoDragDrop(SuitsGrid, new DataObject("OverstrikeBAK.Suits", items), DragDropEffects.Move);
			RemoveInsertionAdorner();
		}

		/// <summary>Where a drop at 'e' would insert: index in the visible list, plus the cell and side to draw the marker on.</summary>
		private (int index, ListBoxItem container, bool after) GetDropTarget(DragEventArgs e) {
			var container = Utils.DragDrop.FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
			if (container?.DataContext is not SuitView view) {
				return (_visible.Count, null, true);
			}

			var after = IsSingleColumn
				? e.GetPosition(container).Y > container.ActualHeight / 2
				: e.GetPosition(container).X > container.ActualWidth / 2;
			var index = _visible.IndexOf(view) + (after ? 1 : 0);
			return (index, container, after);
		}

		private void SuitsGrid_DragOver(object sender, DragEventArgs e) {
			if (!e.Data.GetDataPresent("OverstrikeBAK.Suits")) {
				e.Effects = DragDropEffects.None;
				e.Handled = true;
				return;
			}

			e.Effects = DragDropEffects.Move;
			e.Handled = true;

			var (_, container, after) = GetDropTarget(e);
			RemoveInsertionAdorner();
			if (container != null) {
				var layer = AdornerLayer.GetAdornerLayer(container);
				if (layer != null) {
					_insertionAdorner = new InsertionAdorner(container, after, IsSingleColumn);
					layer.Add(_insertionAdorner);
				}
			}

			AutoScroll(e);
		}

		private void AutoScroll(DragEventArgs e) {
			var scroll = FindChild<ScrollViewer>(SuitsGrid);
			if (scroll == null) return;

			var y = e.GetPosition(SuitsGrid).Y;
			if (y < 40) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 12);
			else if (y > SuitsGrid.ActualHeight - 40) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 12);
		}

		private void SuitsGrid_DragLeave(object sender, DragEventArgs e) {
			RemoveInsertionAdorner();
		}

		private void SuitsGrid_Drop(object sender, DragEventArgs e) {
			RemoveInsertionAdorner();
			if (e.Data.GetData("OverstrikeBAK.Suits") is not List<string> items) return;

			var (index, _, _) = GetDropTarget(e);
			MoveItems(items, index);
		}

		private void RemoveInsertionAdorner() {
			if (_insertionAdorner == null) return;
			AdornerLayer.GetAdornerLayer(_insertionAdorner.AdornedElement)?.Remove(_insertionAdorner);
			_insertionAdorner = null;
		}

		private static T FindChild<T>(DependencyObject parent) where T : DependencyObject {
			for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); ++i) {
				var child = VisualTreeHelper.GetChild(parent, i);
				if (child is T found) return found;
				var deeper = FindChild<T>(child);
				if (deeper != null) return deeper;
			}
			return null;
		}

		private class InsertionAdorner: Adorner {
			private readonly bool _after;
			private readonly bool _horizontal;
			private static readonly Brush Brush = new SolidColorBrush(Color.FromRgb(0x3F, 0xD8, 0xE8));

			public InsertionAdorner(UIElement element, bool after, bool horizontal): base(element) {
				_after = after;
				_horizontal = horizontal;
				IsHitTestVisible = false;
			}

			protected override void OnRender(DrawingContext dc) {
				var size = ((FrameworkElement)AdornedElement).RenderSize;
				if (_horizontal) {
					var y = _after ? size.Height + 1 : -3;
					dc.DrawRectangle(Brush, null, new Rect(-2, y, size.Width + 4, 3));
				} else {
					var x = _after ? size.Width + 1 : -3;
					dc.DrawRectangle(Brush, null, new Rect(x, -2, 3, size.Height + 4));
				}
			}
		}

		#endregion
		#region right-click menu

		private void ShowInModsList_Click(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is SuitView { CanShowInMods: true } view) {
				_host.ShowModInList(view.Suit.ModPaths[0]);
			}
		}

		/// <summary>
		/// Changes the name the game shows, in the mod's own GFxUI.int (so it moves with the mod). When several mod
		/// folders name the same item, all of them are changed: which one the game uses isn't certain.
		/// </summary>
		private void RenameSuit_Click(object sender, RoutedEventArgs e) {
			if (((FrameworkElement)sender).DataContext is not SuitView { CanRename: true } view) return;
			var suit = view.Suit;

			var prompt = new TextPromptWindow("Rename", "Name in the game:", suit.Name, name => {
				if (name.Length == 0) return "Enter a name.";
				if (name.Contains('"')) return "The name can't contain \".";
				return null;
			}) { Owner = Window.GetWindow(this) };
			if (prompt.ShowDialog() != true || prompt.Value == suit.Name) return;

			try {
				foreach (var file in suit.NameFiles) Suits.Localization.SetShowcaseName(file, suit.Item, prompt.Value);
			} catch (Exception ex) {
				Dialog.Error($"Couldn't rename it: {ex.Message}", "Suits Menu");
				return;
			}

			_host.OnSuitRenamed(suit.ModPaths, suit.Name, prompt.Value);
			_host.ShowStatus($"Renamed to {prompt.Value}.");
			RequestReload();
		}

		#endregion
		#region details

		private void SuitsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			UpdateDetails();
		}

		private void UpdateDetails() {
			var selected = SuitsGrid.SelectedItems.Cast<SuitView>().ToList();
			if (selected.Count == 0) {
				DetailsPanel.Visibility = Visibility.Collapsed;
				NoSelectionLabel.Visibility = Visibility.Visible;
				NoSelectionLabel.Text = _model == null ? "" : "Select a suit to see its details.\n\nDrag suits in the grid to change their order, then press Save.";
				return;
			}

			DetailsPanel.Visibility = Visibility.Visible;
			NoSelectionLabel.Visibility = Visibility.Collapsed;

			var view = selected[0];
			var suit = view.Suit;
			SuitNameLabel.Text = suit.Name;
			SuitCharacterLabel.Text = suit.CharacterName;
			SuitDescriptionLabel.Text = suit.Description;
			SuitDescriptionLabel.Visibility = string.IsNullOrEmpty(suit.Description) ? Visibility.Collapsed : Visibility.Visible;

			var saved = _savedOrder.TryGetValue(_page, out var s) ? s.IndexOf(suit.Item) + 1 : 0;
			SuitPositionLabel.Text = view.Position == saved || _pendingRestoreOriginal ? $"{view.Position} of {FullOrder.Count}" : $"{view.Position} of {FullOrder.Count} (now {saved} in game)";
			SuitItemLabel.Text = suit.Item;
			SuitSkinLabel.Text = string.IsNullOrEmpty(suit.Skin) ? "—" : suit.Skin;
			SuitSourceLabel.Text = string.Join("\n", suit.Sources.Count > 0 ? suit.Sources : new List<string> { suit.SourceLabel });

			var notes = new List<string>();
			if (suit.WBPlay) notes.Add("Needs a WB Play unlock: the Showcase only lists it once it's unlocked.");
			else if (!suit.Unlocked) notes.Add("Locked until it's unlocked in the game.");
			if (suit.Condition == "Unavailable") notes.Add("Marked unavailable in the Showcase.");
			if (suit.Condition == "RaceOnly") notes.Add("Race only.");
			if (suit.Tied) notes.Add($"Shares SortIndex {suit.SortIndex} with another suit: its place can shift when mods are added, removed or moved, and may differ slightly from the game's until you save.");
			if (suit.Appearances > 1) notes.Add($"Shows up {suit.Appearances} times in game (configs give it different SortIndex values). Saving any order fixes it.");
			if (suit.ThumbnailPackage == null) notes.Add($"No thumbnail package (Showcase_UI_{suit.Item}_SF.upk) was found.");
			SuitNotesLabel.Text = string.Join("\n", notes);
			SuitNotesLabel.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

			SelectionCountLabel.Text = selected.Count > 1 ? $"{selected.Count} suits selected" : "";
			SelectionCountLabel.Visibility = selected.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
		}

		#endregion
		#region saving

		private void UpdateModifiedState() {
			var changed = HasChanges;

			if (_host?.Profile != null && _host.Deployments != null) {
				var appliedBy = _host.Deployments.SuitOrderProfile;
				OrderStateLabel.Text = appliedBy == null
					? "The game has its original suit order."
					: (appliedBy == _host.Profile.Name ? "" : $"The game has profile '{appliedBy}''s suit order.");
			}

			// nothing to say when the game already has this profile's order
			var stateShown = !changed && OrderStateLabel.Text.Length > 0;
			ModifiedPanel.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
			NotModifiedPanel.Visibility = stateShown ? Visibility.Visible : Visibility.Collapsed;
			StateSeparator.Visibility = changed || stateShown ? Visibility.Visible : Visibility.Collapsed;
		}

		/// <summary>Asks before throwing away unsaved changes. Returns false if the user wants to keep them.</summary>
		public bool ConfirmDiscardChanges() {
			if (!HasChanges) return true;

			if (!Dialog.Confirm("The suit order has unsaved changes.", "Discard them", "Cancel", "Suits Menu")) return false;

			Undo();
			return true;
		}

		private void UndoButton_Click(object sender, RoutedEventArgs e) {
			Undo();
		}

		private void Undo() {
			var selected = SelectedItems();
			_order = CloneOrder(_savedOrder);
			_pendingRestoreOriginal = false;
			_forcedPages.Clear();
			RefreshChips();
			RefreshGrid(selected);
			UpdateModifiedState();
		}

		private async void SaveButton_Click(object sender, RoutedEventArgs e) {
			if (!HasChanges || _model == null) return;

			if (Game.GameInstall.IsRunning()) {
				Dialog.Info("The game is running: the new order shows up next time you start it.", "Suits Menu");
			}

			var profile = _host.Profile;
			var deployments = _host.Deployments;
			var model = _model;
			var restore = _pendingRestoreOriginal;

			// pages to write: the ones changed now, plus the ones this profile already orders
			var pages = ShowcaseModel.Pages.Where(p => PageChanged(p) || profile.SuitOrder?.Get(p) != null).ToList();
			var order = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var page in pages) order[page] = new List<string>(_order[page]);

			SuitOrderWriter.Result result = null;
			var ok = await _host.RunTask(restore ? "Restoring the original suit order..." : "Saving the suit order...", progress => {
				if (restore) {
					SuitOrderWriter.RestoreOriginal(profile.GamePath);
					deployments.SuitOrderProfile = null;
					deployments.Save();
					return;
				}

				progress("Backing up the original order...");
				SaveOriginalOrderSnapshot(profile, deployments);

				progress("Writing SortIndex values...");
				result = SuitOrderWriter.Apply(model, order);
				deployments.SuitOrderProfile = profile.Name;
				deployments.Save();
			});

			if (!ok) {
				RequestReload();
				return;
			}

			if (restore) {
				profile.SuitOrder = null;
			} else {
				var suitOrder = new SuitOrder();
				foreach (var (page, items) in order) suitOrder.Pages[page] = items;
				profile.SuitOrder = suitOrder;
			}
			_host.SaveProfile();

			_pendingRestoreOriginal = false;
			_forcedPages.Clear();
			_order = new(StringComparer.OrdinalIgnoreCase); // drop edits: reload reads the new state from the files
			_savedOrder = new(StringComparer.OrdinalIgnoreCase);
			RequestReload();

			if (result != null && result.Warnings.Count > 0) {
				Dialog.Warning("Saved, with warnings:\n" + string.Join("\n", result.Warnings.Take(10)), "Suits Menu");
			}
			_host.ShowStatus(restore ? "Done! Original suit order restored." : $"Done! Suit order saved ({result?.FilesChanged ?? 0} files changed).");
		}

		/// <summary>The very first time, writes the original order (from the backed up/library files) next to the backups.</summary>
		private static void SaveOriginalOrderSnapshot(Profile profile, Deployments deployments) {
			var backups = new SuitBackups(profile.GamePath);
			var path = Path.Combine(backups.Root, "Original suit order.json");
			if (File.Exists(path)) return;

			var original = ShowcaseModel.Build(profile.GamePath, deployments, _ => null, true);
			var order = new SuitOrder();
			foreach (var page in ShowcaseModel.Pages) order.Pages[page] = original.GetPage(page).Select(s => s.Item).ToList();

			Directory.CreateDirectory(backups.Root);
			order.Save(path);
		}

		#endregion
		#region suit profiles (presets)

		private bool _reactToPresetSelection = true;

		private void RefreshPresets(string select = null) {
			var current = select ?? (PresetsComboBox.SelectedItem as PresetItem)?.Name;

			var items = new List<PresetItem> {
				new PresetItem() { Name = OriginalPresetName, IsOriginal = true },
				new PresetItem() { Name = VanillaFirstPresetName, IsVanillaFirst = true },
			};
			try {
				Directory.CreateDirectory(PresetsDirectory);
				foreach (var file in Directory.GetFiles(PresetsDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
					items.Add(new PresetItem() { Name = Path.GetFileNameWithoutExtension(file), FilePath = file });
				}
			} catch {}

			_reactToPresetSelection = false;
			PresetsComboBox.ItemsSource = items;
			PresetsComboBox.SelectedItem = items.FirstOrDefault(i => i.Name == current) ?? items[0];
			_reactToPresetSelection = true;
			UpdatePresetButtons();
		}

		private void PresetsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if (!_reactToPresetSelection) return;
			UpdatePresetButtons();
		}

		private void UpdatePresetButtons() {
			var item = PresetsComboBox.SelectedItem as PresetItem;
			DeletePresetButton.IsEnabled = item != null && !item.IsBuiltIn;
			LoadPresetButton.IsEnabled = item != null && _model != null;
		}

		private async void LoadPreset_Click(object sender, RoutedEventArgs e) {
			if (PresetsComboBox.SelectedItem is not PresetItem item || _model == null) return;

			var selected = SelectedItems();

			if (item.IsVanillaFirst) {
				// the game's original order, character by character: each character's base game and official DLC
				// suits, then the ones mods add for that character
				var profile = _host.Profile;
				var deployments = _host.Deployments;
				ShowcaseModel original = null;
				var ok = await _host.RunTask("Reading the original suit order...", progress => {
					original = ShowcaseModel.Build(profile.GamePath, deployments, _host.ModName, true);
				});
				if (!ok || original == null) return;

				foreach (var page in ShowcaseModel.Pages) {
					var suits = original.GetPage(page);
					var list = suits.Select(s => s.Character).Distinct(StringComparer.OrdinalIgnoreCase)
						.SelectMany(c => {
							var mine = suits.Where(s => string.Equals(s.Character, c, StringComparison.OrdinalIgnoreCase)).ToList();
							return mine.Where(s => s.IsOfficial).Concat(mine.Where(s => !s.IsOfficial));
						})
						.Select(s => s.Item).ToList();
					_order[page] = Merge(page, list);
				}
				_pendingRestoreOriginal = false;
			} else if (item.IsOriginal) {
				var profile = _host.Profile;
				var deployments = _host.Deployments;
				ShowcaseModel original = null;
				var ok = await _host.RunTask("Reading the original suit order...", progress => {
					original = ShowcaseModel.Build(profile.GamePath, deployments, _host.ModName, true);
				});
				if (!ok || original == null) return;

				foreach (var page in ShowcaseModel.Pages) {
					_order[page] = Merge(page, original.GetPage(page).Select(s => s.Item).ToList());
				}
				_forcedPages.Clear();
				_pendingRestoreOriginal = deployments.SuitOrderProfile != null || ShowcaseModel.Pages.Any(PageChanged);
			} else {
				SuitOrder preset;
				try {
					preset = SuitOrder.Load(item.FilePath);
				} catch (Exception ex) {
					Dialog.Error($"Couldn't read {item.Name}: {ex.Message}", "Suits Menu");
					return;
				}

				foreach (var page in ShowcaseModel.Pages) {
					var list = preset.Get(page);
					if (list != null) _order[page] = Merge(page, list);
				}
				_pendingRestoreOriginal = false;
			}

			RefreshChips();
			RefreshGrid(selected);
			UpdateModifiedState();
			_host.ShowStatus(HasChanges ? $"'{item.Name}' loaded. Press Save to write it to the game." : $"'{item.Name}' is the order the game already has.");
		}

		private void SavePresetAs_Click(object sender, RoutedEventArgs e) {
			if (_model == null) return;

			var initial = (PresetsComboBox.SelectedItem as PresetItem) is { IsBuiltIn: false } p ? p.Name : "";
			var prompt = new TextPromptWindow("Save suit profile", "Suit profile name:", initial, name => {
				if (name.Length == 0) return "Enter a name.";
				if (name.Equals(OriginalPresetName, StringComparison.OrdinalIgnoreCase) || name.Equals(VanillaFirstPresetName, StringComparison.OrdinalIgnoreCase)) return "That name is reserved.";
				if (name.IndexOfAny(Path.GetInvalidFileNameChars()) != -1) return "The name can't contain \\ / : * ? \" < > |";
				return null;
			}) { Owner = Window.GetWindow(this) };

			if (prompt.ShowDialog() != true) return;

			var path = Path.Combine(PresetsDirectory, prompt.Value + ".json");
			if (File.Exists(path) && !Dialog.Confirm($"Replace the suit profile {prompt.Value}?", "Replace", "Cancel", "Suits Menu")) {
				return;
			}

			try {
				Directory.CreateDirectory(PresetsDirectory);
				var order = new SuitOrder();
				foreach (var (page, items) in _order) order.Pages[page] = new List<string>(items);
				order.Save(path);
			} catch (Exception ex) {
				Dialog.Error($"Couldn't save {prompt.Value}: {ex.Message}", "Suits Menu");
				return;
			}

			RefreshPresets(prompt.Value);
			_host.ShowStatus($"Suit profile '{prompt.Value}' saved.");
		}

		private void DeletePreset_Click(object sender, RoutedEventArgs e) {
			if (PresetsComboBox.SelectedItem is not PresetItem item || item.IsBuiltIn) return;
			if (!Dialog.Confirm($"Delete the suit profile {item.Name}?", "Delete", "Cancel", "Suits Menu", DialogIcon.Warning)) return;

			try {
				File.Delete(item.FilePath);
			} catch {}

			RefreshPresets(OriginalPresetName);
		}

		#endregion
	}
}

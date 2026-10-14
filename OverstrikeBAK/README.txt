Overstrike-BAK
==============

A mod manager for Batman: Arkham Knight (PC), forked from Overstrike by Tkachov.
It handles the two kinds of Arkham Knight mods:

  * DLC mods (add-on skins for Batman, the Batmobile and the other characters)
      -> Batman Arkham Knight\DLC\<number>\<ModFolder>   (356474 by default; change it in Settings)
  * BmSDK script mods (folders with a mod.toml)
      -> Batman Arkham Knight\BmGame\Mods\<ModFolder>
         They need BmSDK-AK (https://github.com/Team-BmSDK/BmSDK-AK), which only supports the Steam
         version of the game. If it's missing, Overstrike-BAK offers to install it for you.

Requirements: Windows 10/11 x64 and the .NET 8 Desktop Runtime
(https://dotnet.microsoft.com/download/dotnet/8.0).


Getting started
---------------

1. Put Overstrike-BAK in its own folder (not inside the game folder) and run Overstrike-BAK.exe.
2. On first launch it finds the game (Steam libraries and the usual Epic/GOG places) and creates a
   profile. You can also add the game folder by hand.
3. If mods are already in the game, it offers to import them (nothing is copied).


Mods tab
--------

* Add mods with the + button (.zip, .7z, .rar archives or mod folders), or drop them on the list.
  Adding a mod with the same folder name as one you have replaces it completely: that's how you
  update a mod. If it was installed, the new version is installed in the same place.
* Tick the mods you want installed and untick the others, then press Apply. Revert undoes the ticks.
  "Check all" / "Uncheck all" ticks or unticks every mod shown.
* Presets: save the ticks under a name ("Save current preset"), and load them later to switch
  between sets of mods (then Apply).
* Sort by Name, Date added, Checked or Type. Choose the same one again to reverse it (arrow down):
  Z-A, oldest first, unchecked first, SDK mods first.
* A mod's files live in one place only: in the "Mods Library" folder while it's not installed, in the
  game once it is (the library then keeps only its overstrike-bak.json, to move it back later).
* Mods are named after their Nexus Mods page. When several mods share a page title, or a mod isn't
  linked, a DLC mod with one suit is named like in the Suits Menu.
* Right-click a mod to open its Nexus Mods page, link one, update its info from Nexus Mods, show its
  suits in the Suits Menu, open its folder or delete it. Archives downloaded from Nexus Mods are linked automatically.
* Mods linked to Nexus Mods are checked for updates at launch: a small arrow next to the name means
  a newer version is out; click it to open the page, then add the download to replace the old one.
  The version comes from the Nexus download's file name; when it's not there, the mod is taken to be
  the latest version.

TFC Installer mods (.upk.PackagePatch, .TFCMapping or loose .tfc files, like the Community Patch)
are not supported: install them with TFC Installer (https://www.nexusmods.com/site/mods/588).
(.tfc files that are part of a DLC mod are fine.)


Suits Menu tab
--------------

Shows the main menu Showcase as the game builds it: the same pages (Suits / Batmobiles), the same
order, the same layout (5 suits per row, Batmobiles in a single column) and the same thumbnails. Use the character chips to see one character
at a time.

* Drag suits to reorder them (Ctrl/Shift-click to move several), or use the buttons on the right
  (Ctrl+arrows also work). Then press Save to write the order into the game.
* Suits marked TIE share their SortIndex with another suit. The game orders those by the order it
  loads mod folders: always the same, but it can shift when you add, remove or move mods. Saving
  gives every suit its own SortIndex, which pins the order.
* Every file is backed up before its first change. The "Original (backup)" suit profile puts the
  game's original order back, and "Vanilla first, Mods to bottom" orders each character's suits with
  the base game and official DLC ones first, then the ones mods add (Batman, modded Batman, Robin...).
* Suit profiles: save the current order with "Save as...", load it later with "Load" (then Save).
* Right-click a suit to show its mod in the Mods list, or to rename it: the new name is written into
  the mod's own GFxUI.int, so the game shows it too.


Files
-----

  Mods Library\    your mods (or just their overstrike-bak.json while installed)
  Profiles\        profiles and settings
  Suit Profiles\   saved suit orders
  Mod Profiles\    presets: saved sets of ticked mods
  Backups\         original copies of the config files changed by the Suits Menu
  Cache\           suit thumbnails
  errors.log       details when something goes wrong


License
-------

Overstrike-BAK by excinus. GNU GPLv3 (see LICENSE.txt).
Based on Overstrike by Tkachov: https://github.com/Tkachov/Overstrike
Batman: Arkham Knight is a trademark of its owners.

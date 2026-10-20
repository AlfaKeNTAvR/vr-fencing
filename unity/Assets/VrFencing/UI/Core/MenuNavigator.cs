using System;

namespace VrFencing.UI
{
    /// <summary>The menu's screens. Running is the small panel shown during a Drill; Hidden is during Strip placement.</summary>
    public enum MenuScreen
    {
        Hidden,
        Home,
        Settings,
        Results,
        History,
        Running,

        /// <summary>The grip's angles and the thrust calibration (PER-52), opened from Settings.</summary>
        Grip,

        /// <summary>
        /// "New Strip here?" (PER-91): asked at launch in a room with no saved Strip while another room's Strip was
        /// found. Shown over every page until answered, even with the panels folded away.
        /// </summary>
        NewStripPrompt,

        /// <summary>
        /// The Partner page (PER-80), next to Drills: Host, Join (its keypad), the Bout / Paired Practice switch and
        /// Leave.
        /// </summary>
        Partner,
    }

    /// <summary>
    /// Which screen the menu shows. The Fencer moves between Home, Settings, Results and History; a running Drill
    /// always shows the Running panel, and placing the Strip hides the menu so the off-hand trigger is free for it.
    /// A finished Drill opens its results, unless no Rep was judged.
    ///
    /// The left controller's Menu button folds the panels away (<see cref="IsCollapsed"/>) so they do not block the view
    /// of the Dummy, and opens them again (PER-50). A finished Drill with results opens them, so the results are seen;
    /// the Menu button folds them again.
    ///
    /// Settings opens on the list of groups (PER-70); picking one shows its Settings, and Back returns to the list.
    /// </summary>
    public sealed class MenuNavigator
    {
        /// <summary>The page chosen while no Drill runs.</summary>
        public MenuScreen Page { get; private set; } = MenuScreen.Home;

        /// <summary>The panels are folded away by the Menu button; the chosen page is kept for when they open.</summary>
        public bool IsCollapsed { get; private set; }

        /// <summary>The settings group on view, or null while Settings shows the list of groups (PER-70).</summary>
        public SettingGroup? SettingsGroup { get; private set; }

        /// <summary>
        /// The thrust calibration runs (PER-52): the panels and the laser are put away so the targets are in clear view, and
        /// the off-hand trigger cancels it.
        /// </summary>
        public bool IsCalibrating { get; private set; }

        /// <summary>Starts or ends the thrust calibration; it ends back on the grip page.</summary>
        public void SetCalibrating(bool isCalibrating)
        {
            IsCalibrating = isCalibrating;
            if (!isCalibrating)
            {
                Page = MenuScreen.Grip;
            }
        }

        /// <summary>Folds the panels away, or opens them again.</summary>
        public void ToggleCollapsed()
        {
            IsCollapsed = !IsCollapsed;
        }

        /// <summary>Sets the folded state, e.g. as remembered from the last session.</summary>
        public void SetCollapsed(bool isCollapsed)
        {
            IsCollapsed = isCollapsed;
        }

        /// <summary>
        /// Opens a page. Running and Hidden follow the Drill and the Strip, so they cannot be opened. Settings opens on
        /// the list of groups.
        /// </summary>
        public void Open(MenuScreen page)
        {
            if (page == MenuScreen.Running || page == MenuScreen.Hidden)
            {
                throw new ArgumentException($"{page} is not a page the Fencer opens.", nameof(page));
            }

            Page = page;
            if (page == MenuScreen.Settings)
            {
                SettingsGroup = null;
            }
        }

        /// <summary>Opens Settings on one group's Settings.</summary>
        public void OpenSettingsGroup(SettingGroup group)
        {
            Page = MenuScreen.Settings;
            SettingsGroup = group;
        }

        /// <summary>Back on the settings screen: from a group to the list of groups, from the list to Home.</summary>
        public void GoBackFromSettings()
        {
            if (SettingsGroup.HasValue)
            {
                Page = MenuScreen.Settings;
                SettingsGroup = null;
            }
            else
            {
                Open(MenuScreen.Home);
            }
        }

        /// <param name="hasResults">The run judged at least one Rep, so it was saved and has results to show.</param>
        public void OnDrillFinished(bool hasResults)
        {
            Page = hasResults ? MenuScreen.Results : MenuScreen.Home;
            if (hasResults)
            {
                IsCollapsed = false;
            }
        }

        /// <param name="isAskingForNewStrip">The Strip waits for an answer to "New Strip here?" (<see cref="MenuScreen.NewStripPrompt"/>).</param>
        public MenuScreen GetVisibleScreen(bool isPlacingStrip, bool isDrillRunning, bool isAskingForNewStrip = false)
        {
            if (isPlacingStrip)
            {
                return MenuScreen.Hidden;
            }

            if (isAskingForNewStrip)
            {
                return MenuScreen.NewStripPrompt;
            }

            return isDrillRunning ? MenuScreen.Running : Page;
        }

        /// <summary>
        /// The screen whose panel is on view: <see cref="GetVisibleScreen"/>, or Hidden while collapsed or calibrating.
        /// "New Strip here?" stays on view while collapsed, since it waits for an answer.
        /// </summary>
        public MenuScreen GetShownPanel(bool isPlacingStrip, bool isDrillRunning, bool isAskingForNewStrip = false)
        {
            var visible = GetVisibleScreen(isPlacingStrip, isDrillRunning, isAskingForNewStrip);
            if (visible == MenuScreen.NewStripPrompt)
            {
                return visible;
            }

            return IsCollapsed || IsCalibrating ? MenuScreen.Hidden : visible;
        }

        /// <summary>The menu laser is out while a panel is on view: none to point at while folded or placing the Strip.</summary>
        public bool IsPointerShown(bool isPlacingStrip, bool isDrillRunning, bool isAskingForNewStrip = false)
        {
            return GetShownPanel(isPlacingStrip, isDrillRunning, isAskingForNewStrip) != MenuScreen.Hidden;
        }
    }

    /// <summary>Which slice of a long list one page of a panel shows.</summary>
    public readonly struct PageWindow
    {
        public PageWindow(int pageIndex, int pageCount, int firstItem, int itemCount)
        {
            PageIndex = pageIndex;
            PageCount = pageCount;
            FirstItem = firstItem;
            ItemCount = itemCount;
        }

        /// <summary>Zero-based, clamped to the pages that exist.</summary>
        public int PageIndex { get; }

        /// <summary>At least one, even for an empty list.</summary>
        public int PageCount { get; }

        public int FirstItem { get; }

        /// <summary>Items on this page.</summary>
        public int ItemCount { get; }

        public bool HasPrevious => PageIndex > 0;

        public bool HasNext => PageIndex < PageCount - 1;

        /// <summary>The page <paramref name="requestedPage"/> of a list, clamped to the pages that exist.</summary>
        public static PageWindow For(int totalItems, int pageSize, int requestedPage)
        {
            if (totalItems < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalItems), totalItems, "Must not be negative.");
            }

            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Must be at least one.");
            }

            var pageCount = Math.Max(1, (totalItems + pageSize - 1) / pageSize);
            var pageIndex = Math.Min(Math.Max(requestedPage, 0), pageCount - 1);
            var first = pageIndex * pageSize;
            return new PageWindow(pageIndex, pageCount, first, Math.Min(pageSize, totalItems - first));
        }
    }
}

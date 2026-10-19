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
    }

    /// <summary>
    /// Which screen the menu shows. The Fencer moves between Home, Settings, Results and History; a running Drill
    /// always shows the Running panel, and placing the Strip hides the menu so the off-hand trigger is free for it.
    /// A finished Drill opens its results, unless no Rep was judged.
    /// </summary>
    public sealed class MenuNavigator
    {
        /// <summary>The page chosen while no Drill runs.</summary>
        public MenuScreen Page { get; private set; } = MenuScreen.Home;

        /// <summary>Opens a page. Running and Hidden follow the Drill and the Strip, so they cannot be opened.</summary>
        public void Open(MenuScreen page)
        {
            if (page == MenuScreen.Running || page == MenuScreen.Hidden)
            {
                throw new ArgumentException($"{page} is not a page the Fencer opens.", nameof(page));
            }

            Page = page;
        }

        /// <param name="hasResults">The run judged at least one Rep, so it was saved and has results to show.</param>
        public void OnDrillFinished(bool hasResults)
        {
            Page = hasResults ? MenuScreen.Results : MenuScreen.Home;
        }

        public MenuScreen GetVisibleScreen(bool isPlacingStrip, bool isDrillRunning)
        {
            if (isPlacingStrip)
            {
                return MenuScreen.Hidden;
            }

            return isDrillRunning ? MenuScreen.Running : Page;
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

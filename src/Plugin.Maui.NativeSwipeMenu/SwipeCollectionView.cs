using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Plugin.Maui.NativeSwipeMenu;

/// <summary>
/// A CollectionView whose rows get platform-native swipe actions.
/// <para>
/// iOS / Mac Catalyst: rendered by its own handler on a UICollectionView list layout, so swipes
/// are real UIContextualActions (spring physics, full-swipe, haptics, system styling). That handler
/// supports a flat list only: ItemsSource, ItemTemplate (including selectors), EmptyView /
/// EmptyViewTemplate, SelectionMode None/Single, SelectedItem and SelectionChangedCommand.
/// Grouping, headers/footers, grid layouts and RemainingItemsThreshold aren't supported there.
/// </para>
/// <para>
/// Android: the stock CollectionView with a native ItemTouchHelper attached. A swipe runs the
/// first available action for that side (the Gmail pattern), so only one action per side shows.
/// </para>
/// </summary>
public class SwipeCollectionView : CollectionView
{
	public static readonly BindableProperty PerformsFirstActionOnFullSwipeProperty =
		BindableProperty.Create(nameof(PerformsFirstActionOnFullSwipe), typeof(bool), typeof(SwipeCollectionView), true);

	public static readonly BindableProperty ShowsSeparatorsProperty =
		BindableProperty.Create(nameof(ShowsSeparators), typeof(bool), typeof(SwipeCollectionView), true);

	public SwipeCollectionView()
	{
		var leading = new ObservableCollection<SwipeAction>();
		var trailing = new ObservableCollection<SwipeAction>();
		leading.CollectionChanged += OnActionsChanged;
		trailing.CollectionChanged += OnActionsChanged;
		LeadingActions = leading;
		TrailingActions = trailing;
	}

	/// <summary>Actions revealed by swiping toward the trailing edge (left-to-right in LTR).</summary>
	public IList<SwipeAction> LeadingActions { get; }

	/// <summary>Actions revealed by swiping toward the leading edge (right-to-left in LTR). This is where Delete goes.</summary>
	public IList<SwipeAction> TrailingActions { get; }

	/// <summary>iOS: a long swipe runs the first action, like Mail. Android always runs on a full swipe.</summary>
	public bool PerformsFirstActionOnFullSwipe
	{
		get => (bool)GetValue(PerformsFirstActionOnFullSwipeProperty);
		set => SetValue(PerformsFirstActionOnFullSwipeProperty, value);
	}

	/// <summary>iOS: native list separators. Turn this off if the item template draws its own.</summary>
	public bool ShowsSeparators
	{
		get => (bool)GetValue(ShowsSeparatorsProperty);
		set => SetValue(ShowsSeparatorsProperty, value);
	}

	public event EventHandler<SwipeActionInvokedEventArgs>? SwipeActionInvoked;

	internal SwipeAction[] GetAvailableActions(object item, bool trailing) =>
		(trailing ? TrailingActions : LeadingActions).Where(a => a.IsAvailableFor(item)).ToArray();

	internal void InvokeAction(SwipeAction action, object item)
	{
		action.Invoke(item);
		SwipeActionInvoked?.Invoke(this, new SwipeActionInvokedEventArgs(item, action));
	}

	void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		// Actions live in the logical tree so they inherit BindingContext and resolve resources.
		if (e.OldItems is not null)
			foreach (SwipeAction action in e.OldItems)
				RemoveLogicalChild(action);

		if (e.NewItems is not null)
			foreach (SwipeAction action in e.NewItems)
				AddLogicalChild(action);
	}
}

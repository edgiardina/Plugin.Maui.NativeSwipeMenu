using System.Collections.Specialized;
using Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.NativeSwipeMenu;

/// <summary>
/// Renders <see cref="SwipeCollectionView"/> as a UICollectionView with a list layout. Only list
/// sections + UICollectionViewListCell get UIKit's real swipe actions, which is why this doesn't
/// reuse MAUI's CollectionView handler (its cells are plain UICollectionViewCells).
/// </summary>
public partial class SwipeCollectionViewHandler : ViewHandler<SwipeCollectionView, UICollectionView>
{
	public static readonly IPropertyMapper<SwipeCollectionView, SwipeCollectionViewHandler> Mapper =
		new PropertyMapper<SwipeCollectionView, SwipeCollectionViewHandler>(ViewMapper)
		{
			[nameof(ItemsView.ItemsSource)] = MapItemsSource,
			[nameof(ItemsView.ItemTemplate)] = MapItemTemplate,
			[nameof(ItemsView.EmptyView)] = MapEmptyView,
			[nameof(ItemsView.EmptyViewTemplate)] = MapEmptyView,
			[nameof(SwipeCollectionView.ShowsSeparators)] = MapShowsSeparators,
			[nameof(SelectableItemsView.SelectionMode)] = MapSelectionMode,
			[nameof(SelectableItemsView.SelectedItem)] = MapSelectedItem,
		};

	const string TextCellReuseId = "NativeSwipeMenu.TextCell";

	readonly List<object> _items = [];
	readonly Dictionary<DataTemplate, string> _reuseIds = [];
	INotifyCollectionChanged? _observedSource;
	UIView? _emptyPlatformView;
	View? _emptyMauiView;
	bool _updatingSelection;

	public SwipeCollectionViewHandler() : base(Mapper)
	{
	}

	public SwipeCollectionViewHandler(IPropertyMapper? mapper) : base(mapper ?? Mapper)
	{
	}

	internal IReadOnlyList<object> Items => _items;

	ILogger? Logger => MauiContext?.Services.GetService<ILogger<SwipeCollectionViewHandler>>();

	// The typed VirtualView/PlatformView getters throw once they're null, and MAUI clears PlatformView
	// *before* it calls DisconnectHandler. Anything that can run during or after disconnect (the
	// disconnect itself, deferred reloads, UIKit callbacks) has to go through these instead.
	SwipeCollectionView? CurrentView => ((IElementHandler)this).VirtualView as SwipeCollectionView;
	UICollectionView? CurrentPlatformView => ((IElementHandler)this).PlatformView as UICollectionView;

	protected override UICollectionView CreatePlatformView()
	{
		var collectionView = new UICollectionView(CoreGraphics.CGRect.Empty, CreateLayout())
		{
			AlwaysBounceVertical = true,
			DataSource = new SwipeListDataSource(this),
			Delegate = new SwipeListDelegate(this),
		};
		collectionView.RegisterClassForCell(typeof(SwipeListCell), TextCellReuseId);
		return collectionView;
	}

	protected override void ConnectHandler(UICollectionView platformView)
	{
		base.ConnectHandler(platformView);
		PreloadActionImages();
	}

	protected override void DisconnectHandler(UICollectionView platformView)
	{
		if (_observedSource is not null)
			_observedSource.CollectionChanged -= OnItemsSourceCollectionChanged;
		_observedSource = null;
		ClearEmptyView(platformView);
		base.DisconnectHandler(platformView);
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		// A scroll view has no natural size, so fill whatever we're given. When unconstrained,
		// fall back to the content size so it still shows up inside a StackLayout.
		var content = PlatformView.CollectionViewLayout.CollectionViewContentSize;
		var width = double.IsInfinity(widthConstraint) ? content.Width : widthConstraint;
		var height = double.IsInfinity(heightConstraint) ? content.Height : heightConstraint;

		if (VirtualView.WidthRequest >= 0)
			width = Math.Min(VirtualView.WidthRequest, widthConstraint);
		if (VirtualView.HeightRequest >= 0)
			height = Math.Min(VirtualView.HeightRequest, heightConstraint);

		return new Size(width, height);
	}

	UICollectionViewLayout CreateLayout()
	{
		var weakHandler = new WeakReference<SwipeCollectionViewHandler>(this);

		var config = new UICollectionLayoutListConfiguration(UICollectionLayoutListAppearance.Plain)
		{
			ShowsSeparators = CurrentView?.ShowsSeparators ?? true,
			BackgroundColor = UIColor.Clear,
			TrailingSwipeActionsConfigurationProvider = indexPath =>
				weakHandler.TryGetTarget(out var handler) ? handler.BuildSwipeConfiguration(indexPath, trailing: true) : null!,
			LeadingSwipeActionsConfigurationProvider = indexPath =>
				weakHandler.TryGetTarget(out var handler) ? handler.BuildSwipeConfiguration(indexPath, trailing: false) : null!,
		};

		return UICollectionViewCompositionalLayout.GetLayout(config);
	}

	UISwipeActionsConfiguration? BuildSwipeConfiguration(NSIndexPath indexPath, bool trailing)
	{
		if (CurrentView is not { } view || indexPath.Row < 0 || indexPath.Row >= _items.Count)
			return null;

		var item = _items[indexPath.Row];
		var actions = view.GetAvailableActions(item, trailing);
		if (actions.Length == 0)
			return null;

		var configuration = UISwipeActionsConfiguration.FromActions(
			actions.Select(action => CreateContextualAction(view, action, item)).ToArray());
		configuration.PerformsFirstActionWithFullSwipe = view.PerformsFirstActionOnFullSwipe;
		return configuration;
	}

	UIContextualAction CreateContextualAction(SwipeCollectionView view, SwipeAction action, object item)
	{
		var style = action.Style == SwipeActionStyle.Destructive
			? UIContextualActionStyle.Destructive
			: UIContextualActionStyle.Normal;

		var contextualAction = UIContextualAction.FromContextualActionStyle(style, action.Text, (_, _, completion) =>
		{
			try
			{
				view.InvokeAction(action, item);
				completion(true);
			}
			catch (Exception ex)
			{
				Logger?.LogError(ex, "Swipe action {Action} failed", action.Text);
				completion(false);
			}
		});

		if (action.BackgroundColor is { } color)
			contextualAction.BackgroundColor = color.ToPlatform();

		contextualAction.Image = ResolveImage(action);
		return contextualAction;
	}

	UIImage? ResolveImage(SwipeAction action)
	{
		if (!string.IsNullOrEmpty(action.SystemImage) && UIImage.GetSystemImage(action.SystemImage) is { } symbol)
			return symbol;

		if (action.PlatformImage is UIImage image)
			return image;

		// Not loaded yet (or the source changed). Kick off a load so the next swipe has it.
		if (action.IconImageSource is not null)
			_ = LoadActionImageAsync(action);

		return null;
	}

	void PreloadActionImages()
	{
		if (CurrentView is not { } view)
			return;

		foreach (var action in view.LeadingActions.Concat(view.TrailingActions))
			if (action.IconImageSource is not null && action.PlatformImage is null)
				_ = LoadActionImageAsync(action);
	}

	async Task LoadActionImageAsync(SwipeAction action)
	{
		if (MauiContext is null || action.IconImageSource is not { } source)
			return;

		try
		{
			var result = await source.GetPlatformImageAsync(MauiContext);
			if (ReferenceEquals(source, action.IconImageSource))
				action.PlatformImage = result?.Value;
		}
		catch (Exception ex)
		{
			Logger?.LogWarning(ex, "Couldn't load the icon for swipe action {Action}", action.Text);
		}
	}

	// ---- Items ----

	static void MapItemsSource(SwipeCollectionViewHandler handler, SwipeCollectionView view)
	{
		if (handler._observedSource is not null)
			handler._observedSource.CollectionChanged -= handler.OnItemsSourceCollectionChanged;

		handler._observedSource = view.ItemsSource as INotifyCollectionChanged;
		if (handler._observedSource is not null)
			handler._observedSource.CollectionChanged += handler.OnItemsSourceCollectionChanged;

		handler.ReloadAll();
	}

	static void MapItemTemplate(SwipeCollectionViewHandler handler, SwipeCollectionView view) =>
		handler.ReloadAll();

	void ReloadAll()
	{
		// Can arrive via BeginInvokeOnMainThread after the page has been popped.
		if (CurrentPlatformView is not { } collectionView)
			return;

		_items.Clear();
		if (CurrentView?.ItemsSource is { } source)
			_items.AddRange(source.Cast<object>());

		collectionView.ReloadData();
		UpdateEmptyView();
		SyncNativeSelection();
	}

	void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (!MainThread.IsMainThread)
		{
			// The event args describe a state we can't apply in order from here, so just resync.
			MainThread.BeginInvokeOnMainThread(ReloadAll);
			return;
		}

		if (CurrentPlatformView is not { } collectionView)
			return;

		// Batch updates on a collection view that isn't on screen can throw consistency errors.
		if (collectionView.Window is null || !TryApplyIncrementalChange(e))
		{
			ReloadAll();
			return;
		}

		UpdateEmptyView();
	}

	bool TryApplyIncrementalChange(NotifyCollectionChangedEventArgs e)
	{
		var collectionView = PlatformView;

		switch (e.Action)
		{
			case NotifyCollectionChangedAction.Add when e.NewItems is { Count: > 0 } added
				&& e.NewStartingIndex >= 0 && e.NewStartingIndex <= _items.Count:
				collectionView.PerformBatchUpdates(() =>
				{
					_items.InsertRange(e.NewStartingIndex, added.Cast<object>());
					collectionView.InsertItems(IndexPaths(e.NewStartingIndex, added.Count));
				}, null);
				return true;

			case NotifyCollectionChangedAction.Remove when e.OldItems is { Count: > 0 } removed
				&& e.OldStartingIndex >= 0 && e.OldStartingIndex + removed.Count <= _items.Count:
				collectionView.PerformBatchUpdates(() =>
				{
					_items.RemoveRange(e.OldStartingIndex, removed.Count);
					collectionView.DeleteItems(IndexPaths(e.OldStartingIndex, removed.Count));
				}, null);
				return true;

			case NotifyCollectionChangedAction.Replace when e.NewItems is { } replaced && e.OldItems is { } old
				&& replaced.Count == old.Count && e.NewStartingIndex >= 0
				&& e.NewStartingIndex + replaced.Count <= _items.Count:
				collectionView.PerformBatchUpdates(() =>
				{
					for (var i = 0; i < replaced.Count; i++)
						_items[e.NewStartingIndex + i] = replaced[i]!;
					collectionView.ReloadItems(IndexPaths(e.NewStartingIndex, replaced.Count));
				}, null);
				return true;

			case NotifyCollectionChangedAction.Move when e.OldItems is { Count: 1 }
				&& e.OldStartingIndex >= 0 && e.OldStartingIndex < _items.Count
				&& e.NewStartingIndex >= 0 && e.NewStartingIndex < _items.Count:
				collectionView.PerformBatchUpdates(() =>
				{
					var moved = _items[e.OldStartingIndex];
					_items.RemoveAt(e.OldStartingIndex);
					_items.Insert(e.NewStartingIndex, moved);
					collectionView.MoveItem(
						NSIndexPath.FromItemSection(e.OldStartingIndex, 0),
						NSIndexPath.FromItemSection(e.NewStartingIndex, 0));
				}, null);
				return true;

			default:
				return false;
		}
	}

	static NSIndexPath[] IndexPaths(int start, int count) =>
		Enumerable.Range(start, count).Select(i => NSIndexPath.FromItemSection(i, 0)).ToArray();

	internal UICollectionViewCell GetCell(UICollectionView collectionView, NSIndexPath indexPath)
	{
		// UIKit can still ask for cells while the page is being torn down.
		if (CurrentView is not { } view || MauiContext is null || indexPath.Row >= _items.Count)
			return (UICollectionViewCell)collectionView.DequeueReusableCell(TextCellReuseId, indexPath);

		var item = _items[indexPath.Row];
		var template = view.ItemTemplate is DataTemplateSelector selector
			? selector.SelectTemplate(item, view)
			: view.ItemTemplate;

		var reuseId = template is null ? TextCellReuseId : ReuseIdFor(collectionView, template);
		var cell = (SwipeListCell)collectionView.DequeueReusableCell(reuseId, indexPath);
		cell.Bind(item, template, view, MauiContext);
		return cell;
	}

	string ReuseIdFor(UICollectionView collectionView, DataTemplate template)
	{
		if (!_reuseIds.TryGetValue(template, out var reuseId))
		{
			reuseId = $"NativeSwipeMenu.TemplateCell.{_reuseIds.Count}";
			_reuseIds[template] = reuseId;
			collectionView.RegisterClassForCell(typeof(SwipeListCell), reuseId);
		}

		return reuseId;
	}

	// ---- Separators ----

	static void MapShowsSeparators(SwipeCollectionViewHandler handler, SwipeCollectionView view) =>
		handler.PlatformView.SetCollectionViewLayout(handler.CreateLayout(), false);

	// ---- Selection ----

	static void MapSelectionMode(SwipeCollectionViewHandler handler, SwipeCollectionView view)
	{
		handler.PlatformView.AllowsSelection = view.SelectionMode != SelectionMode.None;
		handler.PlatformView.AllowsMultipleSelection = view.SelectionMode == SelectionMode.Multiple;
		handler.SyncNativeSelection();
	}

	static void MapSelectedItem(SwipeCollectionViewHandler handler, SwipeCollectionView view) =>
		handler.SyncNativeSelection();

	void SyncNativeSelection()
	{
		if (_updatingSelection || CurrentView is not { } view || CurrentPlatformView is not { } collectionView
			|| view.SelectionMode != SelectionMode.Single)
			return;

		var selected = collectionView.GetIndexPathsForSelectedItems() ?? [];
		var target = view.SelectedItem is null ? -1 : _items.IndexOf(view.SelectedItem);

		foreach (var path in selected)
			if (path.Row != target)
				collectionView.DeselectItem(path, true);

		if (target >= 0 && !selected.Any(p => p.Row == target))
			collectionView.SelectItem(NSIndexPath.FromItemSection(target, 0), false, UICollectionViewScrollPosition.None);
	}

	internal void OnItemSelected(NSIndexPath indexPath, bool selected)
	{
		if (CurrentView is not { } view || indexPath.Row >= _items.Count)
			return;

		var item = _items[indexPath.Row];
		_updatingSelection = true;
		try
		{
			if (view.SelectionMode == SelectionMode.Single && selected)
				view.SelectedItem = item;
			else if (view.SelectionMode == SelectionMode.Multiple)
			{
				if (selected && !view.SelectedItems.Contains(item))
					view.SelectedItems.Add(item);
				else if (!selected)
					view.SelectedItems.Remove(item);
			}
		}
		finally
		{
			_updatingSelection = false;
		}
	}

	// ---- Empty view ----

	static void MapEmptyView(SwipeCollectionViewHandler handler, SwipeCollectionView view)
	{
		handler.ClearEmptyView(handler.CurrentPlatformView);
		handler.UpdateEmptyView();
	}

	void UpdateEmptyView()
	{
		if (CurrentView is not { } view || CurrentPlatformView is not { } collectionView)
			return;

		var showEmpty = _items.Count == 0 && (view.EmptyView is not null || view.EmptyViewTemplate is not null);
		collectionView.BackgroundView = showEmpty ? _emptyPlatformView ??= CreateEmptyView(view) : null;
	}

	UIView CreateEmptyView(SwipeCollectionView view)
	{
		View? mauiView = null;

		if (view.EmptyViewTemplate is { } template)
		{
			mauiView = template.CreateContent() as View;
			if (mauiView is not null && view.EmptyView is not null)
				mauiView.BindingContext = view.EmptyView;
		}
		else if (view.EmptyView is View emptyView)
		{
			mauiView = emptyView;
		}

		if (mauiView is null)
		{
			return new UILabel
			{
				Text = view.EmptyView?.ToString(),
				TextAlignment = UITextAlignment.Center,
				TextColor = UIColor.SecondaryLabel,
				Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body),
				Lines = 0,
			};
		}

		if (mauiView.Parent is null)
			view.AddLogicalChild(mauiView);

		_emptyMauiView = mauiView;
		return new MauiViewContainer(mauiView, MauiContext!);
	}

	// Takes the platform view explicitly: during DisconnectHandler the handler's own PlatformView
	// is already null (and its getter throws), so the caller passes the one it was handed.
	void ClearEmptyView(UICollectionView? platformView)
	{
		if (_emptyMauiView is not null && CurrentView is { } view && ReferenceEquals(_emptyMauiView.Parent, view))
			view.RemoveLogicalChild(_emptyMauiView);

		_emptyMauiView = null;
		_emptyPlatformView?.RemoveFromSuperview();
		_emptyPlatformView = null;
		if (platformView is not null)
			platformView.BackgroundView = null;
	}

	sealed class SwipeListDataSource(SwipeCollectionViewHandler handler) : UICollectionViewDataSource
	{
		readonly WeakReference<SwipeCollectionViewHandler> _handler = new(handler);

		public override nint NumberOfSections(UICollectionView collectionView) => 1;

		public override nint GetItemsCount(UICollectionView collectionView, nint section) =>
			_handler.TryGetTarget(out var handler) ? handler.Items.Count : 0;

		public override UICollectionViewCell GetCell(UICollectionView collectionView, NSIndexPath indexPath) =>
			_handler.TryGetTarget(out var handler)
				? handler.GetCell(collectionView, indexPath)
				: (UICollectionViewCell)collectionView.DequeueReusableCell(TextCellReuseId, indexPath);
	}

	sealed class SwipeListDelegate(SwipeCollectionViewHandler handler) : UICollectionViewDelegate
	{
		readonly WeakReference<SwipeCollectionViewHandler> _handler = new(handler);

		public override void ItemSelected(UICollectionView collectionView, NSIndexPath indexPath)
		{
			if (_handler.TryGetTarget(out var handler))
				handler.OnItemSelected(indexPath, selected: true);
		}

		public override void ItemDeselected(UICollectionView collectionView, NSIndexPath indexPath)
		{
			if (_handler.TryGetTarget(out var handler))
				handler.OnItemSelected(indexPath, selected: false);
		}
	}
}

using System.Collections;
using Android.Graphics;
using Android.Graphics.Drawables;
using AndroidX.RecyclerView.Widget;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Platform;
using AColor = Android.Graphics.Color;
using APaint = Android.Graphics.Paint;
using ARectF = Android.Graphics.RectF;
using AView = Android.Views.View;

namespace Plugin.Maui.NativeSwipeMenu;

/// <summary>
/// The stock CollectionView handler with a native ItemTouchHelper attached. Swiping a row past the
/// threshold runs the first available action for that side and paints its color and icon behind
/// the row while dragging (the Gmail / Material pattern).
/// </summary>
public partial class SwipeCollectionViewHandler : CollectionViewHandler
{
	// Material 3 error red and a neutral gray, used when an action has no BackgroundColor.
	static readonly AColor DestructiveColor = AColor.ParseColor("#B3261E");
	static readonly AColor NormalColor = AColor.ParseColor("#5F6368");

	// Destructive commands often remove the item after an await (DB write, confirmation). Wait this
	// long before deciding the app kept the item and the row should slide back.
	const int DestructiveRestoreDelayMs = 750;

	ItemTouchHelper? _touchHelper;

	SwipeCollectionView? SwipeView => VirtualView as SwipeCollectionView;

	ILogger? Logger => MauiContext?.Services.GetService<ILogger<SwipeCollectionViewHandler>>();

	protected override void ConnectHandler(RecyclerView platformView)
	{
		base.ConnectHandler(platformView);
		_touchHelper = new ItemTouchHelper(new SwipeCallback(this));
		_touchHelper.AttachToRecyclerView(platformView);
		PreloadActionImages();
	}

	protected override void DisconnectHandler(RecyclerView platformView)
	{
		_touchHelper?.AttachToRecyclerView(null);
		_touchHelper = null;
		base.DisconnectHandler(platformView);
	}

	object? ItemFor(RecyclerView.ViewHolder holder)
	{
		if (holder is not TemplatedItemViewHolder { View.BindingContext: { } item } || SwipeView is not { } view)
			return null;

		// Header/footer templates use the same holder type; they're never swipeable.
		return ReferenceEquals(item, view.Header) || ReferenceEquals(item, view.Footer) ? null : item;
	}

	SwipeAction? ActionFor(object item, int direction)
	{
		if (SwipeView is not { } view)
			return null;

		var rtl = PlatformView.LayoutDirection == Android.Views.LayoutDirection.Rtl;
		var trailing = (direction == ItemTouchHelper.Left) != rtl;
		return view.GetAvailableActions(item, trailing).FirstOrDefault();
	}

	void OnSwiped(RecyclerView.ViewHolder holder, int direction)
	{
		if (SwipeView is not { } view || ItemFor(holder) is not { } item || ActionFor(item, direction) is not { } action)
		{
			RestoreIfStillPresent(ItemFor(holder));
			return;
		}

		try
		{
			view.InvokeAction(action, item);
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Swipe action {Action} failed", action.Text);
		}

		var delay = action.Style == SwipeActionStyle.Destructive ? DestructiveRestoreDelayMs : 0;
		PlatformView.PostDelayed(() => RestoreIfStillPresent(item), delay);
	}

	void RestoreIfStillPresent(object? item)
	{
		if (item is null || SwipeView is not { ItemsSource: { } source } view || PlatformView?.GetAdapter() is not { } adapter)
			return;

		var index = IndexOf(source, item);
		if (index >= 0)
			adapter.NotifyItemChanged(index + (view.Header is null ? 0 : 1));
	}

	static int IndexOf(IEnumerable source, object item)
	{
		if (source is IList list)
			return list.IndexOf(item);

		var index = 0;
		foreach (var candidate in source)
		{
			if (Equals(candidate, item))
				return index;
			index++;
		}

		return -1;
	}

	void PreloadActionImages()
	{
		if (SwipeView is not { } view || MauiContext is null)
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
			if (result?.Value is { } drawable && ReferenceEquals(source, action.IconImageSource))
			{
				var tinted = drawable.Mutate();
				tinted.SetTint(AColor.White);
				action.PlatformImage = tinted;
			}
		}
		catch (Exception ex)
		{
			Logger?.LogWarning(ex, "Couldn't load the icon for swipe action {Action}", action.Text);
		}
	}

	void DrawActionBackground(Canvas canvas, AView itemView, float dX, SwipeAction action)
	{
		var density = itemView.Resources?.DisplayMetrics?.Density ?? 1f;
		var swipingLeft = dX < 0;

		var bounds = swipingLeft
			? new ARectF(itemView.Right + dX, itemView.Top, itemView.Right, itemView.Bottom)
			: new ARectF(itemView.Left, itemView.Top, itemView.Left + dX, itemView.Bottom);

		var color = action.BackgroundColor?.ToPlatform()
			?? (action.Style == SwipeActionStyle.Destructive ? DestructiveColor : NormalColor);

		canvas.Save();
		canvas.ClipRect(bounds);

		using (var background = new APaint { Color = color })
			canvas.DrawRect(bounds, background);

		var margin = 20 * density;

		if (action.PlatformImage is Drawable icon)
		{
			var size = (int)(24 * density);
			var top = itemView.Top + (itemView.Height - size) / 2;
			var left = swipingLeft ? (int)(itemView.Right - margin - size) : (int)(itemView.Left + margin);
			icon.SetBounds(left, top, left + size, top + size);
			icon.Draw(canvas);
		}
		else if (!string.IsNullOrEmpty(action.Text))
		{
			using var text = new APaint(PaintFlags.AntiAlias)
			{
				Color = AColor.White,
				TextSize = 14 * (itemView.Resources?.DisplayMetrics?.ScaledDensity ?? density),
				TextAlign = swipingLeft ? APaint.Align.Right! : APaint.Align.Left!,
			};
			text.SetTypeface(Typeface.DefaultBold);
			var baseline = itemView.Top + itemView.Height / 2f - (text.Descent() + text.Ascent()) / 2f;
			var x = swipingLeft ? itemView.Right - margin : itemView.Left + margin;
			canvas.DrawText(action.Text, x, baseline, text);
		}

		canvas.Restore();
	}

	sealed class SwipeCallback(SwipeCollectionViewHandler handler) : ItemTouchHelper.SimpleCallback(0, 0)
	{
		readonly WeakReference<SwipeCollectionViewHandler> _handler = new(handler);

		public override int GetSwipeDirs(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
		{
			if (!_handler.TryGetTarget(out var handler) || handler.ItemFor(viewHolder) is not { } item)
				return 0;

			var directions = 0;
			if (handler.ActionFor(item, ItemTouchHelper.Left) is not null)
				directions |= ItemTouchHelper.Left;
			if (handler.ActionFor(item, ItemTouchHelper.Right) is not null)
				directions |= ItemTouchHelper.Right;
			return directions;
		}

		public override bool OnMove(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder, RecyclerView.ViewHolder target) => false;

		public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction)
		{
			if (_handler.TryGetTarget(out var handler))
				handler.OnSwiped(viewHolder, direction);
		}

		public override void OnChildDraw(Canvas c, RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder,
			float dX, float dY, int actionState, bool isCurrentlyActive)
		{
			if (actionState == ItemTouchHelper.ActionStateSwipe && dX != 0
				&& _handler.TryGetTarget(out var handler)
				&& handler.ItemFor(viewHolder) is { } item
				&& handler.ActionFor(item, dX < 0 ? ItemTouchHelper.Left : ItemTouchHelper.Right) is { } action)
			{
				handler.DrawActionBackground(c, viewHolder.ItemView, dX, action);
			}

			base.OnChildDraw(c, recyclerView, viewHolder, dX, dY, actionState, isCurrentlyActive);
		}
	}
}

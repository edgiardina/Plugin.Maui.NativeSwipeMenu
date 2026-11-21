using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.NativeSwipeMenu;

/// <summary>
/// A real UICollectionViewListCell (required for native swipe actions) hosting a MAUI item template.
/// With no template it falls back to the system list content configuration showing ToString().
/// </summary>
sealed class SwipeListCell : UICollectionViewListCell
{
	View? _view;
	UIView? _platformView;
	DataTemplate? _template;
	WeakReference<SwipeCollectionView>? _owner;
	bool _sizeInvalidationQueued;

	[Export("initWithFrame:")]
	public SwipeListCell(CGRect frame) : base(frame)
	{
	}

	public void Bind(object item, DataTemplate? template, SwipeCollectionView owner, IMauiContext mauiContext)
	{
		if (template is null)
		{
			RemoveTemplateView();
			var content = UIListContentConfiguration.CellConfiguration;
			content.Text = item?.ToString();
			ContentConfiguration = content;
			return;
		}

		if (_view is null || _template != template)
		{
			RemoveTemplateView();
			ContentConfiguration = null;

			_view = template.CreateContent() as View
				?? throw new InvalidOperationException("SwipeCollectionView item templates must create a View.");
			_template = template;
			_owner = new WeakReference<SwipeCollectionView>(owner);

			// Logical parent lets RelativeSource AncestorType bindings and DynamicResources resolve.
			owner.AddLogicalChild(_view);
			_view.MeasureInvalidated += OnTemplateMeasureInvalidated;
			// ToPlatform can hand back a wrapper (shadow/clip), so keep the view we actually added.
			_platformView = _view.ToPlatform(mauiContext);
			ContentView.AddSubview(_platformView);
		}

		_view.BindingContext = item;
		SetNeedsLayout();
	}

	public override UICollectionViewLayoutAttributes PreferredLayoutAttributesFittingAttributes(UICollectionViewLayoutAttributes layoutAttributes)
	{
		if (_view is not IView view)
			return base.PreferredLayoutAttributesFittingAttributes(layoutAttributes);

		var attributes = (UICollectionViewLayoutAttributes)layoutAttributes.Copy();
		var width = attributes.Frame.Width;
		var size = view.Measure(width, double.PositiveInfinity);
		attributes.Frame = new CGRect(attributes.Frame.Location, new CGSize(width, Math.Ceiling(size.Height)));
		return attributes;
	}

	public override void LayoutSubviews()
	{
		base.LayoutSubviews();

		if (_view is IView view)
		{
			var bounds = ContentView.Bounds;
			view.Measure(bounds.Width, bounds.Height);
			view.Arrange(new Rect(0, 0, bounds.Width, bounds.Height));
		}
	}

	void OnTemplateMeasureInvalidated(object? sender, EventArgs e)
	{
		// Bindings landing after the first layout can change the row height. Coalesce the
		// flood of invalidations from a single bind into one resize request.
		if (_sizeInvalidationQueued)
			return;

		_sizeInvalidationQueued = true;
		CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
		{
			_sizeInvalidationQueued = false;
			if (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16))
				InvalidateIntrinsicContentSize();
			else
				(Superview as UICollectionView)?.CollectionViewLayout.InvalidateLayout();
			SetNeedsLayout();
		});
	}

	void RemoveTemplateView()
	{
		if (_view is null)
			return;

		_view.MeasureInvalidated -= OnTemplateMeasureInvalidated;
		_platformView?.RemoveFromSuperview();
		_platformView = null;

		if (_owner?.TryGetTarget(out var owner) == true)
			owner.RemoveLogicalChild(_view);

		_view.Handler?.DisconnectHandler();
		_view = null;
		_template = null;
	}
}

/// <summary>Hosts a MAUI view (the empty view) as a plain UIView that measures and arranges it on layout.</summary>
sealed class MauiViewContainer : UIView
{
	readonly IView _view;

	public MauiViewContainer(View view, IMauiContext mauiContext)
	{
		_view = view;
		AddSubview(view.ToPlatform(mauiContext));
	}

	public override void LayoutSubviews()
	{
		base.LayoutSubviews();
		var bounds = Bounds;
		_view.Measure(bounds.Width, bounds.Height);
		_view.Arrange(new Rect(0, 0, bounds.Width, bounds.Height));
	}
}

using System.Windows.Input;

namespace Plugin.Maui.NativeSwipeMenu;

public enum SwipeActionStyle
{
	/// <summary>Gray on iOS unless BackgroundColor is set.</summary>
	Normal,

	/// <summary>Red on both platforms unless BackgroundColor is set. On iOS this is UIContextualActionStyle.Destructive.</summary>
	Destructive,
}

/// <summary>
/// One action revealed when the user swipes a row of a <see cref="SwipeCollectionView"/>.
/// Actions are declared once on the list, not per item. The swiped item is passed to
/// <see cref="Command"/> unless <see cref="CommandParameter"/> is set.
/// </summary>
public class SwipeAction : Element
{
	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(SwipeAction), null);

	public static readonly BindableProperty IconImageSourceProperty =
		BindableProperty.Create(nameof(IconImageSource), typeof(ImageSource), typeof(SwipeAction), null,
			propertyChanged: (b, _, _) => ((SwipeAction)b).PlatformImage = null);

	public static readonly BindableProperty SystemImageProperty =
		BindableProperty.Create(nameof(SystemImage), typeof(string), typeof(SwipeAction), null);

	public static readonly BindableProperty BackgroundColorProperty =
		BindableProperty.Create(nameof(BackgroundColor), typeof(Color), typeof(SwipeAction), null);

	public static readonly BindableProperty StyleProperty =
		BindableProperty.Create(nameof(Style), typeof(SwipeActionStyle), typeof(SwipeAction), SwipeActionStyle.Normal);

	public static readonly BindableProperty CommandProperty =
		BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SwipeAction), null);

	public static readonly BindableProperty CommandParameterProperty =
		BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(SwipeAction), null);

	public static readonly BindableProperty IsVisibleProperty =
		BindableProperty.Create(nameof(IsVisible), typeof(bool), typeof(SwipeAction), true);

	public string? Text
	{
		get => (string?)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	/// <summary>Icon for the action. Rendered white on both platforms.</summary>
	public ImageSource? IconImageSource
	{
		get => (ImageSource?)GetValue(IconImageSourceProperty);
		set => SetValue(IconImageSourceProperty, value);
	}

	/// <summary>
	/// SF Symbol name (for example "trash" or "flag.fill"). iOS only, and it wins over
	/// <see cref="IconImageSource"/> there. Android ignores it.
	/// </summary>
	public string? SystemImage
	{
		get => (string?)GetValue(SystemImageProperty);
		set => SetValue(SystemImageProperty, value);
	}

	public Color? BackgroundColor
	{
		get => (Color?)GetValue(BackgroundColorProperty);
		set => SetValue(BackgroundColorProperty, value);
	}

	public SwipeActionStyle Style
	{
		get => (SwipeActionStyle)GetValue(StyleProperty);
		set => SetValue(StyleProperty, value);
	}

	/// <summary>Runs with the swiped item, or <see cref="CommandParameter"/> when it's set. CanExecute(false) hides the action for that item.</summary>
	public ICommand? Command
	{
		get => (ICommand?)GetValue(CommandProperty);
		set => SetValue(CommandProperty, value);
	}

	public object? CommandParameter
	{
		get => GetValue(CommandParameterProperty);
		set => SetValue(CommandParameterProperty, value);
	}

	public bool IsVisible
	{
		get => (bool)GetValue(IsVisibleProperty);
		set => SetValue(IsVisibleProperty, value);
	}

	public event EventHandler<SwipeActionInvokedEventArgs>? Invoked;

	// Platform image (UIImage / Drawable) loaded from IconImageSource by the handler. Reset when the source changes.
	internal object? PlatformImage { get; set; }

	internal bool IsAvailableFor(object item) =>
		IsVisible && (Command?.CanExecute(CommandParameter ?? item) ?? true);

	internal void Invoke(object item)
	{
		Invoked?.Invoke(this, new SwipeActionInvokedEventArgs(item, this));

		var parameter = CommandParameter ?? item;
		if (Command?.CanExecute(parameter) == true)
			Command.Execute(parameter);
	}
}

public class SwipeActionInvokedEventArgs(object item, SwipeAction action) : EventArgs
{
	public object Item { get; } = item;
	public SwipeAction Action { get; } = action;
}

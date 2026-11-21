namespace Plugin.Maui.NativeSwipeMenu;

public static class MauiAppBuilderExtensions
{
	/// <summary>Registers the native handler for <see cref="SwipeCollectionView"/>.</summary>
	public static MauiAppBuilder UseNativeSwipeMenu(this MauiAppBuilder builder)
	{
#if IOS || MACCATALYST || ANDROID
		builder.ConfigureMauiHandlers(handlers =>
			handlers.AddHandler<SwipeCollectionView, SwipeCollectionViewHandler>());
#endif
		return builder;
	}
}

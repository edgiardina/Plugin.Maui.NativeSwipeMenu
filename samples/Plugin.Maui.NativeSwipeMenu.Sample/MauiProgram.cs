using Plugin.Maui.NativeSwipeMenu;

namespace NativeSwipeMenuSample;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseNativeSwipeMenu();

		return builder.Build();
	}
}

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new NavigationPage(new MainPage()));
}

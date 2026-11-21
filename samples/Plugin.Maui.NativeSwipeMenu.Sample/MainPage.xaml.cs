using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NativeSwipeMenuSample;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
		BindingContext = new MainViewModel();
	}

#if IOS
	protected override async void OnAppearing()
	{
		base.OnAppearing();
		// SWIPE_DEMO=leading|trailing opens that side's actions on row 4 (a two-line row) for simulator screenshots.
		var demo = Environment.GetEnvironmentVariable("SWIPE_DEMO");
		if (DeviceInfo.DeviceType != DeviceType.Virtual || string.IsNullOrEmpty(demo))
			return;

		await Task.Delay(1500);
		try
		{
			var collectionView = SwipeDemo.FindCollectionView(Window?.Handler?.PlatformView is UIKit.UIWindow w ? w : null);
			Console.WriteLine($"SWIPEDEMO collectionView {(collectionView is null ? "missing" : "found")}");
			if (collectionView is not null)
				SwipeDemo.OpenSwipe(collectionView, 3, leading: demo == "leading");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"SWIPEDEMO failed {ex}");
		}
	}
#endif
}

public class Message : INotifyPropertyChanged
{
	string _flag = "";

	public required string From { get; init; }
	public required string Subject { get; init; }

	public string Flag
	{
		get => _flag;
		set { _flag = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flag))); }
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}

public class MainViewModel : INotifyPropertyChanged
{
	string _status = "Swipe a row left or right.";

	public MainViewModel()
	{
		var senders = new[] { "Bowen Kerins", "Keith Elwin", "Zach Meny", "Escher Lefkoff", "Jaap Bakker", "Johannes Ostermeier", "Robert Gagno", "Cayle George", "Daniele Acciari", "Raymond Davidson" };
		Messages = new(senders.Select((s, i) => new Message
		{
			From = s,
			Subject = i % 3 == 0
				? "Results posted for the league finals, plus a long subject line that wraps onto a second line"
				: $"Re: Tournament #{1000 + i}",
		}));

		DeleteCommand = new Command<Message>(m => { Messages.Remove(m); Status = $"Deleted {m.From}"; });
		FlagCommand = new Command<Message>(m => { m.Flag = m.Flag.Length == 0 ? "🚩" : ""; Status = $"Flagged {m.From}"; });
		ToggleUnreadCommand = new Command<Message>(m => Status = $"Unread {m.From}");
		OpenCommand = new Command<Message?>(m => { if (m is not null) Status = $"Selected {m.From}"; });

		if (Environment.GetEnvironmentVariable("SWIPE_SELFTEST") is not null)
			_ = SelfTestAsync();
	}

	public ObservableCollection<Message> Messages { get; }
	public ICommand DeleteCommand { get; }
	public ICommand FlagCommand { get; }
	public ICommand ToggleUnreadCommand { get; }
	public ICommand OpenCommand { get; }

	public string Status
	{
		get => _status;
		set { _status = value; OnPropertyChanged(); }
	}

	// Exercises incremental updates on simulators/emulators, since those can't be swiped from a script.
	async Task SelfTestAsync()
	{
		await Task.Delay(4000);
		Messages.RemoveAt(1);
		Status = "Self-test: removed row 2";
		await Task.Delay(1500);
		Messages.Insert(0, new Message { From = "IFPA", Subject = "Self-test: inserted at the top" });
		Messages[3].Flag = "🚩";
		Status = "Self-test: inserted row 1, flagged row 4";
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

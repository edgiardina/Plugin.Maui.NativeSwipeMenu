# Plugin.Maui.NativeSwipeMenu

`SwipeCollectionView` is a .NET MAUI `CollectionView` with platform-native swipe actions.

- **iOS and Mac Catalyst**: The plugin uses `UIContextualAction` on a `UICollectionView` list layout. You get the system swipe physics, full-swipe to perform, haptics, and system styling. This is the same control that Mail uses.
- **Android**: The plugin attaches a native `ItemTouchHelper` to the stock `CollectionView`. A swipe past the threshold performs the action. The action color and icon show behind the row during the drag.

## Why

The MAUI `SwipeView` on iOS is a custom pan gesture that moves `UIButton` views. It does not use the UIKit swipe actions. UIKit gives native swipe actions only to `UICollectionViewListCell` in a list section. The MAUI `CollectionView` cells are plain `UICollectionViewCell` objects. Thus this plugin has its own iOS handler.

## Setup

```csharp
builder
    .UseMauiApp<App>()
    .UseNativeSwipeMenu();
```

## Usage

```xml
xmlns:swipe="clr-namespace:Plugin.Maui.NativeSwipeMenu;assembly=Plugin.Maui.NativeSwipeMenu"

<swipe:SwipeCollectionView ItemsSource="{Binding Messages}">
    <swipe:SwipeCollectionView.LeadingActions>
        <swipe:SwipeAction Text="Unread" SystemImage="envelope.badge.fill"
                           BackgroundColor="#0A84FF" Command="{Binding ToggleUnreadCommand}" />
    </swipe:SwipeCollectionView.LeadingActions>
    <swipe:SwipeCollectionView.TrailingActions>
        <swipe:SwipeAction Text="Delete" Style="Destructive" SystemImage="trash.fill"
                           IconImageSource="{FontImageSource Glyph=..., FontFamily=...}"
                           Command="{Binding DeleteCommand}" />
    </swipe:SwipeCollectionView.TrailingActions>
    <swipe:SwipeCollectionView.ItemTemplate>
        <DataTemplate>...</DataTemplate>
    </swipe:SwipeCollectionView.ItemTemplate>
</swipe:SwipeCollectionView>
```

The actions are on the list, not on each item. Thus each action gets the page `BindingContext`. The command receives the swiped item as its parameter. If you set `CommandParameter`, the command receives that value. If `CanExecute` returns false for an item, the action does not show for that item.

### SwipeAction

| Property | Description |
|---|---|
| `Text` | The action label. |
| `SystemImage` | The SF Symbol name. iOS only. On iOS, this value has priority over `IconImageSource`. |
| `IconImageSource` | Any MAUI `ImageSource`. The plugin shows it in white. Use it for the Android icon. |
| `Style` | `Normal` or `Destructive`. `Destructive` is red by default. |
| `BackgroundColor` | Overrides the default color. |
| `Command`, `CommandParameter` | Runs when the user selects the action. |
| `IsVisible` | Hides the action. |
| `Invoked` event | Occurs with the item and the action. |

### SwipeCollectionView

| Property | Description |
|---|---|
| `LeadingActions`, `TrailingActions` | The swipe actions. Put Delete in `TrailingActions`. |
| `PerformsFirstActionOnFullSwipe` | iOS. A long swipe performs the first action. The default is `true`. |
| `ShowsSeparators` | iOS. Shows the native list separators. Set it to `false` if your template draws its own separator. |
| `SwipeActionInvoked` event | Occurs for each action. |

## Platform limits

**iOS** uses a dedicated handler. This handler supports these features:

- `ItemsSource`, with incremental animated updates for `INotifyCollectionChanged`
- `ItemTemplate` and `DataTemplateSelector`
- `EmptyView` and `EmptyViewTemplate`
- `SelectionMode`, `SelectedItem`, `SelectedItems`, and `SelectionChangedCommand`

This handler does not support grouping, `Header`, `Footer`, grid `ItemsLayout`, `ScrollTo`, or `RemainingItemsThreshold`.

**Android** uses the stock `CollectionView`, so all of its features work. A swipe performs the first available action for that side. Thus only one action shows on each side. This is the Material pattern, for example in Gmail. Rows need an `ItemTemplate`.

If a destructive command does not remove the item in 750 ms, the row moves back into position. A row moves back immediately for a normal action.

## Sample

The sample app is in `samples/Plugin.Maui.NativeSwipeMenu.Sample`. On a simulator or emulator, the sample removes a row, inserts a row, and flags a row on a timer. This lets you examine the animated updates without touch input.

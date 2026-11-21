using System.Runtime.InteropServices;
using ObjCRuntime;
using UIKit;

namespace NativeSwipeMenuSample;

// Simulator-only screenshot helper. simctl can't send touches, so this pokes UIKit's private swipe
// controller to open a row's actions in-process. Never ship anything like this in an app.
static class SwipeDemo
{
	const string ObjC = "/usr/lib/libobjc.dylib";

	[DllImport(ObjC)] static extern IntPtr class_copyMethodList(IntPtr cls, out uint count);
	[DllImport(ObjC)] static extern IntPtr method_getName(IntPtr method);
	[DllImport(ObjC)] static extern IntPtr method_getTypeEncoding(IntPtr method);
	[DllImport(ObjC)] static extern IntPtr sel_getName(IntPtr sel);
	[DllImport(ObjC)] static extern void free(IntPtr ptr);

	public static void DumpMethods(string className, string filter)
	{
		var cls = Class.GetHandle(className);
		if (cls == IntPtr.Zero)
		{
			Console.WriteLine($"SWIPEDEMO no class {className}");
			return;
		}

		var list = class_copyMethodList(cls, out var count);
		for (var i = 0; i < count; i++)
		{
			var method = Marshal.ReadIntPtr(list, i * IntPtr.Size);
			var name = Marshal.PtrToStringAnsi(sel_getName(method_getName(method)));
			if (name is not null && name.Contains(filter, StringComparison.OrdinalIgnoreCase))
				Console.WriteLine($"SWIPEDEMO {className} {name} {Marshal.PtrToStringAnsi(method_getTypeEncoding(method))}");
		}
		free(list);
	}

	[DllImport(ObjC)] static extern IntPtr class_copyIvarList(IntPtr cls, out uint count);
	[DllImport(ObjC)] static extern IntPtr ivar_getName(IntPtr ivar);

	public static void DumpIvars(string className, string filter)
	{
		var cls = Class.GetHandle(className);
		if (cls == IntPtr.Zero) return;
		var list = class_copyIvarList(cls, out var count);
		for (var i = 0; i < count; i++)
		{
			var name = Marshal.PtrToStringAnsi(ivar_getName(Marshal.ReadIntPtr(list, i * IntPtr.Size)));
			if (name is not null && name.Contains(filter, StringComparison.OrdinalIgnoreCase))
				Console.WriteLine($"SWIPEDEMO ivar {className} {name}");
		}
		free(list);
	}

	[DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr target, IntPtr sel);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr target, IntPtr sel, IntPtr arg);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern ulong SendBool(IntPtr target, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool arg);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")]
	static extern void SendSwipe(IntPtr target, IntPtr sel, IntPtr indexPath, IntPtr config, ulong direction, [MarshalAs(UnmanagedType.I1)] bool animated, IntPtr completion);
	[DllImport(ObjC)] static extern IntPtr object_getClass(IntPtr obj);
	[DllImport(ObjC)] static extern IntPtr class_getName(IntPtr cls);
	[DllImport(ObjC)] static extern IntPtr object_getIvar(IntPtr obj, IntPtr ivar);
	[DllImport(ObjC)] static extern IntPtr class_getInstanceVariable(IntPtr cls, string name);

	static string ClassOf(IntPtr obj) => obj == IntPtr.Zero ? "nil" : Marshal.PtrToStringAnsi(class_getName(object_getClass(obj)))!;

	static IntPtr Ivar(IntPtr obj, string name)
	{
		for (var cls = object_getClass(obj); cls != IntPtr.Zero; cls = Send(cls, Selector.GetHandle("superclass")))
		{
			var ivar = class_getInstanceVariable(cls, name);
			if (ivar != IntPtr.Zero)
				return object_getIvar(obj, ivar);
		}
		return IntPtr.Zero;
	}

	// Opens the swipe actions on `row` the way a finger would, through UIKit's own controller.
	public static void OpenSwipe(UICollectionView collectionView, int row, bool leading)
	{
		var layout = collectionView.CollectionViewLayout.Handle;
		Send(layout, Selector.GetHandle("_createSwipeActionsModuleIfNeeded"));
		var module = Ivar(layout, "_swipeActionsModule");
		Console.WriteLine($"SWIPEDEMO module {ClassOf(module)}");
		if (module == IntPtr.Zero) return;

		var controller = IntPtr.Zero;
		foreach (var name in new[] { "_swipeActionController", "_swipeController", "swipeActionController" })
		{
			controller = Ivar(module, name);
			if (controller != IntPtr.Zero) break;
		}
		Console.WriteLine($"SWIPEDEMO controller {ClassOf(controller)}");
		if (controller == IntPtr.Zero) return;

		var indexPath = Foundation.NSIndexPath.FromItemSection(row, 0);
		var config = Send(layout,
			Selector.GetHandle(leading ? "_leadingSwipeActionsConfigurationForIndexPath:" : "_trailingSwipeActionsConfigurationForIndexPath:"),
			indexPath.Handle);
		var direction = SendBool(controller, Selector.GetHandle("_swipeDirectionForLeadingEdge:"), leading);
		Console.WriteLine($"SWIPEDEMO config {ClassOf(config)} direction {direction}");

		SendSwipe(controller, Selector.GetHandle("swipeItemAtIndexPath:configuration:direction:animated:completion:"),
			indexPath.Handle, config, direction, true, IntPtr.Zero);
		Console.WriteLine("SWIPEDEMO swiped");
	}

	public static UICollectionView? FindCollectionView(UIView? view)
	{
		if (view is null) return null;
		if (view is UICollectionView cv) return cv;
		foreach (var sub in view.Subviews)
			if (FindCollectionView(sub) is { } found) return found;
		return null;
	}
}

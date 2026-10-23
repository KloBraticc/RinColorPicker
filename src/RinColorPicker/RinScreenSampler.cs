using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;

namespace RinColor;

public interface IRinScreenSampler
{
	bool TrySample(out Color color);
}

public interface IRinScreenColorPicker
{
	bool IsAvailable { get; }

	Task<Color?> PickAsync();
}

public static partial class RinScreenSampler
{
	public static IRinScreenSampler? Current { get; set; } = OperatingSystem.IsWindows() ? new WindowsSampler() : null;

	public static IRinScreenColorPicker? Picker { get; set; }

	public static bool IsAvailable => Current != null || Picker is { IsAvailable: true };

	private sealed partial class WindowsSampler : IRinScreenSampler
	{
		private const uint InvalidColor = 0xFFFFFFFF;

		public bool TrySample(out Color color)
		{
			color = default;
			if (!GetCursorPos(out NativePoint point))
				return false;
			IntPtr screen = GetDC(IntPtr.Zero);
			if (screen == IntPtr.Zero)
				return false;
			try
			{
				uint pixel = GetPixel(screen, point.X, point.Y);
				if (pixel == InvalidColor)
					return false;
				color = Color.FromRgb((byte)(pixel & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)((pixel >> 16) & 0xFF));
				return true;
			}
			finally
			{
				_ = ReleaseDC(IntPtr.Zero, screen);
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativePoint
		{
			public int X;
			public int Y;
		}

		[LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool GetCursorPos(out NativePoint point);

		[LibraryImport("user32.dll", EntryPoint = "GetDC")]
		private static partial IntPtr GetDC(IntPtr window);

		[LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
		private static partial int ReleaseDC(IntPtr window, IntPtr context);

		[LibraryImport("gdi32.dll", EntryPoint = "GetPixel")]
		private static partial uint GetPixel(IntPtr context, int x, int y);
	}
}

public static class RinColorClipboard
{
	public static Func<string?>? GetText { get; set; }

	public static Func<string, bool>? SetText { get; set; }

	internal static string? Read()
	{
		if (GetText != null)
			return GetText();
		try
		{
			return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
		}
		catch (ExternalException)
		{
			return null;
		}
	}

	internal static void Write(string text)
	{
		if (SetText != null && SetText(text))
			return;
		try
		{
			System.Windows.Clipboard.SetText(text);
		}
		catch (ExternalException)
		{
		}
	}
}

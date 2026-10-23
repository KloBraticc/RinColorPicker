using System;

namespace RinColor;

public sealed class RinColorPickerText
{
	public string Red { get; init; } = "Red";

	public string Green { get; init; } = "Green";

	public string Blue { get; init; } = "Blue";

	public string Hue { get; init; } = "Hue";

	public string Saturation { get; init; } = "Saturation";

	public string Value { get; init; } = "Brightness";

	public string Lightness { get; init; } = "Lightness";

	public string Alpha { get; init; } = "Opacity";

	public string Hex { get; init; } = "Hex";

	public string Recent { get; init; } = "Recent";

	public string Saved { get; init; } = "Saved";

	public string SaveColor { get; init; } = "Save this colour";

	public string RemoveColor { get; init; } = "Right click to remove";

	public string PickFromScreen { get; init; } = "Pick a colour from the screen";

	public string Copy { get; init; } = "Copy hex";

	public string Paste { get; init; } = "Paste hex";

	public string Original { get; init; } = "Original, click to restore";

	public string Current { get; init; } = "New";

	public static RinColorPickerText Default { get; } = new();

	public static Func<RinColorPickerText> Provider { get; set; } = () => Default;
}

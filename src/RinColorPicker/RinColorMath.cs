using System;
using System.Globalization;
using System.Windows.Media;

namespace RinColor;

public static class RinColorMath
{
	public static Color FromHsv(double hue, double saturation, double value, byte alpha = 255)
	{
		hue = ((hue % 360) + 360) % 360;
		saturation = Math.Clamp(saturation, 0, 1);
		value = Math.Clamp(value, 0, 1);
		double chroma = value * saturation;
		double x = chroma * (1 - Math.Abs(hue / 60.0 % 2 - 1));
		double m = value - chroma;
		(double r, double g, double b) = Sector(hue, chroma, x);
		return Color.FromArgb(alpha, ToByte(r + m), ToByte(g + m), ToByte(b + m));
	}

	public static void ToHsv(Color color, out double hue, out double saturation, out double value)
	{
		double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
		double max = Math.Max(r, Math.Max(g, b));
		double min = Math.Min(r, Math.Min(g, b));
		double delta = max - min;
		value = max;
		saturation = max <= 0 ? 0 : delta / max;
		hue = Hue(r, g, b, max, delta);
	}

	public static Color FromHsl(double hue, double saturation, double lightness, byte alpha = 255)
	{
		hue = ((hue % 360) + 360) % 360;
		saturation = Math.Clamp(saturation, 0, 1);
		lightness = Math.Clamp(lightness, 0, 1);
		double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
		double x = chroma * (1 - Math.Abs(hue / 60.0 % 2 - 1));
		double m = lightness - chroma / 2;
		(double r, double g, double b) = Sector(hue, chroma, x);
		return Color.FromArgb(alpha, ToByte(r + m), ToByte(g + m), ToByte(b + m));
	}

	public static void ToHsl(Color color, out double hue, out double saturation, out double lightness)
	{
		double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
		double max = Math.Max(r, Math.Max(g, b));
		double min = Math.Min(r, Math.Min(g, b));
		double delta = max - min;
		lightness = (max + min) / 2;
		saturation = delta <= 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
		saturation = Math.Clamp(saturation, 0, 1);
		hue = Hue(r, g, b, max, delta);
	}

	public static string ToHex(Color color, bool includeAlpha)
	{
		return includeAlpha && color.A != 255
			? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
			: $"#{color.R:X2}{color.G:X2}{color.B:X2}";
	}

	public static bool TryParseHex(string? text, out Color color)
	{
		color = default;
		if (string.IsNullOrWhiteSpace(text))
			return false;
		string hex = text.Trim().TrimStart('#');
		if (hex.Length == 3 || hex.Length == 4)
		{
			char[] expanded = new char[hex.Length * 2];
			for (int i = 0; i < hex.Length; i++)
			{
				expanded[i * 2] = hex[i];
				expanded[i * 2 + 1] = hex[i];
			}
			hex = new string(expanded);
		}
		if (hex.Length != 6 && hex.Length != 8)
			return false;
		if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
			return false;
		color = hex.Length == 8
			? Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
			: Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
		return true;
	}

	public static double Luminance(Color color)
	{
		return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
	}

	private static (double R, double G, double B) Sector(double hue, double chroma, double x)
	{
		if (hue < 60)
			return (chroma, x, 0);
		if (hue < 120)
			return (x, chroma, 0);
		if (hue < 180)
			return (0, chroma, x);
		if (hue < 240)
			return (0, x, chroma);
		if (hue < 300)
			return (x, 0, chroma);
		return (chroma, 0, x);
	}

	private static double Hue(double r, double g, double b, double max, double delta)
	{
		if (delta <= 0)
			return 0;
		double hue;
		if (max == r)
			hue = 60 * (((g - b) / delta) % 6);
		else if (max == g)
			hue = 60 * ((b - r) / delta + 2);
		else
			hue = 60 * ((r - g) / delta + 4);
		return hue < 0 ? hue + 360 : hue;
	}

	private static byte ToByte(double channel)
	{
		return (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
	}
}

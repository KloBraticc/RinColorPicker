using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace RinColor;

public interface IRinColorSwatchStore
{
	IReadOnlyList<Color> Recent { get; }

	IReadOnlyList<Color> Saved { get; }

	void AddRecent(Color color);

	void AddSaved(Color color);

	void RemoveSaved(Color color);
}

public class RinColorSwatchMemoryStore : IRinColorSwatchStore
{
	public const int Capacity = 12;

	private readonly List<Color> _recent = [];

	private readonly List<Color> _saved = [];

	public IReadOnlyList<Color> Recent => _recent;

	public IReadOnlyList<Color> Saved => _saved;

	public void AddRecent(Color color)
	{
		Push(_recent, color);
		OnChanged();
	}

	public void AddSaved(Color color)
	{
		Push(_saved, color);
		OnChanged();
	}

	public void RemoveSaved(Color color)
	{
		if (_saved.Remove(color))
			OnChanged();
	}

	public void Load(IEnumerable<Color> recent, IEnumerable<Color> saved)
	{
		_recent.Clear();
		_saved.Clear();
		foreach (Color color in recent)
			if (_recent.Count < Capacity && !_recent.Contains(color))
				_recent.Add(color);
		foreach (Color color in saved)
			if (_saved.Count < Capacity && !_saved.Contains(color))
				_saved.Add(color);
	}

	protected virtual void OnChanged()
	{
	}

	private static void Push(List<Color> list, Color color)
	{
		list.Remove(color);
		list.Insert(0, color);
		if (list.Count > Capacity)
			list.RemoveRange(Capacity, list.Count - Capacity);
	}
}

public static class RinColorSwatches
{
	public static IRinColorSwatchStore Store { get; set; } = new RinColorSwatchMemoryStore();
}

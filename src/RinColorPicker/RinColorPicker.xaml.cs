using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RinColor;

public partial class RinColorPicker : UserControl
{
	public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
		nameof(SelectedColor),
		typeof(Color),
		typeof(RinColorPicker),
		new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

	public static readonly DependencyProperty AlphaEnabledProperty = DependencyProperty.Register(
		nameof(AlphaEnabled),
		typeof(bool),
		typeof(RinColorPicker),
		new PropertyMetadata(true, OnAlphaEnabledChanged));

	public static readonly DependencyProperty SwatchesEnabledProperty = DependencyProperty.Register(
		nameof(SwatchesEnabled),
		typeof(bool),
		typeof(RinColorPicker),
		new PropertyMetadata(true, OnSwatchesEnabledChanged));

	private const double HandleSize = 18.0;
	private const double ThumbSize = 14.0;
	private const int SampleIntervalMs = 33;
	private const int SwatchSize = 24;

	private enum DragTarget
	{
		None,
		Spectrum,
		Hue,
		Alpha
	}

	private double _h;
	private double _s = 1;
	private double _v = 1;
	private double _a = 1;
	private bool _updating;
	private DragTarget _drag;
	private int _inputRefreshPending;
	private Color? _originalColor;
	private bool _sampling;
	private Color _beforeSample;
	private DispatcherTimer? _sampleTimer;

	private readonly SolidColorBrush _spectrumHueBrush = new(Colors.Red);
	private readonly SolidColorBrush _thumbBrush = new(Colors.White);
	private readonly SolidColorBrush _previewBrush = new(Colors.White);
	private readonly SolidColorBrush _originalBrush = new(Colors.White);
	private readonly GradientStop _alphaStart = new(Colors.Transparent, 0);
	private readonly GradientStop _alphaEnd = new(Colors.White, 1);

	public Color SelectedColor
	{
		get => (Color)GetValue(SelectedColorProperty);
		set => SetValue(SelectedColorProperty, value);
	}

	public bool AlphaEnabled
	{
		get => (bool)GetValue(AlphaEnabledProperty);
		set => SetValue(AlphaEnabledProperty, value);
	}

	public bool SwatchesEnabled
	{
		get => (bool)GetValue(SwatchesEnabledProperty);
		set => SetValue(SwatchesEnabledProperty, value);
	}

	public Color OriginalColor
	{
		get => _originalColor ?? SelectedColor;
		set
		{
			_originalColor = value;
			_originalBrush.Color = value;
		}
	}

	public bool IsInputRefreshPending => Volatile.Read(ref _inputRefreshPending) != 0;

	public bool IsSampling => _sampling;

	public event EventHandler<Color>? ColorChanged;

	public RinColorPicker()
	{
		InitializeComponent();
		SpectrumHue.Fill = _spectrumHueBrush;
		SpectrumThumb.Stroke = _thumbBrush;
		PreviewRect.Fill = _previewBrush;
		OriginalRect.Fill = _originalBrush;
		AlphaGradientRect.Fill = new LinearGradientBrush
		{
			StartPoint = new Point(0, 0.5),
			EndPoint = new Point(1, 0.5),
			GradientStops = new GradientStopCollection { _alphaStart, _alphaEnd }
		};
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		SizeChanged += OnSizeChanged;
		LostMouseCapture += OnLostMouseCapture;
		PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
		PreviewMouseDown += OnPreviewMouseDown;
		PreviewKeyDown += OnPreviewKeyDown;
	}

	public void RememberSelection()
	{
		RinColorSwatches.Store.AddRecent(SelectedColor);
		RebuildSwatches();
	}

	public void SetSaturationValueFromPosition(Point point)
	{
		double width = SpectrumGrid.ActualWidth;
		double height = SpectrumGrid.ActualHeight;
		if (width < 1 || height < 1)
			return;
		double saturation = Math.Clamp(point.X / width, 0, 1);
		double value = 1 - Math.Clamp(point.Y / height, 0, 1);
		if (Math.Abs(_s - saturation) < 0.0001 && Math.Abs(_v - value) < 0.0001)
			return;
		_s = saturation;
		_v = value;
		Commit();
	}

	public void SetHueFromPosition(double x)
	{
		double hue = TrackFraction(HueTrack, x) * 360.0;
		if (Math.Abs(_h - hue) < 0.0001)
			return;
		_h = hue;
		Commit();
	}

	public void SetAlphaFromPosition(double x)
	{
		double alpha = TrackFraction(AlphaTrack, x);
		if (Math.Abs(_a - alpha) < 0.0001)
			return;
		_a = alpha;
		Commit();
	}

	public void BeginInteraction()
	{
		_drag = DragTarget.Alpha;
	}

	public void EndInteraction()
	{
		EndDrag(true);
	}

	private static double TrackFraction(FrameworkElement track, double x)
	{
		double width = track.ActualWidth;
		if (width <= HandleSize)
			return Math.Clamp(x / Math.Max(1, width), 0, 1);
		return Math.Clamp((x - HandleSize / 2.0) / (width - HandleSize), 0, 1);
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		LoadFromColor(SelectedColor, true);
		if (_originalColor == null)
			OriginalColor = SelectedColor;
		ApplyText();
		ApplyAlphaVisibility();
		EyedropperButton.Visibility = RinScreenSampler.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
		RebuildSwatches();
		RefreshAll();
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		EndDrag(true);
		StopSampling(false);
	}

	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RefreshVisuals();
	}

	private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var self = (RinColorPicker)d;
		if (self._updating)
			return;
		self.LoadFromColor((Color)e.NewValue, false);
		self.RefreshAll();
	}

	private static void OnAlphaEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var self = (RinColorPicker)d;
		self.ApplyAlphaVisibility();
		if (self.IsLoaded)
			self.RefreshInputs(null);
	}

	private static void OnSwatchesEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var self = (RinColorPicker)d;
		self.SwatchSection.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
	}

	private void ApplyAlphaVisibility()
	{
		bool on = AlphaEnabled;
		AlphaRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
		AlphaInput.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
		AlphaInputGap.Width = on ? new GridLength(8) : new GridLength(0);
		AlphaInputColumn.Width = on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
	}

	private void ApplyText()
	{
		RinColorPickerText text = RinColorPickerText.Provider() ?? RinColorPickerText.Default;
		AlphaLabel.Text = text.Alpha;
		RecentLabel.Text = text.Recent;
		SavedLabel.Text = text.Saved;
		HexBox.ToolTip = text.Hex;
		EyedropperButton.ToolTip = text.PickFromScreen;
		CopyButton.ToolTip = text.Copy;
		PasteButton.ToolTip = text.Paste;
		OriginalRect.ToolTip = text.Original;
		PreviewRect.ToolTip = text.Current;
	}

	private void LoadFromColor(Color color, bool resetHue)
	{
		RinColorMath.ToHsv(color, out double h, out double s, out double v);
		if (resetHue || (s > 0.005 && v > 0.005))
			_h = h;
		if (resetHue || v > 0.005)
			_s = s;
		_v = v;
		_a = color.A / 255.0;
	}

	private Color CurrentColor()
	{
		return RinColorMath.FromHsv(_h, _s, _v, (byte)Math.Round(_a * 255));
	}

	private void Commit()
	{
		Color color = CurrentColor();
		_updating = true;
		SelectedColor = color;
		_updating = false;
		ColorChanged?.Invoke(this, color);
		RefreshVisuals();
		if (_drag != DragTarget.None || _sampling)
			QueueInputRefresh();
		else
			RefreshInputs(null);
	}

	private void CommitFromInput(TextBox source)
	{
		Color color = CurrentColor();
		_updating = true;
		SelectedColor = color;
		_updating = false;
		ColorChanged?.Invoke(this, color);
		RefreshVisuals();
		RefreshInputs(source);
	}

	private void QueueInputRefresh()
	{
		if (Interlocked.Exchange(ref _inputRefreshPending, 1) != 0)
			return;
		Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushInputRefresh));
	}

	private void FlushInputRefresh()
	{
		Volatile.Write(ref _inputRefreshPending, 0);
		if (IsLoaded)
			RefreshInputs(null);
	}

	private void RefreshAll()
	{
		RefreshVisuals();
		RefreshInputs(null);
	}

	private void RefreshVisuals()
	{
		_spectrumHueBrush.Color = RinColorMath.FromHsv(_h, 1, 1);
		double width = SpectrumGrid.ActualWidth;
		double height = SpectrumGrid.ActualHeight;
		if (width > 1 && height > 1)
		{
			Canvas.SetLeft(SpectrumThumb, _s * width - ThumbSize / 2.0);
			Canvas.SetTop(SpectrumThumb, (1 - _v) * height - ThumbSize / 2.0);
		}
		Color solid = RinColorMath.FromHsv(_h, _s, _v);
		_thumbBrush.Color = RinColorMath.Luminance(solid) < 0.6 ? Colors.White : Colors.Black;
		PlaceHandle(HueTrack, HueHandle, _h / 360.0);
		_alphaStart.Color = Color.FromArgb(0, solid.R, solid.G, solid.B);
		_alphaEnd.Color = solid;
		PlaceHandle(AlphaTrack, AlphaHandle, _a);
		_previewBrush.Color = CurrentColor();
	}

	private static void PlaceHandle(FrameworkElement track, FrameworkElement handle, double fraction)
	{
		double width = track.ActualWidth;
		if (width <= HandleSize)
			return;
		Canvas.SetLeft(handle, Math.Clamp(fraction, 0, 1) * (width - HandleSize));
		Canvas.SetTop(handle, 0);
	}

	private void RefreshInputs(TextBox? skip)
	{
		if (!IsLoaded && skip == null)
			return;
		RinColorPickerText text = RinColorPickerText.Provider() ?? RinColorPickerText.Default;
		_updating = true;
		Color color = CurrentColor();
		if (!ReferenceEquals(skip, HexBox))
			HexBox.Text = RinColorMath.ToHex(color, AlphaEnabled);
		int c1, c2, c3;
		switch (ModeBox.SelectedIndex)
		{
			case 1:
				C1Label.Text = text.Hue;
				C2Label.Text = text.Saturation;
				C3Label.Text = text.Value;
				c1 = (int)Math.Round(_h) % 360;
				c2 = (int)Math.Round(_s * 100);
				c3 = (int)Math.Round(_v * 100);
				break;
			case 2:
				RinColorMath.ToHsl(color, out double hue, out double saturation, out double lightness);
				if (saturation <= 0.0001)
					hue = _h;
				C1Label.Text = text.Hue;
				C2Label.Text = text.Saturation;
				C3Label.Text = text.Lightness;
				c1 = (int)Math.Round(hue) % 360;
				c2 = (int)Math.Round(saturation * 100);
				c3 = (int)Math.Round(lightness * 100);
				break;
			default:
				C1Label.Text = text.Red;
				C2Label.Text = text.Green;
				C3Label.Text = text.Blue;
				c1 = color.R;
				c2 = color.G;
				c3 = color.B;
				break;
		}
		SetBox(C1Box, c1, skip);
		SetBox(C2Box, c2, skip);
		SetBox(C3Box, c3, skip);
		SetBox(AlphaBox, (int)Math.Round(_a * 100), skip);
		_updating = false;
	}

	private static void SetBox(TextBox box, int value, TextBox? skip)
	{
		if (!ReferenceEquals(box, skip))
			box.Text = value.ToString(CultureInfo.InvariantCulture);
	}

	private void BeginDrag(UIElement target, DragTarget drag)
	{
		_drag = drag;
		target.Focus();
		Mouse.Capture(target, CaptureMode.Element);
	}

	private void EndDrag(bool releaseCapture)
	{
		if (_drag == DragTarget.None)
			return;
		_drag = DragTarget.None;
		if (releaseCapture && Mouse.Captured != null && !_sampling)
			Mouse.Capture(null);
		QueueInputRefresh();
	}

	private void OnLostMouseCapture(object sender, MouseEventArgs e)
	{
		if (_sampling)
		{
			StopSampling(true);
			return;
		}
		EndDrag(false);
	}

	private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_drag != DragTarget.None)
		{
			EndDrag(true);
			e.Handled = true;
		}
	}

	private bool DragMove(DragTarget target, MouseEventArgs e)
	{
		if (_drag != target)
			return false;
		if (e.LeftButton != MouseButtonState.Pressed)
		{
			EndDrag(true);
			return false;
		}
		e.Handled = true;
		return true;
	}

	private void Spectrum_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left || _sampling)
			return;
		BeginDrag(SpectrumGrid, DragTarget.Spectrum);
		SetSaturationValueFromPosition(e.GetPosition(SpectrumGrid));
		e.Handled = true;
	}

	private void Spectrum_MouseMove(object sender, MouseEventArgs e)
	{
		if (DragMove(DragTarget.Spectrum, e))
			SetSaturationValueFromPosition(e.GetPosition(SpectrumGrid));
	}

	private void Spectrum_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left && _drag == DragTarget.Spectrum)
		{
			EndDrag(true);
			e.Handled = true;
		}
	}

	private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left || _sampling)
			return;
		BeginDrag(HueTrack, DragTarget.Hue);
		SetHueFromPosition(e.GetPosition(HueTrack).X);
		e.Handled = true;
	}

	private void Hue_MouseMove(object sender, MouseEventArgs e)
	{
		if (DragMove(DragTarget.Hue, e))
			SetHueFromPosition(e.GetPosition(HueTrack).X);
	}

	private void Hue_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left && _drag == DragTarget.Hue)
		{
			EndDrag(true);
			e.Handled = true;
		}
	}

	private void Alpha_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left || _sampling)
			return;
		BeginDrag(AlphaTrack, DragTarget.Alpha);
		SetAlphaFromPosition(e.GetPosition(AlphaTrack).X);
		e.Handled = true;
	}

	private void Alpha_MouseMove(object sender, MouseEventArgs e)
	{
		if (DragMove(DragTarget.Alpha, e))
			SetAlphaFromPosition(e.GetPosition(AlphaTrack).X);
	}

	private void Alpha_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left && _drag == DragTarget.Alpha)
		{
			EndDrag(true);
			e.Handled = true;
		}
	}

	private static double Step(double small, double large)
	{
		return (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? large : small;
	}

	private void Spectrum_KeyDown(object sender, KeyEventArgs e)
	{
		double step = Step(0.01, 0.1);
		switch (e.Key)
		{
			case Key.Left:
				_s = Math.Max(0, _s - step);
				break;
			case Key.Right:
				_s = Math.Min(1, _s + step);
				break;
			case Key.Up:
				_v = Math.Min(1, _v + step);
				break;
			case Key.Down:
				_v = Math.Max(0, _v - step);
				break;
			default:
				return;
		}
		Commit();
		e.Handled = true;
	}

	private void Hue_KeyDown(object sender, KeyEventArgs e)
	{
		double hue = _h;
		if (!ApplyTrackKey(e.Key, ref hue, 360, Step(1, 10)))
			return;
		_h = hue;
		Commit();
		e.Handled = true;
	}

	private void Alpha_KeyDown(object sender, KeyEventArgs e)
	{
		double alpha = _a;
		if (!ApplyTrackKey(e.Key, ref alpha, 1, Step(0.01, 0.1)))
			return;
		_a = alpha;
		Commit();
		e.Handled = true;
	}

	private static bool ApplyTrackKey(Key key, ref double value, double maximum, double step)
	{
		switch (key)
		{
			case Key.Left:
			case Key.Down:
				value = Math.Max(0, value - step);
				return true;
			case Key.Right:
			case Key.Up:
				value = Math.Min(maximum, value + step);
				return true;
			case Key.Home:
				value = 0;
				return true;
			case Key.End:
				value = maximum;
				return true;
			default:
				return false;
		}
	}

	private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_updating || !HexBox.IsKeyboardFocused)
			return;
		if (!RinColorMath.TryParseHex(HexBox.Text, out Color color))
			return;
		if (!AlphaEnabled)
			color.A = (byte)Math.Round(_a * 255);
		LoadFromColor(color, false);
		CommitFromInput(HexBox);
	}

	private int ChannelMaximum(TextBox box)
	{
		if (ReferenceEquals(box, AlphaBox))
			return 100;
		if (ModeBox.SelectedIndex == 0)
			return 255;
		return ReferenceEquals(box, C1Box) ? 359 : 100;
	}

	private void Channel_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_updating)
			return;
		var box = (TextBox)sender;
		if (!box.IsKeyboardFocused || !int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
			return;
		value = Math.Clamp(value, 0, ChannelMaximum(box));
		Color current = CurrentColor();
		switch (ModeBox.SelectedIndex)
		{
			case 1:
				if (ReferenceEquals(box, C1Box))
					_h = value;
				else if (ReferenceEquals(box, C2Box))
					_s = value / 100.0;
				else
					_v = value / 100.0;
				break;
			case 2:
				RinColorMath.ToHsl(current, out double hue, out double saturation, out double lightness);
				if (saturation <= 0.0001)
					hue = _h;
				if (ReferenceEquals(box, C1Box))
					hue = value;
				else if (ReferenceEquals(box, C2Box))
					saturation = value / 100.0;
				else
					lightness = value / 100.0;
				Color fromHsl = RinColorMath.FromHsl(hue, saturation, lightness, current.A);
				LoadFromColor(fromHsl, false);
				_h = hue;
				break;
			default:
				byte r = current.R, g = current.G, b = current.B;
				if (ReferenceEquals(box, C1Box))
					r = (byte)value;
				else if (ReferenceEquals(box, C2Box))
					g = (byte)value;
				else
					b = (byte)value;
				LoadFromColor(Color.FromArgb(current.A, r, g, b), false);
				break;
		}
		CommitFromInput(box);
	}

	private void AlphaBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_updating || !AlphaBox.IsKeyboardFocused || !int.TryParse(AlphaBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
			return;
		_a = Math.Clamp(value, 0, 100) / 100.0;
		CommitFromInput(AlphaBox);
	}

	private void Number_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Up && e.Key != Key.Down)
			return;
		var box = (TextBox)sender;
		int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value);
		int step = (int)Step(1, 10);
		value = Math.Clamp(value + (e.Key == Key.Up ? step : -step), 0, ChannelMaximum(box));
		box.Text = value.ToString(CultureInfo.InvariantCulture);
		box.CaretIndex = box.Text.Length;
		e.Handled = true;
	}

	private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (IsLoaded)
			RefreshInputs(null);
	}

	private void Original_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		ApplyColor(OriginalColor);
	}

	private void ApplyColor(Color color)
	{
		if (!AlphaEnabled)
			color.A = (byte)Math.Round(_a * 255);
		LoadFromColor(color, false);
		Commit();
	}

	private void Copy_Click(object sender, RoutedEventArgs e)
	{
		RinColorClipboard.Write(RinColorMath.ToHex(SelectedColor, AlphaEnabled));
	}

	private void Paste_Click(object sender, RoutedEventArgs e)
	{
		if (RinColorMath.TryParseHex(RinColorClipboard.Read(), out Color color))
			ApplyColor(color);
	}

	private void RebuildSwatches()
	{
		RinColorPickerText text = RinColorPickerText.Provider() ?? RinColorPickerText.Default;
		IRinColorSwatchStore store = RinColorSwatches.Store;
		RecentPanel.Children.Clear();
		foreach (Color color in store.Recent)
			RecentPanel.Children.Add(CreateSwatch(color, RinColorMath.ToHex(color, true), false));
		Visibility recent = store.Recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		RecentLabel.Visibility = recent;
		RecentPanel.Visibility = recent;
		SavedLabel.Margin = store.Recent.Count > 0 ? new Thickness(2, 10, 0, 4) : new Thickness(2, 0, 0, 4);

		SavedPanel.Children.Clear();
		foreach (Color color in store.Saved)
			SavedPanel.Children.Add(CreateSwatch(color, RinColorMath.ToHex(color, true) + "\n" + text.RemoveColor, true));
		SavedPanel.Children.Add(CreateAddSwatch(text.SaveColor));
	}

	private Border CreateSwatch(Color color, string tooltip, bool removable)
	{
		var fill = new Rectangle { Fill = new SolidColorBrush(color) };
		var grid = new Grid();
		grid.Children.Add(new Rectangle { Fill = (Brush)FindResource("RinCheckerboard") });
		grid.Children.Add(fill);
		var swatch = new Border
		{
			Width = SwatchSize,
			Height = SwatchSize,
			Margin = new Thickness(0, 0, 6, 6),
			CornerRadius = new CornerRadius(5),
			BorderThickness = new Thickness(1),
			ClipToBounds = true,
			Cursor = Cursors.Hand,
			ToolTip = tooltip,
			Tag = color,
			Child = grid
		};
		swatch.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
		swatch.MouseLeftButtonUp += Swatch_MouseLeftButtonUp;
		if (removable)
			swatch.MouseRightButtonUp += SavedSwatch_MouseRightButtonUp;
		return swatch;
	}

	private Border CreateAddSwatch(string tooltip)
	{
		var plus = new Path
		{
			Data = Geometry.Parse("M6,1 V11 M1,6 H11"),
			Width = 10,
			Height = 10,
			Stretch = Stretch.Uniform,
			StrokeThickness = 1.4,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		plus.SetResourceReference(Shape.StrokeProperty, "TextFillColorPrimaryBrush");
		var add = new Border
		{
			Width = SwatchSize,
			Height = SwatchSize,
			Margin = new Thickness(0, 0, 6, 6),
			CornerRadius = new CornerRadius(5),
			BorderThickness = new Thickness(1),
			Background = Brushes.Transparent,
			Cursor = Cursors.Hand,
			ToolTip = tooltip,
			Child = plus
		};
		add.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
		add.MouseLeftButtonUp += AddSwatch_MouseLeftButtonUp;
		return add;
	}

	private void Swatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is FrameworkElement { Tag: Color color })
			ApplyColor(color);
		e.Handled = true;
	}

	private void SavedSwatch_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is FrameworkElement { Tag: Color color })
		{
			RinColorSwatches.Store.RemoveSaved(color);
			RebuildSwatches();
		}
		e.Handled = true;
	}

	private void AddSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		RinColorSwatches.Store.AddSaved(SelectedColor);
		RebuildSwatches();
		e.Handled = true;
	}

	private async void Eyedropper_Click(object sender, RoutedEventArgs e)
	{
		if (_sampling)
			return;
		if (RinScreenSampler.Current == null)
		{
			await PickOnceAsync();
			return;
		}
		EndDrag(true);
		_beforeSample = SelectedColor;
		_sampling = true;
		Mouse.OverrideCursor = Cursors.Cross;
		Focus();
		if (!Mouse.Capture(this, CaptureMode.SubTree))
		{
			StopSampling(false);
			return;
		}
		_sampleTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(SampleIntervalMs)
		};
		_sampleTimer.Tick += SampleTimer_Tick;
		_sampleTimer.Start();
	}

	private async System.Threading.Tasks.Task PickOnceAsync()
	{
		IRinScreenColorPicker? picker = RinScreenSampler.Picker;
		if (picker is not { IsAvailable: true })
			return;
		EyedropperButton.IsEnabled = false;
		try
		{
			Color? picked = await picker.PickAsync();
			if (picked is Color color && IsLoaded)
			{
				color.A = (byte)Math.Round(_a * 255);
				LoadFromColor(color, false);
				Commit();
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			EyedropperButton.IsEnabled = true;
		}
	}

	private void SampleTimer_Tick(object? sender, EventArgs e)
	{
		IRinScreenSampler? sampler = RinScreenSampler.Current;
		if (!_sampling || sampler == null || !sampler.TrySample(out Color color))
			return;
		color.A = (byte)Math.Round(_a * 255);
		if (color == SelectedColor)
			return;
		LoadFromColor(color, false);
		Commit();
	}

	private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (!_sampling)
			return;
		if (e.ChangedButton == MouseButton.Left)
		{
			SampleTimer_Tick(null, EventArgs.Empty);
			StopSampling(true);
		}
		else
		{
			StopSampling(false);
		}
		e.Handled = true;
	}

	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (!_sampling)
			return;
		if (e.Key == Key.Escape)
		{
			StopSampling(false);
			e.Handled = true;
		}
		else if (e.Key == Key.Enter || e.Key == Key.Space)
		{
			StopSampling(true);
			e.Handled = true;
		}
	}

	private void StopSampling(bool keep)
	{
		if (!_sampling)
			return;
		_sampling = false;
		if (_sampleTimer != null)
		{
			_sampleTimer.Stop();
			_sampleTimer.Tick -= SampleTimer_Tick;
			_sampleTimer = null;
		}
		Mouse.OverrideCursor = null;
		if (ReferenceEquals(Mouse.Captured, this))
			Mouse.Capture(null);
		if (!keep)
		{
			LoadFromColor(_beforeSample, false);
			Commit();
		}
		else
		{
			RefreshInputs(null);
		}
	}
}

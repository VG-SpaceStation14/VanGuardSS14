using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._VanGuard.UserInterface.Controls;

// condensed version of the original ColorSlider set
public sealed partial class LegacyColorSelectorSliders : Control
{
    public Color Color
    {
        get => _currentColor;
        set
        {
            _currentColor = value;
            _colorData = GetStrategy().ToColorData(value);

            UpdateAllSliders();
        }
    }

    public ColorSelectorType SelectorType
    {
        get => _currentType;
        set
        {
            _currentType = value;
            _typeSelector.Select(_types.IndexOf(value));
            _colorData = GetStrategy().ToColorData(_currentColor);

            UpdateType();
            UpdateAllSliders();
        }
    }

    public bool IsAlphaVisible
    {
        get => _isAlphaVisible;
        set
        {
            _isAlphaVisible = value;

            _alphaSliderBox.Visible = _isAlphaVisible;
        }
    }

    public Action<Color>? OnColorChanged;
    public Action<Color>? OnColorReleased;

    public bool IsGrabbed =>
        _topColorSlider.Grabbed ||
        _middleColorSlider.Grabbed ||
        _bottomColorSlider.Grabbed ||
        _alphaSlider.Grabbed;

    private readonly static HsvSliderStrategy _hsvStrategy = new();
    private readonly static RgbSliderStrategy _rgbStrategy = new();

    private const float AlphaDivisor = 100.0f;

    private Color _currentColor = Color.White;
    private Vector4 _colorData;
    private ColorSelectorType _currentType = ColorSelectorType.Rgb;
    private bool _isAlphaVisible = false;

    private ColorableSlider _topColorSlider;
    private ColorableSlider _middleColorSlider;
    private ColorableSlider _bottomColorSlider;
    private Slider _alphaSlider;

    private BoxContainer _alphaSliderBox = new();

    private SpinBox _topInputBox;
    private SpinBox _middleInputBox;
    private SpinBox _bottomInputBox;
    private SpinBox _alphaInputBox;

    private Label _topSliderLabel = new();
    private Label _middleSliderLabel = new();
    private Label _bottomSliderLabel = new();
    private Label _alphaSliderLabel = new();

    private OptionButton _typeSelector;
    private List<ColorSelectorType> _types = new();

    private ColorSelectorStyleBox _topStyle;
    private ColorSelectorStyleBox _middleStyle;
    private ColorSelectorStyleBox _bottomStyle;

    private bool _colorChangePending;
    private float _colorThrottleTimer;
    private const float ColorThrottleDelay = 0.001f;
    private ColorSliderOrder _lastOrder;

    public LegacyColorSelectorSliders()
    {
        IoCManager.InjectDependencies(this);

        _topColorSlider = new ColorableSlider
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            BackgroundStyleBoxOverride = _topStyle = new(),
            MaxValue = 1.0f
        };

        _middleColorSlider = new ColorableSlider
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            BackgroundStyleBoxOverride = _middleStyle = new(),
            MaxValue = 1.0f
        };

        _bottomColorSlider = new ColorableSlider
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            BackgroundStyleBoxOverride = _bottomStyle = new(),
            MaxValue = 1.0f
        };

        _alphaSlider = new Slider
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            MaxValue = 1.0f,
        };

        _topColorSlider.OnValueChanged += r => { OnSliderValueChangedThrottled(ColorSliderOrder.Top); };
        _middleColorSlider.OnValueChanged += r => { OnSliderValueChangedThrottled(ColorSliderOrder.Middle); };
        _bottomColorSlider.OnValueChanged += r => { OnSliderValueChangedThrottled(ColorSliderOrder.Bottom); };
        _alphaSlider.OnValueChanged += r => { OnSliderValueChangedThrottled(ColorSliderOrder.Alpha); };

        _topColorSlider.OnReleased += _ => OnColorReleased?.Invoke(_currentColor);
        _middleColorSlider.OnReleased += _ => OnColorReleased?.Invoke(_currentColor);
        _bottomColorSlider.OnReleased += _ => OnColorReleased?.Invoke(_currentColor);
        _alphaSlider.OnReleased += _ => OnColorReleased?.Invoke(_currentColor);

        _topInputBox = new SpinBox
        {
            IsValid = value => IsSpinBoxValid(value, ColorSliderOrder.Top)
        };
        _topInputBox.InitDefaultButtons();

        _middleInputBox = new SpinBox
        {
            IsValid = value => IsSpinBoxValid(value, ColorSliderOrder.Middle)
        };
        _middleInputBox.InitDefaultButtons();

        _bottomInputBox = new SpinBox
        {
            IsValid = value => IsSpinBoxValid(value, ColorSliderOrder.Bottom)
        };
        _bottomInputBox.InitDefaultButtons();

        _alphaInputBox = new SpinBox
        {
            IsValid = value => IsSpinBoxValid(value, ColorSliderOrder.Alpha)
        };
        _alphaInputBox.InitDefaultButtons();

        _topInputBox.ValueChanged += value => { OnInputBoxValueChanged(value, ColorSliderOrder.Top); };
        _middleInputBox.ValueChanged += value => { OnInputBoxValueChanged(value, ColorSliderOrder.Middle); };
        _bottomInputBox.ValueChanged += value => { OnInputBoxValueChanged(value, ColorSliderOrder.Bottom); };
        _alphaInputBox.ValueChanged += value => { OnInputBoxValueChanged(value, ColorSliderOrder.Alpha); };

        _alphaSliderLabel.Text = Loc.GetString("color-selector-sliders-alpha");

        _typeSelector = new OptionButton();
        foreach (var ty in Enum.GetValues<ColorSelectorType>())
        {
            _typeSelector.AddItem(Loc.GetString($"color-selector-sliders-{ty.ToString().ToLower()}"));
            _types.Add(ty);
        }

        _typeSelector.OnItemSelected += args =>
        {
            SelectorType = _types[args.Id];
            _typeSelector.Select(args.Id);
        };

        var rootBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical
        };
        AddChild(rootBox);

        var headerBox = new BoxContainer();
        rootBox.AddChild(headerBox);

        headerBox.AddChild(_typeSelector);

        var bodyBox = new BoxContainer()
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical
        };

        var topSliderBox = new BoxContainer();
        topSliderBox.AddChild(_topSliderLabel);
        topSliderBox.AddChild(_topColorSlider);
        topSliderBox.AddChild(_topInputBox);

        var middleSliderBox = new BoxContainer();
        middleSliderBox.AddChild(_middleSliderLabel);
        middleSliderBox.AddChild(_middleColorSlider);
        middleSliderBox.AddChild(_middleInputBox);

        var bottomSliderBox = new BoxContainer();
        bottomSliderBox.AddChild(_bottomSliderLabel);
        bottomSliderBox.AddChild(_bottomColorSlider);
        bottomSliderBox.AddChild(_bottomInputBox);

        _alphaSliderBox.Visible = IsAlphaVisible;
        _alphaSliderBox.AddChild(_alphaSliderLabel);
        _alphaSliderBox.AddChild(_alphaSlider);
        _alphaSliderBox.AddChild(_alphaInputBox);

        bodyBox.AddChild(topSliderBox);
        bodyBox.AddChild(middleSliderBox);
        bodyBox.AddChild(bottomSliderBox);
        bodyBox.AddChild(_alphaSliderBox);

        rootBox.AddChild(bodyBox);

        UpdateType();
        Color = _currentColor;
    }

    private void OnSliderValueChangedThrottled(ColorSliderOrder order)
    {
        _colorChangePending = true;
        _colorThrottleTimer = ColorThrottleDelay;
        _lastOrder = order;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!_colorChangePending) return;

        _colorThrottleTimer -= args.DeltaSeconds;
        if (_colorThrottleTimer > 0) return;

        _colorChangePending = false;
        OnSliderValueChanged(_lastOrder);
    }

    private ColorSliderStrategy GetStrategy()
    {
        return SelectorType switch
        {
            ColorSelectorType.Rgb => _rgbStrategy,
            ColorSelectorType.Hsv => _hsvStrategy,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private (Slider slider, SpinBox inputBox) GetSliderByOrder(ColorSliderOrder order)
    {
        return order switch
        {
            ColorSliderOrder.Top => (_topColorSlider, _topInputBox),
            ColorSliderOrder.Middle => (_middleColorSlider, _middleInputBox),
            ColorSliderOrder.Bottom => (_bottomColorSlider, _bottomInputBox),
            ColorSliderOrder.Alpha => (_alphaSlider, _alphaInputBox),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private float GetColorValueDivisor(ColorSliderOrder order)
    {
        return order == ColorSliderOrder.Alpha
            ? AlphaDivisor
            : GetStrategy().GetColorValueDivisor(order);
    }

    private void UpdateType()
    {
        var strategy = GetStrategy();
        var labels = strategy.GetSliderLabelTexts();
        _topSliderLabel.Text = labels.top;
        _middleSliderLabel.Text = labels.middle;
        _bottomSliderLabel.Text = labels.bottom;

        _topStyle.ConfigureSlider(strategy.TopSliderStyle);
        _middleStyle.ConfigureSlider(strategy.MiddleSliderStyle);
        _bottomStyle.ConfigureSlider(strategy.BottomSliderStyle);
    }

    private void UpdateSlider(ColorSliderOrder order)
    {
        var (slider, inputBox) = GetSliderByOrder(order);
        var divisor = GetColorValueDivisor(order);

        var dataValue = order switch
        {
            ColorSliderOrder.Top => _colorData.X,
            ColorSliderOrder.Middle => _colorData.Y,
            ColorSliderOrder.Bottom => _colorData.Z,
            ColorSliderOrder.Alpha => _colorData.W,
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };

        slider.SetValueWithoutEvent(dataValue);
        inputBox.OverrideValue((int)(dataValue * divisor));
    }

    private void UpdateSliderVisuals()
    {
        _topStyle.SetBaseColor(_colorData);
        _middleStyle.SetBaseColor(_colorData);
        _bottomStyle.SetBaseColor(_colorData);
    }

    private void UpdateAllSliders()
    {
        UpdateSliderVisuals();
        UpdateSlider(ColorSliderOrder.Top);
        UpdateSlider(ColorSliderOrder.Middle);
        UpdateSlider(ColorSliderOrder.Bottom);
        UpdateSlider(ColorSliderOrder.Alpha);
    }

    private bool IsSpinBoxValid(int value, ColorSliderOrder ordering)
    {
        var divisor = GetColorValueDivisor(ordering);
        var channelValue = value / divisor;

        return channelValue >= 0.0f && channelValue <= 1.0f;
    }

    private void OnInputBoxValueChanged(ValueChangedEventArgs args, ColorSliderOrder order)
    {
        var (slider, _) = GetSliderByOrder(order);
        var value = args.Value / GetColorValueDivisor(order);

        slider.Value = value;
    }

    private void OnSliderValueChanged(ColorSliderOrder order)
    {
        _colorData = new Vector4(
            _topColorSlider.Value,
            _middleColorSlider.Value,
            _bottomColorSlider.Value,
            _alphaSlider.Value);

        _currentColor = GetStrategy().FromColorData(_colorData);
        OnColorChanged?.Invoke(_currentColor);

        UpdateSliderVisuals();
        UpdateSlider(order);
    }

    private enum ColorSliderOrder
    {
        Top,
        Middle,
        Bottom,
        Alpha
    }

    public enum ColorSelectorType
    {
        Rgb,
        Hsv,
    }

    private abstract class ColorSliderStrategy
    {
        public abstract ColorSelectorStyleBox.ColorSliderPreset TopSliderStyle { get; }
        public abstract ColorSelectorStyleBox.ColorSliderPreset MiddleSliderStyle { get; }
        public abstract ColorSelectorStyleBox.ColorSliderPreset BottomSliderStyle { get; }
        public abstract Vector4 ToColorData(Color color);
        public abstract Color FromColorData(Vector4 colorData);
        public abstract float GetColorValueDivisor(ColorSliderOrder order);
        public abstract (string top, string middle, string bottom) GetSliderLabelTexts();
    }

    private sealed class RgbSliderStrategy : ColorSliderStrategy
    {
        private const float ChannelMaxValue = byte.MaxValue;

        public override ColorSelectorStyleBox.ColorSliderPreset TopSliderStyle
            => ColorSelectorStyleBox.ColorSliderPreset.Red;
        public override ColorSelectorStyleBox.ColorSliderPreset MiddleSliderStyle
            => ColorSelectorStyleBox.ColorSliderPreset.Green;
        public override ColorSelectorStyleBox.ColorSliderPreset BottomSliderStyle
            => ColorSelectorStyleBox.ColorSliderPreset.Blue;

        public override Vector4 ToColorData(Color color) => new(color.R, color.G, color.B, color.A);
        public override Color FromColorData(Vector4 colorData)
            => new(colorData.X, colorData.Y, colorData.Z, colorData.W);

        public override float GetColorValueDivisor(ColorSliderOrder order) => ChannelMaxValue;

        public override (string top, string middle, string bottom) GetSliderLabelTexts()
        {
            return (
                Loc.GetString("color-selector-sliders-red"),
                Loc.GetString("color-selector-sliders-green"),
                Loc.GetString("color-selector-sliders-blue"));
        }
    }

    private sealed class HsvSliderStrategy : ColorSliderStrategy
    {
        private const float HueMaxValue = 360.0f;
        private const float SliderMaxValue = 100.0f;

        public override ColorSelectorStyleBox.ColorSliderPreset TopSliderStyle
            => ColorSelectorStyleBox.ColorSliderPreset.Hue;
        public override ColorSelectorStyleBox.ColorSliderPreset MiddleSliderStyle
            => ColorSelectorStyleBox.ColorSliderPreset.Saturation;
        public override ColorSelectorStyleBox.ColorSliderPreset BottomSliderStyle
            => ColorSelectorStyleBox.ColorSliderPreset.Value;

        public override Vector4 ToColorData(Color color) => Color.ToHsv(color);
        public override Color FromColorData(Vector4 colorData) => Color.FromHsv(colorData);

        public override float GetColorValueDivisor(ColorSliderOrder order)
        {
            return order switch
            {
                ColorSliderOrder.Top => HueMaxValue,
                _ => SliderMaxValue,
            };
        }

        public override (string top, string middle, string bottom) GetSliderLabelTexts()
        {
            return (
                Loc.GetString("color-selector-sliders-hue"),
                Loc.GetString("color-selector-sliders-saturation"),
                Loc.GetString("color-selector-sliders-value"));
        }
    }
}
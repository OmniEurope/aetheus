// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Shared;
using Bunit;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// Behavioural coverage for <see cref="LabeledToggle"/>: the whole row (a native
/// <c>&lt;label&gt;</c>) must be clickable and flip the value - the reason the component exists,
/// since Radzen's own label association does not toggle a checkbox/switch on click.
/// </summary>
public class LabeledToggleTests : BunitContext
{
    public LabeledToggleTests() => BunitTestHelper.RegisterServices(this, isAdmin: true);

    [Fact]
    public void ClickingTheLabelRow_TogglesValue_FromFalseToTrue()
    {
        bool? received = null;
        var cut = Render<LabeledToggle>(p => p
            .Add(x => x.Value, false)
            .Add(x => x.Text, "Enable")
            .Add(x => x.ValueChanged, (bool v) => received = v));

        cut.Find(".labeled-toggle-native-input").Change(true);

        Assert.True(received);
    }

    [Fact]
    public void ClickingTheLabelRow_TogglesValue_FromTrueToFalse()
    {
        bool? received = null;
        var cut = Render<LabeledToggle>(p => p
            .Add(x => x.Value, true)
            .Add(x => x.Text, "Enable")
            .Add(x => x.ValueChanged, (bool v) => received = v));

        cut.Find(".labeled-toggle-native-input").Change(false);

        Assert.False(received);
    }

    [Fact]
    public void ClickingTheTextSpan_AlsoToggles()
    {
        bool? received = null;
        var cut = Render<LabeledToggle>(p => p
            .Add(x => x.Value, false)
            .Add(x => x.Text, "Enable")
            .Add(x => x.ValueChanged, (bool v) => received = v));

        cut.Find(".labeled-toggle-native-input").Change(true);

        Assert.True(received);
    }

    [Fact]
    public void Disabled_DoesNotToggle()
    {
        bool? received = null;
        var cut = Render<LabeledToggle>(p => p
            .Add(x => x.Value, false)
            .Add(x => x.Text, "Enable")
            .Add(x => x.Disabled, true)
            .Add(x => x.ValueChanged, (bool v) => received = v));

        cut.Find(".labeled-toggle-native-input").Change(true);

        Assert.Null(received);
    }

    [Fact]
    public void SwitchKind_RendersASwitch_CheckboxKind_RendersACheckbox()
    {
        var asSwitch = Render<LabeledToggle>(p => p
            .Add(x => x.Kind, ToggleKind.Switch)
            .Add(x => x.Text, "S"));
        Assert.NotEmpty(asSwitch.FindAll(".rz-switch"));

        var asCheckbox = Render<LabeledToggle>(p => p
            .Add(x => x.Kind, ToggleKind.Checkbox)
            .Add(x => x.Text, "C"));
        Assert.NotEmpty(asCheckbox.FindAll(".rz-chkbox"));
    }

    [Fact]
    public void NativeInput_IsKeyboardOperableWithoutCustomKeyHandler()
    {
        bool? received = null;
        var cut = Render<LabeledToggle>(p => p
            .Add(x => x.Value, false)
            .Add(x => x.Text, "Enable")
            .Add(x => x.ValueChanged, (bool v) => received = v));

        var input = cut.Find("input[type=checkbox]");
        input.Change(true);

        Assert.True(received);
        Assert.False(input.HasAttribute("tabindex"));
    }

    [Fact]
    public void Disabled_DoesNotToggleOnKeyboard()
    {
        bool? received = null;
        var cut = Render<LabeledToggle>(p => p
            .Add(x => x.Value, false)
            .Add(x => x.Text, "Enable")
            .Add(x => x.Disabled, true)
            .Add(x => x.ValueChanged, (bool v) => received = v));

        cut.Find(".labeled-toggle-native-input").Change(true);

        Assert.Null(received);
    }

    [Fact]
    public void NativeInput_IsTheOnlyTabStop_AndRadzenControlIsHidden()
    {
        // The wrapping label owns focus + ARIA (role/tabindex=0); the inner Radzen control must be
        // pulled out of the tab order (tabindex=-1) and hidden from AT (aria-hidden) so there is a
        // single, correct focus stop. This is the core of the keyboard-a11y fix - assert it can't regress.
        foreach (var kind in new[] { ToggleKind.Switch, ToggleKind.Checkbox })
        {
            var cut = Render<LabeledToggle>(p => p
                .Add(x => x.Kind, kind)
                .Add(x => x.Text, "T"));

            Assert.False(cut.Find("input[type=checkbox]").HasAttribute("tabindex"));
            Assert.Contains("tabindex=\"-1\"", cut.Markup);
            Assert.Contains("aria-hidden=\"true\"", cut.Markup);
        }
    }

    [Fact]
    public void ExposesNativeCheckedState_ForScreenReaders()
    {
        var cut = Render<LabeledToggle>(p => p
            .Add(x => x.Kind, ToggleKind.Switch)
            .Add(x => x.Value, true)
            .Add(x => x.Text, "Enable"));

        var input = cut.Find("input[type=checkbox]");
        Assert.True(input.HasAttribute("checked"));
    }

    [Fact]
    public void Renders_Text_AndOptionalIcon()
    {
        var cut = Render<LabeledToggle>(p => p
            .Add(x => x.Text, "Prune volumes")
            .Add(x => x.Icon, "storage"));

        Assert.Contains("Prune volumes", cut.Markup);
        Assert.Contains("storage", cut.Markup);
    }
}

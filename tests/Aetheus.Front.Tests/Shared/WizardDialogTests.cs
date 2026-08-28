// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Shared;

public class WizardDialogTests : BunitContext
{
    public WizardDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    private static List<WizardStep> CreateSteps(int count)
    {
        var steps = new List<WizardStep>();
        for (var i = 0; i < count; i++)
        {
            var index = i;
            steps.Add(new WizardStep
            {
                Title = $"Step {index + 1}",
                Icon = "star",
                Content = (RenderTreeBuilder b) => b.AddContent(0, $"Content of step {index + 1}")
            });
        }
        return steps;
    }

    [Fact]
    public void Renders_AllSteps()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        Assert.Contains("Step 1", cut.Markup);
        Assert.Contains("Content of step 1", cut.Markup);
    }

    [Fact]
    public void Renders_ProgressBar()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        Assert.Contains("wizard-progress-bar", cut.Markup);
    }

    [Fact]
    public void Renders_CancelButton()
    {
        var steps = CreateSteps(2);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        Assert.Contains("Cancel", cut.Markup);
    }

    [Fact]
    public void Renders_NextButton_OnFirstStep()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        Assert.Contains("WizardNext", cut.Markup);
        Assert.DoesNotContain("WizardPrevious", cut.Markup);
    }

    [Fact]
    public void Renders_FinishButton_OnLastStep_WithCustomText()
    {
        var steps = CreateSteps(1);
        var cut = Render<WizardDialog>(p => p
            .Add(x => x.Steps, steps)
            .Add(x => x.FinishText, "Done!"));

        Assert.Contains("Done!", cut.Markup);
    }

    [Fact]
    public void Renders_FinishButton_OnLastStep_DefaultText()
    {
        var steps = CreateSteps(1);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        Assert.Contains("WizardFinish", cut.Markup);
    }

    [Fact]
    public void Renders_StepIcons()
    {
        var steps = CreateSteps(2);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        Assert.Contains("star", cut.Markup);
    }

    [Fact]
    public void CurrentStep_StartsAtZero()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        Assert.Equal(0, cut.Instance.CurrentStep);
    }

    [Fact]
    public void CanAdvance_DisablesNextButton_WhenFalse()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p
            .Add(x => x.Steps, steps)
            .Add(x => x.CanAdvance, false));

        var nextBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("WizardNext"));
        Assert.NotNull(nextBtn);
        Assert.True(nextBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void Renders_StepContent_WithoutIcon()
    {
        var steps = new List<WizardStep>
        {
            new()
            {
                Title = "NoIcon",
                Content = (RenderTreeBuilder b) => b.AddContent(0, "No icon step")
            }
        };
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        Assert.Contains("No icon step", cut.Markup);
        Assert.DoesNotContain("wizard-step-icon", cut.Markup);
    }

    [Fact]
    public async Task OnCancelClicked_InvokesCallback()
    {
        var cancelled = false;
        var steps = CreateSteps(2);
        var cut = Render<WizardDialog>(p => p
            .Add(x => x.Steps, steps)
            .Add(x => x.OnCancelClicked, () => { cancelled = true; }));

        var cancelBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        await cut.InvokeAsync(() => cancelBtn.Click());

        Assert.True(cancelled);
    }

    [Fact]
    public async Task OnFinishClicked_InvokesCallback()
    {
        var finished = false;
        var steps = CreateSteps(1);
        var cut = Render<WizardDialog>(p => p
            .Add(x => x.Steps, steps)
            .Add(x => x.OnFinishClicked, () => { finished = true; }));

        var finishBtn = cut.FindAll("button").First(b => b.TextContent.Contains("WizardFinish"));
        await cut.InvokeAsync(() => finishBtn.Click());

        Assert.True(finished);
    }

    [Fact]
    public void NextButton_IsRendered_WhenMultipleSteps()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p
            .Add(x => x.Steps, steps));

        var nextBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("WizardNext"));
        Assert.NotNull(nextBtn);
        Assert.False(nextBtn.HasAttribute("disabled"));
    }

    // === Additional coverage: NextStep, PreviousStep, OnStepChanged, OnKeyDown, progress ===

    [Fact]
    public async Task NextStep_AdvancesCurrentStep()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        await cut.InvokeAsync(() => cut.Instance.NextStep());

        Assert.Equal(1, cut.Instance.CurrentStep);
    }

    [Fact]
    public async Task NextStep_AtLastStep_DoesNotAdvance()
    {
        var steps = CreateSteps(1);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        await cut.InvokeAsync(() => cut.Instance.NextStep());

        Assert.Equal(0, cut.Instance.CurrentStep);
    }

    [Fact]
    public async Task PreviousStep_FromStep1_GoesBackToStep0()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        // Advance first
        await cut.InvokeAsync(() => cut.Instance.NextStep());
        Assert.Equal(1, cut.Instance.CurrentStep);

        // Go back
        var prevMethod = typeof(WizardDialog)
            .GetMethod("PreviousStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)prevMethod.Invoke(cut.Instance, [])!);

        Assert.Equal(0, cut.Instance.CurrentStep);
    }

    [Fact]
    public async Task PreviousStep_AtStep0_DoesNotGoNegative()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        var prevMethod = typeof(WizardDialog)
            .GetMethod("PreviousStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)prevMethod.Invoke(cut.Instance, [])!);

        Assert.Equal(0, cut.Instance.CurrentStep);
    }

    [Fact]
    public async Task OnStepChanged_UpdatesCurrentStep()
    {
        var steps = CreateSteps(3);
        // Set _highestStep to 2 so step 2 is accessible
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));
        typeof(WizardDialog).GetField("_highestStep", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, 2);

        var method = typeof(WizardDialog)
            .GetMethod("OnStepChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [2])!);

        Assert.Equal(2, cut.Instance.CurrentStep);
    }

    [Fact]
    public async Task OnStepChanged_SameStep_DoesNotAnimate()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        var method = typeof(WizardDialog)
            .GetMethod("OnStepChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Set same step (0 → 0) - should be a no-op for animation
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [0])!);

        Assert.Equal(0, cut.Instance.CurrentStep);
    }

    [Fact]
    public async Task NextStep_FiresStepChangedCallback()
    {
        var firedStep = -1;
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p
            .Add(x => x.Steps, steps)
            .Add(x => x.StepChanged, EventCallback.Factory.Create<int>(this, s => firedStep = s)));

        await cut.InvokeAsync(() => cut.Instance.NextStep());

        Assert.Equal(1, firedStep);
    }

    [Fact]
    public void Progress_SingleStep_IsComplete()
    {
        var steps = CreateSteps(1);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        var field = typeof(WizardDialog)
            .GetField("_progress", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var pct = (double)field.GetValue(cut.Instance)!;
        Assert.Equal(100, pct);
    }

    [Fact]
    public void Progress_MultiStep_IsZeroAtStart()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        var field = typeof(WizardDialog)
            .GetField("_progress", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var pct = (double)field.GetValue(cut.Instance)!;
        Assert.Equal(0, pct);
    }

    [Fact]
    public async Task Progress_MultiStep_ReachesOneHundredOnLastStep()
    {
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, CreateSteps(3)));

        await cut.InvokeAsync(cut.Instance.NextStep);
        await cut.InvokeAsync(cut.Instance.NextStep);

        var field = typeof(WizardDialog)
            .GetField("_progress", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(100, (double)field.GetValue(cut.Instance)!);
    }

    [Fact]
    public void IsStepAccessible_Step0_AlwaysTrue()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        var method = typeof(WizardDialog)
            .GetMethod("IsStepAccessible", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (bool)method.Invoke(cut.Instance, [0])!;
        Assert.True(result);
    }

    [Fact]
    public void IsStepAccessible_UnvisitedStep_ReturnsFalse()
    {
        var steps = CreateSteps(3);
        var cut = Render<WizardDialog>(p => p.Add(x => x.Steps, steps));

        var method = typeof(WizardDialog)
            .GetMethod("IsStepAccessible", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Step 2 not visited yet, _highestStep == 0
        var result = (bool)method.Invoke(cut.Instance, [2])!;
        Assert.False(result);
    }

    [Fact]
    public async Task OnKeyDown_Escape_InvokesCancelCallback()
    {
        var cancelled = false;
        var steps = CreateSteps(2);
        var cut = Render<WizardDialog>(p => p
            .Add(x => x.Steps, steps)
            .Add(x => x.OnCancelClicked, () => { cancelled = true; }));

        var method = typeof(WizardDialog)
            .GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var keyArgs = new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" };
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [keyArgs])!);

        Assert.True(cancelled);
    }

    [Fact]
    public async Task OnKeyDown_NonEscape_LeavesTheFlagOff()
    {
        var cancelled = false;
        var steps = CreateSteps(2);
        var cut = Render<WizardDialog>(p => p
            .Add(x => x.Steps, steps)
            .Add(x => x.OnCancelClicked, () => { cancelled = true; }));

        var method = typeof(WizardDialog)
            .GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var keyArgs = new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" };
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [keyArgs])!);

        Assert.False(cancelled);
    }
}

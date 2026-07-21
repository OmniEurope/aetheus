// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;

namespace Aetheus.Front.Tests;

internal static class FormModelTestHelper
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static object GetFormModel(object component, string fieldName) =>
        component.GetType().GetField(fieldName, PrivateInstance)!.GetValue(component)!;

    public static void SetFormValue(object component, string fieldName, string propertyName, object? value)
    {
        var model = GetFormModel(component, fieldName);
        model.GetType().GetProperty(propertyName)!.SetValue(model, value);
    }

    public static T GetFormValue<T>(object component, string fieldName, string propertyName)
    {
        var model = GetFormModel(component, fieldName);
        return (T)model.GetType().GetProperty(propertyName)!.GetValue(model)!;
    }

    public static async Task InvokeFormSubmitAsync(object component, string methodName, string fieldName)
    {
        var model = GetFormModel(component, fieldName);
        var method = component.GetType().GetMethod(methodName, PrivateInstance, [model.GetType()])!;
        await (Task)method.Invoke(component, [model])!;
    }
}

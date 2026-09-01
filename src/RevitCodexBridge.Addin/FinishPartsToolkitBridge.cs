using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitCodexBridge.Addin;

internal static class FinishPartsToolkitBridge
{
    private const string AssemblyName = "FinishPartsToolkit.Addin";
    private const string ApiTypeName = "FinishPartsToolkit.Addin.Ai.FinishPartsAiApi";

    public static object GetStatus()
    {
        var assembly = FindAssembly();
        var apiType = assembly?.GetType(ApiTypeName, throwOnError: false);
        return new
        {
            installed = assembly is not null,
            aiApiAvailable = apiType is not null,
            assemblyVersion = assembly?.GetName().Version?.ToString(),
            message = assembly is null
                ? "未检测到 FinishPartsToolkit.Addin，请先安装并启用呆猫工作室精装插件。"
                : apiType is null
                    ? "已检测到精装插件，但版本不包含 AI 调用入口，请重新安装最新版。"
                    : "呆猫工作室精装 Skill 已就绪。"
        };
    }

    public static object Execute(UIApplication application, string action)
    {
        var assembly = FindAssembly()
            ?? throw new InvalidOperationException("未检测到已加载的呆猫工作室精装插件。请确认 FinishPartsToolkit 已安装并随 Revit 启动。");
        var apiType = assembly.GetType(ApiTypeName, throwOnError: false)
            ?? throw new InvalidOperationException("当前精装插件版本不包含 AI 调用入口，请重新安装最新版 FinishPartsToolkit。");
        var method = apiType.GetMethod(
            "Execute",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(UIApplication), typeof(string)],
            modifiers: null)
            ?? throw new InvalidOperationException("精装插件 AI 调用入口签名不兼容。");

        try
        {
            return method.Invoke(null, [application, action])
                ?? throw new InvalidOperationException("精装插件没有返回执行结果。");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidOperationException(ex.InnerException.Message, ex.InnerException);
        }
    }

    private static Assembly? FindAssembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(
                assembly.GetName().Name,
                AssemblyName,
                StringComparison.OrdinalIgnoreCase));
    }
}

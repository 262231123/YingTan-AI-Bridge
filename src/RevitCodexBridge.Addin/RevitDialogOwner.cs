using System.Windows.Forms;

namespace RevitCodexBridge.Addin;

internal sealed class RevitDialogOwner : IWin32Window
{
    public RevitDialogOwner(IntPtr handle)
    {
        Handle = handle;
    }

    public IntPtr Handle { get; }
}

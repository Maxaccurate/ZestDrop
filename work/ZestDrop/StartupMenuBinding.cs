using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace ZestDrop;

// Bind the actual menu checkbox to the startup provider. A click first toggles
// CheckOnClick, then writes that requested state; it must not invert it again.
// Old asynchronous reads must not overwrite a newer choice or pending write.
internal sealed class StartupMenuBinding : IDisposable
{
    private readonly Forms.ToolStripMenuItem item;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Action<string> reportProblem;
    private bool changing, disposed;
    private int revision;

    public StartupMenuBinding(Forms.ToolStripMenuItem item, Forms.ContextMenuStrip menu, Action<string> reportProblem)
    {
        this.item = item;
        this.menu = menu;
        this.reportProblem = reportProblem;
        item.CheckOnClick = true;
        item.Click += Click;
        menu.Opening += Opening;
        StartupManager.Changed += Refresh;
        Refresh();
    }

    private bool Alive => !disposed && !item.IsDisposed;
    private async void Refresh() => await RefreshAsync();
    private void Opening(object? sender, CancelEventArgs e) => Refresh();

    internal async Task RefreshAsync()
    {
        if (!Alive || changing) return;
        int requested = ++revision;
        var status = await StartupManager.GetAsync();
        if (Alive && !changing && requested == revision) Apply(status);
    }

    private async void Click(object? sender, EventArgs e)
    {
        if (!Alive || changing) return;
        bool requested = item.Checked;
        changing = true;
        ++revision;
        item.Enabled = false;
        try
        {
            var status = await StartupManager.SetAsync(requested);
            if (!Alive) return;
            Apply(status);
            if (status.Message != null) reportProblem(status.Message);
        }
        finally
        {
            changing = false;
        }
    }

    private void Apply(StartupStatus status)
    {
        item.Checked = status.Enabled;
        item.Enabled = status.CanChange;
        item.ToolTipText = status.Message ?? L.T("登录 Windows 后在后台运行，不打开欢迎窗口。");
    }

    public void Dispose()
    {
        disposed = true;
        ++revision;
        item.Click -= Click;
        menu.Opening -= Opening;
        StartupManager.Changed -= Refresh;
    }
}

using System.Runtime.InteropServices;

namespace Rune.Voice;

// Older settings editors retain native resize/accessibility behaviour, but use
// Rune's colours for both the caption and controls instead of the Windows accent.
internal sealed class RunePopupForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal RunePopupForm()
    {
        BackColor = RuneTheme.Stone; ForeColor = RuneTheme.Bone;
        ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Set(20, 1); Set(35, ColorTranslator.ToWin32(RuneTheme.Stone));
        Set(36, ColorTranslator.ToWin32(RuneTheme.Bone)); Set(34, ColorTranslator.ToWin32(RuneTheme.Iron));
    }
    private void Set(int attribute, int value) => DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int));
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        void StyleChildren(Control parent)
        {
            foreach (Control child in parent.Controls) {
                if (child is TextBox or ComboBox && (child.ForeColor == SystemColors.WindowText || child.ForeColor == SystemColors.ControlText)) RuneTheme.Field(child);
                if (child is Button button && button.BackColor == SystemColors.Control) { button.FlatStyle = FlatStyle.Flat; button.ForeColor = RuneTheme.Bone; RuneTheme.SetButtonTone(button, RuneButtonTone.Neutral); }
                if (child is Label or CheckBox && child.ForeColor == SystemColors.ControlText) child.ForeColor = RuneTheme.Bone;
                StyleChildren(child);
            }
        }
        StyleChildren(this);
    }
}
